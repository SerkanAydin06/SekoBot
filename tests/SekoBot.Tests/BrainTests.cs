using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class BrainTests
{
    private static GameState State(double ms, double? hp = 1, double? target = null) =>
        new(TimeSpan.FromMilliseconds(ms), 800, 600, hp, 1, target, 1);

    private static (Brain Brain, BotProfile Profile) Started(Action<BotProfile>? configure = null)
    {
        // These tests cover the blind attack loop; mob search has its own tests.
        var profile = new BotProfile { AutoTarget = false, NavigateMode = false };
        configure?.Invoke(profile);
        var brain = new Brain(() => profile);
        brain.Start(TimeSpan.Zero);
        return (brain, profile);
    }

    private static List<BotAction> Run(Brain brain, double from, double to, Func<double, GameState>? state = null)
    {
        var actions = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            actions.AddRange(brain.Tick(state?.Invoke(t) ?? State(t)));
        return actions;
    }

    [Fact]
    public void Holds_attack_then_loots_and_counts_cycle()
    {
        var (brain, p) = Started();
        var fight = Run(brain, 0, p.FightMs);
        Assert.Contains(fight, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
        Assert.Equal(BotPhase.Fighting, brain.Phase);

        var rest = Run(brain, p.FightMs, p.FightMs + 3000);
        Assert.Contains(rest, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.Equal(p.LootPresses, rest.OfType<KeyTapAction>().Count(a => a.Key == p.Keys.Pickup));
        Assert.Equal(1, brain.Cycles);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
    }

    [Fact]
    public void Rotates_camera_every_n_cycles()
    {
        var (brain, p) = Started(x => x.RotateEveryCycles = 2);
        var actions = Run(brain, 0, 60000);
        int rotations = actions.OfType<KeyHoldAction>().Count(a => a.Key == p.Keys.RotateCamera);
        Assert.True(brain.Cycles >= 4);
        Assert.Equal(brain.Cycles / 2, rotations);
    }

    [Fact]
    public void Keeps_fighting_while_target_alive_and_turns_when_nothing_is_left()
    {
        var (brain, p) = Started(x => { x.TargetBar = new BarSpec(); x.RotateEveryCycles = 0; });
        Run(brain, 0, p.FightMs + 3000, t => State(t, target: 0.6));
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.Equal(0, brain.Cycles);

        var after = Run(brain, p.FightMs + 3000, p.FightMs + 7000, t => State(t, target: 0));
        Assert.Equal(1, brain.Cycles);
        Assert.Contains(after, a => a is KeyHoldAction h && h.Key == p.Keys.RotateCamera);
    }

    [Fact]
    public void Gives_up_on_target_after_max_fight_time()
    {
        var (brain, p) = Started(x => x.TargetBar = new BarSpec());
        Run(brain, 0, p.FightMaxMs + 200, t => State(t, target: 0.9));
        Assert.Equal(BotPhase.Looting, brain.Phase);
    }

    [Fact]
    public void Drinks_potion_with_cooldown_and_stops_on_critical_hp()
    {
        var (brain, p) = Started();
        Assert.DoesNotContain(brain.Tick(State(100, hp: 0.4)).OfType<KeyTapAction>(), a => a.Key == p.Keys.HpPotion);
        Assert.Contains(brain.Tick(State(450, hp: 0.4)).OfType<KeyTapAction>(), a => a.Key == p.Keys.HpPotion);
        Assert.DoesNotContain(brain.Tick(State(600, hp: 0.4)).OfType<KeyTapAction>(), a => a.Key == p.Keys.HpPotion);
        Assert.Empty(brain.Tick(State(700, hp: 0.1)).OfType<StopAction>());
        var stop = brain.Tick(State(1050, hp: 0.1));
        Assert.Single(stop.OfType<StopAction>());
        Assert.Contains(stop, a => a is KeyUpAction);
        Assert.Equal(BotPhase.Stopped, brain.Phase);
        Assert.Equal(1, brain.Potions);
    }

    [Fact]
    public void Single_bad_frames_do_not_stop_or_drink()
    {
        var (brain, p) = Started();
        var actions = new List<BotAction>();
        for (int i = 0; i < 15; i++)
        {
            actions.AddRange(brain.Tick(State(100 + i * 200, hp: 0.05)));
            actions.AddRange(brain.Tick(State(200 + i * 200, hp: 1)));
        }
        Assert.DoesNotContain(actions, a => a is StopAction);
        Assert.DoesNotContain(actions.OfType<KeyTapAction>(), a => a.Key == p.Keys.HpPotion);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
    }
}
