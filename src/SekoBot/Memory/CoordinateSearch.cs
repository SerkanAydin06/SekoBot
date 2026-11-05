namespace SekoBot.Memory;

/// <param name="MapXAddress">The value that follows the arrow's left/right position on the big map.</param>
/// <param name="MapYAddress">The value that follows the arrow's up/down position.</param>
/// <param name="Size">4 = float, 8 = double.</param>
/// <param name="Ax">Map x = Ax · value(MapXAddress) + Bx (0..1 of the map width).</param>
/// <param name="Ay">Map y = Ay · value(MapYAddress) + By.</param>
internal sealed record CoordinateMatch(long MapXAddress, long MapYAddress, int Size,
    double Ax, double Bx, double Ay, double By, double R2X, double R2Y);

/// <summary>
/// Finds the character's world position in the game's memory, using the yellow arrow on the
/// big map as the reference (like a Cheat Engine "changed / unchanged" search, but automatic):
/// <list type="number">
/// <item><see cref="Begin"/>: copy of all writable memory, standing still.</item>
/// <item><see cref="FirstMove"/> after walking: keep values that changed (and their neighbours:
/// X and Y sit next to each other, and a walk may change only one of them).</item>
/// <item><see cref="Still"/> after standing a moment: drop values that change on their own
/// (animations, timers).</item>
/// <item><see cref="Move"/> after more walks in other directions: record the values.</item>
/// <item><see cref="Result"/>: the pair of neighbouring values that rises and falls with the
/// arrow's x and y (straight-line fit), one per map axis.</item>
/// </list>
/// Pure logic over <see cref="IMemorySource"/>, so it is tested with fake memory.
/// </summary>
internal sealed class CoordinateSearch
{
    public const long MaxSnapshotBytes = 3L * 1024 * 1024 * 1024;
    public const int MaxCandidates = 25_000_000;
    /// <summary>How well a value must follow the arrow (1 = perfectly). The arrow is read to a
    /// pixel or two, so a real coordinate scores ~0.85–1.</summary>
    public const double MinFit = 0.7;
    private const int ChunkBytes = 64 * 1024;

    private List<(MemoryRegion Region, byte[] Data)>? _snapshot;
    private long[] _address = [];
    private byte[] _size = [];
    /// <summary>One column per reading where the character had walked (the first is the start).</summary>
    private readonly List<double[]> _values = [];
    private readonly List<(double X, double Y)> _arrows = [];
    /// <summary>Values read in the middle of the current walk (map closed), or null.</summary>
    private double[]? _midWalk;

    /// <summary>Values still in the running.</summary>
    public int Count => _address.Length;

    /// <summary>Readings taken so far (start + walks).</summary>
    public int Readings => _arrows.Count;

    /// <returns>An error message, or null.</returns>
    public string? Begin(IMemorySource memory, PointF arrow)
    {
        var regions = memory.Regions();
        long total = regions.Sum(r => r.Size);
        if (total == 0)
            return "okunabilir bellek bulunamadı";
        if (total > MaxSnapshotBytes)
            return $"oyunun belleği çok büyük ({total >> 20:N0} MB)";
        _snapshot = [];
        foreach (var region in regions)
        {
            var data = new byte[region.Size];
            if (memory.Read(region.Base, data, data.Length))
                _snapshot.Add((region, data));
        }
        _values.Clear();
        _arrows.Clear();
        _address = [];
        _size = [];
        _arrows.Add((arrow.X, arrow.Y));
        return _snapshot.Count == 0 ? "bellek okunamadı" : null;
    }

    /// <summary>Call after the character walked away from where <see cref="Begin"/> was called.</summary>
    /// <returns>An error message, or null.</returns>
    public string? FirstMove(IMemorySource memory, PointF arrow)
    {
        if (_snapshot == null)
            return "önce bellek fotoğrafı çekilmeli";
        var address = new List<long>();
        var size = new List<byte>();
        var before = new List<double>();
        var after = new List<double>();
        try
        {
            foreach (var (region, old) in _snapshot)
            {
                var now = new byte[old.Length];
                if (!memory.Read(region.Base, now, now.Length))
                    continue;
                Collect(region.Base, old, now, 4, address, size, before, after);
                Collect(region.Base, old, now, 8, address, size, before, after);
                if (address.Count > MaxCandidates)
                    return "çok fazla değer değişiyor; oyunda hareketsiz bir yerde tekrar dene";
            }
        }
        finally
        {
            _snapshot = null; // the big copy is not needed any more
        }
        if (address.Count == 0)
            return "yürürken değişen bir değer bulunamadı";

        // Sorted by address, so later reads can fetch neighbours in one go.
        var order = Enumerable.Range(0, address.Count).ToArray();
        Array.Sort(address.ToArray(), order);
        _address = order.Select(i => address[i]).ToArray();
        _size = order.Select(i => size[i]).ToArray();
        _values.Add(order.Select(i => before[i]).ToArray());
        _values.Add(order.Select(i => after[i]).ToArray());
        _arrows.Add((arrow.X, arrow.Y));
        return null;
    }

    private static double Number(byte[] data, int offset, int size) =>
        size == 8 ? BitConverter.ToDouble(data, offset) : BitConverter.ToSingle(data, offset);

    /// <summary>
    /// Could be a world coordinate. Halves of pointers and other non-numbers read as floats are
    /// tiny denormals (like 4e-43) or huge (like -1e26); a coordinate is neither.
    /// </summary>
    internal static bool Plausible(double v) =>
        double.IsFinite(v) && (v == 0 || (Math.Abs(v) >= 1e-3 && Math.Abs(v) < 1e7));

    private static bool Changed(byte[] old, byte[] now, int offset, int size)
    {
        bool differs = size == 8
            ? BitConverter.ToInt64(old, offset) != BitConverter.ToInt64(now, offset)
            : BitConverter.ToInt32(old, offset) != BitConverter.ToInt32(now, offset);
        return differs && Plausible(Number(old, offset, size)) && Plausible(Number(now, offset, size));
    }

    /// <summary>Keeps every slot that changed, plus the slots right before and after one that did.</summary>
    private static void Collect(long baseAddress, byte[] old, byte[] now, int size,
        List<long> address, List<byte> sizes, List<double> before, List<double> after)
    {
        int slots = old.Length / size;
        bool previous = false, current = slots > 0 && Changed(old, now, 0, size);
        for (int i = 0; i < slots; i++)
        {
            bool next = i + 1 < slots && Changed(old, now, (i + 1) * size, size);
            if (current || previous || next)
            {
                int offset = i * size;
                double a = Number(old, offset, size), b = Number(now, offset, size);
                if (Plausible(a) && Plausible(b))
                {
                    address.Add(baseAddress + offset);
                    sizes.Add((byte)size);
                    before.Add(a);
                    after.Add(b);
                }
            }
            previous = current;
            current = next;
        }
    }

    /// <summary>Call after standing still for a moment: whatever changed meanwhile is not our position.</summary>
    public void Still(IMemorySource memory)
    {
        if (_values.Count < 2)
            return;
        var now = ReadAll(memory);
        var last = _values[^1];
        var previous = _values[^2];
        Keep(i =>
        {
            if (double.IsNaN(now[i]))
                return false;
            // Standing still, the position stays exactly where it is.
            return Math.Abs(now[i] - last[i]) <= 1e-3 + 1e-7 * Math.Abs(last[i]);
        });
    }

    /// <summary>Call after another walk, with the arrow's new place on the map.</summary>
    /// <summary>
    /// Call halfway through a walk, with the map closed. Our real position changes while we walk;
    /// a copy that is only updated while the map is open (the arrow's place on screen) doesn't.
    /// </summary>
    public void MidWalk(IMemorySource memory)
    {
        if (_values.Count >= 2)
            _midWalk = ReadAll(memory);
    }

    public void Move(IMemorySource memory, PointF arrow)
    {
        if (_values.Count < 2)
            return;
        var now = ReadAll(memory);
        var last = _values[^1];
        var mid = _midWalk;
        _midWalk = null;
        var before = _arrows[^1];
        // Only when the walk really took us somewhere (not blocked).
        bool walked = Math.Sqrt(Math.Pow(arrow.X - before.X, 2) + Math.Pow(arrow.Y - before.Y, 2)) >= 0.004;
        _values.Add(now);
        _arrows.Add((arrow.X, arrow.Y));
        Keep(i => !double.IsNaN(now[i]) &&
                  (mid == null || !walked || (!double.IsNaN(mid[i]) && Math.Abs(mid[i] - last[i]) > 1e-3) ||
                   Math.Abs(now[i] - last[i]) <= 1e-3));
    }

    /// <summary>Current value of every candidate (NaN when it can't be read any more).</summary>
    private double[] ReadAll(IMemorySource memory)
    {
        var result = new double[_address.Length];
        var buffer = new byte[ChunkBytes];
        int i = 0;
        while (i < _address.Length)
        {
            long start = _address[i];
            int j = i;
            while (j + 1 < _address.Length && _address[j + 1] + _size[j + 1] - start <= ChunkBytes)
                j++;
            int length = (int)(_address[j] + _size[j] - start);
            if (memory.Read(start, buffer, length))
            {
                for (int k = i; k <= j; k++)
                {
                    double v = Number(buffer, (int)(_address[k] - start), _size[k]);
                    result[k] = Plausible(v) ? v : double.NaN;
                }
            }
            else
            {
                for (int k = i; k <= j; k++)
                    result[k] = memory.TryReadNumber(_address[k], _size[k], out double v) && Plausible(v) ? v : double.NaN;
            }
            i = j + 1;
        }
        return result;
    }

    private void Keep(Func<int, bool> keep)
    {
        var kept = new List<int>(_address.Length);
        for (int i = 0; i < _address.Length; i++)
            if (keep(i))
                kept.Add(i);
        if (kept.Count == _address.Length)
            return;
        _address = kept.Select(i => _address[i]).ToArray();
        _size = kept.Select(i => _size[i]).ToArray();
        for (int c = 0; c < _values.Count; c++)
        {
            var column = _values[c];
            _values[c] = kept.Select(i => column[i]).ToArray();
        }
    }

    /// <summary>Straight-line fit map = a·value + b; R² says how well it holds (0..1).</summary>
    private (double R2, double A, double B) Fit(int candidate, bool mapY)
    {
        int n = _arrows.Count;
        double mv = 0, mm = 0;
        for (int s = 0; s < n; s++)
        {
            mv += _values[s][candidate];
            mm += mapY ? _arrows[s].Y : _arrows[s].X;
        }
        mv /= n;
        mm /= n;
        double cov = 0, varV = 0, varM = 0;
        for (int s = 0; s < n; s++)
        {
            double dv = _values[s][candidate] - mv, dm = (mapY ? _arrows[s].Y : _arrows[s].X) - mm;
            cov += dv * dm;
            varV += dv * dv;
            varM += dm * dm;
        }
        if (varV <= 0 || varM <= 0)
            return (0, 0, 0);
        double a = cov / varV;
        return (cov * cov / (varV * varM), a, mm - a * mv);
    }

    /// <summary>The best pair of neighbouring values (X next to Y) following the arrow, or null.</summary>
    public CoordinateMatch? Result() => Results(1).FirstOrDefault();

    /// <summary>Changed in most walks: our position moves with every walk (unless something blocked it).</summary>
    private bool MovesWithUs(int candidate)
    {
        // Whole numbers every time: a place on screen in pixels (UI), not a world position.
        bool whole = true;
        for (int s = 0; s < _values.Count && whole; s++)
            whole = _values[s][candidate] == Math.Round(_values[s][candidate]);
        if (whole)
            return false;
        int changes = 0;
        for (int s = 1; s < _values.Count; s++)
            if (Math.Abs(_values[s][candidate] - _values[s - 1][candidate]) > 1e-3)
                changes++;
        return changes * 2 >= _values.Count - 1;
    }

    /// <summary>The best pairs, best first (to be checked one by one with a last walk).</summary>
    public List<CoordinateMatch> Results(int max)
    {
        var results = new List<(double Score, CoordinateMatch Match)>();
        if (_arrows.Count < 4 || _address.Length == 0)
            return [];
        var fits = new Dictionary<long, (int Index, (double R2, double A, double B) X, (double R2, double A, double B) Y)>();
        for (int i = 0; i < _address.Length; i++)
        {
            var x = Fit(i, mapY: false);
            var y = Fit(i, mapY: true);
            if (Math.Max(x.R2, y.R2) >= MinFit && MovesWithUs(i))
                fits[_address[i] * 16 + _size[i]] = (i, x, y);
        }

        foreach (var first in fits.Values)
        {
            int size = _size[first.Index];
            long address = _address[first.Index];
            if (!fits.TryGetValue((address + size) * 16 + size, out var second))
                continue;
            // Either this value follows map x and the next one map y, or the other way round.
            foreach (bool firstIsX in new[] { true, false })
            {
                var fx = firstIsX ? first.X : second.X;
                var fy = firstIsX ? second.Y : first.Y;
                if (fx.R2 < MinFit || fy.R2 < MinFit)
                    continue;
                // The map shows both axes at the same scale.
                double ratio = Math.Abs(fx.A) / Math.Abs(fy.A);
                if (!(ratio > 0.5 && ratio < 2))
                    continue;
                // World units across the map: from a tiny arena (100) to a huge world.
                double worldPerMap = 1 / Math.Abs(fx.A);
                if (!(worldPerMap > 100 && worldPerMap < 1e8))
                    continue;
                long xAddress = firstIsX ? address : address + size, yAddress = firstIsX ? address + size : address;
                results.Add((fx.R2 + fy.R2, new CoordinateMatch(xAddress, yAddress, size, fx.A, fx.B, fy.A, fy.B, fx.R2, fy.R2)));
            }
        }
        return results.OrderByDescending(r => r.Score).Take(max).Select(r => r.Match).ToList();
    }

    /// <summary>Where a match puts us now (map coordinates), to check it against the arrow.</summary>
    public static PointF? Predict(IMemorySource memory, CoordinateMatch match)
    {
        if (!memory.TryReadNumber(match.MapXAddress, match.Size, out double x) ||
            !memory.TryReadNumber(match.MapYAddress, match.Size, out double y))
            return null;
        return new PointF((float)(match.Ax * x + match.Bx), (float)(match.Ay * y + match.By));
    }
}

/// <summary>
/// Finds a way to the position that survives a game restart: from one of the objects on the
/// HP pointer path (the character's objects), up to two pointer steps, then an offset.
/// </summary>
internal static class PointerSearch
{
    private const int FirstBlock = 0x2000, SecondBlock = 0x800, MaxOffset = 0x1000;

    /// <returns>(node index, pointer offsets, offset of x, offset of y) or null.</returns>
    public static (int Node, long[] Offsets, long XOffset, long YOffset)? Find(
        IMemorySource memory, IReadOnlyList<long> nodes, long x, long y)
    {
        bool Hit(long p) => MemoryReads.IsPointer(p) && Math.Min(x, y) >= p && Math.Max(x, y) - p < MaxOffset;

        for (int i = 0; i < nodes.Count; i++)
            if (Hit(nodes[i]))
                return (i, Array.Empty<long>(), x - nodes[i], y - nodes[i]);

        var first = new byte[FirstBlock];
        var second = new byte[SecondBlock];
        var blocks = new List<(int Node, byte[] Data)>();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!MemoryReads.IsPointer(nodes[i]) || !memory.Read(nodes[i], first, FirstBlock))
                continue;
            var copy = (byte[])first.Clone();
            blocks.Add((i, copy));
            for (int o = 0; o < FirstBlock; o += 8)
            {
                long p = BitConverter.ToInt64(copy, o);
                if (Hit(p))
                    return (i, new long[] { o }, x - p, y - p);
            }
        }
        foreach (var (node, data) in blocks)
        {
            for (int o1 = 0; o1 < FirstBlock; o1 += 8)
            {
                long p1 = BitConverter.ToInt64(data, o1);
                if (!MemoryReads.IsPointer(p1) || !memory.Read(p1, second, SecondBlock))
                    continue;
                for (int o2 = 0; o2 < SecondBlock; o2 += 8)
                {
                    long p2 = BitConverter.ToInt64(second, o2);
                    if (Hit(p2))
                        return (node, new long[] { o1, o2 }, x - p2, y - p2);
                }
            }
        }
        return null;
    }

    /// <summary>Follows a path found by <see cref="Find"/>; returns the base the offsets apply to.</summary>
    public static long? Resolve(IMemorySource memory, IReadOnlyList<long> nodes, int node, IReadOnlyList<long> offsets)
    {
        if (node < 0 || node >= nodes.Count)
            return null;
        long p = nodes[node];
        foreach (long offset in offsets)
        {
            if (!MemoryReads.IsPointer(p) || !memory.TryReadInt64(p + offset, out p))
                return null;
        }
        return MemoryReads.IsPointer(p) ? p : null;
    }

    /// <summary>The objects on the HP pointer path (see <see cref="CharacterStatsReader"/>).</summary>
    public static long[]? HpNodes(IMemorySource memory, long moduleBase)
    {
        var nodes = new List<long>();
        long cursor = moduleBase + CharacterStatsReader.HpBaseOffset;
        foreach (long offset in CharacterStatsReader.HpPath)
        {
            if (!memory.TryReadInt64(cursor, out long next) || !MemoryReads.IsPointer(next))
                return null;
            nodes.Add(next);
            cursor = next + offset;
        }
        if (!memory.TryReadInt64(cursor, out long last) || !MemoryReads.IsPointer(last))
            return null;
        nodes.Add(last);
        return nodes.ToArray();
    }
}
