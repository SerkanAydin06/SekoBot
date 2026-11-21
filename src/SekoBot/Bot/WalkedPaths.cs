using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// The ways the characters really walked, per map (also while the bot is stopped: the user walking
/// a character teaches it too), shared by every SekoBot in yuruyus-yollari.json. The map's obstacles
/// don't hold the ground itself (a hill, a steep slope up to the town), so a straight route can lead
/// into a slope; a way once walked is known to lead through. The map is cut in cells of
/// <see cref="Cell"/>; two cells walked one after the other are linked. <see cref="Way"/> finds the
/// shortest chain of links between two places.
/// </summary>
internal sealed class WalkedPaths
{
    /// <summary>The cell size (world units, 2 m).</summary>
    public const double Cell = 200;
    /// <summary>A step longer than this between two reads isn't walked (a teleport, a load).</summary>
    public const double LongestStep = 700;
    /// <summary>A walked way is joined when it passes this close to the start and the goal.</summary>
    public const double JoinWithin = 1500;

    private Dictionary<string, HashSet<string>> _maps;
    private readonly object _gate = new();
    private readonly List<(string Map, string Link)> _news = [];
    private string _lastMap = "";
    private (int X, int Y)? _lastCell;
    private Vec3? _lastAt;

    public WalkedPaths(Dictionary<string, HashSet<string>>? maps = null) => _maps = maps ?? [];

    public static string FilePath => Path.Combine(ProfileStore.Directory, "yuruyus-yollari.json");

    private static (int X, int Y) CellOf(double x, double y) => ((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell));
    private static string Key((int X, int Y) a, (int X, int Y) b)
    {
        var (p, q) = a.X < b.X || a.X == b.X && a.Y <= b.Y ? (a, b) : (b, a);
        return $"{p.X},{p.Y},{q.X},{q.Y}";
    }

    /// <summary>The character stands at <paramref name="at"/> now (read about every half second).</summary>
    public void Saw(string map, Vec3 at)
    {
        if (map.Length == 0)
            return;
        var cell = CellOf(at.X, at.Y);
        lock (_gate)
        {
            if (map == _lastMap && _lastCell is { } before && _lastAt is Vec3 was && before != cell && was.DistanceXY(at) <= LongestStep)
            {
                string link = Key(before, cell);
                if (!_maps.TryGetValue(map, out var links))
                    _maps[map] = links = [];
                if (links.Add(link))
                    _news.Add((map, link));
            }
            if (map != _lastMap || _lastCell != cell)
            {
                _lastMap = map;
                _lastCell = cell;
            }
            _lastAt = at;
        }
    }

    /// <summary>Every walked link on the map: the two cells' middles (world units).</summary>
    public List<(double AX, double AY, double BX, double BY)> Links(string map)
    {
        lock (_gate)
        {
            if (!_maps.TryGetValue(map, out var links))
                return [];
            var all = new List<(double, double, double, double)>(links.Count);
            foreach (string link in links)
            {
                var p = link.Split(',');
                if (p.Length == 4 && int.TryParse(p[0], out int a) && int.TryParse(p[1], out int b) && int.TryParse(p[2], out int c) && int.TryParse(p[3], out int d))
                    all.Add(((a + 0.5) * Cell, (b + 0.5) * Cell, (c + 0.5) * Cell, (d + 0.5) * Cell));
            }
            return all;
        }
    }

    public int Count(string map)
    {
        lock (_gate)
            return _maps.TryGetValue(map, out var l) ? l.Count : 0;
    }

    /// <summary>
    /// A walked way from <paramref name="from"/> to <paramref name="to"/>: the cell middles to walk through,
    /// from the walked cell nearest the start (within <see cref="JoinWithin"/>) to the one nearest the goal.
    /// Null when no walked way joins them.
    /// </summary>
    public List<Vec3>? Way(string map, Vec3 from, Vec3 to, IReadOnlyList<MapTerrain.Circle>? avoid = null)
    {
        // Cells where the character got stuck on this trip (something new stands there: a stall…) are left out,
        // otherwise the same way is taken again and again into the same spot (seen: 60 times a trip, all night).
        bool Blocked(int x, int y) => avoid is { Count: > 0 } && avoid.Any(c =>
            Math.Abs((x + 0.5) * Cell - c.X) < c.R + Cell / 2 && Math.Abs((y + 0.5) * Cell - c.Y) < c.R + Cell / 2);
        Dictionary<(int, int), List<(int, int)>> graph = [];
        lock (_gate)
        {
            if (!_maps.TryGetValue(map, out var links) || links.Count == 0)
                return null;
            foreach (var link in links)
            {
                var n = link.Split(',');
                if (n.Length != 4 || !int.TryParse(n[0], out int ax) || !int.TryParse(n[1], out int ay) || !int.TryParse(n[2], out int bx) || !int.TryParse(n[3], out int by))
                    continue;
                if (Blocked(ax, ay) || Blocked(bx, by))
                    continue;
                (graph.TryGetValue((ax, ay), out var la) ? la : graph[(ax, ay)] = []).Add((bx, by));
                (graph.TryGetValue((bx, by), out var lb) ? lb : graph[(bx, by)] = []).Add((ax, ay));
            }
        }
        Vec3 Mid((int X, int Y) c, double z) => new((c.X + 0.5) * Cell, (c.Y + 0.5) * Cell, z);
        double D((int X, int Y) c, Vec3 p) => Mid(c, 0).DistanceXY(p);
        if (graph.Count == 0)
            return null;
        // Joined near both ends: the goal end chosen among the cells reachable from the start end.
        var start = graph.Keys.MinBy(c => D(c, from));
        if (D(start, from) > JoinWithin)
            return null;
        var cost = new Dictionary<(int, int), double> { [start] = 0 };
        var came = new Dictionary<(int, int), (int, int)>();
        var open = new PriorityQueue<(int, int), double>();
        open.Enqueue(start, 0);
        while (open.TryDequeue(out var c, out double cc))
        {
            if (cc > cost[c])
                continue;
            foreach (var n in graph[c])
            {
                double nc = cost[c] + Mid(c, 0).DistanceXY(Mid(n, 0));
                if (cost.TryGetValue(n, out double old) && old <= nc)
                    continue;
                cost[n] = nc;
                came[n] = c;
                open.Enqueue(n, nc);
            }
        }
        var goal = cost.Keys.MinBy(c => D(c, to));
        if (D(goal, to) > JoinWithin || goal == start && D(start, to) > Cell * 3)
            return null;
        var cells = new List<(int, int)>();
        for (var c = goal; ; c = came[c])
        {
            cells.Add(c);
            if (c == start)
                break;
        }
        cells.Reverse();
        var way = cells.Select(c => Mid(c, to.Z)).ToList();
        way.Add(to);
        return way;
    }

    public static WalkedPaths Load(string? path = null)
    {
        try
        {
            string file = path ?? FilePath;
            if (File.Exists(file) && System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(File.ReadAllText(file)) is { } maps)
                return new WalkedPaths(maps);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        return new WalkedPaths();
    }

    /// <summary>Shares: this SekoBot's new links go into the file, the others' come back.</summary>
    public void Save(string? path = null)
    {
        List<(string Map, string Link)> news;
        lock (_gate)
        {
            news = [.. _news];
            _news.Clear();
        }
        var all = SharedFile.Update<Dictionary<string, HashSet<string>>>(path ?? FilePath, d =>
        {
            foreach (var (map, link) in news)
                (d.TryGetValue(map, out var l) ? l : d[map] = []).Add(link);
        });
        lock (_gate)
        {
            if (all == null)
            {
                _news.InsertRange(0, news);
                return;
            }
            foreach (var (map, link) in _news)
                (all.TryGetValue(map, out var l) ? l : all[map] = []).Add(link);
            _maps = all;
        }
    }
}
