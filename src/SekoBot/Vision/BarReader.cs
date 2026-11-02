using SekoBot.Capture;
using SekoBot.Config;

namespace SekoBot.Vision;

/// <summary>
/// Reads how full a horizontal bar is by comparing pixels against its
/// calibrated colour. Costs a few hundred pixel reads per bar.
/// </summary>
internal static class BarReader
{
    /// <returns>0..1 fill ratio, or null when the bar is outside the frame.</returns>
    public static double? Read(Frame frame, BarSpec spec)
    {
        var area = spec.Area.ToPixels(frame.Width, frame.Height);
        if (area.Width < 4 || area.Height < 1)
            return null;

        // Sample three rows around the middle; a column counts as filled if
        // the majority of its samples match.
        int[] rows =
        [
            area.Top + area.Height / 2,
            area.Top + Math.Max(0, area.Height / 2 - Math.Max(1, area.Height / 4)),
            area.Top + Math.Min(area.Height - 1, area.Height / 2 + Math.Max(1, area.Height / 4))
        ];
        int tolerance = spec.Tolerance * spec.Tolerance;
        int filled = 0;
        for (int x = area.Left; x < area.Right; x++)
        {
            int hits = 0;
            foreach (int y in rows)
            {
                var (b, g, r) = frame.Pixel(x, y);
                int dr = r - spec.R, dg = g - spec.G, db = b - spec.B;
                if (dr * dr + dg * dg + db * db <= tolerance)
                    hits++;
            }
            if (hits >= 2)
                filled++;
        }
        return (double)filled / area.Width;
    }

    /// <summary>
    /// Calibration: the bar is assumed full. Uses the most saturated half of
    /// the pixels so the frame/border colour does not dilute the result.
    /// </summary>
    public static BarSpec Calibrate(Frame frame, Rectangle area)
    {
        area = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height));
        var samples = new List<(int Sat, byte R, byte G, byte B)>();
        for (int y = area.Top; y < area.Bottom; y++)
        for (int x = area.Left; x < area.Right; x++)
        {
            var (b, g, r) = frame.Pixel(x, y);
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            samples.Add((max - min, r, g, b));
        }
        if (samples.Count == 0)
            throw new ArgumentException("Seçilen alan boş.");
        var best = samples.OrderByDescending(s => s.Sat).Take(Math.Max(1, samples.Count / 2)).ToArray();
        return new BarSpec
        {
            Area = NormRect.FromPixels(area, frame.Width, frame.Height),
            R = (byte)best.Average(s => s.R),
            G = (byte)best.Average(s => s.G),
            B = (byte)best.Average(s => s.B)
        };
    }
}
