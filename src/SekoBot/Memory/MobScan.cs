using System.Numerics;

namespace SekoBot.Memory;

/// <summary>A position-like pair of values in memory (same layout as the character's), relative to us.</summary>
internal readonly record struct NearbyPosition(long Address, double Dx, double Dy);

/// <summary>One moment of a mob scan: red names on screen and positions in memory, both relative to us.</summary>
/// <param name="Names">Mob names on screen, pixels from the screen centre (where our character stands).</param>
/// <param name="Positions">Positions in memory near ours, world units from our position.</param>
internal sealed record MobScanFrame(IReadOnlyList<PointF> Names, IReadOnlyList<NearbyPosition> Positions);

/// <param name="Scale">Screen = Scale · world (complex numbers: scale and rotation); with
/// <paramref name="Mirrored"/> the world offset is mirrored first.</param>
/// <param name="Matched">Names that had a memory position at the projected spot.</param>
/// <param name="Names">All names in all frames.</param>
/// <param name="MobAddresses">Addresses of the positions that matched a name.</param>
internal sealed record MobProjection(Complex Scale, bool Mirrored, int Matched, int Names, IReadOnlyList<long> MobAddresses)
{
    public PointF ToScreen(double dx, double dy)
    {
        var w = new Complex(dx, Mirrored ? -dy : dy) * Scale;
        return new PointF((float)w.Real, (float)w.Imaginary);
    }
}

/// <summary>
/// First step towards reading the mobs around us from memory. Mobs are actors like our
/// character, so their positions are stored the same way (two neighbouring floats/doubles).
/// <see cref="FindNear"/> collects every such pair close to our position; <see cref="Match"/>
/// finds the camera projection (scale + rotation, the camera looks straight down) under which
/// those positions land on the red mob names seen on screen.
/// </summary>
internal static class MobScan
{
    /// <summary>A projected position this close (pixels) to a name counts as that name.</summary>
    public const double MatchPixels = 40;
    private const int MaxPerFrame = 400;

    /// <param name="lowFirst">Our position's first value (the lower address).</param>
    /// <param name="highFirst">Our position's second value.</param>
    public static List<NearbyPosition> FindNear(IMemorySource memory, IEnumerable<MemoryRegion> regions,
        int size, double lowFirst, double highFirst, double radius)
    {
        var found = new List<NearbyPosition>();
        foreach (var region in regions)
        {
            var data = new byte[region.Size];
            if (!memory.Read(region.Base, data, data.Length))
                continue;
            for (int o = 0; o + 2 * size <= data.Length; o += size)
            {
                double a = size == 8 ? BitConverter.ToDouble(data, o) : BitConverter.ToSingle(data, o);
                double da = a - lowFirst;
                if (!(Math.Abs(da) < radius))
                    continue;
                double b = size == 8 ? BitConverter.ToDouble(data, o + size) : BitConverter.ToSingle(data, o + size);
                double db = b - highFirst;
                if (!(Math.Abs(db) < radius))
                    continue;
                // Copies of our own position are not mobs.
                if (Math.Abs(da) < 1 && Math.Abs(db) < 1)
                    continue;
                found.Add(new NearbyPosition(region.Base + o, da, db));
            }
        }
        return found;
    }

    /// <summary>The same position is often stored several times; keep one of each (nearest first).</summary>
    public static List<NearbyPosition> Distinct(IEnumerable<NearbyPosition> positions)
    {
        var seen = new HashSet<(long, long)>();
        var result = new List<NearbyPosition>();
        foreach (var p in positions.OrderBy(p => p.Dx * p.Dx + p.Dy * p.Dy))
            if (seen.Add(((long)Math.Round(p.Dx), (long)Math.Round(p.Dy))))
                result.Add(p);
        return result;
    }

    /// <summary>Tries every name ↔ position pairing of the first frames as the projection and keeps
    /// the one under which the most names (in all frames) have a position under them.</summary>
    public static MobProjection? Match(IReadOnlyList<MobScanFrame> frames)
    {
        int totalNames = frames.Sum(f => f.Names.Count);
        if (totalNames == 0)
            return null;
        var prepared = frames.Select(f => (Names: f.Names, Positions: Distinct(f.Positions).Take(MaxPerFrame).ToList())).ToList();

        Complex bestScale = default;
        bool bestMirrored = false;
        int bestScore = 0;
        foreach (var frame in prepared.Take(2))
        foreach (var name in frame.Names)
        {
            var n = new Complex(name.X, name.Y);
            if (n.Magnitude < 30)
                continue;
            foreach (var position in frame.Positions.Take(150))
            foreach (bool mirrored in new[] { false, true })
            {
                var w = new Complex(position.Dx, mirrored ? -position.Dy : position.Dy);
                if (w.Magnitude < 1)
                    continue;
                var scale = n / w;
                int score = Score(prepared, scale, mirrored, 0, null);
                if (score <= bestScore)
                    continue;
                // A projection that squeezes everything into a small spot "matches" names by
                // chance. Such a projection matches the names just as well when they are turned
                // around the centre; the real one doesn't. Count only what beats that.
                int chance = 0;
                for (int turn = 1; turn < 4; turn++)
                    chance = Math.Max(chance, Score(prepared, scale, mirrored, turn, null));
                score -= chance;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestScale = scale;
                    bestMirrored = mirrored;
                }
            }
        }
        if (bestScore < 3)
            return null;
        var addresses = new List<long>();
        int matched = Score(prepared, bestScale, bestMirrored, 0, addresses);
        return new MobProjection(bestScale, bestMirrored, matched, totalNames, addresses);
    }

    private static int Score(List<(IReadOnlyList<PointF> Names, List<NearbyPosition> Positions)> frames,
        Complex scale, bool mirrored, int turn, List<long>? addresses)
    {
        int matched = 0;
        foreach (var (names, positions) in frames)
        {
            foreach (var original in names)
            {
                // turn: the names turned by 90° steps around the centre (for the chance check).
                var name = turn switch
                {
                    1 => new PointF(-original.Y, original.X),
                    2 => new PointF(-original.X, -original.Y),
                    3 => new PointF(original.Y, -original.X),
                    _ => original,
                };
                double best = MatchPixels;
                long address = 0;
                foreach (var p in positions)
                {
                    var s = new Complex(p.Dx, mirrored ? -p.Dy : p.Dy) * scale;
                    double d = Math.Abs(s.Real - name.X) + Math.Abs(s.Imaginary - name.Y);
                    if (d < best)
                    {
                        best = d;
                        address = p.Address;
                    }
                }
                if (address != 0)
                {
                    matched++;
                    addresses?.Add(address);
                }
            }
        }
        return matched;
    }
}
