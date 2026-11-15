using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
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

    // ── Held up: a tree only with no mob around; up a slope it is the slope ──

    private static readonly Vec3 Here = new(20000, 18000, 1900);
    private static readonly CameraView Down = new(Here with { Z = Here.Z + 1000 }, -80, 0, 0, 90); // +X is up (W)

    private static TargetInfo Mob(int id, double x, double y, double dz = Brain.MobHeightOffset, bool attacks = false, bool ranged = false, int exp = 400)
    {
        double d = Math.Sqrt(x * x + y * y);
        return new(id, new Point(960 + (int)(y / 5), 540 - (int)(x / 5)), d, 130, exp, 305, 305, 56, false, attacks,
            Position: new Vec3(Here.X + x, Here.Y + y, Here.Z + dz), Ranged: ranged);
    }

    private static (Brain, BotProfile, ObstacleMap) Ready()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PickUpDrops = false, PullGroups = false };
        var map = new ObstacleMap();
        var brain = new Brain(() => p) { Obstacles = map };
        brain.Start(TimeSpan.Zero);
        return (brain, p, map);
    }

    private static List<BotAction> Run(Brain brain, TargetInfo[] mobs, double from, double to, bool walking)
    {
        var all = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            all.AddRange(brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), 1920, 1080, 1, 1, 0, 1)
                { Targets = mobs, Position = Here, Camera = Down, Walking = walking, AttackPower = 189, MapName = "c3" }));
        return all;
    }

    [Fact]
    public void Held_up_in_the_open_notes_a_tree()
    {
        var (brain, _, map) = Ready();
        var actions = Run(brain, [Mob(1, 800, 0)], 0, 3000, walking: false);
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("takıldı"));
        Assert.Equal(1, map.Noted("c3"));
    }

    [Fact]
    public void Held_up_next_to_a_mob_is_no_tree()
    {
        var (brain, _, map) = Ready();
        var actions = Run(brain, [Mob(1, 800, 0, exp: 900), Mob(2, 250, 150, exp: 1)], 0, 3000, walking: false);   // a sleeping one right where we bumped
        Assert.True(actions.Any(a => a is NoteAction n && n.Text.Contains("takıldı")), string.Join(" | ", actions.Select(a => a.ToString())));
        Assert.Equal(0, map.Noted("c3"));
    }

    [Fact]
    public void Held_up_walking_up_to_a_mob_is_the_slope_not_a_tree()
    {
        var (brain, _, map) = Ready();
        Run(brain, [Mob(1, 800, 0, dz: 150)], 0, 3000, walking: false);                        // 2 m higher
        Assert.Equal(0, map.Noted("c3"));
    }

    [Fact]
    public void An_archer_on_a_ledge_is_on_another_level()
    {
        Assert.True(Brain.OnLedge(Mob(1, 400, 0, dz: 200), Here));      // 2.6 m up, 4 m away
        Assert.False(Brain.OnLedge(Mob(1, 400, 0), Here));              // flat
        Assert.False(Brain.OnLedge(Mob(1, 400, 0, dz: -118), Here));    // a slope (seen: −118 at 4 m)
        Assert.False(Brain.OnLedge(Mob(1, 2000, 0, dz: 300), Here));    // gently up, far away
    }

    [Fact]
    public void An_archer_shooting_from_a_ledge_is_not_walked_after()
    {
        TargetInfo[] flat = [Mob(1, 500, 0, attacks: true, ranged: true), Mob(2, -600, 0)];
        var (brain, p, _) = Ready();
        Assert.Contains(Run(brain, flat, 0, 1000, walking: true), a => a is KeyDownAction d && d.Key == p.Keys.MoveForward);
        TargetInfo[] ledge = [Mob(1, 500, 0, dz: 300, attacks: true, ranged: true), Mob(2, -600, 0)];
        var (up, p2, _) = Ready();
        var actions = Run(up, ledge, 0, 1000, walking: true);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p2.Keys.MoveForward);
    }

    [Fact]
    public void A_mob_behind_a_known_obstacle_is_not_walked_to_straight()
    {
        var obstacle = new Vec3(500, 0, 0);
        Assert.True(Brain.WayBlocked([(obstacle, null, TimeSpan.Zero)], Me, new Vec3(1000, 0, 0)));
        Assert.False(Brain.WayBlocked([(obstacle, null, TimeSpan.Zero)], Me, new Vec3(0, 1000, 0)));
    }
}
