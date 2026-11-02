using SekoBot.Capture;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class NameFinderTests
{
    private const int W = 800, H = 600;

    private sealed class Canvas
    {
        public readonly byte[] Data = new byte[W * H * 4];

        public Canvas()
        {
            // Greenish textured ground: never "red".
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                byte v = (byte)(60 + ((x * 37 + y * 91) ^ (x * y)) % 120);
                Data[i] = (byte)(v / 2); Data[i + 1] = v; Data[i + 2] = (byte)(v * 3 / 4);
            }
        }

        public void Fill(int x0, int y0, int w, int h, byte r = 220, byte g = 40, byte b = 40)
        {
            for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                int i = (y * W + x) * 4;
                Data[i] = b; Data[i + 1] = g; Data[i + 2] = r;
            }
        }

        /// <summary>Like in the game: a dark red label box with light pink text inside.</summary>
        public void Label(int x0, int y0, int w)
        {
            Fill(x0, y0, w, 16, 120, 30, 30);
            for (int k = 0; k + 7 < w - 4; k += 9)
                Fill(x0 + 3 + k, y0 + 3, 5, 10, 250, 150, 150);
        }

        /// <summary>A red name: "F"-like letters, 10 px tall, 9 px apart.</summary>
        public void Name(int x0, int y0, int letters)
        {
            for (int k = 0; k < letters; k++)
            {
                int lx = x0 + k * 9;
                Fill(lx, y0, 2, 10);
                Fill(lx, y0, 7, 2);
                Fill(lx, y0 + 4, 5, 2);
            }
        }

        public Frame ToFrame() => Frame.FromArray(W, H, Data);
    }

    [Fact]
    public void Finds_red_names_but_not_bars_or_own_name()
    {
        var c = new Canvas();
        c.Name(250, 150, 8);     // mob
        c.Name(600, 420, 5);     // another mob
        c.Fill(520, 250, 100, 6);  // thin solid red strip: a bar, not a name
        c.Label(150, 380, 40);     // "Kurt" on its dark red label
        c.Name(380, 250, 6);     // own character's name, in the middle
        c.Fill(20, 560, 120, 6); // HP bar in the HUD
        using var frame = c.ToFrame();
        var names = NameFinder.Find(frame);
        Assert.Equal(3, names.Count);
        Assert.Contains(names, n => Math.Abs(n.X - 150) <= 4 && Math.Abs(n.Y - 380) <= 4);
        Assert.Contains(names, n => Math.Abs(n.X - 250) <= 4 && Math.Abs(n.Y - 150) <= 4 && n.Width >= 64);
        Assert.Contains(names, n => Math.Abs(n.X - 600) <= 4 && Math.Abs(n.Y - 420) <= 4);
    }

    [Fact]
    public void Plain_ground_has_no_names()
    {
        using var frame = new Canvas().ToFrame();
        Assert.Empty(NameFinder.Find(frame));
    }

    [Fact]
    public void Click_goes_below_the_name_onto_the_moving_mob_when_there_is_one()
    {
        var name = new Rectangle(250, 150, 72, 12);
        // Moving thing just below the name, plus an unrelated one far away.
        var motion = new List<Point> { new(700, 450), new(290, 200) };
        var spots = MobLocator.Combine([name], motion, W, H);
        Assert.Equal(new Point(290, 200), spots[0]);
        Assert.Equal(new Point(700, 450), spots[1]);
        Assert.Equal(2, spots.Count);
    }

    [Fact]
    public void Without_motion_click_a_fixed_distance_below_the_name()
    {
        var name = new Rectangle(250, 150, 72, 12);
        var spots = MobLocator.Combine([name], [], W, H);
        var spot = Assert.Single(spots);
        Assert.Equal(286, spot.X);
        Assert.InRange(spot.Y, name.Bottom + 25, name.Bottom + 70);
    }

    [Fact]
    public void Names_come_before_motion_nearest_first()
    {
        var near = new Rectangle(330, 200, 60, 12);
        var far = new Rectangle(60, 100, 60, 12);
        var spots = MobLocator.Combine([far, near], [new(420, 330)], W, H);
        Assert.Equal(3, spots.Count);
        Assert.Equal(360, spots[0].X); // near name
        Assert.Equal(90, spots[1].X);  // far name
        Assert.Equal(new Point(420, 330), spots[2]); // plain motion last
    }

    /// <summary>Calm grass (little noise), so the mob's body stands out from it.</summary>
    private static Frame Grass(Action<byte[]>? draw = null)
    {
        var data = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int i = (y * W + x) * 4;
            int n = ((x * 7 + y * 13) ^ (x * y)) % 11 - 5;
            data[i] = (byte)(40 + n); data[i + 1] = (byte)(120 + n); data[i + 2] = (byte)(60 + n);
        }
        draw?.Invoke(data);
        return Frame.FromArray(W, H, data);
    }

    private static void Block(byte[] data, int x0, int y0, int w, int h, byte gray)
    {
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++)
        {
            int i = (y * W + x) * 4;
            data[i] = gray; data[i + 1] = gray; data[i + 2] = gray;
        }
    }

    [Fact]
    public void Finds_the_mob_body_under_its_name()
    {
        var name = new Rectangle(250, 150, 72, 12);
        // A dark grey "wolf" a little below the name.
        using var frame = Grass(d => Block(d, 260, 190, 50, 30, 50));
        var body = MobLocator.BodyBelow(frame, name);
        Assert.NotNull(body);
        Assert.InRange(body!.Value.X, 265, 305);
        Assert.InRange(body.Value.Y, 192, 218);

        // Used for the click when nothing moves there.
        var spots = MobLocator.Combine([name], [], W, H, n => MobLocator.BodyBelow(frame, n));
        Assert.Equal(body.Value, spots[0]);
    }

    [Fact]
    public void No_body_on_empty_grass()
    {
        using var frame = Grass();
        Assert.Null(MobLocator.BodyBelow(frame, new Rectangle(250, 150, 72, 12)));
    }
}
