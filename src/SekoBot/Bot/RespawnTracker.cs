using System.Text.Json;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Measures how long mobs take to come back, from memory: a mob that dies close by (it is gone
/// from the world) leaves its spot; a new mob (a number never seen before) showing up near that
/// spot, well inside the ~40 m memory shows, is its replacement. Only counted when the character
/// stayed near the spot the whole time (else it may have come back unseen long before). Kept by
/// the dead mob's level (higher mobs take longer, the user says) and saved between sessions.
/// </summary>
internal sealed class RespawnTracker
{
    /// <summary>Deaths and new mobs this close to the character count (well inside what memory shows).</summary>
    public const double WatchRange = 2500;
    /// <summary>A new mob this close to where one died is its replacement (spawns scatter a little).</summary>
    public const double SpotRange = 1200;
    /// <summary>The character further than this from the spot meanwhile: the wait isn't known.</summary>
    public const double AwayRange = 3200;
    public const double MinSeconds = 5, MaxSeconds = 900;
    private const int KeepSamples = 30;

    private sealed record Death(int Level, Vec3 At, TimeSpan When);

    private readonly Dictionary<int, (WorldMob Mob, TimeSpan Seen)> _alive = [];
    private readonly HashSet<int> _known = [];
    private readonly List<Death> _deaths = [];
    private Dictionary<int, List<double>> _samples;
    /// <summary>Measured since the last share (added to the file, which every SekoBot fills).</summary>
    private readonly List<(int Level, double Seconds)> _news = [];
    private readonly object _gate = new();
    private TimeSpan _started = TimeSpan.MinValue;

    public RespawnTracker(Dictionary<int, List<double>>? samples = null) => _samples = samples ?? [];

    /// <summary>Raised when a wait was measured: (level, seconds).</summary>
    public event Action<int, double>? Measured;

    public IReadOnlyDictionary<int, List<double>> Samples => _samples;

    public void Update(WorldSnapshot world, TimeSpan now)
    {
        if (_started == TimeSpan.MinValue)
            _started = now;
        var me = world.Player;
        var present = new HashSet<int>();
        foreach (var mob in world.Mobs)
        {
            if (mob.IsMetin)
                continue;
            present.Add(mob.Id);
            bool fresh = _known.Add(mob.Id);
            // The first look around (and mobs walking into view at the edge) aren't spawns.
            if (fresh && now - _started > TimeSpan.FromSeconds(10) && mob.Position.DistanceXY(me) <= WatchRange)
                Spawned(mob, now);
            _alive[mob.Id] = (mob, now);
        }
        foreach (var (id, (mob, _)) in _alive.ToList())
        {
            if (present.Contains(id))
                continue;
            _alive.Remove(id);
            if (mob.Position.DistanceXY(me) <= WatchRange)
                _deaths.Add(new Death(mob.Level, mob.Position, now));
        }
        // Walked away from a spot: its wait can't be known any more.
        _deaths.RemoveAll(d => d.At.DistanceXY(me) > AwayRange || now - d.When > TimeSpan.FromSeconds(MaxSeconds));
        if (_known.Count > 20000)
            _known.IntersectWith(present);
    }

    private void Spawned(WorldMob mob, TimeSpan now)
    {
        var death = _deaths.Where(d => d.At.DistanceXY(mob.Position) <= SpotRange).MinBy(d => d.When);
        if (death == null)
            return;
        _deaths.Remove(death);
        double seconds = (now - death.When).TotalSeconds;
        if (seconds < MinSeconds)
            return;
        lock (_gate)
        {
            Add(_samples, death.Level, Math.Round(seconds, 1));
            _news.Add((death.Level, Math.Round(seconds, 1)));
        }
        Measured?.Invoke(death.Level, seconds);
    }

    private static void Add(Dictionary<int, List<double>> samples, int level, double seconds)
    {
        if (!samples.TryGetValue(level, out var list))
            samples[level] = list = [];
        list.Add(seconds);
        if (list.Count > KeepSamples)
            list.RemoveAt(0);
    }

    /// <summary>The usual wait for mobs of a level (the median), from that level or the nearest
    /// measured one within 3 levels; null when nothing near was measured.</summary>
    public double? Seconds(int level)
    {
        Dictionary<int, List<double>> samples;
        lock (_gate)
            samples = _samples;
        var near = samples.Where(s => s.Value.Count >= 2 && Math.Abs(s.Key - level) <= 3)
            .OrderBy(s => Math.Abs(s.Key - level)).ThenByDescending(s => s.Value.Count).FirstOrDefault();
        if (near.Value is not { } list)
            return null;
        var sorted = list.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    // ── Saved between sessions ──

    private static string FilePath => Path.Combine(ProfileStore.Directory, "dogma-sureleri.json");

    public static RespawnTracker Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return new RespawnTracker(JsonSerializer.Deserialize<Dictionary<int, List<double>>>(File.ReadAllText(FilePath)));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new RespawnTracker();
    }

    /// <summary>Shares with the other SekoBots: our measurements go into the file, theirs come back.</summary>
    public void Save()
    {
        List<(int Level, double Seconds)> news;
        lock (_gate)
        {
            news = [.. _news];
            _news.Clear();
        }
        var all = SharedFile.Update<Dictionary<int, List<double>>>(FilePath, d => news.ForEach(n => Add(d, n.Level, n.Seconds)));
        lock (_gate)
        {
            if (all == null)
            {
                _news.InsertRange(0, news);
                return;
            }
            foreach (var n in _news)
                Add(all, n.Level, n.Seconds);
            _samples = all;
        }
    }
}
