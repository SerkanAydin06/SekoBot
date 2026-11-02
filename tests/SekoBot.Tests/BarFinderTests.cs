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
    public void Finds_hp_mp_and_target_bars()
    {
        using var frame = GameFrame(target: true);
        var found = BarFinder.Find(frame);
        AssertNear(HpArea, found.Hp);
        AssertNear(MpArea, found.Mp);
        AssertNear(TargetArea, found.Target);
    }

    [Fact]
    public void No_target_bar_when_nothing_is_selected()
    {
        using var frame = GameFrame();
        Assert.Null(BarFinder.Find(frame).Target);
    }

    [Fact]
    public void Auto_bars_read_correct_values()
    {
        var profile = new BotProfile();
        var auto = new AutoBars();
        using (var full = GameFrame())
            Assert.NotEmpty(auto.Apply(profile, BarFinder.Find(full), full));
        Assert.True(profile.HpBar!.Auto);
        using var half = GameFrame(hp: 0.5, mp: 0.25);
        Assert.InRange(BarReader.Read(half, profile.HpBar)!.Value, 0.42, 0.58);
        Assert.InRange(BarReader.Read(half, profile.MpBar!)!.Value, 0.18, 0.32);
    }

    [Fact]
    public void Bar_found_while_not_full_grows_later_and_never_shrinks()
    {
        var profile = new BotProfile();
        var auto = new AutoBars();
        using (var half = GameFrame(hp: 0.5))
            auto.Apply(profile, BarFinder.Find(half), half);
        Assert.InRange(profile.HpBar!.Area.ToPixels(W, H).Width, 55, 65);

        using (var full = GameFrame())
            auto.Apply(profile, BarFinder.Find(full), full);
        AssertNear(HpArea, profile.HpBar!.Area.ToPixels(W, H));

        using (var low = GameFrame(hp: 0.3))
            auto.Apply(profile, BarFinder.Find(low), low);
        AssertNear(HpArea, profile.HpBar!.Area.ToPixels(W, H));
    }

    [Fact]
    public void Manually_marked_bar_is_never_replaced()
    {
        var manual = new BarSpec { Area = NormRect.FromPixels(new Rectangle(500, 100, 50, 5), W, H) };
        var profile = new BotProfile { HpBar = manual };
        var auto = new AutoBars();
        for (int i = 0; i < 20; i++)
            using (var frame = GameFrame())
                auto.Apply(profile, BarFinder.Find(frame), frame);
        Assert.Same(manual, profile.HpBar);
        Assert.NotNull(profile.MpBar);
    }
}
