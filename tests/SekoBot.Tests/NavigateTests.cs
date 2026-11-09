using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class NavigateTests
{
    private const int W = 1920, H = 1080;
    private static readonly Point Center = new(W / 2, H / 2);

    private static GameState State(double ms, params Point[] mobs) =>
        new(TimeSpan.FromMilliseconds(ms), W, H, 1, 1, null, 1) { MobCandidates = mobs };

    private static (Brain Brain, BotProfile Profile) Ready() => Ready([]);

    /// <summary>Keys held after these actions (key-downs minus key-ups).</summary>
    private static HashSet<Keys> Held(IEnumerable<BotAction> actions)
    {
        var held = new HashSet<Keys>();
        foreach (var a in actions)
        {
            if (a is KeyDownAction d) held.Add(d.Key);
            else if (a is KeyUpAction u) held.Remove(u.Key);
        }
        return held;
    }

    /// <param name="log">Collects the setup actions, to know which keys are held afterwards.</param>
    private static (Brain Brain, BotProfile Profile) Ready(List<BotAction> log)
    {
        var profile = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5] };
        Assert.True(profile.NavigateMode); // the default
        var brain = new Brain(() => profile);
        brain.Start(TimeSpan.Zero);
        // Camera setup starts right away (G, then F) and doesn't hold anything up.
        var setup = brain.Tick(State(0));
        log.AddRange(setup);
        Assert.Contains(setup, a => a is KeyDownAction d && d.Key == profile.Keys.CameraTop);
        Assert.Equal(BotPhase.Searching, brain.Phase);
        var later = brain.Tick(State(2 * profile.CameraSetupHoldMs + 500));
        log.AddRange(later);
        Assert.Contains(later, a => a is KeyUpAction u && u.Key == profile.Keys.ZoomOut);
        return (brain, profile);
    }

    private static List<BotAction> Run(Brain brain, double from, double to, Func<double, Point[]> mobs)
    {
        var actions = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            actions.AddRange(brain.Tick(State(t, mobs(t))));
        return actions;
    }

    [Fact]
    public void Walks_diagonally_towards_the_nearest_red_name_without_attacking()
    {
        var log = new List<BotAction>();
        var (brain, p) = Ready(log);
        var far = new Point(Center.X - 700, Center.Y - 400);
        var near = new Point(Center.X + 400, Center.Y + 300); // down-right, nearer
        var actions = Run(brain, 4000, 4500, _ => [far, near]);
        Assert.Equal(BotPhase.Moving, brain.Phase);
        // Exploring may already have been holding one of the keys, so look at what is held now.
        var held = Held(log.Concat(actions));
        Assert.Contains(p.Keys.MoveRight, held);
        Assert.Contains(p.Keys.MoveBack, held);
        Assert.DoesNotContain(p.Keys.MoveLeft, held);
        Assert.DoesNotContain(p.Keys.MoveForward, held);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
    }

    [Fact]
    public void Straight_up_only_presses_w()
    {
        var (brain, p) = Ready();
        var actions = Run(brain, 4000, 4300, _ => [new Point(Center.X + 10, Center.Y - 500)]);
        // Only movement keys matter here (camera/turn keys run alongside on their own).
        var moves = new[] { p.Keys.MoveForward, p.Keys.MoveBack, p.Keys.MoveLeft, p.Keys.MoveRight };
        var downs = actions.OfType<KeyDownAction>().Select(d => d.Key).Where(moves.Contains).Distinct().ToList();
        Assert.Equal(new[] { p.Keys.MoveForward }, downs);
    }

    [Fact]
    public void Stops_and_holds_space_when_the_mob_is_next_to_the_character_then_loots()
    {
        var (brain, p) = Ready();
        p.PickUpDrops = false; // Z every few seconds (when the bot doesn't walk to the items itself)
        var mob = new Point(Center.X + 300, Center.Y);
        Run(brain, 4000, 4500, _ => [mob]); // walking right
        // Arrived: the name is now right next to the middle.
        var fight = Run(brain, 4500, 7000, _ => [new Point(Center.X + 10, Center.Y + 5)]);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.Contains(fight, a => a is KeyUpAction u && u.Key == p.Keys.MoveRight);
        Assert.Contains(fight, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
        Assert.True(fight.OfType<KeyTapAction>().Count(a => a.Key == p.Keys.Pickup) >= 1);

        // Mob dead, nothing else around: release Space, pick up, count the kill.
        var after = Run(brain, 7000, 7500, _ => []);
        Assert.Contains(after, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.True(after.OfType<KeyTapAction>().Count(a => a.Key == p.Keys.Pickup) >= 2);
        Assert.Equal(1, brain.Cycles);
    }

    [Fact]
    public void Goes_to_the_next_mob_after_a_kill()
    {
        var (brain, p) = Ready();
        Run(brain, 4000, 5000, _ => [new Point(Center.X, Center.Y + 30)]); // fighting
        var next = Run(brain, 5000, 5500, _ => [new Point(Center.X - 500, Center.Y)]);
        Assert.Contains(next, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.Contains(next, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
        Assert.Equal(BotPhase.Moving, brain.Phase);
    }

    [Fact]
    public void Explores_when_no_red_name_is_on_screen()
    {
        var (brain, p) = Ready();
        var actions = Run(brain, 4000, 9000, _ => []);
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveForward);
        // Zigzag instead of turning the camera (the area check needs the camera's direction).
        Assert.Contains(actions, a => a is KeyDownAction d && (d.Key == p.Keys.MoveLeft || d.Key == p.Keys.MoveRight));
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p.Keys.RotateCamera);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
    }

    [Fact]
    public void Gives_up_on_a_mob_it_cannot_reach()
    {
        var (brain, p) = Ready();
        var actions = Run(brain, 4000, 4000 + p.MoveStuckMs + 500, _ => [new Point(Center.X + 600, Center.Y)]);
        Assert.Contains(actions, a => a is NoteAction);
        Assert.Contains(actions, a => a is KeyUpAction u && u.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void Still_drinks_potions_and_never_stops_on_low_hp()
    {
        var (brain, p) = Ready();
        var low = new GameState(TimeSpan.FromMilliseconds(4000), W, H, 0.4, 1, null, 1);
        brain.Tick(low);
        var potion = brain.Tick(low with { Time = TimeSpan.FromMilliseconds(4400) });
        Assert.Contains(potion, a => a is KeyTapAction t && t.Key == p.Keys.HpPotion);
        var crit = new GameState(TimeSpan.FromMilliseconds(5000), W, H, 0.05, 1, null, 1);
        brain.Tick(crit);
        Assert.DoesNotContain(brain.Tick(crit with { Time = TimeSpan.FromMilliseconds(5400) }), a => a is StopAction);
        Assert.NotEqual(BotPhase.Stopped, brain.Phase);
    }

    [Fact]
    public void Keeps_steering_with_wasd_while_attacking()
    {
        var (brain, p) = Ready();
        p.AttackRadius = 0.05; // distances below are in pixels for a 5 % radius
        Run(brain, 4000, 4500, _ => [new Point(Center.X, Center.Y + 20)]); // attacking, centred
        // The mob drifts up-left but is still in range: attack AND steer.
        var drift = Run(brain, 4500, 5000, _ => [new Point(Center.X - 35, Center.Y - 35)]);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.Contains(drift, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
        Assert.Contains(drift, a => a is KeyDownAction d && d.Key == p.Keys.MoveForward);
        Assert.DoesNotContain(drift, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        // A bit beyond the attack radius: keep attacking (no flip-flop) ...
        var edge = Run(brain, 5000, 5300, _ => [new Point(Center.X + 65, Center.Y)]);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.DoesNotContain(edge, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        // ... but clearly away: stop attacking and walk.
        var away = Run(brain, 5300, 5600, _ => [new Point(Center.X + 400, Center.Y)]);
        Assert.Contains(away, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.Equal(BotPhase.Moving, brain.Phase);
    }

    [Fact]
    public void Presses_g_and_f_again_while_walking_without_stopping()
    {
        var (brain, p) = Ready();
        p.MoveStuckMs = 1_000_000; // keep walking to the far mob the whole time
        var actions = Run(brain, 4000, p.CameraRefreshMs + 2 * p.CameraSetupHoldMs + 500,
            _ => [new Point(Center.X + 900, Center.Y)]);
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.CameraTop);
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.ZoomOut);
        Assert.Contains(actions, a => a is KeyUpAction u && u.Key == p.Keys.ZoomOut);
        Assert.DoesNotContain(actions, a => a is KeyUpAction u && u.Key == p.Keys.MoveRight);
        Assert.DoesNotContain(actions, a => a is KeyHoldAction);
    }

    [Fact]
    public void Presses_g_and_f_during_a_fight_without_letting_go_of_space()
    {
        var (brain, p) = Ready();
        var mob = new Point(Center.X, Center.Y + 10);
        var fight = Run(brain, 4000, p.CameraRefreshMs + 2 * p.CameraSetupHoldMs + 500, _ => [mob]);
        Assert.Contains(fight, a => a is KeyDownAction d && d.Key == p.Keys.CameraTop);
        Assert.DoesNotContain(fight, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
    }

    [Fact]
    public void Picks_up_loot_all_the_time_also_while_walking()
    {
        var (brain, p) = Ready();
        p.PickUpDrops = false; // Z every few seconds (when the bot doesn't walk to the items itself)
        p.MoveStuckMs = 1_000_000;
        var walk = Run(brain, 4000, 8000, _ => [new Point(Center.X - 900, Center.Y)]);
        Assert.Equal(BotPhase.Moving, brain.Phase);
        Assert.True(walk.OfType<KeyTapAction>().Count(a => a.Key == p.Keys.Pickup) >= 2);
    }

    [Fact]
    public void Attack_starts_only_when_the_mob_is_close()
    {
        var (brain, p) = Ready();
        Assert.Equal(0.10, p.AttackRadius);
        // ~15 % of the screen height away: still walking, no Space.
        var near = Run(brain, 4000, 4500, _ => [new Point(Center.X + (int)(H * 0.15), Center.Y)]);
        Assert.Equal(BotPhase.Moving, brain.Phase);
        Assert.DoesNotContain(near, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
        // Right next to it: attack.
        Run(brain, 4500, 4800, _ => [new Point(Center.X + 30, Center.Y)]);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
    }

    [Fact]
    public void Old_profile_moves_to_the_wider_attack_distance_but_keeps_user_choices()
    {
        var old = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], Version = 3, AttackRadius = 0.05 };
        Assert.True(ProfileStore.Upgrade(old));
        Assert.Equal(0.10, old.AttackRadius);
        Assert.Equal(BotProfile.CurrentVersion, old.Version);

        var picked = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], Version = 0, AttackRadius = 0.07 };
        ProfileStore.Upgrade(picked);
        Assert.Equal(0.07, picked.AttackRadius);

        Assert.False(ProfileStore.Upgrade(old)); // only once
    }

    private static Point[] Crowd(int dx, int dy) =>
    [
        new(Center.X + dx, Center.Y + dy), new(Center.X + dx + 40, Center.Y + dy + 10),
        new(Center.X + dx - 30, Center.Y + dy + 30), new(Center.X + dx + 10, Center.Y + dy - 35),
    ];

    [Fact]
    public void Walks_into_the_crowd_rather_than_to_a_lone_mob_nearby()
    {
        var (brain, p) = Ready();
        p.PreferCrowds = true; // off by default now (replaced by gathering groups)
        p.AttackRadius = 0.05; // distances below are in pixels for a 5 % radius
        var lone = new Point(Center.X + 200, Center.Y); // nearest, but alone
        var mobs = Crowd(-350, 0).Append(lone).ToArray(); // four wolves to the left
        var walk = Run(brain, 4000, 4500, _ => mobs);
        Assert.Contains(walk, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
        Assert.DoesNotContain(walk, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void Only_goes_for_the_nearest_when_crowds_are_off_or_too_small()
    {
        var log = new List<BotAction>();
        var (brain, p) = Ready(log);
        p.PreferCrowds = false;
        var lone = new Point(Center.X + 200, Center.Y);
        var walk = Run(brain, 4000, 4500, _ => Crowd(-350, 0).Append(lone).ToArray());
        Assert.Equal(new HashSet<Keys> { p.Keys.MoveRight }, Held(log.Concat(walk)).Where(IsMove(p)).ToHashSet());

        var log2 = new List<BotAction>();
        var (brain2, p2) = Ready(log2);
        // Only two to the left: not a crowd (needs 3), so the nearest one wins.
        var two = new[] { new Point(Center.X - 350, Center.Y), new Point(Center.X - 320, Center.Y + 20), lone };
        var walk2 = Run(brain2, 4000, 4500, _ => two);
        Assert.Equal(new HashSet<Keys> { p2.Keys.MoveRight }, Held(log2.Concat(walk2)).Where(IsMove(p2)).ToHashSet());
    }

    private static Func<Keys, bool> IsMove(BotProfile p) =>
        k => k == p.Keys.MoveForward || k == p.Keys.MoveBack || k == p.Keys.MoveLeft || k == p.Keys.MoveRight;

    [Fact]
    public void Finishes_the_mob_next_to_it_before_heading_for_a_crowd()
    {
        var (brain, p) = Ready();
        var adjacent = new Point(Center.X + 20, Center.Y + 10);
        var fight = Run(brain, 4000, 5000, _ => Crowd(-500, 0).Append(adjacent).ToArray());
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.DoesNotContain(fight, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
    }

    [Fact]
    public void A_lone_mob_almost_reached_is_taken_first()
    {
        var log = new List<BotAction>();
        var (brain, p) = Ready(log);
        var close = new Point(Center.X + 80, Center.Y); // just outside attack range
        var walk = Run(brain, 4000, 4500, _ => Crowd(-400, 0).Append(close).ToArray());
        Assert.Equal(new HashSet<Keys> { p.Keys.MoveRight }, Held(log.Concat(walk)).Where(IsMove(p)).ToHashSet());
    }

    [Fact]
    public void Sticks_to_the_chosen_crowd_instead_of_zigzagging()
    {
        var (brain, p) = Ready();
        p.PreferCrowds = true; // off by default now (replaced by gathering groups)
        // Two similar crowds left and right; the left one slightly better at first.
        var left = Crowd(-300, 0);
        var right = Crowd(330, 0);
        Run(brain, 4000, 4300, _ => left.Concat(right).ToArray());
        // Now the right one is a touch closer, but not clearly better: keep going left.
        var leftLater = Crowd(-320, 0);
        var rightLater = Crowd(300, 0);
        var walk = Run(brain, 4300, 5000, _ => leftLater.Concat(rightLater).ToArray());
        Assert.DoesNotContain(walk, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }
}
