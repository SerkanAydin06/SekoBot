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
/// point on a 5 m grid is scored by the experience an hour farming the mobs within 15 m of it
/// would bring, and the best point wins. Pure logic; the walking around to see more is done by
/// the engine.
/// <para>
/// Experience an hour, not the sum of the mobs: five boars (61 EXP each) must not beat a Black
/// Bear (384) just because there are more of them. Every mob takes its kill time + 1 s + the
/// walk to the next one (the fewer mobs, the further apart: the 15 m circle shared out); killing
/// them all takes at least <see cref="RespawnSec"/> — after that one waits for them to come back.
/// So: total EXP ÷ max(time to kill them all, respawn). Preferred Metin stones count ×3.
/// </para>
/// </summary>
internal static class FarmSpotFinder
{
    public const double Radius = 1500;   // mobs within 15 m of a spot count for it
    public const double Grid = 500;      // candidate spots every 5 m
    /// <summary>Roughly how long the mobs of a place take to come back (an estimate, seconds).</summary>
    public const double RespawnSec = 60;
    /// <summary>Walking with stops, in world units a second (as in the target choice).</summary>
    private const double WalkSpeed = 200;

    public static FarmSpot? Best(IReadOnlyCollection<WorldMob> mobs, BotProfile profile, Vec3 from, string map,
        int attackPower, double maxDistance = 8000)
    {
        var wanted = mobs.Where(m => BotEngine.Wanted(m, profile)).ToList();
        if (wanted.Count == 0)
            return null;
        double dps = Math.Max(50, attackPower);
        double Exp(WorldMob m)
        {
            double exp = m.Exp > 0 ? m.Exp : m.MaxHp * 0.3;
            return m.IsMetin && profile.Metins == MetinMode.Prefer ? exp * 3 : exp;
        }
        // Experience an hour from a group of mobs farmed together.
        double PerHour(List<WorldMob> around)
        {
            if (around.Count == 0)
                return 0;
            double spacing = Math.Sqrt(Math.PI * Radius * Radius / around.Count); // between two mobs
            double seconds = around.Sum(m => m.MaxHp / dps + 1 + spacing / WalkSpeed);
            return around.Sum(Exp) / Math.Max(seconds, RespawnSec) * 3600;
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
            double score = PerHour(wanted.Where(m => m.Position.DistanceXY(spot) <= Radius).ToList());
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
