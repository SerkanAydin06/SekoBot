using System.Text.Json;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>What one kind of mob was seen to drop on one map.</summary>
internal sealed class MobDrops
{
    public int Kills { get; set; }
    public long Yang { get; set; }
    /// <summary>Item number → how many times it dropped.</summary>
    public Dictionary<int, int> Items { get; set; } = [];
}

/// <summary>
/// Learns what mobs drop by watching (the game's full drop lists stay on the server): a mob that
/// dies close to the character leaves its spot; items showing up on that spot in the next seconds
/// are its drops. Counted per map and kind of mob, saved between sessions — over time this gives
/// "Siyah Ayı: 200 kills, Tahta Bilezik ×3, Beceri Kitabı ×1". Pure logic apart from the file.
/// </summary>
internal sealed class DropJournal
{
    public const double DeathRange = 800;      // only mobs dying this close to us (our kills)
    public const double SpotRange = 350;       // drops this close to where it died are its
    public static readonly TimeSpan DropDelay = TimeSpan.FromSeconds(4);

    private sealed record Death(int Vnum, Vec3 At, TimeSpan When);

    private readonly Dictionary<int, (int Vnum, Vec3 At)> _alive = [];
    private readonly HashSet<long> _seenDrops = [];
    private readonly List<Death> _deaths = [];
    private bool _started;

    /// <summary>Map → mob number → drops.</summary>
    public Dictionary<string, Dictionary<int, MobDrops>> Maps { get; private set; } = [];

    public bool Changed { get; set; }

    public MobDrops? For(string map, int mob) =>
        Maps.TryGetValue(map, out var mobs) && mobs.TryGetValue(mob, out var drops) ? drops : null;

    public void Update(WorldSnapshot world, TimeSpan now)
    {
        var me = world.Player;
        var present = new HashSet<int>();
        foreach (var mob in world.Mobs)
        {
            present.Add(mob.Id);
            _alive[mob.Id] = (mob.Vnum, mob.Position);
        }
        foreach (var (id, (vnum, at)) in _alive.ToList())
        {
            if (present.Contains(id))
                continue;
            _alive.Remove(id);
            if (vnum > 0 && at.DistanceXY(me) <= DeathRange)
            {
                _deaths.Add(new Death(vnum, at, now));
                Entry(world.MapName, vnum).Kills++;
                Changed = true;
            }
        }
        _deaths.RemoveAll(d => now - d.When > DropDelay);

        foreach (var drop in world.Drops ?? [])
        {
            if (!_seenDrops.Add(drop.Id) || !_started || drop.Vnum <= 0)
                continue;
            var death = _deaths.Where(d => d.At.DistanceXY(drop.Position) <= SpotRange)
                .MinBy(d => d.At.DistanceXY(drop.Position));
            if (death == null)
                continue;
            var entry = Entry(world.MapName, death.Vnum);
            if (drop.Vnum == 1)
                entry.Yang += drop.Count;
            else
                entry.Items[drop.Vnum] = entry.Items.GetValueOrDefault(drop.Vnum) + 1;
            Changed = true;
        }
        // Items already lying around when the bot started aren't anyone's drops.
        _started = true;
        if (_seenDrops.Count > 5000)
            _seenDrops.Clear();
    }

    private MobDrops Entry(string map, int mob)
    {
        if (!Maps.TryGetValue(map, out var mobs))
            Maps[map] = mobs = [];
        if (!mobs.TryGetValue(mob, out var drops))
            mobs[mob] = drops = new MobDrops();
        return drops;
    }

    /// <summary>"Kaplan Postu ×12 (%6), Tahta Bilezik ×1" — most first.</summary>
    public static string Describe(MobDrops drops, int take = 4) =>
        drops.Items.Count == 0 ? "—" : string.Join(", ", drops.Items.OrderByDescending(i => i.Value).Take(take)
            .Select(i => $"{ItemBook.NameOf(i.Key)} ×{i.Value}" + (drops.Kills >= 20 ? $" (%{100.0 * i.Value / drops.Kills:0.#})" : "")));

    // ── Saved between sessions ──

    public static DropJournal Load(string path)
    {
        var journal = new DropJournal();
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, MobDrops>>>(File.ReadAllText(path)) is { } maps)
                journal.Maps = maps;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return journal;
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Maps, new JsonSerializerOptions { WriteIndented = true }));
            Changed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
