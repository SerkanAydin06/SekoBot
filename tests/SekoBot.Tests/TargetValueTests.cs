using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Choosing mobs by what they're worth, with real (world) distances.</summary>
public class TargetValueTests
{
    private const int W = 1920, H = 1080;
    private static readonly Point Center = new(W / 2, H / 2);

    private static (Brain, BotProfile) Ready()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PickUpDrops = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        brain.Tick(State(0));
        brain.Tick(State(2 * p.CameraSetupHoldMs + 500));
        return (brain, p);
    }

    private static GameState State(double ms, params TargetInfo[] targets) =>
        new(TimeSpan.FromMilliseconds(ms), W, H, 1, 1, 0, 1)
        {
            Targets = targets,
            MobCandidates = targets.Select(t => t.Spot).ToList(),
            AttackPower = 189,
            Walking = true,
        };

    private static List<BotAction> Run(Brain brain, double from, double to, Func<double, TargetInfo[]> targets)
    {
        var actions = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            actions.AddRange(brain.Tick(State(t, targets(t))));
        return actions;
    }

    // As read in the game: Aç Ayı 305 HP / 103 EXP, Siyah Ayı 624 HP / 384 EXP.
    private static TargetInfo CheapBear(Point spot, double distance, int hp = 305) =>
        new(1, spot, distance, 130, 103, hp, 305, 56, false);

    private static TargetInfo BlackBear(Point spot, double distance, int hp = 624) =>
        new(2, spot, distance, 130, 384, hp, 624, 62, false);

    [Fact]
    public void Goes_for_the_mob_worth_more_even_if_a_bit_further()
    {
        var (brain, p) = Ready();
        var cheap = CheapBear(new Point(Center.X + 200, Center.Y), 300);  // right, nearer
        var black = BlackBear(new Point(Center.X - 330, Center.Y), 500);  // left, further
        var actions = Run(brain, 4000, 4500, _ => [cheap, black]);
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void A_mob_close_by_comes_before_a_better_one_across_the_field()
    {
        var (brain, p) = Ready();
        var cheap = CheapBear(new Point(Center.X + 200, Center.Y), 500);   // 5 m, right
        var black = BlackBear(new Point(Center.X - 900, Center.Y), 2000);  // 20 m, left
        // (up to 5.1 s: held keys are sent again every second, D may be held from exploring already)
        var actions = Run(brain, 4000, 5100, _ => [cheap, black]);
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);

        // Nothing near: then the far one.
        var (brain2, _) = Ready();
        var far = Run(brain2, 4000, 4500, _ => [black]);
        Assert.Contains(far, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
    }

    [Fact]
    public void Finishes_the_hurt_mob_it_is_fighting_before_a_better_one()
    {
        var (brain, p) = Ready();
        var hurt = CheapBear(new Point(Center.X + 30, Center.Y), 90);
        Run(brain, 4000, 4500, _ => [hurt]);
        Assert.Equal(BotPhase.Fighting, brain.Phase);

        // Half dead now, and a Siyah Ayı shows up: keep hitting the hurt one.
        var half = CheapBear(hurt.Spot, 90, hp: 150);
        var black = BlackBear(new Point(Center.X - 300, Center.Y), 450);
        var actions = Run(brain, 4500, 6000, _ => [half, black]);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.DoesNotContain(actions, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
    }

    [Fact]
    public void A_hurt_mob_knocked_out_of_reach_waits_while_one_next_to_us_is_hit()
    {
        var (brain, p) = Ready();
        var hurt = CheapBear(new Point(Center.X + 30, Center.Y), 90);
        Run(brain, 4000, 4500, _ => [hurt]);
        // Knocked back to 220 (right), while another bear stands right next to us (left).
        var pushed = CheapBear(new Point(Center.X + 200, Center.Y), 220, hp: 150);
        var other = new TargetInfo(3, new Point(Center.X - 30, Center.Y), 80, 130, 103, 305, 305, 56, false);
        var actions = Run(brain, 4500, 5500, _ => [pushed, other]);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);

        // Nobody else in reach: go after the hurt one again.
        var (brain2, _) = Ready();
        Run(brain2, 4000, 4500, _ => [hurt]);
        var chase = Run(brain2, 4500, 5500, _ => [pushed]);
        Assert.Contains(chase, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void Attacks_by_the_real_distance_not_by_how_far_it_looks_on_screen()
    {
        var (brain, p) = Ready();
        // Far on screen (zoomed in) but 100 units away: within reach (130).
        var close = CheapBear(new Point(Center.X + 300, Center.Y), 100);
        Run(brain, 4000, 4500, _ => [close]);
        Assert.Equal(BotPhase.Fighting, brain.Phase);

        var (brain2, _) = Ready();
        // Next to us on screen (zoomed out) but 300 units away: walk to it first.
        var far = CheapBear(new Point(Center.X + 40, Center.Y), 300);
        var walk = Run(brain2, 4000, 4500, _ => [far]);
        Assert.Equal(BotPhase.Moving, brain2.Phase);
        Assert.DoesNotContain(walk, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
    }
}
