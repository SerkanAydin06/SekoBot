using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Places the character couldn't get to (a lake, a cliff) are remembered.</summary>
public class NoGoTests
{
    private static readonly DateTime T0 = new(2026, 11, 17, 18, 0, 0);

    [Fact]
    public void Given_up_once_left_for_a_while_twice_for_good()
    {
        var map = new NoGoMap();
        var lake = new Vec3(5000, 5000, 0);
        map.GaveUp("c3", lake, T0);
        Assert.True(map.Blocked("c3", new Vec3(5300, 5000, 0), T0.AddMinutes(1)));   // 3 m from it
        Assert.False(map.Blocked("c3", new Vec3(6000, 5000, 0), T0.AddMinutes(1)));  // 10 m: another place
        Assert.False(map.Blocked("c2", lake, T0.AddMinutes(1)));                     // another map
        Assert.False(map.Blocked("c3", lake, T0.AddMinutes(11)));                    // once: forgotten after 10 min
        map.GaveUp("c3", new Vec3(5100, 5000, 0), T0.AddMinutes(12));
        Assert.True(map.Blocked("c3", lake, T0.AddDays(3)));                          // twice: for good
        map.Walked("c3", new Vec3(5050, 5000, 0), T0.AddMinutes(12.5));   // just given up: still kept
        Assert.True(map.Blocked("c3", lake, T0.AddDays(3)));
        map.Walked("c3", new Vec3(5050, 5000, 0), T0.AddDays(3));                                   // we stood there: reachable
        Assert.False(map.Blocked("c3", lake, T0.AddDays(3)));
    }

    [Fact]
    public void Kept_across_starts()
    {
        string path = Path.Combine(Path.GetTempPath(), "sekobot-nogo-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var map = new NoGoMap();
            map.GaveUp("c3", new Vec3(1, 2, 0), T0);
            map.GaveUp("c3", new Vec3(1, 2, 0), T0);
            map.Save(path);
            var other = new NoGoMap();
            other.GaveUp("c2", new Vec3(9, 9, 0), T0);
            other.Save(path);                                       // another SekoBot: c3 is kept
            var loaded = NoGoMap.Load(path);
            Assert.True(loaded.Blocked("c3", new Vec3(1, 2, 0), T0.AddDays(1)));
            Assert.Equal(1, loaded.Count("c2"));
            // A third one started before c3 was learned (its copy of c3 is empty): it doesn't wipe it.
            var third = new NoGoMap(new() { ["c3"] = [] });
            third.GaveUp("c5", new Vec3(5, 5, 0), T0);
            third.Save(path);
            Assert.True(NoGoMap.Load(path).Blocked("c3", new Vec3(1, 2, 0), T0.AddDays(1)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_mob_at_such_a_place_is_not_gone_for_unless_it_attacks()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PullGroups = false, PickUpDrops = false };
        var noGo = new NoGoMap();
        noGo.GaveUp("c3", new Vec3(600, 0, 0), DateTime.Now);
        var camera = new CameraView(new Vec3(0, 0, 1000), -90, 0, 0, 90);
        TargetInfo Lake(bool attacks) => new(5, new Point(1080, 540), 600, 130, 365, 600, 600, 50, false, AttacksMe: attacks) { Position = new Vec3(600, 0, 0) };
        // When the walk to it (to the right) starts, in ms; null: never in the first second.
        double? WalksTo(NoGoMap? known, bool attacks)
        {
            var brain = new Brain(() => p) { NoGo = known };
            brain.Start(TimeSpan.Zero);
            for (double t = 0; t < 1000; t += 100)
                if (brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), 1920, 1080, 1, 1, 0, 1)
                        { Walking = true, Position = new Vec3(0, 0, 0), Camera = camera, MapName = "c3", Targets = [Lake(attacks)], AttackPower = 189 })
                    .Any(a => a is KeyDownAction d && d.Key == p.Keys.MoveRight))
                    return t;
            return null;
        }
        Assert.NotNull(WalksTo(null, false));          // not known: gone for
        Assert.Null(WalksTo(noGo, false));             // the place it couldn't get to: left alone
        Assert.NotNull(WalksTo(noGo, true));           // it attacks us: fought
    }

    [Fact]
    public void SekoBots_running_at_the_same_time_learn_from_each_other()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sekobot-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string noGo = Path.Combine(dir, "ulasilamaz.json"), trees = Path.Combine(dir, "engeller.json");
            var unicorn = NoGoMap.Load(noGo);
            var medusa = NoGoMap.Load(noGo);                         // both started with nothing
            unicorn.GaveUp("c2", new Vec3(100, 100, 0), DateTime.Now);
            unicorn.GaveUp("c2", new Vec3(100, 100, 0), DateTime.Now);
            medusa.GaveUp("c2", new Vec3(9000, 9000, 0), DateTime.Now);
            unicorn.Save(noGo);
            medusa.Save(noGo);                                       // shares its own, takes Unicorn's
            Assert.True(medusa.Blocked("c2", new Vec3(100, 100, 0), DateTime.Now.AddDays(1)));
            unicorn.Save(noGo);
            Assert.Equal(2, unicorn.Count("c2"));                    // and Unicorn gets Medusa's

            var a = ObstacleMap.Load(trees);
            var b = ObstacleMap.Load(trees);
            a.Stuck("c2", 500, 500, DateTime.Now);
            a.Stuck("c2", 500, 500, DateTime.Now);                   // a tree, for sure
            b.Stuck("c2", 3000, 3000, DateTime.Now);
            a.Save(trees);
            b.Save(trees);
            Assert.Single(b.Near("c2", new Vec3(500, 500, 0), 300)); // B knows A's tree
            a.Save(trees);
            Assert.Equal(2, a.Noted("c2"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
