using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Remembers where the mobs are. The world is cut into 10 m squares; every square the character
/// can see (within <see cref="SeeRange"/>) gets a running average of how many mobs were in it, so a
/// square that emptied out drops again. Squares out of sight keep what was last seen there, fading
/// slowly (half after <see cref="HalfLife"/>). Metin stones count as several mobs.
/// When there is nothing to fight on screen, <see cref="Best"/> says where to walk.
/// </summary>
internal sealed class HotspotMap
{
    public const double Cell = 1000;          // 10 m squares
    public const double SeeRange = 2500;      // mobs are known about 25 m around the character
    public const double MetinWeight = 5;
    public static readonly TimeSpan HalfLife = TimeSpan.FromMinutes(10);

    private readonly Dictionary<(int X, int Y), (double Value, TimeSpan Seen)> _cells = [];

    public int Count => _cells.Count;

    public void Clear() => _cells.Clear();

    private static (int, int) Key(double x, double y) => ((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell));

    private static Vec3 Centre((int X, int Y) key, double z) => new((key.X + 0.5) * Cell, (key.Y + 0.5) * Cell, z);

    /// <summary>One look around: the mobs (and Metin stones) the character sees now.</summary>
    public void Observe(Vec3 player, IEnumerable<WorldMob> mobs, TimeSpan now)
    {
        var counts = new Dictionary<(int, int), double>();
        foreach (var mob in mobs)
        {
            if (mob.Position.DistanceXY(player) > SeeRange)
                continue;
            var key = Key(mob.Position.X, mob.Position.Y);
            counts[key] = counts.GetValueOrDefault(key) + (mob.IsMetin ? MetinWeight : 1);
        }
        // Every square in sight moves towards what is there now (empty squares too), and so does
        // every square a mob was seen in (its middle may be just out of sight).
        var (px, py) = Key(player.X, player.Y);
        int reach = (int)Math.Ceiling(SeeRange / Cell);
        var squares = new HashSet<(int, int)>(counts.Keys);
        for (int dx = -reach; dx <= reach; dx++)
        for (int dy = -reach; dy <= reach; dy++)
            if (Centre((px + dx, py + dy), player.Z).DistanceXY(player) <= SeeRange)
                squares.Add((px + dx, py + dy));
        foreach (var key in squares)
        {
            double seen = counts.GetValueOrDefault(key);
            double value = _cells.TryGetValue(key, out var old) ? Faded(old, now) * 0.8 + seen * 0.2 : seen;
            if (value < 0.05 && seen == 0)
                _cells.Remove(key);
            else
                _cells[key] = (value, now);
        }
    }

    private static double Faded((double Value, TimeSpan Seen) cell, TimeSpan now) =>
        cell.Value * Math.Pow(0.5, (now - cell.Seen) / HalfLife);

    /// <summary>
    /// Where to go when nothing is on screen: the square with the most mobs for the walk
    /// (mobs ÷ (1 + distance / 30 m)), not the one we're standing in, and inside the area limit.
    /// Null when nothing worth it is known.
    /// </summary>
    /// <param name="home">The starting point, with <paramref name="leash"/> the area limit (world units, 0 = none).</param>
    public Vec3? Best(Vec3 player, TimeSpan now, Vec3? home = null, double leash = 0)
    {
        Vec3? best = null;
        double bestScore = 0;
        foreach (var (key, cell) in _cells)
        {
            var centre = Centre(key, player.Z);
            double distance = centre.DistanceXY(player);
            if (distance < 1200) // that's where we are: nothing here, or we'd see it
                continue;
            if (leash > 0 && home is Vec3 h && centre.DistanceXY(h) > leash * 0.9)
                continue;
            double value = Faded(cell, now);
            if (value < 0.5)
                continue;
            double score = value / (1 + distance / 3000);
            if (score > bestScore)
            {
                bestScore = score;
                best = centre;
            }
        }
        return best;
    }
}
