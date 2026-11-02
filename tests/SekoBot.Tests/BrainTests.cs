using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class BrainTests
{
    private static readonly BotProfile Profile = new() { TargetBar = new BarSpec() };

    private static GameState State(double ms, double? hp = 1, double? target = null, params Rectangle[] stones) =>
        new(TimeSpan.FromMilliseconds(ms), 800, 600, hp, 1, target,
            stones.Select(s => new Detection(s, 0.9)).ToArray(), 1);

    private static Brain Started()
    {
        var brain = new Brain(() => Profile, new Random(1));
        brain.Start(TimeSpan.Zero);
        return brain;
    }

    [Fact]
    public void Clicks_nearest_stone_to_screen_center()
    {
        var brain = Started();
        var far = new Rectangle(20, 20, 40, 60);
        var near = new Rectangle(380, 260, 40, 60);
        var actions = brain.Tick(State(100, stones: [far, near]));
        var click = Assert.Single(actions.OfType<ClickAction>());
        Assert.True(near.Contains(click.Client));
        Assert.Equal(BotPhase.Approaching, brain.Phase);
    }

    [Fact]
    public void Full_cycle_counts_a_destroyed_stone_and_loots()
    {
        var brain = Started();
        var stone = new Rectangle(380, 260, 40, 60);
        brain.Tick(State(100, stones: [stone]));
        brain.Tick(State(600, target: 1.0, stones: [stone]));
        Assert.Equal(BotPhase.Attacking, brain.Phase);
        brain.Tick(State(5000, target: 0.3, stones: [stone]));
        brain.Tick(State(5200, target: 0));
        Assert.Equal(BotPhase.Attacking, brain.Phase); // grace period
        brain.Tick(State(6200, target: 0));
        Assert.Equal(BotPhase.Looting, brain.Phase);
        Assert.Equal(1, brain.StonesDestroyed);

        int pickups = 0;
        for (int t = 6600; t < 10000 && brain.Phase == BotPhase.Looting; t += 400)
            pickups += brain.Tick(State(t)).OfType<KeyTapAction>().Count(a => a.Key == Profile.Keys.Pickup);
        Assert.Equal(Profile.LootPresses, pickups);
        Assert.Equal(BotPhase.Searching, brain.Phase);
    }

    [Fact]
    public void Unreachable_stone_is_blacklisted_after_timeout()
    {
        var brain = Started();
        var stone = new Rectangle(380, 260, 40, 60);
        brain.Tick(State(100, stones: [stone]));
        brain.Tick(State(100 + Profile.ApproachTimeoutMs + 50, stones: [stone]));
        Assert.Equal(BotPhase.Searching, brain.Phase);
        Assert.Equal(1, brain.TargetsSkipped);

        var actions = brain.Tick(State(100 + Profile.ApproachTimeoutMs + 2000, stones: [stone]));
        Assert.Empty(actions.OfType<ClickAction>());
    }

    [Fact]
    public void Rotates_camera_then_walks_when_nothing_is_visible()
    {
        var brain = Started();
        var actions = new List<BotAction>();
        for (int t = 1000; t < 60000; t += 100)
            actions.AddRange(brain.Tick(State(t)));
        var holds = actions.OfType<KeyHoldAction>().ToList();
        Assert.Contains(holds, h => h.Key == Profile.Keys.RotateCamera);
        Assert.Contains(holds, h => h.Key == Profile.Keys.MoveForward);
    }

    [Fact]
    public void Drinks_potion_with_cooldown_and_stops_on_critical_hp()
    {
        var brain = Started();
        Assert.Contains(brain.Tick(State(100, hp: 0.4)).OfType<KeyTapAction>(), a => a.Key == Profile.Keys.HpPotion);
        Assert.DoesNotContain(brain.Tick(State(300, hp: 0.4)).OfType<KeyTapAction>(), a => a.Key == Profile.Keys.HpPotion);
        Assert.Single(brain.Tick(State(500, hp: 0.1)).OfType<StopAction>());
        Assert.Equal(BotPhase.Stopped, brain.Phase);
    }
}
