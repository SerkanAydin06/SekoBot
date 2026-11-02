using SekoBot.Capture;

namespace SekoBot.Vision;

/// <summary>
/// Notices a window opened over the game. Two signs, measured on real 1920×1080 screenshots:
/// <list type="bullet">
/// <item>a long, perfectly flat title bar (the shop advert after a level-up: a 586 px flat run
/// over 10+ rows; the plain game's longest flat run is 156 px, at the screen edge);</item>
/// <item>a blurred game behind a menu (the Esc system menu: 97 % of rows have a flat run longer
/// than a third of the screen; the plain game 0 %, the advert 6 %).</item>
/// </list>
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
            // A long flat stretch that doesn't touch the screen edges (dark vignette): a title bar.
            bool bar = best >= barRun && bestStart > w * 0.05 && bestStart + best < w * 0.95;
            barRows = bar ? barRows + 1 : 0;
            if (barRows >= 6)
                return true;
            rows++;
            if (best >= blurRun)
                blurRows++;
        }
        // Most of the screen smooth: the game is blurred behind a menu.
        return rows > 0 && blurRows * 100 >= rows * 40;
    }
}
