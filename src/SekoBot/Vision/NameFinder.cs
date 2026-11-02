using SekoBot.Capture;

namespace SekoBot.Vision;

/// <summary>
/// Finds red mob names floating above monsters. Text is a thin horizontal line of red
/// pixels with gaps between letters; a solid red strip (HP/target bar) is not text.
/// The mob itself stands right below its name.
/// </summary>
internal static class NameFinder
{
    private const int Cell = 4;

    /// <returns>Bounding boxes (client pixels) of red names, in no particular order.</returns>
    public static List<Rectangle> Find(Frame frame)
    {
        int w = frame.Width, h = frame.Height;
        int gw = w / Cell, gh = h / Cell;
        var names = new List<Rectangle>();
        if (gw < 8 || gh < 8)
            return names;

        // A cell is "red" when at least two of its sampled pixels are clearly red.
        var red = new bool[gw * gh];
        var data = frame.Data;
        for (int gy = 0; gy < gh; gy++)
        {
            double y01 = (gy + 0.5) / gh;
            for (int gx = 0; gx < gw; gx++)
            {
                if (Ignored((gx + 0.5) / gw, y01))
                    continue;
                int hits = 0;
                for (int sy = 1; sy < Cell; sy += 2)
                {
                    int row = (gy * Cell + sy) * frame.Stride;
                    for (int sx = 0; sx < Cell; sx++)
                    {
                        int i = row + (gx * Cell + sx) * 4;
                        if (BarFinder.Classify(data[i + 2], data[i + 1], data[i]) == 1)
                            hits++;
                    }
                }
                red[gy * gw + gx] = hits >= 2;
            }
        }

        // Group red cells into text lines: letters are bridged sideways, lines are not merged.
        var seen = new bool[red.Length];
        var stack = new Stack<int>();
        for (int start = 0; start < red.Length; start++)
        {
            if (!red[start] || seen[start])
                continue;
            int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                int cx = c % gw, cy = c / gw;
                left = Math.Min(left, cx); right = Math.Max(right, cx);
                top = Math.Min(top, cy); bottom = Math.Max(bottom, cy);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= gw || ny >= gh)
                        continue;
                    int n = ny * gw + nx;
                    if (red[n] && !seen[n])
                    {
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
            }
            int cw = right - left + 1, ch = bottom - top + 1;
            // 8–28 px tall, at least 16 px and clearly wider than tall, at most ~260 px.
            if (ch < 2 || ch > 7 || cw < 4 || cw < ch * 1.5 || cw > 65)
                continue;
            var box = new Rectangle(left * Cell, top * Cell, cw * Cell, ch * Cell);
            if (PixelDensity(frame, box) > 0.6)
                continue; // solid strip: a bar, not text
            names.Add(box);
        }
        return names;
    }

    /// <summary>HUD bars, minimap, and the player's own character/name in the middle.</summary>
    private static bool Ignored(double x, double y) =>
        y < 0.12 || y > 0.80 || (x > 0.78 && y < 0.32) || (Math.Abs(x - 0.5) < 0.07 && y > 0.30 && y < 0.62);

    private static double PixelDensity(Frame frame, Rectangle box)
    {
        int hits = 0, total = 0;
        var data = frame.Data;
        for (int y = box.Top; y < box.Bottom; y += 2)
        for (int x = box.Left; x < box.Right; x++)
        {
            int i = y * frame.Stride + x * 4;
            total++;
            if (BarFinder.Classify(data[i + 2], data[i + 1], data[i]) == 1)
                hits++;
        }
        return total == 0 ? 0 : (double)hits / total;
    }
}

internal static class MobLocator
{
    /// <summary>
    /// Where to click: below each red name (on the moving thing there, if any, else a fixed
    /// distance below), then the remaining moving things. Nearest to the character first.
    /// </summary>
    public static List<Point> Combine(IReadOnlyList<Rectangle> names, IReadOnlyList<Point> motion, int width, int height)
    {
        var center = new Point(width / 2, height / 2);
        double Dist(Point p) => Math.Sqrt(Math.Pow(p.X - center.X, 2) + Math.Pow(p.Y - center.Y, 2));
        int below = Math.Clamp((int)(height * 0.06), 25, 70);
        var used = new HashSet<Point>();
        var fromNames = new List<Point>();
        foreach (var name in names)
        {
            int cx = name.X + name.Width / 2;
            var under = motion
                .Where(m => !used.Contains(m) && Math.Abs(m.X - cx) < Math.Max(50, name.Width) &&
                            m.Y > name.Bottom && m.Y < name.Bottom + below * 3)
                .OrderBy(m => Math.Abs(m.X - cx) + Math.Abs(m.Y - (name.Bottom + below)))
                .ToList();
            if (under.Count > 0)
            {
                used.Add(under[0]);
                fromNames.Add(under[0]);
            }
            else
            {
                fromNames.Add(new Point(cx, Math.Min(height - 1, name.Bottom + below)));
            }
        }
        var rest = motion.Where(m => !used.Contains(m));
        return fromNames.OrderBy(Dist).Concat(rest.OrderBy(Dist)).Take(6).ToList();
    }
}
