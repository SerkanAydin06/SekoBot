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
