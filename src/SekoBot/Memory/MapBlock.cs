namespace SekoBot.Memory;

/// <summary>
/// Something on a map the character can't walk through (a wall, a house, a rock, a tree trunk, a
/// barrel), as the game's collision has it: its outline seen from above (a convex polygon, world
/// units) and how high it reaches. Saved in the map files, so plain settable properties.
/// </summary>
internal sealed class MapBlock
{
    /// <summary>The mesh's name in the game (e.g. "a1-038-wall-lin2_duel", "SM_Big_Rock_02").</summary>
    public string Name { get; set; } = "";
    /// <summary>The outline: x0, y0, x1, y1, … (counter-clockwise).</summary>
    public float[] Points { get; set; } = [];
    public float Bottom { get; set; }
    public float Top { get; set; }
    /// <summary>Only its bounding box was known (the game collides with the exact mesh): it may be smaller.</summary>
    public bool Rough { get; set; }
    /// <summary>The character was seen standing on it or walking through it (a floor, a bridge, a gate): no obstacle.</summary>
    public bool Passable { get; set; }

    public int Corners => Points.Length / 2;
    public double X(int i) => Points[2 * i];
    public double Y(int i) => Points[2 * i + 1];

    public (double X, double Y) Centre
    {
        get
        {
            double x = 0, y = 0;
            for (int i = 0; i < Corners; i++)
            {
                x += X(i);
                y += Y(i);
            }
            return Corners == 0 ? (0, 0) : (x / Corners, y / Corners);
        }
    }

    public (double MinX, double MinY, double MaxX, double MaxY) Box
    {
        get
        {
            double a = double.MaxValue, b = double.MaxValue, c = double.MinValue, d = double.MinValue;
            for (int i = 0; i < Corners; i++)
            {
                a = Math.Min(a, X(i));
                b = Math.Min(b, Y(i));
                c = Math.Max(c, X(i));
                d = Math.Max(d, Y(i));
            }
            return (a, b, c, d);
        }
    }

    public double Area
    {
        get
        {
            double sum = 0;
            for (int i = 0, j = Corners - 1; i < Corners; j = i++)
                sum += X(j) * Y(i) - X(i) * Y(j);
            return Math.Abs(sum) / 2;
        }
    }

    /// <summary>How wide the outline is at its narrowest (a wall: its thickness).</summary>
    public double Width
    {
        get
        {
            double best = double.MaxValue;
            for (int i = 0, j = Corners - 1; i < Corners; j = i++)
            {
                double ex = X(i) - X(j), ey = Y(i) - Y(j), l = Math.Sqrt(ex * ex + ey * ey);
                if (l < 1e-6)
                    continue;
                double far = 0;
                for (int k = 0; k < Corners; k++)
                    far = Math.Max(far, Math.Abs((X(k) - X(j)) * ey - (Y(k) - Y(j)) * ex) / l);
                best = Math.Min(best, far);
            }
            return best == double.MaxValue ? 0 : best;
        }
    }

    /// <summary>The point is inside the outline.</summary>
    public bool Contains(double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = Corners - 1; i < Corners; j = i++)
            if (Y(i) > y != Y(j) > y && x < (X(j) - X(i)) * (y - Y(i)) / (Y(j) - Y(i)) + X(i))
                inside = !inside;
        return inside;
    }

    /// <summary>How far the point is from the outline (0 inside).</summary>
    public double Distance(double x, double y)
    {
        if (Corners == 0)
            return double.MaxValue;
        if (Corners >= 3 && Contains(x, y))
            return 0;
        double best = double.MaxValue;
        for (int i = 0, j = Corners - 1; i < Corners; j = i++)
            best = Math.Min(best, SegmentDistance(x, y, X(j), Y(j), X(i), Y(i)));
        return best;
    }

    internal static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-9 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / l2, 0, 1);
        double x = ax + t * dx - px, y = ay + t * dy - py;
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>The outline (convex hull, counter-clockwise) of points seen from above.</summary>
    public static float[] Hull(IReadOnlyList<(double X, double Y)> points)
    {
        var p = points.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        if (p.Count < 3)
            return [.. p.SelectMany(q => new[] { (float)q.X, (float)q.Y })];
        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new (double X, double Y)[2 * p.Count];
        int k = 0;
        for (int i = 0; i < p.Count; i++)
        {
            while (k >= 2 && Cross(hull[k - 2], hull[k - 1], p[i]) <= 0)
                k--;
            hull[k++] = p[i];
        }
        for (int i = p.Count - 2, t = k + 1; i >= 0; i--)
        {
            while (k >= t && Cross(hull[k - 2], hull[k - 1], p[i]) <= 0)
                k--;
            hull[k++] = p[i];
        }
        return [.. hull.Take(k - 1).SelectMany(q => new[] { (float)Math.Round(q.X, 1), (float)Math.Round(q.Y, 1) })];
    }
}
