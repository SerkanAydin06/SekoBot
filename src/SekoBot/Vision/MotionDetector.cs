using SekoBot.Capture;

namespace SekoBot.Vision;

/// <summary>
/// Finds mobs as things that move while the camera stands still. The frame is sampled on a
/// coarse grid; cells whose brightness keeps changing over the last few frames are "active",
/// and groups of active cells become candidates (nearest to the character first).
/// Whole-screen change means the camera or the character moved: the history is reset then.
/// The HUD, the minimap and the character itself are ignored.
/// </summary>
internal sealed class MotionDetector
{
    private const int Cell = 4;
    private const int DiffThreshold = 16;
    private const int Bridge = 6;
    private const float Decay = 0.75f, ActiveHeat = 0.9f;
    private const double CameraMovedFraction = 0.20;
    private static readonly TimeSpan MissMemory = TimeSpan.FromSeconds(15);

    private byte[]? _previous;
    private float[] _heat = [];
    private int _gw, _gh, _width, _height;
    private readonly List<(Point Spot, TimeSpan Time)> _misses = [];
    private readonly object _missGate = new();

    /// <summary>Remember a spot that was clicked without selecting anything; it is skipped for a while.</summary>
    public void MarkMiss(Point spot, TimeSpan now)
    {
        lock (_missGate)
            _misses.Add((spot, now));
    }

    /// <summary>True when a click near this spot recently selected nothing.</summary>
    public bool RecentlyMissed(Point spot, TimeSpan now)
    {
        lock (_missGate)
            return _misses.Any(m => now - m.Time <= MissMemory &&
                                    Math.Abs(m.Spot.X - spot.X) < 40 && Math.Abs(m.Spot.Y - spot.Y) < 40);
    }

    /// <summary>Forget the motion history (camera turned, character walked).</summary>
    public void Reset() => _previous = null;

    public IReadOnlyList<Point> Update(Frame frame, TimeSpan now)
    {
        int gw = frame.Width / Cell, gh = frame.Height / Cell;
        if (gw < 8 || gh < 8)
            return [];
        if (_previous == null || gw != _gw || gh != _gh)
        {
            _gw = gw;
            _gh = gh;
            _width = frame.Width;
            _height = frame.Height;
            _previous = Sample(frame, gw, gh);
            _heat = new float[gw * gh];
            return [];
        }

        var current = Sample(frame, gw, gh);
        int changed = 0, considered = 0;
        var moving = new bool[gw * gh];
        for (int gy = 0; gy < gh; gy++)
        for (int gx = 0; gx < gw; gx++)
        {
            int i = gy * gw + gx;
            if (Ignored(gx, gy))
                continue;
            considered++;
            if (Math.Abs(current[i] - _previous[i]) > DiffThreshold)
            {
                moving[i] = true;
                changed++;
            }
        }
        _previous = current;
        if (considered == 0 || changed > considered * CameraMovedFraction)
        {
            Array.Clear(_heat);
            return [];
        }
        for (int i = 0; i < _heat.Length; i++)
            _heat[i] = _heat[i] * Decay + (moving[i] ? 1 : 0);

        return Candidates(now);
    }

    /// <summary>HUD (top/bottom bars), minimap (top right) and the character in the middle.</summary>
    private bool Ignored(int gx, int gy)
    {
        double x = (gx + 0.5) / _gw, y = (gy + 0.5) / _gh;
        if (y < 0.12 || y > 0.80)
            return true;
        if (x > 0.78 && y < 0.32)
            return true;
        return Math.Abs(x - 0.5) < 0.06 && Math.Abs(y - 0.5) < 0.11;
    }

    private static byte[] Sample(Frame frame, int gw, int gh)
    {
        var data = frame.Data;
        var gray = new byte[gw * gh];
        for (int gy = 0; gy < gh; gy++)
        {
            int row = (gy * Cell + Cell / 2) * frame.Stride;
            for (int gx = 0; gx < gw; gx++)
            {
                int i = row + (gx * Cell + Cell / 2) * 4;
                gray[gy * gw + gx] = (byte)((data[i] + data[i + 1] * 2 + data[i + 2]) >> 2);
            }
        }
        return gray;
    }

    private List<Point> Candidates(TimeSpan now)
    {
        List<(Point Spot, TimeSpan Time)> misses;
        lock (_missGate)
        {
            _misses.RemoveAll(m => now - m.Time > MissMemory);
            misses = [.. _misses];
        }

        var seen = new bool[_heat.Length];
        var found = new List<(Point Center, double Distance)>();
        var stack = new Stack<int>();
        int maxCells = _gw * _gh / 40;
        for (int start = 0; start < _heat.Length; start++)
        {
            if (seen[start] || _heat[start] < ActiveHeat)
                continue;
            // Flood fill over active cells, bridging gaps up to 6 cells (24 px): a moving mob only
            // changes at its edges (its middle looks the same in both frames), so join the edges.
            long sx = 0, sy = 0;
            int count = 0;
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                int cx = c % _gw, cy = c / _gw;
                sx += cx;
                sy += cy;
                count++;
                for (int dy = -Bridge; dy <= Bridge; dy++)
                for (int dx = -Bridge; dx <= Bridge; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= _gw || ny >= _gh)
                        continue;
                    int n = ny * _gw + nx;
                    if (!seen[n] && _heat[n] >= ActiveHeat)
                    {
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
            }
            if (count < 3 || count > maxCells)
                continue;
            var center = new Point((int)(sx * Cell / count + Cell / 2), (int)(sy * Cell / count + Cell / 2));
            if (misses.Any(m => Math.Abs(m.Spot.X - center.X) < 40 && Math.Abs(m.Spot.Y - center.Y) < 40))
                continue;
            double distance = Math.Sqrt(Math.Pow(center.X - _width / 2.0, 2) + Math.Pow(center.Y - _height / 2.0, 2));
            found.Add((center, distance));
        }
        return found.OrderBy(f => f.Distance).Take(6).Select(f => f.Center).ToList();
    }
}
