using System.Collections;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Where the ground is too steep to walk (mountain sides, cliffs, the steep sides of small hills),
/// from the map's height grid (<see cref="HeightGrid"/>). A cell is steep when the ground rises more
/// than <see cref="SteepRise"/> per unit across it (measured over two cells); the grid's empty
/// filler (outside the map) counts as steep too. What the character was seen walking over is never
/// steep from then on (the heights can mislead: a ramp, a bridge, stairs).
/// </summary>
internal sealed class SlopeMap
{
    /// <summary>
    /// Rise per unit (about 50°) above which the ground isn't walked. Calibrated on c3: of 3066 walked
    /// links, 99 % lay on gentler ground than 1.07, 99.5 % gentler than 1.3; a quarter of the map is steeper than 1.0.
    /// </summary>
    internal const double SteepRise = 1.2;
    /// <summary>Heights this low are the grid's filler (outside the map, holes), not ground.</summary>
    internal const int Filler = -1300;
    /// <summary>The character's middle passing over a cell clears it and the ones this close (world units).</summary>
    internal const double WalkedClears = 45;

    private readonly BitArray _steep;
    /// <summary>The ground's height per cell (short.MinValue: filler).</summary>
    private readonly short[] _ground;

    public SlopeMap(HeightGrid grid)
    {
        Map = grid.Map;
        StartX = grid.StartX;
        StartY = grid.StartY;
        Cell = grid.Cell;
        Columns = grid.Columns;
        Rows = grid.Rows;
        _steep = new BitArray(Columns * Rows);
        var h = grid.Heights;
        _ground = new short[h.Length];
        for (int i = 0; i < h.Length; i++)
            _ground[i] = h[i] <= Filler ? short.MinValue : (short)Math.Clamp(h[i], short.MinValue + 1, short.MaxValue);
        double limit = SteepRise * 2 * Cell;
        int steep = 0;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
            {
                int i = r * Columns + c;
                bool s = h[i] <= Filler;
                if (!s)
                {
                    int l = h[r * Columns + Math.Max(0, c - 1)], rt = h[r * Columns + Math.Min(Columns - 1, c + 1)];
                    int u = h[Math.Max(0, r - 1) * Columns + c], d = h[Math.Min(Rows - 1, r + 1) * Columns + c];
                    double gx = l <= Filler || rt <= Filler ? 0 : rt - l, gy = u <= Filler || d <= Filler ? 0 : d - u;
                    s = gx * gx + gy * gy > limit * limit;
                }
                if (s)
                {
                    _steep[i] = true;
                    steep++;
                }
            }
        SteepCells = steep;
    }

    public string Map { get; }
    public double StartX { get; }
    public double StartY { get; }
    public double Cell { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int SteepCells { get; private set; }

    /// <summary>The share of the grid too steep to walk (0…1).</summary>
    public double SteepShare => (double)SteepCells / Math.Max(1, Columns * Rows);

    /// <summary>Whether the ground at the world point is too steep to walk (outside the grid: yes).</summary>
    public bool Steep(double x, double y)
    {
        int c = (int)Math.Floor((x - StartX) / Cell), r = (int)Math.Floor((y - StartY) / Cell);
        return c < 0 || r < 0 || c >= Columns || r >= Rows || _steep[r * Columns + c];
    }

    /// <summary>The ground's height at the world point (the character's feet stand about there); null outside the map.</summary>
    public double? Ground(double x, double y)
    {
        int c = (int)Math.Floor((x - StartX) / Cell), r = (int)Math.Floor((y - StartY) / Cell);
        if (c < 0 || r < 0 || c >= Columns || r >= Rows)
            return null;
        short g = _ground[r * Columns + c];
        return g == short.MinValue ? null : g;
    }

    /// <summary>The share (0…1) of the square around (x, y), <paramref name="size"/> wide, that is steep.</summary>
    public double SteepIn(double x, double y, double size)
    {
        int k = Math.Clamp((int)Math.Ceiling(size / Cell), 1, 6);
        int all = 0, steep = 0;
        for (int i = 0; i < k; i++)
            for (int j = 0; j < k; j++)
            {
                all++;
                if (Steep(x - size / 2 + (i + 0.5) * size / k, y - size / 2 + (j + 0.5) * size / k))
                    steep++;
            }
        return (double)steep / all;
    }

    /// <summary>The character's middle is at (x, y): the ground there (and right around) is walkable.</summary>
    /// <returns>How many cells were cleared.</returns>
    public int Walked(double x, double y, double around = WalkedClears)
    {
        int cleared = 0;
        int c0 = (int)Math.Floor((x - around - StartX) / Cell), c1 = (int)Math.Floor((x + around - StartX) / Cell);
        int r0 = (int)Math.Floor((y - around - StartY) / Cell), r1 = (int)Math.Floor((y + around - StartY) / Cell);
        for (int r = Math.Max(0, r0); r <= Math.Min(Rows - 1, r1); r++)
            for (int c = Math.Max(0, c0); c <= Math.Min(Columns - 1, c1); c++)
            {
                double cx = StartX + (c + 0.5) * Cell - x, cy = StartY + (r + 0.5) * Cell - y;
                int i = r * Columns + c;
                if (_steep[i] && cx * cx + cy * cy <= (around + Cell * 0.5) * (around + Cell * 0.5))
                {
                    _steep[i] = false;
                    cleared++;
                }
            }
        SteepCells -= cleared;
        return cleared;
    }

    /// <summary>A straight line from a to b walked (a link of walked cells): cleared along it.</summary>
    public int WalkedLine(double ax, double ay, double bx, double by, double around)
    {
        double length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        int steps = Math.Max(1, (int)Math.Ceiling(length / Cell)), cleared = 0;
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            cleared += Walked(ax + (bx - ax) * t, ay + (by - ay) * t, around);
        }
        return cleared;
    }

    /// <summary>Whether the straight way from a to b keeps off steep ground (the first <paramref name="leave"/> units don't count:
    /// where the character stands can't be too steep).</summary>
    public bool LineClear(double ax, double ay, double bx, double by, double leave = 60)
    {
        double length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        if (length <= leave)
            return true;
        int steps = Math.Max(1, (int)Math.Ceiling(length / (Cell * 0.7)));
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            if (t * length >= leave && Steep(ax + (bx - ax) * t, ay + (by - ay) * t))
                return false;
        }
        return true;
    }
}
