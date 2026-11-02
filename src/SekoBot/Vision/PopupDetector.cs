using SekoBot.Capture;

namespace SekoBot.Vision;

/// <summary>
/// Notices a window opened over the game. Measured on real 1920×1080 screenshots:
/// <list type="bullet">
/// <item>the shop advert after a level-up has a title bar: a flat run over 20 % of the width,
/// not touching the edges, for 6+ rows, and all one colour (each channel varies ≤ 25);</item>
/// <item>the Esc system menu blurs the game (96 % of the rows have a flat run over a third of
/// the width) and has a near-black button panel in the middle (83 % black).</item>
/// </list>
/// Big blurry leaves right in front of the top-down camera are smooth too, but their long
/// runs drift in colour (no one-colour bar) and the middle isn't black (19 %), so they don't
/// count — they used to, and the bot kept pressing Esc under trees.
/// </summary>
internal static class PopupDetector
{
    public static bool IsOpen(Frame frame)
    {
        int w = frame.Width, h = frame.Height;
        int barRun = (int)(w * 0.20), blurRun = (int)(w * 0.30);
        var data = frame.Data;
        int barRows = 0, blurRows = 0, rows = 0;
        for (int y = (int)(h * 0.15); y < (int)(h * 0.85); y++)
        {
            int row = y * frame.Stride;
            int best = 0, bestStart = 0, run = 0, start = 0;
            for (int x = 0; x < w - 1; x++)
            {
                int i = row + x * 4;
                int diff = Math.Abs(data[i] - data[i + 4]) + Math.Abs(data[i + 1] - data[i + 5]) +
                           Math.Abs(data[i + 2] - data[i + 6]);
                if (diff <= 12)
                {
                    if (run == 0) start = x;
                    if (++run > best) { best = run; bestStart = start; }
                }
                else
                {
                    run = 0;
                }
            }
            bool bar = best >= barRun && bestStart > w * 0.05 && bestStart + best < w * 0.95 &&
                       OneColour(data, row, bestStart, best + 1);
            barRows = bar ? barRows + 1 : 0;
            if (barRows >= 6)
                return true;
            rows++;
            if (best >= blurRun)
                blurRows++;
        }
        // Blurred game behind a menu: most rows smooth and a black panel in the middle.
        return rows > 0 && blurRows * 100 >= rows * 40 && DarkShare(frame, 0.40, 0.60, 0.05, 0.92) >= 0.5;
    }

    /// <summary>Each channel stays within 25 over the run: a flat bar, not a smooth gradient.</summary>
    private static bool OneColour(byte[] data, int row, int start, int length)
    {
        int minB = 255, maxB = 0, minG = 255, maxG = 0, minR = 255, maxR = 0;
        for (int x = start; x < start + length; x++)
        {
            int i = row + x * 4;
            minB = Math.Min(minB, data[i]); maxB = Math.Max(maxB, data[i]);
            minG = Math.Min(minG, data[i + 1]); maxG = Math.Max(maxG, data[i + 1]);
            minR = Math.Min(minR, data[i + 2]); maxR = Math.Max(maxR, data[i + 2]);
        }
        return maxB - minB <= 25 && maxG - minG <= 25 && maxR - minR <= 25;
    }

    private static double DarkShare(Frame frame, double x0, double x1, double y0, double y1)
    {
        int left = (int)(frame.Width * x0), right = (int)(frame.Width * x1);
        int top = (int)(frame.Height * y0), bottom = (int)(frame.Height * y1);
        var data = frame.Data;
        int dark = 0, total = 0;
        for (int y = top; y < bottom; y += 2)
        for (int x = left; x < right; x += 2)
        {
            int i = y * frame.Stride + x * 4;
            total++;
            if (data[i] < 30 && data[i + 1] < 30 && data[i + 2] < 30)
                dark++;
        }
        return total == 0 ? 0 : (double)dark / total;
    }
}
