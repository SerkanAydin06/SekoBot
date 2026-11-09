using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class TerrainTests
{
    private static readonly Vec3 Me = new(20000, 18000, 1900);

    private static TargetInfo Mob(int id, double ahead, double dz = Brain.MobHeightOffset, int exp = 103, bool attacks = false) =>
        new(id, new Point(960, 540 - (int)(ahead / 5)), Math.Abs(ahead), 130, exp, 305, 305, 56, attacks, attacks,
            Position: new Vec3(Me.X + ahead, Me.Y, Me.Z + dz));

    [Fact]
    public void A_mob_on_a_ledge_is_on_another_level()
    {
        Assert.False(Brain.OnOtherLevel(Mob(1, 500), Me));                // flat ground (as seen: ~60 lower)
        Assert.False(Brain.OnOtherLevel(Mob(1, 400, dz: -120), Me));      // a slope (seen: −118 at 4 m)
        Assert.True(Brain.OnOtherLevel(Mob(1, 500, dz: 600), Me));        // 6 m up a ledge, 5 m away
        Assert.True(Brain.OnOtherLevel(Mob(1, 300, dz: -500), Me));       // down a cliff
    }

    [Fact]
    public void A_way_through_a_stuck_spot_is_blocked()
    {
        var to = new Vec3(Me.X + 1000, Me.Y, Me.Z);
        (Vec3, Vec3?, TimeSpan) Spot(double x, double y, Vec3? heading = null) => (new Vec3(Me.X + x, Me.Y + y, Me.Z), heading, TimeSpan.Zero);
        Assert.True(Brain.WayBlocked([Spot(500, 50)], Me, to));          // half way, on the line
        Assert.False(Brain.WayBlocked([Spot(500, 400)], Me, to));        // 4 m to the side
        Assert.False(Brain.WayBlocked([Spot(1500, 0)], Me, to));         // past the mob
        // Standing at the spot: only the way we were trying to go.
        Assert.True(Brain.WayBlocked([Spot(0, 0, to)], Me, new Vec3(Me.X + 600, Me.Y + 100, Me.Z)));
        Assert.False(Brain.WayBlocked([Spot(0, 0, to)], Me, new Vec3(Me.X - 600, Me.Y, Me.Z)));
    }

    private static (Brain, BotProfile) Ready()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PickUpDrops = false, PullGroups = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        return (brain, p);
    }

    private static List<BotAction> Run(Brain brain, TargetInfo[] mobs)
    {
        // Camera looking along +X: ahead (+X) is up on the screen (W), behind is down (S).
        var camera = new CameraView(Me with { Z = Me.Z + 1000 }, -80, 0, 0, 90);
        var all = new List<BotAction>();
        for (double t = 0; t < 1000; t += 100)
            all.AddRange(brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), 1920, 1080, 1, 1, 0, 1)
                { Targets = mobs, Position = Me, Camera = camera, Walking = true, AttackPower = 189 }));
        return all;
    }

    [Fact]
    public void Goes_for_another_mob_when_the_way_to_the_best_one_got_stuck_before()
    {
        // A rich mob 6 m ahead, a cheap one 6 m behind: the rich one first...
        TargetInfo[] mobs = [Mob(1, 600, exp: 900), Mob(2, -600, exp: 50)];
        var (brain, p) = Ready();
        Assert.Contains(Run(brain, mobs), a => a is KeyDownAction d && d.Key == p.Keys.MoveForward);
        // ...but we got stuck right here walking to it a moment ago: the one behind.
        var (stuck, p2) = Ready();
        stuck.RememberStuck(Me, new Vec3(Me.X + 600, Me.Y, Me.Z), TimeSpan.Zero);
        var actions = Run(stuck, mobs);
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p2.Keys.MoveBack);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p2.Keys.MoveForward);
    }

    [Fact]
    public void Leaves_a_mob_on_a_ledge_but_not_one_attacking_us()
    {
        var (brain, p) = Ready();
        TargetInfo[] mobs = [Mob(1, 600, dz: 700, exp: 900), Mob(2, -600, exp: 50)];
        var actions = Run(brain, mobs);
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveBack);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveForward);

        // The same mob coming at us from up there: it's fought.
        var (brain2, p2) = Ready();
        var hit = Run(brain2, [Mob(1, 600, dz: 700, exp: 900, attacks: true), Mob(2, -600, exp: 50)]);
        Assert.Contains(hit, a => a is KeyDownAction d && d.Key == p2.Keys.MoveForward);
    }
}
