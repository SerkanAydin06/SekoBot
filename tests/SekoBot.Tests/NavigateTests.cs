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

    private static (Brain Brain, BotProfile Profile) Ready()
    {
        var profile = new BotProfile();
        Assert.True(profile.NavigateMode); // the default
        var brain = new Brain(() => profile);
        brain.Start(TimeSpan.Zero);
        // Camera setup: G then F held; done after both.
        var setup = brain.Tick(State(0));
        Assert.Contains(setup, a => a is KeyHoldAction h && h.Key == profile.Keys.CameraTop);
        Assert.Contains(setup, a => a is KeyHoldAction h && h.Key == profile.Keys.ZoomOut);
        Assert.Equal(BotPhase.Setup, brain.Phase);
        brain.Tick(State(2 * profile.CameraSetupHoldMs + 500));
        Assert.Equal(BotPhase.Searching, brain.Phase);
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
        var (brain, p) = Ready();
        var far = new Point(Center.X - 700, Center.Y - 400);
        var near = new Point(Center.X + 400, Center.Y + 300); // down-right, nearer
        var actions = Run(brain, 4000, 4500, _ => [far, near]);
        Assert.Equal(BotPhase.Moving, brain.Phase);
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveBack);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d &&
            (d.Key == p.Keys.MoveLeft || d.Key == p.Keys.MoveForward || d.Key == p.Keys.Attack));
    }

    [Fact]
    public void Straight_up_only_presses_w()
    {
        var (brain, p) = Ready();
        var actions = Run(brain, 4000, 4300, _ => [new Point(Center.X + 10, Center.Y - 500)]);
        var downs = actions.OfType<KeyDownAction>().Select(d => d.Key).Distinct().ToList();
        Assert.Equal(new[] { p.Keys.MoveForward }, downs);
    }

    [Fact]
    public void Stops_and_holds_space_when_the_mob_is_next_to_the_character_then_loots()
    {
        var (brain, p) = Ready();
        var mob = new Point(Center.X + 300, Center.Y);
        Run(brain, 4000, 4500, _ => [mob]); // walking right
        // Arrived: the name is now right next to the middle.
        var fight = Run(brain, 4500, 7000, _ => [new Point(Center.X + 40, Center.Y + 20)]);
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
        Assert.Contains(actions, a => a is KeyHoldAction h && h.Key == p.Keys.MoveForward);
        Assert.Contains(actions, a => a is KeyHoldAction h && h.Key == p.Keys.RotateCamera);
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
    public void Still_drinks_potions_and_stops_on_critical_hp()
    {
        var (brain, p) = Ready();
        var low = new GameState(TimeSpan.FromMilliseconds(4000), W, H, 0.4, 1, null, 1);
        brain.Tick(low);
        var potion = brain.Tick(low with { Time = TimeSpan.FromMilliseconds(4400) });
        Assert.Contains(potion, a => a is KeyTapAction t && t.Key == p.Keys.HpPotion);
        var crit = new GameState(TimeSpan.FromMilliseconds(5000), W, H, 0.1, 1, null, 1);
        brain.Tick(crit);
        Assert.Contains(brain.Tick(crit with { Time = TimeSpan.FromMilliseconds(5400) }), a => a is StopAction);
    }
}
