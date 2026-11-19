using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class WalkedPathsTests
{
    [Fact]
    public void A_way_once_walked_is_followed_and_a_jump_is_no_way()
    {
        var paths = new WalkedPaths();
        // Up an L-shaped road: east, then north.
        for (int x = 0; x <= 4000; x += 150)
            paths.Saw("c3", new Vec3(x, 0, 500));
        for (int y = 0; y <= 4000; y += 150)
            paths.Saw("c3", new Vec3(4000, y, 900));
        var way = paths.Way("c3", new Vec3(100, 100, 500), new Vec3(4000, 4000, 1200));
        Assert.NotNull(way);
        Assert.Equal(new Vec3(4000, 4000, 1200), way![^1]);
        // Through the corner, not across.
        Assert.Contains(way, p => p.X > 3800 && p.Y < 300);
        Assert.DoesNotContain(way, p => p.X < 3000 && p.Y > 1000);
        // Nothing walked near the goal: no way.
        Assert.Null(paths.Way("c3", new Vec3(0, 0, 500), new Vec3(20000, 20000, 500)));
        Assert.Null(paths.Way("c2", new Vec3(0, 0, 500), new Vec3(4000, 4000, 500)));

        // A teleport isn't walked.
        var jump = new WalkedPaths();
        jump.Saw("c3", new Vec3(0, 0, 0));
        jump.Saw("c3", new Vec3(5000, 0, 0));
        Assert.Equal(0, jump.Count("c3"));
    }

    [Fact]
    public void A_spot_the_character_got_stuck_at_takes_the_walked_way_around_it_or_none()
    {
        var paths = new WalkedPaths();
        // Two roads to the goal: straight east (short), and a detour north.
        for (int x = 0; x <= 4000; x += 150)
            paths.Saw("c3", new Vec3(x, 0, 0));
        for (int y = 0; y <= 2000; y += 150)
            paths.Saw("c3", new Vec3(0, y, 0));
        for (int x = 0; x <= 4000; x += 150)
            paths.Saw("c3", new Vec3(x, 2000, 0));
        for (int y = 2000; y >= 0; y -= 150)
            paths.Saw("c3", new Vec3(4000, y, 0));
        var from = new Vec3(100, 100, 0);
        var to = new Vec3(4000, 0, 0);
        Assert.DoesNotContain(paths.Way("c3", from, to)!, p => p.Y > 1000);
        // Stuck halfway on the short road: the detour.
        var stuck = new List<MapTerrain.Circle> { new(2000, 0, 90) };
        var around = paths.Way("c3", from, to, stuck);
        Assert.NotNull(around);
        Assert.Contains(around, p => p.Y > 1800);
        Assert.DoesNotContain(around, p => Math.Abs(p.X - 2000) < 300 && p.Y < 300);

        // Only one road, stuck on it: no walked way (the planned route is drawn instead).
        var one = new WalkedPaths();
        for (int x = 0; x <= 4000; x += 150)
            one.Saw("c3", new Vec3(x, 0, 0));
        Assert.Null(one.Way("c3", from, to, stuck));
    }

    [Fact]
    public void Sell_trips_that_fail_in_a_row_wait_longer()
    {
        Assert.Equal(10, BotEngine.SellWaitAfter(1).TotalMinutes);
        Assert.Equal(20, BotEngine.SellWaitAfter(2).TotalMinutes);
        Assert.Equal(40, BotEngine.SellWaitAfter(3).TotalMinutes);
        Assert.Equal(60, BotEngine.SellWaitAfter(6).TotalMinutes);
    }

    [Fact]
    public void Walked_ways_are_shared_through_the_file()
    {
        string file = Path.Combine(Path.GetTempPath(), $"yollar-{Guid.NewGuid():N}.json");
        try
        {
            var one = new WalkedPaths();
            for (int x = 0; x <= 2000; x += 150)
                one.Saw("c3", new Vec3(x, 0, 0));
            one.Save(file);
            var two = WalkedPaths.Load(file);
            Assert.Equal(one.Count("c3"), two.Count("c3"));
            Assert.NotNull(two.Way("c3", new Vec3(0, 0, 0), new Vec3(2000, 0, 0)));
        }
        finally { File.Delete(file); }
    }
}
