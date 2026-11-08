using SekoBot.Config;

namespace SekoBot.Memory;

/// <summary>
/// Learns how world positions (from memory) relate to the big map (the yellow arrow, 0..1 of the
/// map width): each map axis is a straight line of one world axis. Fed with pairs whenever the
/// map is open; a few pairs from different places are enough.
/// </summary>
internal sealed class MapCalibration
{
    /// <summary>Pairs closer than this (world units) to an earlier one add nothing.</summary>
    public const double MinSpacing = 60;
    /// <summary>The pairs must cover at least this much ground on both world axes.</summary>
    public const double MinSpread = 150;
    /// <summary>How well the straight line must fit (1 = perfectly).</summary>
    public const double MinFit = 0.9;
    private const int MaxSamples = 16;

    private readonly List<(Vec3 World, PointF Map)> _samples = [];

    public int Count => _samples.Count;

    public void Clear() => _samples.Clear();

    /// <returns>True when the pair was kept.</returns>
    public bool Add(Vec3 world, PointF map)
    {
        if (_samples.Any(s => s.World.DistanceXY(world) < MinSpacing))
            return false;
        _samples.Add((world, map));
        if (_samples.Count > MaxSamples)
            _samples.RemoveAt(0);
        return true;
    }

    /// <summary>The world → map formula, or null while the pairs aren't enough (or don't agree).</summary>
    public PositionSpec? Fit(string mapName)
    {
        if (_samples.Count < 3)
            return null;
        var wx = _samples.Select(s => s.World.X).ToArray();
        var wy = _samples.Select(s => s.World.Y).ToArray();
        if (wx.Max() - wx.Min() < MinSpread || wy.Max() - wy.Min() < MinSpread)
            return null;
        var mx = _samples.Select(s => (double)s.Map.X).ToArray();
        var my = _samples.Select(s => (double)s.Map.Y).ToArray();

        PositionSpec? best = null;
        double bestFit = MinFit;
        foreach (bool swapped in new[] { false, true })
        {
            var (ax, bx, fitX) = Line(swapped ? wy : wx, mx);
            var (ay, by, fitY) = Line(swapped ? wx : wy, my);
            double fit = Math.Min(fitX, fitY);
            // Both axes must be roughly the same scale (the map isn't stretched).
            double ratio = Math.Abs(ax) / Math.Max(1e-12, Math.Abs(ay));
            if (fit < bestFit || ratio < 0.7 || ratio > 1.4)
                continue;
            bestFit = fit;
            best = new PositionSpec
            {
                Version = PositionSpec.CurrentVersion,
                MapName = mapName,
                Swapped = swapped,
                Ax = ax, Bx = bx, Ay = ay, By = by,
                Fit = fit,
            };
        }
        return best;
    }

    /// <summary>Least squares y = a·x + b, with R² (1 = perfect).</summary>
    private static (double A, double B, double R2) Line(double[] x, double[] y)
    {
        double mx = x.Average(), my = y.Average();
        double sxx = 0, sxy = 0, syy = 0;
        for (int i = 0; i < x.Length; i++)
        {
            sxx += (x[i] - mx) * (x[i] - mx);
            sxy += (x[i] - mx) * (y[i] - my);
            syy += (y[i] - my) * (y[i] - my);
        }
        if (sxx <= 0 || syy <= 0)
            return (0, 0, 0);
        double a = sxy / sxx;
        return (a, my - a * mx, sxy * sxy / (sxx * syy));
    }
}
