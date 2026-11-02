using SekoBot.Capture;

namespace SekoBot.Vision;

/// <summary>
/// Notices a window opened over the game (e.g. the shop advert after a level-up). Such a
/// window has a long, perfectly flat title bar; the game world (grass, stone, trees, water)
/// is always textured. Measured on real 1920×1080 screenshots: the advert's title bar is a
/// 586 px flat run over 10+ rows, the plain game's longest flat run is 156 px at the edge.
/// </summary>
internal static class PopupDetector
{
    public static bool IsOpen(Frame frame)
    {
        int w = frame.Width, h = frame.Height;
        int minRun = (int)(w * 0.20);
        var data = frame.Data;
        int rows = 0;
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
            // A long flat stretch that doesn't touch the screen edges (dark vignette).
            bool bar = best >= minRun && bestStart > w * 0.05 && bestStart + best < w * 0.95;
            rows = bar ? rows + 1 : 0;
            if (rows >= 6)
                return true;
        }
        return false;
    }
}
