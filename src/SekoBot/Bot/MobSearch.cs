namespace SekoBot.Bot;

/// <summary>
/// What the bot has learned about the game's cursors this session. Metin2 switches to an
/// attack cursor over a monster; which handle that is gets learned by clicking and checking
/// whether a target bar appears. Thread-safe: used by the input worker.
/// </summary>
internal sealed class MobCursors
{
    private const int FailuresBeforeBad = 3;
    private readonly object _gate = new();
    private readonly HashSet<nint> _good = [];
    private readonly Dictionary<nint, int> _failures = [];

    public bool IsGood(nint cursor) { lock (_gate) return _good.Contains(cursor); }

    public bool IsBad(nint cursor)
    {
        lock (_gate)
            return !_good.Contains(cursor) && _failures.GetValueOrDefault(cursor) >= FailuresBeforeBad;
    }

    /// <returns>True the first time this cursor is learned as the attack cursor.</returns>
    public bool MarkGood(nint cursor)
    {
        lock (_gate)
        {
            _failures.Remove(cursor);
            return _good.Add(cursor);
        }
    }

    public void MarkFailed(nint cursor)
    {
        lock (_gate)
            if (!_good.Contains(cursor))
                _failures[cursor] = _failures.GetValueOrDefault(cursor) + 1;
    }

    public int GoodCount { get { lock (_gate) return _good.Count; } }
}

internal static class MobSearch
{
    /// <summary>Points (client pixels) to hover, nearest to the character first, skipping the HUD.</summary>
    public static List<Point> ScanPoints(int width, int height)
    {
        var points = new List<Point>();
        double cx = width / 2.0, cy = height / 2.0;
        double maxRadius = Math.Min(width, height) * 0.42;
        int ring = 0;
        for (double r = 70; r <= maxRadius; r += 55, ring++)
        {
            int n = Math.Max(8, (int)Math.Round(2 * Math.PI * r / 60));
            // Rotate every other ring so points don't line up on the same rays.
            double offset = ring % 2 == 0 ? 0 : Math.PI / n;
            for (int i = 0; i < n; i++)
            {
                double a = offset + 2 * Math.PI * i / n;
                int x = (int)Math.Round(cx + r * Math.Cos(a));
                int y = (int)Math.Round(cy + r * Math.Sin(a));
                if (x < 8 || x >= width - 8 || y < height * 0.12 || y > height * 0.82)
                    continue;
                points.Add(new Point(x, y));
            }
        }
        return points;
    }

    /// <summary>
    /// Given the cursors seen so far (in scan order), returns the index to click, or -1.
    /// A learned attack cursor wins. Otherwise, once enough points were seen, the most common
    /// cursor is the plain ground cursor and the first different, not-yet-rejected one is tried.
    /// </summary>
    public static int Choose(IReadOnlyList<nint> seen, MobCursors memory, int groundSample = 8, bool final = false)
    {
        for (int i = 0; i < seen.Count; i++)
            if (seen[i] != 0 && memory.IsGood(seen[i]))
                return i;
        if (seen.Count < groundSample && !final)
            return -1;
        var known = seen.Where(c => c != 0).ToList();
        if (known.Count == 0)
            return -1;
        nint ground = known.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        for (int i = 0; i < seen.Count; i++)
            if (seen[i] != 0 && seen[i] != ground && !memory.IsBad(seen[i]))
                return i;
        return -1;
    }
}
