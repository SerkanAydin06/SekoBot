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
    private const int MaxCells = 600, Bucket = 1000;
    /// <summary>A coarse grid cell this much steep (share of it) is not walked through.</summary>
    internal const double SteepCellShare = 0.3;

    private readonly Dictionary<(int, int), List<MapBlock>> _buckets = [];

    public MapTerrain(string map, List<MapBlock> blocks, DateTime read)
    {
        Map = map;
        Blocks = blocks;
        Read = read;
        foreach (var b in blocks)
        {
            // A tree once taken for passable (walked under its crown): its trunk is there all the same.
            if (b.IsTree && b.Passable)
            {
                b.Passable = false;
                Changed = true;
            }
            var (minX, minY, maxX, maxY) = b.Box;
            for (int x = (int)Math.Floor(minX / Bucket); x <= (int)Math.Floor(maxX / Bucket); x++)
                for (int y = (int)Math.Floor(minY / Bucket); y <= (int)Math.Floor(maxY / Bucket); y++)
                {
                    if (!_buckets.TryGetValue((x, y), out var list))
                        _buckets[(x, y)] = list = [];
                    list.Add(b);
                }
        }
        // What lies under a walkable deck (a bridge's beams over the river): walked over, not into.
        foreach (var b in blocks)
        {
            if (b.Passable)
                continue;
            var (minX, minY, maxX, maxY) = b.Box;
            double area = Math.Max(1, (maxX - minX) * (maxY - minY));
            foreach (var d in Near(minX, minY, maxX, maxY))
            {
                if (!d.Passable || d.Bottom < b.Top - UnderDeckSlack)
                    continue;
                var (dx0, dy0, dx1, dy1) = d.Box;
                double ox = Math.Min(maxX, dx1) - Math.Max(minX, dx0), oy = Math.Min(maxY, dy1) - Math.Max(minY, dy0);
                if (ox > 0 && oy > 0 && ox * oy >= area * 0.6)
                {
                    _underDeck.Add(b);
                    break;
                }
            }
        }
    }

    /// <summary>Solid blocks lying under a walkable deck (seen on c2: a bridge's beam, 15–318 high under its deck at
    /// 390–550, made every bridge "blocked" — the ground there is the river bed — and the ways went round the river).</summary>
    private readonly HashSet<MapBlock> _underDeck = [];
    /// <summary>A deck this much lower than the top of what is under it still covers it.</summary>
    internal const double UnderDeckSlack = 80;

    private IEnumerable<MapBlock> Near(double minX, double minY, double maxX, double maxY)
    {
        var seen = new HashSet<MapBlock>();
        for (int x = (int)Math.Floor(minX / Bucket); x <= (int)Math.Floor(maxX / Bucket); x++)
            for (int y = (int)Math.Floor(minY / Bucket); y <= (int)Math.Floor(maxY / Bucket); y++)
                if (_buckets.TryGetValue((x, y), out var list))
                    foreach (var b in list)
                        if (seen.Add(b))
                            yield return b;
    }

    public string Map { get; }
    public List<MapBlock> Blocks { get; }
    public DateTime Read { get; }
    public bool Changed { get; set; }

    /// <summary>Where the ground is too steep (hills, mountain sides), from the game's height grid; null: not known.</summary>
    public SlopeMap? Slopes { get; set; }

    /// <summary>The map's roads (off the M map); ways keep to them where they can. Null: not known.</summary>
    public RoadMap? Roads { get; set; }

    /// <summary>Walking off the roads costs this much more (a way along a road may be this much longer and is still taken).</summary>
    internal const float OffRoadCost = 10f;
    /// <summary>Ways longer than this (world units, 30 m) follow the roads when asked to.</summary>
    internal const double RoadsFrom = 3000;

    /// <summary>Whether the block stands in the way of a character whose position is at height <paramref name="z"/>,
    /// <paramref name="away"/> units from it.</summary>
    internal static bool InTheWay(MapBlock b, double z, double away)
    {
        if (b.Passable)
            return false;
        double feet = z - FeetBelow, slope = Math.Min(away * Slope, SlopeMost);
        return b.Top > feet + Step - slope && b.Bottom < feet + Head + slope;
    }

    /// <summary>
    /// Whether the block stands in the way of a character walking from <paramref name="from"/>. With the ground's
    /// heights known, also where the block stands: a tree's crown over the ground there, a floor down a slope
    /// isn't in the way (it only counts when in the way both from where we stand and on the ground under it).
    /// </summary>
    private bool InTheWayFrom(MapBlock b, Vec3 from)
    {
        // Under a bridge's deck, and we are above it: crossed on the deck.
        if (_underDeck.Contains(b) && from.Z > b.Top)
            return false;
        if (!InTheWay(b, from.Z, Away(b, from)))
            return false;
        var (x, y) = b.Centre;
        return Slopes?.Ground(x, y) is not double ground || InTheWay(b, ground + FeetBelow, GroundAway);
    }

    /// <summary>The ground's height under a block is known to about this (as if this far away: <see cref="Slope"/> per unit).</summary>
    internal const double GroundAway = 100;

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
            if (!b.Passable && !b.IsTree && b.Contains(me.X, me.Y) && me.Z - FeetBelow > b.Bottom - 100 && me.Z - FeetBelow < b.Top + 150)
            {
                b.Passable = true;
                Changed = true;
                found++;
            }
        return found;
    }

    /// <summary>Nothing (at our height) within <see cref="Margin"/> of the straight way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public bool LineClear(Vec3 from, Vec3 to, double margin = Margin, IReadOnlyList<Circle>? avoid = null)
    {
        if (avoid != null)
            foreach (var c in avoid)
                if (!c.Holds(to, margin) && (c.Holds(from, margin) ? c.Towards(from, to) : c.Crosses(from, to, margin)))
                    return false;
        if (Slopes is { } slopes && !slopes.LineClear(from.X, from.Y, to.X, to.Y))
            return false;
        double length = from.DistanceXY(to);
        var near = Around(Math.Min(from.X, to.X) - margin, Math.Min(from.Y, to.Y) - margin, Math.Max(from.X, to.X) + margin, Math.Max(from.Y, to.Y) + margin)
            .Where(b => InTheWayFrom(b, from) && !(b.Distance(from.X, from.Y) < margin * 0.6)).ToList();
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
    /// <param name="avoid">Round places kept away from as well (where the bot got stuck before, unreachable
    /// places, mobs on the way); one the character or the goal stands in doesn't count.</param>
    /// <param name="roads">A long way: along the roads (when known) even where the straight way is free.</param>
    public List<Vec3>? WayTo(Vec3 from, Vec3 to, double margin = Margin, double room = 12000, IReadOnlyList<Circle>? avoid = null, bool roads = false)
    {
        // One the character stands in still counts (it got stuck right there): not walked into again.
        var keep = avoid?.Where(c => !c.Holds(to, margin)).ToList();
        bool alongRoads = roads && Roads != null && from.DistanceXY(to) > RoadsFrom;
        if (!alongRoads && LineClear(from, to, margin, keep))
            return [to];
        if (alongRoads && RoadOnlyWay(from, to, margin, keep) is { } byRoad)
            return byRoad;
        // Looked for close around the two points first; a long wall needs more room to get round.
        foreach (double r in Rooms.Where(r => r <= room))
            if (WayWithin(from, to, margin, r, keep) is { } way)
                return way;
        return null;
    }

    /// <summary>The road is looked for this far around where a way starts and ends (world units, 80 m).</summary>
    internal const double RoadReach = 8000;

    /// <summary>
    /// From one place to another far off: only along the roads (the user: "bir alandan başka alana sadece yollardan;
    /// farm alanına en yakın yola gelince oradan geçiş; kısa yol diye atlamayacak" — seen: a shortcut over a cliff).
    /// To the nearest road, along it to where it comes nearest the goal, and from there to the goal. Mobs and the
    /// places the bot got stuck on the road are walked round on the road, never by leaving it. Null: no road near
    /// either end, the two not joined by road, or the road no help (the way to it and back longer than the trip).
    /// </summary>
    private List<Vec3>? RoadOnlyWay(Vec3 from, Vec3 to, double margin, IReadOnlyList<Circle>? avoid)
    {
        if (Roads is not { } roads)
            return null;
        // What stands on the road (walls, a bridge's railings) shuts it there: the way keeps to where one can walk.
        bool Blocked(double x, double y)
        {
            foreach (var b in Around(x - margin, y - margin, x + margin, y + margin))
                if (b.Distance(x, y) < margin && InTheWayFrom(b, from))
                    return true;
            return false;
        }
        var along = roads.WayAlong(from, to, RoadReach, avoid?.Select(c => (c.X, c.Y, c.R + margin)).ToList(), Blocked);
        if (along is not { Count: > 0 })
            return null;
        var onto = along[0];
        var off = along[^1];
        if (from.DistanceXY(onto) + off.DistanceXY(to) >= from.DistanceXY(to))
            return null;
        var way = new List<Vec3>();
        // Onto the road: round what stands between (short; the fields only here).
        if (from.DistanceXY(onto) > 150)
            way.AddRange(LineClear(from, onto, margin, avoid) ? [onto] : WayWithin(from, onto, margin, Rooms[0], avoid) ?? WayWithin(from, onto, margin, Rooms[1], avoid) ?? [onto]);
        way.AddRange(along.Skip(1));
        if (way.Count == 0)
            way.Add(onto);
        // Off the road to the goal.
        if (off.DistanceXY(to) > 150)
            way.AddRange(LineClear(off, to, margin, avoid) ? [to] : WayWithin(off, to, margin, Rooms[0], avoid) ?? WayWithin(off, to, margin, Rooms[1], avoid) ?? [to]);
        else
            way[^1] = to;
        return way;
    }

    /// <summary>A round place to keep away from (world units).</summary>
    internal readonly record struct Circle(double X, double Y, double R)
    {
        public bool Holds(Vec3 p, double margin) => (p.X - X) * (p.X - X) + (p.Y - Y) * (p.Y - Y) < (R + margin) * (R + margin);

        /// <summary>From <paramref name="a"/> (in it) towards <paramref name="b"/> leads further in: more towards its middle than sideways.</summary>
        public bool Towards(Vec3 a, Vec3 b)
        {
            double mx = X - a.X, my = Y - a.Y, dx = b.X - a.X, dy = b.Y - a.Y;
            double m = Math.Sqrt(mx * mx + my * my), d = Math.Sqrt(dx * dx + dy * dy);
            return m > 1 && d > 1 && (mx * dx + my * dy) / (m * d) > 0.3;
        }

        /// <summary>The straight way from a to b passes within R + margin of the middle.</summary>
        public bool Crosses(Vec3 a, Vec3 b, double margin)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
            double t = len <= 0 ? 0 : Math.Clamp(((X - a.X) * dx + (Y - a.Y) * dy) / len, 0, 1);
            double px = a.X + dx * t - X, py = a.Y + dy * t - Y;
            return px * px + py * py < (R + margin) * (R + margin);
        }
    }

    /// <summary>How far around the two points a way is looked for, in turn (world units).</summary>
    private static readonly double[] Rooms = [1500, 5000, 12000];

    private List<Vec3>? WayWithin(Vec3 from, Vec3 to, double margin, double room, IReadOnlyList<Circle>? avoid = null)
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
            if (!InTheWayFrom(b, from))
                continue;
            var (minX, minY, maxX, maxY) = b.Box;
            int x0 = Math.Max(0, (int)((minX - solidWithin - ox) / cell)), x1 = Math.Min(w - 1, (int)((maxX + solidWithin - ox) / cell));
            int y0 = Math.Max(0, (int)((minY - solidWithin - oy) / cell)), y1 = Math.Min(h - 1, (int)((maxY + solidWithin - oy) / cell));
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    if (!solid[y * w + x] && b.Distance(ox + (x + 0.5) * cell, oy + (y + 0.5) * cell) < solidWithin)
                        solid[y * w + x] = true;
        }
        if (Slopes is { } slopes)
        {
            double share = cell <= slopes.Cell * 1.5 ? 0.5 : SteepCellShare;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (!solid[y * w + x] && slopes.SteepIn(ox + (x + 0.5) * cell, oy + (y + 0.5) * cell, cell) >= share)
                        solid[y * w + x] = true;
        }
        // The mountains drawn on the map: never walked into (kept off only away from where the way starts and ends:
        // a character standing at a rock's foot, or a goal beside one, still gets out / there).
        if (Roads is { HasMountains: true } mountains)
        {
            double share = cell <= RoadMap.Cell * 1.5 ? 0.5 : 0.6;
            double clear = TerrainBook.MountainClear + cell;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double px = ox + (x + 0.5) * cell, py = oy + (y + 0.5) * cell;
                    if (solid[y * w + x] || Math.Abs(px - from.X) + Math.Abs(py - from.Y) < clear || Math.Abs(px - to.X) + Math.Abs(py - to.Y) < clear)
                        continue;
                    if (mountains.MountainShareIn(px, py, cell) >= share)
                        solid[y * w + x] = true;
                }
        }
        foreach (var c in avoid ?? [])
        {
            double r = c.R + solidWithin;
            int x0 = Math.Max(0, (int)((c.X - r - ox) / cell)), x1 = Math.Min(w - 1, (int)((c.X + r - ox) / cell));
            int y0 = Math.Max(0, (int)((c.Y - r - oy) / cell)), y1 = Math.Min(h - 1, (int)((c.Y + r - oy) / cell));
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    double cx = ox + (x + 0.5) * cell - c.X, cy = oy + (y + 0.5) * cell - c.Y;
                    if (cx * cx + cy * cy < r * r)
                        solid[y * w + x] = true;
                }
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
        // Off the roads a step costs more: ways go along the roads where they can.
        float[]? stepCost = null;
        if (Roads is { } roads)
        {
            stepCost = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    stepCost[y * w + x] = roads.CostIn(ox + (x + 0.5) * cell, oy + (y + 0.5) * cell, cell) is > 0 and var onRoad ? onRoad : OffRoadCost;
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
                    float c = cost[i] + (dx != 0 && dy != 0 ? 1.4142f : 1f) * (stepCost?[n] ?? 1f);
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
        // A shortcut between two corners mustn't leave the road the way follows (cutting across the fields again).
        bool KeepsToRoad(int k, int j)
        {
            if (stepCost == null || !cells.Skip(k).Take(j - k + 1).Any(c => stepCost[c] < OffRoadCost))
                return true;
            // Kept as near the road's middle as the way itself is there: each point of the shortcut no worse than the
            // way's step at the same share of the stretch (a shortcut across a bend would reach the edge or the fields).
            var a = k == 0 ? from : At(cells[k - 1]);
            var b = At(cells[j]);
            int steps = Math.Max(1, (int)Math.Ceiling(a.DistanceXY(b) / (cell * 0.5)));
            for (int i = 1; i < steps; i++)
            {
                double t = (double)i / steps;
                int x = (int)((a.X + (b.X - a.X) * t - ox) / cell), y = (int)((a.Y + (b.Y - a.Y) * t - oy) / cell);
                if (x < 0 || y < 0 || x >= w || y >= h)
                    continue;
                float there = stepCost[cells[Math.Clamp(k + (int)Math.Round(t * (j - k)), k, j)]];
                if (stepCost[y * w + x] > Math.Max(RoadMap.MiddleCost, there) + 0.01f)
                    return false;
            }
            return true;
        }
        // Only the corners: from each point, on to the furthest one in a straight free line.
        var way = new List<Vec3>();
        var here = from;
        int k = 0;
        while (k < cells.Count)
        {
            int far = k;
            for (int j = cells.Count - 1; j > k; j--)
                if (GridClear(solid, w, cell, ox, oy, here, At(cells[j])) && KeepsToRoad(k, j))
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

    /// <summary>The corner ending the stretch of <paramref name="way"/> closest to <paramref name="me"/> (the first one: from wherever we are).</summary>
    internal static int NextCorner(IReadOnlyList<Vec3> way, Vec3 me)
    {
        int best = 0;
        double bestD = me.DistanceXY(way[0]);
        for (int i = 1; i < way.Count; i++)
        {
            var a = way[i - 1];
            var b = way[i];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
            double t = len <= 0 ? 0 : Math.Clamp(((me.X - a.X) * dx + (me.Y - a.Y) * dy) / len, 0, 1);
            double px = a.X + dx * t - me.X, py = a.Y + dy * t - me.Y, d = Math.Sqrt(px * px + py * py);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
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
        // Along the roads: corner by corner (a shortcut to a further corner would cut across the fields again) — the
        // corner after the stretch we are on (seen: always the first one, the bot went back and forth past it).
        if (Roads != null && way.Count > 1)
        {
            int at = NextCorner(way, me);
            return me.DistanceXY(way[at]) < 150 && at + 1 < way.Count ? way[at + 1] : way[at];
        }
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
    private readonly Dictionary<string, SlopeMap> _slopes = [];
    private readonly Dictionary<string, RoadMap?> _roads = [];
    private readonly object _gate = new();

    public string Folder { get; } = folder ?? MapTerrain.Folder;

    public MapTerrain? Of(string map)
    {
        if (map.Length == 0)
            return null;
        lock (_gate)
        {
            if (!_maps.TryGetValue(map, out var terrain))
            {
                _maps[map] = terrain = MapTerrain.Load(Folder, map);
                if (terrain != null && _slopes.TryGetValue(map, out var s))
                    terrain.Slopes = s;
                if (terrain != null)
                    terrain.Roads = RoadsOfLocked(map);
            }
            return terrain;
        }
    }

    public MapTerrain Update(string map, List<MapBlock> read)
    {
        var terrain = MapTerrain.Renew(map, read, Of(map), DateTime.Now);
        lock (_gate)
        {
            if (_slopes.TryGetValue(map, out var s))
                terrain.Slopes = s;
            terrain.Roads = RoadsOfLocked(map);
            _maps[map] = terrain;
        }
        return terrain;
    }

    /// <summary>The map's steep ground (read from the game's height grid).</summary>
    public void SetSlopes(SlopeMap slopes)
    {
        lock (_gate)
        {
            _slopes[slopes.Map] = slopes;
            if (_maps.TryGetValue(slopes.Map, out var t) && t != null)
                t.Slopes = slopes;
        }
    }

    private readonly Dictionary<string, DateTime> _roadsLookedAt = [];
    private readonly Dictionary<string, DateTime> _roadsFileTime = [];

    /// <summary>When the map's road files were last written (the roads and the removed ones).</summary>
    private DateTime RoadFilesTime(string map)
    {
        try
        {
            var a = File.GetLastWriteTimeUtc(RoadMap.FileOf(Folder, map));
            var b = File.GetLastWriteTimeUtc(RoadMap.RemovedFileOf(Folder, map));
            return a > b ? a : b;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    /// <summary>The map's roads; not there yet: looked for on disk again every minute (another SekoBot may read them).</summary>
    private RoadMap? RoadsOfLocked(string map)
    {
        bool fresh = _roadsLookedAt.TryGetValue(map, out var looked) && DateTime.Now - looked < TimeSpan.FromMinutes(1);
        if (_roads.TryGetValue(map, out var roads) && roads != null && (fresh || RoadFilesTime(map) == _roadsFileTime.GetValueOrDefault(map)))
        {
            if (!fresh)
                _roadsLookedAt[map] = DateTime.Now;
            return roads;
        }
        if (fresh)
            return roads;
        // Not read yet, or its files changed (read again by another PARDUS, a road removed): loaded again.
        _roadsLookedAt[map] = DateTime.Now;
        _roadsFileTime[map] = RoadFilesTime(map);
        _roads[map] = roads = RoadMap.Load(Folder, map);
        if (roads != null && _maps.TryGetValue(map, out var t) && t != null)
            t.Roads = roads;
        return roads;
    }

    /// <summary>The map's roads (loaded from disk once; null: not read yet).</summary>
    public RoadMap? RoadsOf(string map)
    {
        lock (_gate)
            return RoadsOfLocked(map);
    }

    /// <summary>The map's roads, just read off its M map: kept on disk and used from now on.</summary>
    /// <summary>Round where a way starts and ends the mountains don't count (world units): out of a rock's foot.</summary>
    internal const double MountainClear = 600;

    public void SetRoads(RoadMap roads)
    {
        roads.ApplyRemoved(Folder);
        roads.ReadMountains(Folder);
        try { roads.Save(Folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        lock (_gate)
        {
            _roads[roads.Map] = roads;
            if (_maps.TryGetValue(roads.Map, out var t) && t != null)
                t.Roads = roads;
        }
    }

    public SlopeMap? SlopesOf(string map)
    {
        lock (_gate)
            return _slopes.TryGetValue(map, out var s) ? s : null;
    }

    public IEnumerable<MapTerrain> Changed()
    {
        lock (_gate)
            return [.. _maps.Values.OfType<MapTerrain>().Where(t => t.Changed)];
    }
}
