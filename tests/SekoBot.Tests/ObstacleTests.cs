using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Trees and rocks the bot ran into, remembered per map between sessions.</summary>
public class ObstacleTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 1, 30, 0);
    private static readonly Vec3 Me = new(0, 0, 0);

    [Fact]
    public void A_spot_run_into_twice_is_an_obstacle_on_that_map_only()
    {
        var map = new ObstacleMap();
        map.Stuck("c3", 500, 0, Now);
        Assert.Empty(map.Near("c3", Me, 1500));                 // once: maybe a mob stood there
        map.Stuck("c3", 540, 30, Now);
        var o = Assert.Single(map.Near("c3", Me, 1500));
        Assert.InRange(o.X, 500, 540);
        Assert.Empty(map.Near("c4", Me, 1500));
        Assert.Empty(map.Near("c3", new Vec3(5000, 0, 0), 1500)); // far away: not looked at
    }

    [Fact]
    public void Walking_freely_over_a_spot_wears_it_off()
    {
        var map = new ObstacleMap();
        map.Stuck("c3", 500, 0, Now);
        map.Stuck("c3", 500, 0, Now);
        map.Walked("c3", new Vec3(520, 10, 0));
        Assert.Empty(map.Near("c3", Me, 1500));
        map.Walked("c3", new Vec3(500, 0, 0));
        Assert.Equal(0, map.Count("c3"));
    }

    [Fact]
    public void Obstacles_are_kept_between_sessions()
    {
        string path = Path.Combine(Path.GetTempPath(), $"engeller-{Guid.NewGuid():N}.json");
        try
        {
            var map = new ObstacleMap();
            map.Stuck("c3", 500, 0, Now);
            map.Stuck("c3", 500, 0, Now);
            Assert.True(map.Changed);
            map.Save(path);
            Assert.False(map.Changed);
            Assert.Single(ObstacleMap.Load(path).Near("c3", Me, 1500));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Yaw 0: the camera looks along +X, so world +X is "screen up" and world +Y is "screen right".
    private static readonly CameraView Camera = new(new Vec3(0, 0, 1000), -90, 0, 0, 60);

    [Fact]
    public void Walking_bends_around_a_known_obstacle_ahead()
    {
        var (x, y) = Brain.AroundObstacles([new Vec3(300, 10, 0)], Me, Camera, 0, -100);   // walking screen up
        Assert.True(y < 0);                    // still going on
        Assert.True(Math.Abs(x) > 30);         // but sideways past it
    }

    [Fact]
    public void Obstacles_behind_or_beside_the_way_change_nothing()
    {
        Assert.Equal((0.0, -100.0), Brain.AroundObstacles([new Vec3(-300, 0, 0)], Me, Camera, 0, -100));  // behind
        Assert.Equal((0.0, -100.0), Brain.AroundObstacles([new Vec3(300, 400, 0)], Me, Camera, 0, -100)); // 4 m aside
        Assert.Equal((0.0, -100.0), Brain.AroundObstacles([], Me, Camera, 0, -100));
    }

    [Fact]
    public void A_mob_behind_a_known_obstacle_is_not_walked_to_straight()
    {
        var obstacle = new Vec3(500, 0, 0);
        Assert.True(Brain.WayBlocked([(obstacle, null, TimeSpan.Zero)], Me, new Vec3(1000, 0, 0)));
        Assert.False(Brain.WayBlocked([(obstacle, null, TimeSpan.Zero)], Me, new Vec3(0, 1000, 0)));
    }
}
