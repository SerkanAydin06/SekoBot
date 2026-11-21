using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Where the market NPCs stand on each map (silah 9001, zırh 9002, genel market 9003, …), learned by
/// passing near them: the game shows an NPC only within ~40 m and its maps hold no NPC places. Kept
/// in npc-yerleri.json, shared by every SekoBot, so one that once passed a shop knows it for all.
/// Selling can only be done next to such an NPC (the game shows "Sat" only there).
/// </summary>
internal sealed class NpcBook
{
    public sealed class Place
    {
        public int Vnum { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public DateTime Seen { get; set; }
    }

    /// <summary>The market NPCs (the game's npc list: arms, defence, goods…) and their Turkish names.</summary>
    public static readonly IReadOnlyDictionary<int, string> Shops = new Dictionary<int, string>
    {
        [9001] = "Silah satıcısı", [9002] = "Zırh satıcısı", [9003] = "Market", [9007] = "Silah satıcısı",
        [9008] = "Zırh satıcısı", [9010] = "Market", [9014] = "Market",
    };

    public static bool IsShop(int vnum) => Shops.ContainsKey(vnum);

    public static string FilePath => Path.Combine(ProfileStore.Directory, "npc-yerleri.json");
    public static NpcBook Shared => _shared.Value;
    private static readonly Lazy<NpcBook> _shared = new(() => Load(FilePath));

    private Dictionary<string, List<Place>> _maps;
    private readonly object _gate = new();
    private readonly List<(string Map, Place Place)> _news = [];
    /// <summary>Places found wrong (no such NPC there), to take out of the shared file too.</summary>
    private readonly List<(string Map, int Vnum, double X, double Y)> _gone = [];

    /// <summary>No such NPC stands there (the bot went and found none): forgotten, for every SekoBot.</summary>
    public void Forget(string map, int vnum, double x, double y)
    {
        lock (_gate)
        {
            Remove(_maps, map, vnum, x, y);
            _gone.Add((map, vnum, x, y));
            _news.RemoveAll(n => n.Map == map && n.Place.Vnum == vnum && Math.Abs(n.Place.X - x) < 500 && Math.Abs(n.Place.Y - y) < 500);
        }
    }

    private static void Remove(Dictionary<string, List<Place>> maps, string map, int vnum, double x, double y)
    {
        if (maps.TryGetValue(map, out var list))
            list.RemoveAll(q => q.Vnum == vnum && Math.Abs(q.X - x) < 500 && Math.Abs(q.Y - y) < 500);
    }

    public NpcBook(Dictionary<string, List<Place>>? maps = null) => _maps = maps ?? [];

    /// <summary>A market NPC seen: kept (or its place updated). True when it wasn't known yet.</summary>
    public bool Saw(string map, int vnum, Vec3 at, DateTime now)
    {
        if (map.Length == 0 || !IsShop(vnum))
            return false;
        var place = new Place { Vnum = vnum, X = at.X, Y = at.Y, Z = at.Z, Seen = now };
        lock (_gate)
        {
            bool known = Has(_maps, map, place);
            Add(_maps, map, place);
            if (!known)
                _news.Add((map, place));
            return !known;
        }
    }

    private static bool Has(Dictionary<string, List<Place>> maps, string map, Place p) =>
        maps.TryGetValue(map, out var list) && list.Any(q => q.Vnum == p.Vnum && Math.Abs(q.X - p.X) < 500 && Math.Abs(q.Y - p.Y) < 500);

    private static void Add(Dictionary<string, List<Place>> maps, string map, Place p)
    {
        if (!maps.TryGetValue(map, out var list))
            maps[map] = list = [];
        var same = list.FirstOrDefault(q => q.Vnum == p.Vnum && Math.Abs(q.X - p.X) < 500 && Math.Abs(q.Y - p.Y) < 500);
        if (same == null)
            list.Add(p);
        else
        {
            same.X = p.X;
            same.Y = p.Y;
            same.Z = p.Z;
            same.Seen = p.Seen;
        }
    }

    /// <summary>The market NPCs known on a map, the nearest to <paramref name="from"/> first.</summary>
    public List<Place> On(string map, Vec3? from = null)
    {
        lock (_gate)
            return _maps.TryGetValue(map, out var list)
                ? [.. list.OrderBy(p => from is Vec3 f ? (p.X - f.X) * (p.X - f.X) + (p.Y - f.Y) * (p.Y - f.Y) : 0)]
                : [];
    }

    public static NpcBook Load(string path)
    {
        try
        {
            if (File.Exists(path) && System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<Place>>>(File.ReadAllText(path)) is { } maps)
                return new NpcBook(maps);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        return new NpcBook();
    }

    /// <summary>Shares: this SekoBot's new NPCs go into the file, the others' come back.</summary>
    public void Save(string? path = null)
    {
        List<(string Map, Place Place)> news;
        List<(string Map, int Vnum, double X, double Y)> gone;
        lock (_gate)
        {
            news = [.. _news];
            _news.Clear();
            gone = [.. _gone];
            _gone.Clear();
        }
        var all = SharedFile.Update<Dictionary<string, List<Place>>>(path ?? FilePath, d =>
        {
            gone.ForEach(g => Remove(d, g.Map, g.Vnum, g.X, g.Y));
            news.ForEach(n => Add(d, n.Map, n.Place));
        });
        lock (_gate)
        {
            if (all == null)
            {
                _news.InsertRange(0, news);
                _gone.InsertRange(0, gone);
                return;
            }
            foreach (var n in _news)
                Add(all, n.Map, n.Place);
            _maps = all;
        }
    }
}
