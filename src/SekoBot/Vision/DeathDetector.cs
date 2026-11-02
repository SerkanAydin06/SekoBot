using SekoBot.Capture;

namespace SekoBot.Vision;

/// <summary>
/// The death dialog: two black buttons near the top middle, "Burada yeniden başla" (grey
/// text until it can be used, then white) above "Şehirde yeniden başla" (white text).
/// Measured on a real 1920×1080 screenshot (fractions of the screen):
/// <list type="bullet">
/// <item>lower button (x .44–.56, y .215–.245): 85 % near-black, 7.4 % white text;</item>
/// <item>between the buttons (y .172–.200): black, no text — the Esc menu has its
/// "Oyun Seçenekleri" text there (9.4 % white);</item>
/// <item>under the dialog (y .29–.31): the game (9 % black) — the Esc menu goes on (72 %);</item>
/// <item>upper button text: grey (75, 75, 75) while locked, white once it can be clicked.</item>
/// </list>
/// </summary>
internal static class DeathDetector
{
    /// <summary>Where "Burada yeniden başla" is, in client pixels.</summary>
    public static Point ReviveHereButton(int width, int height) => new(width / 2, (int)(height * 0.144));

    public static (bool Dead, bool ReviveReady) Check(Frame frame)
    {
        var lower = Measure(frame, 0.44, 0.56, 0.215, 0.245);
        var gap = Measure(frame, 0.44, 0.56, 0.172, 0.200);
        var below = Measure(frame, 0.44, 0.56, 0.29, 0.31);
        bool dead = lower.Dark >= 0.6 && lower.White >= 0.02 && gap.White < 0.01 && below.Dark < 0.5;
        if (!dead)
            return (false, false);
        var upper = Measure(frame, 0.44, 0.56, 0.13, 0.16);
        return (true, upper.Dark >= 0.5 && upper.White >= 0.02);
    }

    private static (double Dark, double White) Measure(Frame frame, double x0, double x1, double y0, double y1)
    {
        int left = (int)(frame.Width * x0), right = (int)(frame.Width * x1);
        int top = (int)(frame.Height * y0), bottom = (int)(frame.Height * y1);
        var data = frame.Data;
        int dark = 0, white = 0, total = 0;
        for (int y = top; y < bottom; y++)
        for (int x = left; x < right; x++)
        {
            int i = y * frame.Stride + x * 4;
            int b = data[i], g = data[i + 1], r = data[i + 2];
            total++;
            if (r < 30 && g < 30 && b < 30) dark++;
            else if (r >= 200 && g >= 200 && b >= 200) white++;
        }
        return total == 0 ? (0, 0) : ((double)dark / total, (double)white / total);
    }
}
