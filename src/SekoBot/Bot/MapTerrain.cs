using System.Text;
using System.Text.Json;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// One map's obstacles (walls, houses, rocks, trees, crates: read from the game's collision, see
/// <see cref="GameWorldReader.ReadBlocks"/>) and the way around them. Only the obstacles at the
/// character's height count (a bridge's underside, an invisible wall under the ground don't), and
/// a block the character was seen standing in (a floor, a bridge, an open gate) is no obstacle
/// from then on. <see cref="WayTo"/> finds the way to a point around everything (A* on a 40-unit
/// grid around the two points), <see cref="LineClear"/> tells whether the straight way is free.
/// </summary>
internal sealed class MapTerrain
{
    /// <summary>The character's half width plus a little room (world units).</summary>
    internal const double Margin = 50;
    /// <summary>The character's position is this far above its feet (world units).</summary>
    internal const double FeetBelow = 90;
    /// <summary>Things up to this far over the feet are stepped onto; from this far over the head they are passed under.</summary>
    internal const double Step = 40, Head = 190;
    /// <summary>The ground rises or falls up to this much per unit away (a slope), at most <see cref="SlopeMost"/>.</summary>
    internal const double Slope = 0.4, SlopeMost = 600;
    internal const double Cell = 40;
    private const int MaxCells = 450, Bucket = 1000;

    private readonly Dictionary<(int, int), List<MapBlock>> _buckets = [];

    public MapTerrain(string map, List<MapBlock> blocks, DateTime read)
    {
        Map = map;
        Blocks = blocks;
        Read = read;
        foreach (var b in blocks)
        {
            var (minX, minY, maxX, maxY) = b.Box;
            for (int x = (int)Math.Floor(minX / Bucket); x <= (int)Math.Floor(maxX / Bucket); x++)
                for (int y = (int)Math.Floor(minY / Bucket); y <= (int)Math.Floor(maxY / Bucket); y++)
                {
                    if (!_buckets.TryGetValue((x, y), out var list))
                        _buckets[(x, y)] = list = [];
                    list.Add(b);
                }
        }
    }

    public string Map { get; }
    public List<MapBlock> Blocks { get; }
    public DateTime Read { get; }
    public bool Changed { get; set; }

    /// <summary>Whether the block stands in the way of a character whose position is at height <paramref name="z"/>,
    /// <paramref name="away"/> units from it.</summary>
    internal static bool InTheWay(MapBlock b, double z, double away)
    {
        if (b.Passable)
            return false;
        double feet = z - FeetBelow, slope = Math.Min(away * Slope, SlopeMost);
        return b.Top > feet + Step - slope && b.Bottom < feet + Head + slope;
    }

    private IEnumerable<MapBlock> Around(double minX, double minY, double maxX, double maxY)
    {
        var seen = new HashSet<MapBlock>();
        for (int x = (int)Math.Floor(minX / Bucket); x <= (int)Math.Floor(maxX / Bucket); x++)
            for (int y = (int)Math.Floor(minY / Bucket); y <= (int)Math.Floor(maxY / Bucket); y++)
                if (_buckets.TryGetValue((x, y), out var list))
                    foreach (var b in list)
                        if (seen.Add(b))
                            yield return b;
    }

    /// <summary>The obstacles (at our height) within <paramref name="range"/> of <paramref name="me"/>.</summary>
    public List<MapBlock> Near(Vec3 me, double range) =>
        [.. Around(me.X - range, me.Y - range, me.X + range, me.Y + range).Where(b => b.Distance(me.X, me.Y) <= range && InTheWay(b, me.Z, Away(b, me)))];

    private static double Away(MapBlock b, Vec3 me)
    {
        var (x, y) = b.Centre;
        return Math.Sqrt((x - me.X) * (x - me.X) + (y - me.Y) * (y - me.Y));
    }

    /// <summary>The character (its middle) stands at <paramref name="me"/>: whatever it stands in isn't solid.</summary>
    public int Walked(Vec3 me)
    {
        int found = 0;
        foreach (var b in Around(me.X, me.Y, me.X, me.Y))
            if (!b.Passable && b.Contains(me.X, me.Y) && me.Z - FeetBelow > b.Bottom - 100 && me.Z - FeetBelow < b.Top + 150)
            {
                b.Passable = true;
                Changed = true;
                found++;
            }
        return found;
    }

    /// <summary>Nothing (at our height) within <see cref="Margin"/> of the straight way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public bool LineClear(Vec3 from, Vec3 to, double margin = Margin)
    {
        double length = from.DistanceXY(to);
        var near = Around(Math.Min(from.X, to.X) - margin, Math.Min(from.Y, to.Y) - margin, Math.Max(from.X, to.X) + margin, Math.Max(from.Y, to.Y) + margin)
            .Where(b => InTheWay(b, from.Z, Away(b, from)) && !(b.Distance(from.X, from.Y) < margin * 0.6)).ToList();
        if (near.Count == 0)
            return true;
        int steps = Math.Max(1, (int)Math.Ceiling(length / 20));
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps, x = from.X + (to.X - from.X) * t, y = from.Y + (to.Y - from.Y) * t;
            foreach (var b in near)
                if (b.Distance(x, y) < margin)
                    return false;
        }
        return true;
    }

    /// <summary>
    /// The way from <paramref name="from"/> to <paramref name="to"/> around the obstacles: the
    /// corners to walk to in turn (the last one is <paramref name="to"/>, or the free spot closest
    /// to it when it stands in an obstacle). Null when there is no way (within the grid).
    /// </summary>
    /// <param name="room">How far around the two points the way may lead at most (world units).</param>
    public List<Vec3>? WayTo(Vec3 from, Vec3 to, double margin = Margin, double room = 12000)
    {
        if (LineClear(from, to, margin))
            return [to];
        // Looked for close around the two points first; a long wall needs more room to get round.
        foreach (double r in Rooms.Where(r => r <= room))
            if (WayWithin(from, to, margin, r) is { } way)
                return way;
        return null;
    }

    /// <summary>How far around the two points a way is looked for, in turn (world units).</summary>
    private static readonly double[] Rooms = [1500, 5000, 12000];

    private List<Vec3>? WayWithin(Vec3 from, Vec3 to, double margin, double room)
    {
        double span = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) + 2 * room;
        double cell = Math.Max(Cell, span / MaxCells);
        // A coarse grid mustn't slip through a thin wall.
        double solidWithin = margin + (cell - Cell) * 0.5;
        double ox = Math.Min(from.X, to.X) - room, oy = Math.Min(from.Y, to.Y) - room;
        int w = (int)Math.Ceiling((Math.Abs(to.X - from.X) + 2 * room) / cell) + 1, h = (int)Math.Ceiling((Math.Abs(to.Y - from.Y) + 2 * room) / cell) + 1;
        var solid = new bool[w * h];
        foreach (var b in Around(ox, oy, ox + w * cell, oy + h * cell))
        {
            if (!InTheWay(b, from.Z, Away(b, from)))
                continue;
            var (minX, minY, maxX, maxY) = b.Box;
            int x0 = Math.Max(0, (int)((minX - solidWithin - ox) / cell)), x1 = Math.Min(w - 1, (int)((maxX + solidWithin - ox) / cell));
            int y0 = Math.Max(0, (int)((minY - solidWithin - oy) / cell)), y1 = Math.Min(h - 1, (int)((maxY + solidWithin - oy) / cell));
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    if (!solid[y * w + x] && b.Distance(ox + (x + 0.5) * cell, oy + (y + 0.5) * cell) < solidWithin)
                        solid[y * w + x] = true;
        }
        int Index(Vec3 p) => Math.Clamp((int)((p.Y - oy) / cell), 0, h - 1) * w + Math.Clamp((int)((p.X - ox) / cell), 0, w - 1);
        int start = Index(from), goal = Index(to);
        // Standing at an obstacle's edge: the cells around us are free to leave from.
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = start % w + dx, y = start / w + dy;
                if (x >= 0 && y >= 0 && x < w && y < h)
                    solid[y * w + x] = false;
            }
        if (solid[goal])
        {
            // The goal stands in something (a stall, a mob by a wall): the nearest free cell.
            int best = -1;
            double bestD = double.MaxValue;
            int gx = goal % w, gy = goal / w;
            for (int r = 1; r <= 15 && best < 0; r++)
                for (int y = Math.Max(0, gy - r); y <= Math.Min(h - 1, gy + r); y++)
                    for (int x = Math.Max(0, gx - r); x <= Math.Min(w - 1, gx + r); x++)
                        if (!solid[y * w + x] && (x - gx) * (x - gx) + (y - gy) * (y - gy) is int d && d < bestD)
                        {
                            bestD = d;
                            best = y * w + x;
                        }
            if (best < 0)
                return null;
            goal = best;
        }
        var cost = new float[w * h];
        Array.Fill(cost, float.MaxValue);
        var came = new int[w * h];
        var open = new PriorityQueue<int, float>();
        cost[start] = 0;
        open.Enqueue(start, 0);
        int gxx = goal % w, gyy = goal / w;
        float Guess(int i)
        {
            int dx = Math.Abs(i % w - gxx), dy = Math.Abs(i / w - gyy);
            return (float)(Math.Max(dx, dy) + 0.4142 * Math.Min(dx, dy));
        }
        bool found = false;
        while (open.TryDequeue(out int i, out _))
        {
            if (i == goal)
            {
                found = true;
                break;
            }
            int x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int n = ny * w + nx;
                    // Diagonals don't cut a corner.
                    if (solid[n] || dx != 0 && dy != 0 && (solid[y * w + nx] || solid[ny * w + x]))
                        continue;
                    float c = cost[i] + (dx != 0 && dy != 0 ? 1.4142f : 1f);
                    if (c >= cost[n])
                        continue;
                    cost[n] = c;
                    came[n] = i;
                    open.Enqueue(n, c + Guess(n));
                }
        }
        if (!found)
            return null;
        var cells = new List<int>();
        for (int i = goal; i != start; i = came[i])
            cells.Add(i);
        cells.Reverse();
        Vec3 At(int i) => new(ox + (i % w + 0.5) * cell, oy + (i / w + 0.5) * cell, to.Z);
        // Only the corners: from each point, on to the furthest one in a straight free line.
        var way = new List<Vec3>();
        var here = from;
        int k = 0;
        while (k < cells.Count)
        {
            int far = k;
            for (int j = cells.Count - 1; j > k; j--)
                if (GridClear(solid, w, cell, ox, oy, here, At(cells[j])))
                {
                    far = j;
                    break;
                }
            here = At(cells[far]);
            way.Add(here);
            k = far + 1;
        }
        if (goal == Index(to) && way.Count > 0)
            way[^1] = to;
        return way;
    }

    private static bool GridClear(bool[] solid, int w, double cell, double ox, double oy, Vec3 a, Vec3 b)
    {
        int h = solid.Length / w;
        int steps = Math.Max(1, (int)Math.Ceiling(a.DistanceXY(b) / (cell * 0.4)));
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            int x = (int)((a.X + (b.X - a.X) * t - ox) / cell), y = (int)((a.Y + (b.Y - a.Y) * t - oy) / cell);
            if (x < 0 || y < 0 || x >= w || y >= h || solid[y * w + x])
                return false;
        }
        return true;
    }

    /// <summary>How long the way is, walked from <paramref name="from"/>.</summary>
    public static double Length(Vec3 from, IReadOnlyList<Vec3> way)
    {
        double sum = 0;
        var here = from;
        foreach (var p in way)
        {
            sum += here.DistanceXY(p);
            here = p;
        }
        return sum;
    }

    /// <summary>Where to head now on <paramref name="way"/>: the furthest corner still in a straight free line.</summary>
    public Vec3 Aim(IReadOnlyList<Vec3> way, Vec3 me)
    {
        for (int i = way.Count - 1; i > 0; i--)
            if (LineClear(me, way[i]))
                return way[i];
        return way[0];
    }

    // ── Kept on disk: haritalar\[map].json, with a picture of the map (.html) ──

    public static string Folder => Path.Combine(ProfileStore.Directory, "haritalar");

    internal static string Stem(string folder, string map)
    {
        string safe = string.Concat(map.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(folder, safe.Length == 0 ? "harita" : safe);
    }

    private sealed class Saved
    {
        public string Map { get; set; } = "";
        public DateTime Read { get; set; }
        public List<MapBlock> Blocks { get; set; } = [];
    }

    public string ToJson() => JsonSerializer.Serialize(new Saved { Map = Map, Read = Read, Blocks = Blocks });

    /// <summary>Writes the map's file and its picture (with the obstacles the bot ran into, <paramref name="learned"/>); returns the picture's path.</summary>
    public string Save(string folder, IReadOnlyList<Vec3>? learned = null, Vec3? me = null)
    {
        Directory.CreateDirectory(folder);
        string stem = Stem(folder, Map);
        string json = ToJson();
        Changed = false;
        File.WriteAllText(stem + ".json", json, new UTF8Encoding(false));
        File.WriteAllText(stem + ".html", MapReport.Html(this, learned ?? [], me), new UTF8Encoding(false));
        return stem + ".html";
    }

    public static MapTerrain? Load(string folder, string map)
    {
        try
        {
            string file = Stem(folder, map) + ".json";
            if (File.Exists(file) && JsonSerializer.Deserialize<Saved>(File.ReadAllText(file)) is { } saved && saved.Map == map)
                return new MapTerrain(map, saved.Blocks, saved.Read);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>A new reading of the map; what was learned walking it (floors, bridges, gates) carries over.</summary>
    public static MapTerrain Renew(string map, List<MapBlock> read, MapTerrain? before, DateTime now)
    {
        if (before != null)
        {
            var passable = before.Blocks.Where(b => b.Passable).Select(Key).ToHashSet();
            foreach (var b in read)
                if (passable.Contains(Key(b)))
                    b.Passable = true;
        }
        return new MapTerrain(map, read, now) { Changed = true };
    }

    private static (string, int, int, int) Key(MapBlock b)
    {
        var (x, y) = b.Centre;
        return (b.Name, (int)Math.Round(x / 10), (int)Math.Round(y / 10), (int)Math.Round(b.Bottom / 10));
    }
}

/// <summary>Every map's obstacles, loaded from disk when first needed.</summary>
internal sealed class TerrainBook(string? folder = null)
{
    private readonly Dictionary<string, MapTerrain?> _maps = [];
    private readonly object _gate = new();

    public string Folder { get; } = folder ?? MapTerrain.Folder;

    public MapTerrain? Of(string map)
    {
        if (map.Length == 0)
            return null;
        lock (_gate)
        {
            if (!_maps.TryGetValue(map, out var terrain))
                _maps[map] = terrain = MapTerrain.Load(Folder, map);
            return terrain;
        }
    }

    public MapTerrain Update(string map, List<MapBlock> read)
    {
        var terrain = MapTerrain.Renew(map, read, Of(map), DateTime.Now);
        lock (_gate)
            _maps[map] = terrain;
        return terrain;
    }

    public IEnumerable<MapTerrain> Changed()
    {
        lock (_gate)
            return [.. _maps.Values.OfType<MapTerrain>().Where(t => t.Changed)];
    }
}
