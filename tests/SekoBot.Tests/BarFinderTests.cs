using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class BarFinderTests
{
    private const int W = 800, H = 600;
    private static readonly Rectangle HpArea = new(20, 560, 120, 6);
    private static readonly Rectangle MpArea = new(20, 570, 120, 6);
    private static readonly Rectangle TargetArea = new(300, 20, 200, 8);

    /// <summary>Game-like frame: textured greenish ground, red HP and blue MP bars at the bottom left
    /// (filled to the given ratio, dark empty part), optional target bar, plus red/blue noise.</summary>
    private static Frame GameFrame(double hp = 1, double mp = 1, bool target = false)
    {
        var data = new byte[W * H * 4];
        var random = new Random(42);
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = (byte)random.Next(20, 70);      // B
            data[i + 1] = (byte)random.Next(60, 110); // G
            data[i + 2] = (byte)random.Next(40, 90);  // R
        }
        void Fill(Rectangle r, double ratio, (byte R, byte G, byte B) color)
        {
            int fillTo = r.Left + (int)Math.Round(r.Width * ratio);
            for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int i = (y * W + x) * 4;
                (byte R, byte G, byte B) c = x < fillTo ? color : ((byte)20, (byte)15, (byte)15);
                // Slight vertical gradient like the real bars.
                int shade = (y - r.Top) * 6;
                data[i] = (byte)Math.Max(0, c.B - shade);
                data[i + 1] = (byte)Math.Max(0, c.G - shade);
                data[i + 2] = (byte)Math.Max(0, c.R - shade);
            }
        }
        Fill(HpArea, hp, (210, 30, 25));
        Fill(MpArea, mp, (35, 80, 215));
        if (target)
            Fill(TargetArea, 1, (200, 25, 25));
        // Noise: a red mob name (letters with gaps) and a small blue icon.
        for (int x = 400; x < 470; x += 6)
            Fill(new Rectangle(x, 300, 3, 9), 1, (220, 30, 30));
        Fill(new Rectangle(600, 500, 12, 12), 1, (30, 60, 220));
        return Frame.FromArray(W, H, data);
    }

    private static void AssertNear(Rectangle expected, Rectangle? actual, int widthTolerance = 3)
    {
        Assert.NotNull(actual);
        var a = actual!.Value;
        Assert.InRange(a.Left, expected.Left - 2, expected.Left + 2);
        Assert.InRange(a.Top, expected.Top - 2, expected.Top + 2);
        Assert.InRange(a.Width, expected.Width - widthTolerance, expected.Width + widthTolerance);
    }

    [Fact]
    public void Finds_target_bar_and_ignores_character_bars()
    {
        using var frame = GameFrame(target: true);
        var found = BarFinder.Find(frame);
        AssertNear(TargetArea, found.Target);
        // The character's HP/MP bars at the bottom are never looked at (they come from memory).
        Assert.DoesNotContain(found.Candidates, c => c.Area.IntersectsWith(HpArea) || c.Area.IntersectsWith(MpArea));
    }

    [Fact]
    public void No_target_bar_when_nothing_is_selected()
    {
        using var frame = GameFrame();
        Assert.Null(BarFinder.Find(frame).Target);
    }

    [Fact]
    public void Automatically_tracks_only_target_bar()
    {
        var profile = new BotProfile();
        var auto = new AutoBars();
        using var frame = GameFrame(target: true);
        auto.Apply(profile, BarFinder.Find(frame), frame);
        Assert.NotNull(profile.TargetBar);
        Assert.InRange(BarReader.Read(frame, profile.TargetBar!)!.Value, 0.90, 1.0);
    }

    [Fact]
    public void Manual_target_bar_is_preserved()
    {
        var manual = new BarSpec { Area = NormRect.FromPixels(TargetArea, W, H) };
        var profile = new BotProfile { TargetBar = manual };
        var auto = new AutoBars();
        using var frame = GameFrame(target: true);
        auto.Apply(profile, BarFinder.Find(frame), frame);
        Assert.Same(manual, profile.TargetBar);
    }
}
