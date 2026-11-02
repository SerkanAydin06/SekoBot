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

        public void Fill(int x0, int y0, int w, int h)
        {
            for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                int i = (y * W + x) * 4;
                Data[i] = 40; Data[i + 1] = 40; Data[i + 2] = 220;
            }
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
        c.Fill(520, 250, 100, 12); // solid red banner: not text
        c.Name(380, 250, 6);     // own character's name, in the middle
        c.Fill(20, 560, 120, 6); // HP bar in the HUD
        using var frame = c.ToFrame();
        var names = NameFinder.Find(frame);
        Assert.Equal(2, names.Count);
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
}
