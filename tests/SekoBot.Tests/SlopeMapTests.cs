using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class SlopeMapTests
{
    /// <summary>A flat 6 km square (30-unit cells) with a steep ridge across x = 3000 and one pass at y 4500…4800.</summary>
    private static HeightGrid Ridge(bool pass = true)
    {
        const int n = 200;
        var h = new int[n * n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                double x = c * 30, y = r * 30;
                bool wall = x >= 2940 && x < 3060 && !(pass && y >= 4500 && y < 4800);
                h[r * n + c] = wall ? 3000 : 1000;
            }
        return new HeightGrid("t", 0, 0, 30, n, n, h);
    }

    [Fact]
    public void The_sides_of_a_steep_ridge_are_steep_the_flat_ground_not()
    {
        var s = new SlopeMap(Ridge());
        Assert.True(s.Steep(2950, 1000));
        Assert.False(s.Steep(1500, 1000));
        Assert.False(s.Steep(3000, 4650));    // the pass
        Assert.True(s.Steep(-10, 100));       // outside the grid
    }

    [Fact]
    public void The_way_over_a_ridge_leads_through_its_pass()
    {
        var t = new MapTerrain("t", [], DateTime.Now) { Slopes = new SlopeMap(Ridge()) };
        Vec3 from = new(1500, 1000, 1000), to = new(4500, 1000, 1000);
        Assert.False(t.LineClear(from, to));
        var way = t.WayTo(from, to);
        Assert.NotNull(way);
        Assert.Contains(way!, p => p.X > 2700 && p.X < 3300 && p.Y > 4300 && p.Y < 5000);
        // every leg keeps off the steep ground
        var here = from;
        foreach (var p in way!)
        {
            Assert.True(t.Slopes!.LineClear(here.X, here.Y, p.X, p.Y, 0) || here.DistanceXY(p) < 100);
            here = p;
        }
    }

    [Fact]
    public void No_pass_no_way()
    {
        var t = new MapTerrain("t", [], DateTime.Now) { Slopes = new SlopeMap(Ridge(pass: false)) };
        Assert.Null(t.WayTo(new Vec3(1500, 1000, 1000), new Vec3(4500, 1000, 1000)));
    }

    [Fact]
    public void Where_the_character_walked_is_walkable_from_then_on()
    {
        var s = new SlopeMap(Ridge(pass: false));
        int before = s.SteepCells;
        Assert.True(s.Steep(2950, 2000));
        s.WalkedLine(2800, 2000, 3200, 2000, 30);
        Assert.False(s.Steep(2950, 2000));
        Assert.True(s.SteepCells < before);
        var t = new MapTerrain("t", [], DateTime.Now) { Slopes = s };
        Assert.NotNull(t.WayTo(new Vec3(1500, 2000, 1000), new Vec3(4500, 2000, 1000)));
    }

    [Fact]
    public void The_grids_filler_is_no_ground()
    {
        var g = Ridge();
        g.Heights[50 * 200 + 50] = -1386;
        var s = new SlopeMap(g);
        Assert.True(s.Steep(50 * 30 + 5, 50 * 30 + 5));
        Assert.False(s.Steep(60 * 30 + 5, 50 * 30 + 5));
    }

    [Fact]
    public void A_crown_high_over_the_ground_there_is_not_in_the_way()
    {
        // Seen on c3: a tree's shape 560 over the ground (bottom 1308, ground 747) blocked the way of a character standing higher up.
        var flat = new int[200 * 200];
        Array.Fill(flat, 747);
        var crown = new MapBlock { Name = "Acacia_RT", Points = [2900, 900, 3100, 900, 3100, 1100, 2900, 1100], Bottom = 1308, Top = 2285 };
        Vec3 from = new(1500, 1000, 1194), to = new(4500, 1000, 1194);
        Assert.False(new MapTerrain("t", [crown], DateTime.Now).LineClear(from, to));
        Assert.True(new MapTerrain("t", [crown], DateTime.Now) { Slopes = new SlopeMap(new HeightGrid("t", 0, 0, 30, 200, 200, flat)) }.LineClear(from, to));
        // A rock on the ground still is.
        var rock = new MapBlock { Name = "Rock", Points = [2900, 900, 3100, 900, 3100, 1100, 2900, 1100], Bottom = 700, Top = 1100 };
        Assert.False(new MapTerrain("t", [rock], DateTime.Now) { Slopes = new SlopeMap(new HeightGrid("t", 0, 0, 30, 200, 200, flat)) }.LineClear(from, to));
    }

    [Fact]
    public void A_gentle_hill_is_walked_over()
    {
        const int n = 100;
        var h = new int[n * n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                h[r * n + c] = 1000 + (int)(Math.Max(0, 600 - Math.Sqrt((r - 50) * (r - 50) + (c - 50) * (c - 50)) * 30) * 0.5); // rise 0.5 per unit
        var s = new SlopeMap(new HeightGrid("t", 0, 0, 30, n, n, h));
        Assert.Equal(0, s.SteepCells);
    }
}
