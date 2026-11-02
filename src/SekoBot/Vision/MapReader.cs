using SekoBot.Capture;

namespace SekoBot.Vision;

/// <param name="Panel">The big map's picture (under its title bar), client pixels.</param>
/// <param name="Arrow">Our position on the map, 0..1 of the panel width on both axes; null when
/// the arrow wasn't found.</param>
internal readonly record struct MapReading(Rectangle Panel, PointF? Arrow);

/// <summary>
/// Reads the big map (opened with M). Measured on a real 1920×1080 screenshot:
/// <list type="bullet">
/// <item>its title bar ("Genel Görünüm Haritası") is dark teal (≈20, 50, 50: green = blue,
/// little red), 370 px wide, for 10+ rows; the game world has no such teal;</item>
/// <item>our character is the yellow-orange arrow (median 216, 165, 58) inside the map.
/// The same colour also shows on armour and UI icons, so the arrow is only looked for inside
/// the map panel.</item>
/// </list>
/// </summary>
internal static class MapReader
{
    public static MapReading? Read(Frame frame)
    {
        if (FindPanel(frame) is not Rectangle panel)
            return null;
        return new MapReading(panel, FindArrow(frame, panel));
    }

    private static bool IsTeal(int r, int g, int b) =>
        r * 100 < g * 60 && Math.Abs(g - b) <= 12 && g >= 20 && g <= 75;

    internal static bool IsArrow(int r, int g, int b) =>
        r >= 150 && g * 100 >= r * 60 && g * 100 <= r * 88 && b * 100 <= r * 45 && r - b >= 100;

    private static Rectangle? FindPanel(Frame frame)
    {
        int w = frame.Width, h = frame.Height;
        var data = frame.Data;
        var rows = new List<(int Y, int Left, int Right)>();
        for (int y = (int)(h * 0.03); y < (int)(h * 0.6); y++)
        {
            int row = y * frame.Stride;
            // Longest stretch of teal, allowing gaps of up to 40 px for the title text.
            int bestCount = 0, bestLeft = 0, bestRight = 0, count = 0, left = -1, prev = -1000;
            for (int x = 0; x < w; x++)
            {
                int i = row + x * 4;
                if (!IsTeal(data[i + 2], data[i + 1], data[i]))
                    continue;
                if (x - prev > 40)
                {
                    count = 0;
                    left = x;
                }
                count++;
                prev = x;
                if (count > bestCount) { bestCount = count; bestLeft = left; bestRight = x; }
            }
            if (bestCount >= w * 0.12 && bestRight - bestLeft <= w * 0.30)
                rows.Add((y, bestLeft, bestRight));
        }
        // The first group of 6+ (nearly) consecutive rows is the title bar.
        var group = new List<(int Y, int Left, int Right)>();
        foreach (var r in rows)
        {
            if (group.Count > 0 && r.Y - group[^1].Y > 2)
            {
                if (group.Count >= 6)
                    break;
                group.Clear();
            }
            group.Add(r);
        }
        if (group.Count < 6)
            return null;
        int x0 = Median(group.Select(g => g.Left)), x1 = Median(group.Select(g => g.Right));
        int top = group[^1].Y, height = (int)((x1 - x0) * 1.4);
        return Rectangle.Intersect(new Rectangle(x0, top, x1 - x0, height), new Rectangle(0, 0, w, h));
    }

    private static PointF? FindArrow(Frame frame, Rectangle panel)
    {
        var data = frame.Data;
        var points = new List<Point>();
        for (int y = panel.Top; y < panel.Bottom; y++)
        for (int x = panel.Left; x < panel.Right; x++)
        {
            int i = y * frame.Stride + x * 4;
            if (IsArrow(data[i + 2], data[i + 1], data[i]))
                points.Add(new Point(x, y));
        }
        if (points.Count < 8 || points.Count > 4000)
            return null;
        // The densest 25×25 spot is the arrow.
        Point best = default;
        int bestCount = 0;
        foreach (var p in points)
        {
            int n = points.Count(q => Math.Abs(q.X - p.X) <= 12 && Math.Abs(q.Y - p.Y) <= 12);
            if (n > bestCount) { bestCount = n; best = p; }
        }
        if (bestCount < 8)
            return null;
        var near = points.Where(q => Math.Abs(q.X - best.X) <= 12 && Math.Abs(q.Y - best.Y) <= 12).ToList();
        double ax = near.Average(q => q.X), ay = near.Average(q => q.Y);
        return new PointF((float)((ax - panel.Left) / panel.Width), (float)((ay - panel.Top) / panel.Width));
    }

    private static int Median(IEnumerable<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }
}
