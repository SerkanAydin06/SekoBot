using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class TargetChoiceTests
{
    private const int W = 1920, H = 1080;
    private static readonly Point Center = new(W / 2, H / 2);

    private static WorldMob Mob(string name, int level, bool attacksMe = false, bool metin = false) =>
        new(1, 101, name, level, 100, 100, new Vec3(0, 0, 0), attacksMe, metin);

    [Fact]
    public void Profile_choices_decide_which_mobs_to_go_for()
    {
        var p = new BotProfile { MaxMobLevel = 10, SkipMobs = "domuz", OnlyMobs = "" };
        Assert.True(BotEngine.Wanted(Mob("Aç Mavi Kurt", 6), p));
        Assert.False(BotEngine.Wanted(Mob("Aç Gri Kurt", 13), p));          // too high
        Assert.True(BotEngine.Wanted(Mob("Aç Gri Kurt", 13, attacksMe: true), p)); // but it attacks us
        Assert.False(BotEngine.Wanted(Mob("Yaban Domuzu", 7), p));          // skipped by name (any case)

        p = new BotProfile { OnlyMobs = "Mavi Kurt, Köpek" };
        Assert.True(BotEngine.Wanted(Mob("Aç Mavi Kurt", 6), p));
        Assert.True(BotEngine.Wanted(Mob("Aç Yabani Köpek", 1), p));
        Assert.False(BotEngine.Wanted(Mob("Kurt", 3), p));

        Assert.False(BotEngine.Wanted(Mob("Savaş Metini", 15, metin: true), new BotProfile { Metins = MetinMode.Skip }));
        Assert.True(BotEngine.Wanted(Mob("Savaş Metini", 15, metin: true), new BotProfile { Metins = MetinMode.Prefer, OnlyMobs = "Kurt" }));
        // "First": the level limit is for mobs, not for the stone.
        Assert.True(BotEngine.Wanted(Mob("Savaş Metini", 15, metin: true), new BotProfile { Metins = MetinMode.Prefer, MaxMobLevel = 10 }));
        // "Normal": like any mob.
        Assert.False(BotEngine.Wanted(Mob("Savaş Metini", 15, metin: true), new BotProfile { Metins = MetinMode.Normal, MaxMobLevel = 10 }));
    }

    private static (Brain Brain, BotProfile Profile) Ready()
    {
        var profile = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5] };
        var brain = new Brain(() => profile);
        brain.Start(TimeSpan.Zero);
        brain.Tick(State(0));
        brain.Tick(State(2 * profile.CameraSetupHoldMs + 500));
        return (brain, profile);
    }

    private static GameState State(double ms, Point[]? mobs = null, Point[]? priority = null, GroundSpot[]? drops = null) =>
        new(TimeSpan.FromMilliseconds(ms), W, H, 1, 1, 0, 1)
        {
            MobCandidates = mobs ?? [],
            PriorityMobs = priority ?? [],
            Drops = drops ?? [],
            Walking = true,
        };

    private static List<BotAction> Run(Brain brain, double from, double to, Func<double, GameState> state)
    {
        var actions = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            actions.AddRange(brain.Tick(state(t)));
        return actions;
    }

    [Fact]
    public void A_preferred_metin_comes_before_mobs_attacking_us()
    {
        var me = new Vec3(20000, 18000, 900);
        var camera = new CameraView(me with { Z = me.Z + 1000 }, -90, 0, 0, 90);
        WorldMob At(string name, double dx, bool attacks = false, bool metin = false) =>
            new(name.Length, 101, name, 5, 100, 100, me with { X = me.X + dx }, attacks, metin);
        var world = new WorldSnapshot(me, camera,
            [At("Kurt", 150, attacks: true), At("Savaş Metini", 400, metin: true), At("Domuz", 250)], 0, "c2");

        var view = BotEngine.MemoryView(world, new BotProfile { Metins = MetinMode.Prefer }, W, H)!.Value;
        var stone = Assert.Single(view.Priority);
        Assert.True(stone.Y < view.Priority.Concat(view.Mobs).Max(p => p.Y)); // furthest up = the stone (+X is up)
        Assert.Equal(3, view.Mobs.Count);

        var normal = BotEngine.MemoryView(world, new BotProfile { Metins = MetinMode.Normal }, W, H)!.Value;
        Assert.Single(normal.Priority); // the attacking wolf
        Assert.NotEqual(stone, normal.Priority[0]);
    }

    [Fact]
    public void Mobs_off_the_screen_but_known_from_memory_are_targets_too()
    {
        var me = new Vec3(20000, 18000, 900);
        // Looking straight down from close by: only a few metres show on screen; +X is up.
        var camera = new CameraView(me with { Z = me.Z + 600 }, -90, 0, 0, 90);
        WorldMob At(int id, double dx, double dy) => new(id, 101, "Kurt", 5, 100, 100, me with { X = me.X + dx, Y = me.Y + dy });
        var world = new WorldSnapshot(me, camera, [At(1, 1500, 0), At(2, 0, 4000)], 0, "c2");

        var view = BotEngine.MemoryView(world, new BotProfile(), W, H)!.Value;
        Assert.Empty(view.Mobs);                    // neither is on screen
        var target = Assert.Single(view.Targets);   // the one 15 m away is a target; 40 m is too far
        Assert.Equal(1, target.Id);
        Assert.True(target.Spot.Y < 0);             // +X is "up": the walking direction points up
        Assert.Equal(W / 2, target.Spot.X);

        // The brain walks there (W) although nothing is on screen.
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], PickUpDrops = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var actions = new List<BotAction>();
        for (double t = 0; t < 4500; t += 100)
            actions.AddRange(brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), W, H, 1, 1, 0, 1)
                { Targets = view.Targets, Walking = true, AttackPower = 189 }));
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveForward);
        Assert.Equal(BotPhase.Moving, brain.Phase);
    }

    [Fact]
    public void Goes_for_the_mob_attacking_us_before_a_nearer_one()
    {
        var (brain, p) = Ready();
        var near = new Point(Center.X + 250, Center.Y);   // nearer, to the right
        var attacker = new Point(Center.X - 400, Center.Y); // attacking us, to the left
        var actions = Run(brain, 4000, 4600, t => State(t, [near, attacker], [attacker]));
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void Walks_to_loot_close_by_and_picks_it_up_before_the_next_mob()
    {
        var (brain, p) = Ready();
        var mob = new Point(Center.X + 500, Center.Y);
        var item = new GroundSpot(77, new Point(Center.X, Center.Y - 200));
        var walk = Run(brain, 4000, 4500, t => State(t, [mob], drops: [item]));
        Assert.Contains(walk, a => a is KeyDownAction d && d.Key == p.Keys.MoveForward); // towards the item (up)
        Assert.DoesNotContain(walk, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
        Assert.Equal(BotPhase.Looting, brain.Phase);

        // On top of it: stand and press the pickup key.
        var onIt = new GroundSpot(77, new Point(Center.X, Center.Y - 10));
        var pick = Run(brain, 4500, 5500, t => State(t, [mob], drops: [onIt]));
        Assert.Contains(pick, a => a is KeyTapAction k && k.Key == p.Keys.Pickup);

        // Gone (picked up): on to the mob.
        var next = Run(brain, 5500, 6000, t => State(t, [mob]));
        Assert.Contains(next, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void Leaves_loot_it_cannot_pick_up()
    {
        var (brain, _) = Ready();
        var onIt = new GroundSpot(5, new Point(Center.X, Center.Y - 10));
        var actions = Run(brain, 4000, 10_000, t => State(t, drops: [onIt]));
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("alınamadı"));
        Assert.NotEqual(BotPhase.Looting, brain.Phase);
    }
}
