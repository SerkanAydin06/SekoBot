using SekoBot.Capture;

namespace SekoBot.Vision;

/// <summary>
/// Finds red mob names floating above monsters ("Lv.3 Kurt": red text, often on a dark red
/// label). A name is a short horizontal block of red, 8–28 px tall; a thin solid strip is a bar.
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
            // Names sit on a dark red label, so a filled box is fine; only a thin solid strip
            // (≤ 8 px, a bar) is not a name.
            if (ch <= 2 && PixelDensity(frame, box) > 0.6)
                continue;
            names.Add(box);
        }
        return names;
    }

    /// <summary>
    /// Screen parts that are UI, measured on a real 1920×1080 screenshot: the target bar at the
    /// top, the skill/HP bar at the bottom (mobs walk right down to ~90 %), the minimap with
    /// its texts (top right) and the red "Görevler"/"Mesaj" buttons (top left). The middle is
    /// NOT ignored: a mob next to the character must be seen (player names aren't red).
    /// </summary>
    private static bool Ignored(double x, double y) =>
        y < 0.08 || y > 0.91 || (x > 0.78 && y < 0.38) || (x < 0.11 && y < 0.50);

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
    /// Where to click: below each red name (on the moving thing there, else on the mob's body
    /// found under the name, else a fixed distance below), then the remaining moving things.
    /// Nearest to the character first.
    /// </summary>
    public static List<Point> Combine(IReadOnlyList<Rectangle> names, IReadOnlyList<Point> motion, int width, int height,
        Func<Rectangle, Point?>? bodyBelow = null)
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
                fromNames.Add(bodyBelow?.Invoke(name) ?? new Point(cx, Math.Min(height - 1, name.Bottom + below)));
            }
        }
        var rest = motion.Where(m => !used.Contains(m));
        return fromNames.OrderBy(Dist).Concat(rest.OrderBy(Dist)).Take(6).ToList();
    }

    /// <summary>
    /// The mob's body under its name: in a band below the name, the ground is the typical
    /// (median) colour; pixels clearly different from it are the mob. Returns the centre of
    /// the topmost such blob (the nearest one under the name), or null if nothing stands out.
    /// Red pixels are skipped so another name lower down doesn't count as a body.
    /// </summary>
    public static Point? BodyBelow(Frame frame, Rectangle name)
    {
        int half = Math.Max(30, name.Width / 2);
        int cx = name.X + name.Width / 2;
        int x0 = Math.Max(0, cx - half), x1 = Math.Min(frame.Width, cx + half);
        int y0 = Math.Min(frame.Height, name.Bottom + 3);
        int y1 = Math.Min((int)(frame.Height * 0.82), name.Bottom + (int)(frame.Height * 0.15));
        if (x1 - x0 < 10 || y1 - y0 < 10)
            return null;

        var data = frame.Data;
        var samples = new List<(int X, int Y, int R, int G, int B)>();
        for (int y = y0; y < y1; y += 2)
        for (int x = x0; x < x1; x += 2)
        {
            int i = y * frame.Stride + x * 4;
            byte b = data[i], g = data[i + 1], r = data[i + 2];
            if (BarFinder.Classify(r, g, b) == 1)
                continue;
            samples.Add((x, y, r, g, b));
        }
        if (samples.Count < 30)
            return null;
        int Median(Func<(int X, int Y, int R, int G, int B), int> channel)
        {
            var values = samples.Select(channel).ToArray();
            Array.Sort(values);
            return values[values.Length / 2];
        }
        int mr = Median(s => s.R), mg = Median(s => s.G), mb = Median(s => s.B);
        var body = samples.Where(s =>
        {
            int dr = s.R - mr, dg = s.G - mg, db = s.B - mb;
            return dr * dr + dg * dg + db * db > 55 * 55;
        }).ToList();
        if (body.Count < 12)
            return null;
        int top = body.Min(s => s.Y);
        int limit = top + Math.Max(20, (int)(frame.Height * 0.08));
        var nearest = body.Where(s => s.Y < limit).ToList();
        return new Point((int)nearest.Average(s => s.X), (int)nearest.Average(s => s.Y));
    }
}
