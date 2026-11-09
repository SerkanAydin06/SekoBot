using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Config
{
/// <summary>The best place found to farm: its middle and what was seen around it.</summary>
internal sealed class FarmSpot
{
    public string MapName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public DateTime FoundAt { get; set; }
    /// <summary>Mobs seen within <see cref="FarmSpotFinder.Radius"/> of it.</summary>
    public int Mobs { get; set; }
    /// <summary>The most common mobs there, e.g. "Siyah Ayı ×5, Kaplan ×3".</summary>
    public string Names { get; set; } = "";

    public Vec3 Position => new(X, Y, Z);
}
}

namespace SekoBot.Bot
{

/// <summary>
/// Picks the best place to farm from the mobs seen (memory shows them up to ~40 m around): every
/// point on a 5 m grid is scored by the mobs within 15 m of it — each worth its experience for the
/// time it takes to kill (like the target choice), preferred Metin stones extra — and the best
/// point wins. Pure logic; the walking around to see more is done by the engine.
/// </summary>
internal static class FarmSpotFinder
{
    public const double Radius = 1500;   // mobs within 15 m of a spot count for it
    public const double Grid = 500;      // candidate spots every 5 m

    /// <param name="mobs">Every mob seen while looking around (one entry per mob).</param>
    /// <param name="from">Where the search started; spots further than <paramref name="maxDistance"/> are left out.</param>
    public static FarmSpot? Best(IReadOnlyCollection<WorldMob> mobs, BotProfile profile, Vec3 from, string map,
        int attackPower, double maxDistance = 8000)
    {
        var wanted = mobs.Where(m => BotEngine.Wanted(m, profile)).ToList();
        if (wanted.Count == 0)
            return null;
        double dps = Math.Max(50, attackPower);
        double Value(WorldMob m)
        {
            double exp = m.Exp > 0 ? m.Exp : m.MaxHp * 0.3;
            double value = exp / (m.MaxHp / dps + 1);
            if (m.IsMetin && profile.Metins == MetinMode.Prefer)
                value *= 3;
            return value;
        }
        double minX = wanted.Min(m => m.Position.X), maxX = wanted.Max(m => m.Position.X);
        double minY = wanted.Min(m => m.Position.Y), maxY = wanted.Max(m => m.Position.Y);
        Vec3? best = null;
        double bestScore = 0;
        for (double x = Math.Floor(minX / Grid) * Grid; x <= maxX; x += Grid)
        for (double y = Math.Floor(minY / Grid) * Grid; y <= maxY; y += Grid)
        {
            var spot = new Vec3(x, y, from.Z);
            if (spot.DistanceXY(from) > maxDistance)
                continue;
            double score = wanted.Where(m => m.Position.DistanceXY(spot) <= Radius).Sum(Value);
            if (score > bestScore)
            {
                bestScore = score;
                best = spot;
            }
        }
        if (best is not Vec3 b)
            return null;
        var around = wanted.Where(m => m.Position.DistanceXY(b) <= Radius).ToList();
        return new FarmSpot
        {
            MapName = map,
            X = b.X,
            Y = b.Y,
            Z = from.Z,
            FoundAt = DateTime.Now,
            Mobs = around.Count,
            Names = string.Join(", ", around.GroupBy(m => m.Name).OrderByDescending(g => g.Count()).Take(3)
                .Select(g => $"{g.Key} ×{g.Count()}")),
        };
    }
}
}
