using System.Text.Json;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Places on a map the character couldn't get to (mobs in a lake, on a cliff, down a steep
/// slope): where the bot gave up on a mob or a group. Mobs there aren't gone for and don't count
/// as "where the mobs are" — once for a while, twice for good — unless they come to us. Standing
/// there ourselves shows it was reachable after all: forgotten. Kept per map in ulasilamaz.json.
/// </summary>
internal sealed class NoGoMap
{
    public sealed class Spot
    {
        public double X { get; set; }
        public double Y { get; set; }
        public int Hits { get; set; }
        public DateTime Last { get; set; }
    }

    /// <summary>Mobs this close to a spot (world units, 5 m) are left alone.</summary>
    public const double Radius = 500;
    /// <summary>Give-ups at one place before it is left for good.</summary>
    public const int Sure = 2;
    /// <summary>One give-up: left this long.</summary>
    public static readonly TimeSpan Once = TimeSpan.FromMinutes(10);
    /// <summary>The character this close to a spot: it is reachable (3 m).</summary>
    public const double Reached = 300;
    /// <summary>Standing near a place right when giving up on it (the mob was just out of reach) doesn't count.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, List<Spot>> _maps;
    private readonly object _gate = new();
    /// <summary>Maps this SekoBot learned something new on (the others' are taken from the file when saving).</summary>
    private readonly HashSet<string> _touched = [];

    public NoGoMap(Dictionary<string, List<Spot>>? maps = null) => _maps = maps ?? [];

    public bool Changed { get; private set; }

    /// <summary>The bot gave up on a mob at <paramref name="at"/>.</summary>
    public void GaveUp(string map, Vec3 at, DateTime now)
    {
        if (map.Length == 0)
            return;
        lock (_gate)
        {
            if (!_maps.TryGetValue(map, out var spots))
                _maps[map] = spots = [];
            var near = spots.FirstOrDefault(s => Math.Abs(s.X - at.X) < Radius * 0.6 && Math.Abs(s.Y - at.Y) < Radius * 0.6);
            if (near == null)
                spots.Add(new Spot { X = at.X, Y = at.Y, Hits = 1, Last = now });
            else
            {
                near.Hits++;
                near.Last = now;
            }
            _touched.Add(map);
            Changed = true;
        }
    }

    /// <summary>Not to be gone for: near a place given up twice, or once a short while ago.</summary>
    public bool Blocked(string map, Vec3? at, DateTime now)
    {
        if (at is not Vec3 v || map.Length == 0)
            return false;
        lock (_gate)
        {
            return _maps.TryGetValue(map, out var spots) &&
                   spots.Any(s => (s.Hits >= Sure || now - s.Last < Once) &&
                                  (s.X - v.X) * (s.X - v.X) + (s.Y - v.Y) * (s.Y - v.Y) <= Radius * Radius);
        }
    }

    /// <summary>The character stands here: places right here were reachable after all.</summary>
    public void Walked(string map, Vec3 me, DateTime now)
    {
        if (map.Length == 0)
            return;
        lock (_gate)
        {
            if (_maps.TryGetValue(map, out var spots) &&
                spots.RemoveAll(s => now - s.Last > Settle && (s.X - me.X) * (s.X - me.X) + (s.Y - me.Y) * (s.Y - me.Y) <= Reached * Reached) > 0)
            {
                _touched.Add(map);
                Changed = true;
            }
        }
    }

    public int Count(string map)
    {
        lock (_gate)
            return _maps.TryGetValue(map, out var spots) ? spots.Count : 0;
    }

    public static string FilePath => Path.Combine(ProfileStore.Directory, "ulasilamaz.json");

    public static NoGoMap Load(string? path = null)
    {
        path ??= FilePath;
        try
        {
            if (File.Exists(path))
                return new NoGoMap(JsonSerializer.Deserialize<Dictionary<string, List<Spot>>>(File.ReadAllText(path)));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new NoGoMap();
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        try
        {
            // Other SekoBots (other games) write the same file: their maps are kept.
            Dictionary<string, List<Spot>>? onDisk = null;
            try
            {
                if (File.Exists(path))
                    onDisk = JsonSerializer.Deserialize<Dictionary<string, List<Spot>>>(File.ReadAllText(path));
            }
            catch (JsonException) { }
            string json;
            lock (_gate)
            {
                // Maps this one didn't change: as the file has them (another SekoBot may have learned there).
                foreach (var (map, spots) in onDisk ?? [])
                    if (!_touched.Contains(map))
                        _maps[map] = spots;
                json = JsonSerializer.Serialize(_maps);
                Changed = false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
