using System.Text.Json;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// The trees, rocks and walls the bot ran into, per map, kept between sessions. The game's
/// collision can't be read from memory, so every time the character is held up by something (keys
/// held, not moving) the spot right in front of it is noted. A spot run into twice is an obstacle
/// for good: the way to a mob through it is not taken and walking bends around it. Walking freely
/// over a spot later (a mob or a player stood there, not a tree) wears it off again.
/// Shared by every SekoBot (one per game): each one's news goes into the file and it takes the
/// others' (<see cref="Save"/>, every half minute), so a tree one ran into is known to all.
/// </summary>
internal sealed class ObstacleMap
{
    /// <summary>One place something stood in the way (world units, the game's X/Y).</summary>
    public sealed class Spot
    {
        public double X { get; set; }
        public double Y { get; set; }
        public int Hits { get; set; }
        public DateTime Last { get; set; }
    }

    /// <summary>Run into this often: an obstacle for good (once may have been a mob or a player).</summary>
    public const int Sure = 2;
    /// <summary>Getting stuck this close to a known spot is the same obstacle.</summary>
    public const double SameSpot = 120;
    /// <summary>The character's middle passing this close over a spot: nothing stands there.</summary>
    public const double WalkedOver = 45;
    /// <summary>The obstacle is about this far in front of where the character stopped.</summary>
    public const double Ahead = 70;
    private const int KeepPerMap = 3000;

    private Dictionary<string, List<Spot>> _maps;
    private readonly object _gate = new();
    /// <summary>What this SekoBot learned since it last shared (replayed onto the file).</summary>
    private readonly List<Action<Dictionary<string, List<Spot>>>> _news = [];

    public ObstacleMap(Dictionary<string, List<Spot>>? maps = null) => _maps = maps ?? [];

    public bool Changed { get; private set; }

    /// <summary>The character was held up right in front of <paramref name="at"/> on <paramref name="map"/>.</summary>
    public void Stuck(string map, double x, double y, DateTime now)
    {
        if (map.Length == 0)
            return;
        lock (_gate)
        {
            StuckIn(_maps, map, x, y, now);
            _news.Add(d => StuckIn(d, map, x, y, now));
            Changed = true;
        }
    }

    private static void StuckIn(Dictionary<string, List<Spot>> maps, string map, double x, double y, DateTime now)
    {
        {
            if (!maps.TryGetValue(map, out var spots))
                maps[map] = spots = [];
            var near = spots.Where(s => Dist(s, x, y) < SameSpot).MinBy(s => Dist(s, x, y));
            if (near != null)
            {
                // The same obstacle: its middle moves a little towards the new spot.
                near.X = (near.X * near.Hits + x) / (near.Hits + 1);
                near.Y = (near.Y * near.Hits + y) / (near.Hits + 1);
                near.Hits = Math.Min(near.Hits + 1, 20);
                near.Last = now;
            }
            else
            {
                spots.Add(new Spot { X = x, Y = y, Hits = 1, Last = now });
                if (spots.Count > KeepPerMap)
                    spots.Remove(spots.MinBy(s => s.Last)!);
            }
        }
    }

    /// <summary>The character walked freely through <paramref name="at"/>: spots there wear off.</summary>
    public void Walked(string map, Vec3 at)
    {
        lock (_gate)
        {
            if (WalkedIn(_maps, map, at))
            {
                _news.Add(d => WalkedIn(d, map, at));
                Changed = true;
            }
        }
    }

    private static bool WalkedIn(Dictionary<string, List<Spot>> maps, string map, Vec3 at)
    {
        if (!maps.TryGetValue(map, out var spots))
            return false;
        bool any = false;
        foreach (var s in spots.Where(s => Dist(s, at.X, at.Y) < WalkedOver).ToList())
        {
            if (--s.Hits <= 0)
                spots.Remove(s);
            any = true;
        }
        return any;
    }

    /// <summary>The sure obstacles on a map within <paramref name="range"/> of <paramref name="me"/>.</summary>
    public List<Vec3> Near(string map, Vec3 me, double range)
    {
        lock (_gate)
            return _maps.TryGetValue(map, out var spots)
                ? [.. spots.Where(s => s.Hits >= Sure && Dist(s, me.X, me.Y) <= range).Select(s => new Vec3(s.X, s.Y, me.Z))]
                : [];
    }

    /// <summary>How many spots were noted on a map (sure or not).</summary>
    public int Noted(string map)
    {
        lock (_gate)
            return _maps.TryGetValue(map, out var spots) ? spots.Count : 0;
    }

    /// <summary>How many sure obstacles are known on a map.</summary>
    public int Count(string map)
    {
        lock (_gate)
            return _maps.TryGetValue(map, out var spots) ? spots.Count(s => s.Hits >= Sure) : 0;
    }

    private static double Dist(Spot s, double x, double y) => Math.Sqrt((s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y));

    // ── Saved between sessions ──

    public static string FilePath => Path.Combine(ProfileStore.Directory, "engeller.json");

    public static ObstacleMap Load(string? path = null)
    {
        try
        {
            path ??= FilePath;
            if (File.Exists(path))
                return new ObstacleMap(JsonSerializer.Deserialize<Dictionary<string, List<Spot>>>(File.ReadAllText(path)));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new ObstacleMap();
    }

    /// <summary>Shares: this SekoBot's news go into the file, and what the others learned comes back
    /// (every SekoBot uses the same file while they run).</summary>
    public void Save(string? path = null)
    {
        List<Action<Dictionary<string, List<Spot>>>> news;
        lock (_gate)
        {
            news = [.. _news];
            _news.Clear();
            Changed = false;
        }
        var all = SharedFile.Update<Dictionary<string, List<Spot>>>(path ?? FilePath, d => news.ForEach(n => n(d)));
        lock (_gate)
        {
            if (all == null)
            {
                _news.InsertRange(0, news); // not reached: kept for the next time
                Changed = true;
                return;
            }
            // What came meanwhile is played onto the shared state too.
            foreach (var n in _news)
                n(all);
            _maps = all;
        }
    }
}
