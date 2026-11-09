using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class AutoSkillTests
{
    private const int W = 1920, H = 1080;
    private static readonly Point Center = new(W / 2, H / 2);

    // The character in the game: F1 buff, F2 and F5 area, F3 and F4 single.
    private static readonly SkillSlot[] Skills =
    [
        new(Keys.F1, "Güçlü beden", SkillKind.Buff, 115, 76, 148),
        new(Keys.F2, "Güçlü vuruş", SkillKind.Area, 57, 25, 0),
        new(Keys.F3, "Şiddetli vuruş", SkillKind.Single, 67, 15, 0),
    ];

    private static (Brain, BotProfile) Ready()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = [60, 60, 0, 0, 0] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        return (brain, p);
    }

    private static GameState State(double ms, int mp, params Point[] mobs) =>
        new(TimeSpan.FromMilliseconds(ms), W, H, 1, 1, 0, 1) { Skills = Skills, MpPoints = mp, MobCandidates = mobs, Walking = true };

    private static List<Keys> Taps(Brain brain, double from, double to, Func<double, GameState> state)
    {
        var keys = new List<Keys>();
        for (double t = from; t < to; t += 100)
            keys.AddRange(brain.Tick(state(t)).OfType<KeyTapAction>().Select(k => k.Key).Where(k => k is >= Keys.F1 and <= Keys.F5));
        return keys;
    }

    [Fact]
    public void Keeps_the_buff_up_and_uses_attacks_only_when_they_make_sense()
    {
        var (brain, _) = Ready();
        // Nothing around: only the buff, once (it lasts 148 s).
        Assert.Equal([Keys.F1], Taps(brain, 0, 20_000, t => State(t, 500)));

        // One mob right next to us: the single-target attack, not the area one.
        var one = new Point(Center.X + 40, Center.Y);
        Assert.Equal([Keys.F3], Taps(brain, 20_000, 30_000, t => State(t, 500, one)));

        // Five around: the area attack.
        Point[] group = [new(Center.X + 40, Center.Y), new(Center.X - 60, Center.Y), new(Center.X, Center.Y + 80), new(Center.X + 100, Center.Y + 50), new(Center.X - 50, Center.Y - 60)];
        Assert.Contains(Keys.F2, Taps(brain, 30_000, 33_000, t => State(t, 500, group)));
    }

    private static TargetInfo Mob(int id, double distance, bool attacks = true) =>
        new(id, new Point(Center.X + (int)(distance / 5), Center.Y), distance, 130, 103, 305, 305, 56, attacks, attacks);

    [Fact]
    public void Area_attack_only_while_hitting_with_five_mobs_close()
    {
        var (brain, p) = Ready();
        p.PullGroups = false;
        GameState S(double t, TargetInfo[] mobs) => State(t, 500) with { Targets = mobs, AttackPower = 189 };
        Taps(brain, 0, 2_000, t => S(t, []));                          // the buff first
        // One mob being hit, four more chasing us but still 6 m away: no area attack (it would hit one).
        TargetInfo[] coming = [Mob(1, 80), Mob(2, 600), Mob(3, 620), Mob(4, 640), Mob(5, 660)];
        Assert.DoesNotContain(Keys.F2, Taps(brain, 2_000, 6_000, t => S(t, coming)));
        // Four of them close and one at 2.5 m: five within 3 m → the area attack.
        TargetInfo[] here = [Mob(1, 80), Mob(2, 100), Mob(3, 150), Mob(4, 200), Mob(5, 250)];
        Assert.Contains(Keys.F2, Taps(brain, 6_000, 9_000, t => S(t, here)));
        // Four within 3 m only: not enough.
        var (brain2, p2) = Ready();
        p2.PullGroups = false;
        Taps(brain2, 0, 2_000, t => S(t, []));
        TargetInfo[] four = [Mob(1, 80), Mob(2, 100), Mob(3, 150), Mob(4, 200), Mob(5, 900)];
        Assert.DoesNotContain(Keys.F2, Taps(brain2, 2_000, 6_000, t => S(t, four)));
    }

    [Fact]
    public void No_attack_skill_while_walking_to_a_mob()
    {
        var (brain, p) = Ready();
        p.PullGroups = false;
        Taps(brain, 0, 2_000, t => State(t, 500));                    // the buff first
        // Five mobs together but 6 m away: walking there, nothing cast on the way.
        TargetInfo[] far = [Mob(1, 600, false), Mob(2, 610, false), Mob(3, 620, false), Mob(4, 630, false), Mob(5, 640, false)];
        var keys = Taps(brain, 2_000, 5_000, t => State(t, 500, far.Select(m => m.Spot).ToArray()) with { Targets = far, AttackPower = 189 });
        Assert.DoesNotContain(Keys.F2, keys);
        Assert.DoesNotContain(Keys.F3, keys);
    }

    [Fact]
    public void No_skill_without_enough_mp_and_none_before_its_cooldown()
    {
        var (brain, _) = Ready();
        var one = new Point(Center.X + 40, Center.Y);
        Assert.Empty(Taps(brain, 0, 5_000, t => State(t, 50, one))); // every skill costs more than 50 MP
        var keys = Taps(brain, 5_000, 25_000, t => State(t, 500, one));
        Assert.Equal(1, keys.Count(k => k == Keys.F1));
        Assert.Equal(2, keys.Count(k => k == Keys.F3)); // every 15 s
    }

    [Fact]
    public void Off_means_the_old_every_n_seconds_keys()
    {
        var (brain, p) = Ready();
        p.AutoSkills = false;
        var keys = Taps(brain, 0, 5_000, t => State(t, 500));
        Assert.Equal([Keys.F1, Keys.F2], keys);
    }

    [Fact]
    public void Skills_on_quick_slots_get_their_keys()
    {
        var world = new WorldSnapshot(new Vec3(0, 0, 0), null, [], 0, "c2",
            Progress: new CharacterProgress(0, 1, 0, [], [12, 15, 0, 0, 0, -104, -103, -102, -101, -105]),
            Skills: [new SkillInfo(4, "Güçlü beden", 7, SkillKind.Buff, 115, 76, 148, 0),
                     new SkillInfo(3, "Güçlü vuruş", 1, SkillKind.Area, 57, 25, 0, 400),
                     new SkillInfo(9, "Bilinmeyen", 1, SkillKind.Single, 1, 1, 0, 1)]);
        var slots = BotEngine.SkillKeys(world, new BotProfile());
        Assert.Equal([(Keys.F1, "Güçlü beden"), (Keys.F2, "Güçlü vuruş")], slots.Select(s => (s.Key, s.Name)));
    }
}

public class RetreatTests
{
    private const int W = 1920, H = 1080;
    private static readonly Point Center = new(W / 2, H / 2);

    private static (Brain, BotProfile) Ready()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], RetreatAtPotions = 5 };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        return (brain, p);
    }

    private static GameState State(double ms, int potions, Point[] mobs, Point[]? attackers = null) =>
        new(TimeSpan.FromMilliseconds(ms), W, H, 0.8, 1, 0, 1)
        {
            HpPotionsLeft = potions, MobCandidates = mobs, Attackers = attackers ?? [], Walking = true,
        };

    [Fact]
    public void Runs_away_from_the_mobs_then_stops_where_it_is_quiet()
    {
        var (brain, p) = Ready();
        var right = new Point(Center.X + 120, Center.Y);
        for (double t = 0; t < 3000; t += 100)
            brain.Tick(State(t, 40, [right]));

        // Down to 5: away from the mob on the right = to the left.
        var run = new List<BotAction>();
        for (double t = 3000; t < 4000; t += 100)
            run.AddRange(brain.Tick(State(t, 5, [right], [right])));
        Assert.Contains(run, a => a is NoteAction n && n.Text.Contains("uzaklaşılıyor"));
        Assert.Contains(run, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
        Assert.DoesNotContain(run, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
        Assert.DoesNotContain(run, a => a is StopAction);

        // Far enough: nothing near, nothing hitting us → stop after 3 s.
        var quiet = new List<BotAction>();
        for (double t = 4000; t < 8000; t += 100)
            quiet.AddRange(brain.Tick(State(t, 5, [new Point(Center.X + 900, Center.Y)])));
        Assert.Contains(quiet, a => a is StopAction s && s.Reason.Contains("güvenli"));
    }

    [Fact]
    public void Too_few_potions_from_the_start_only_warns()
    {
        var (brain, _) = Ready();
        var actions = new List<BotAction>();
        for (double t = 0; t < 5000; t += 100)
            actions.AddRange(brain.Tick(State(t, 2, [])));
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("Uyarı"));
        Assert.DoesNotContain(actions, a => a is StopAction);
    }
}
