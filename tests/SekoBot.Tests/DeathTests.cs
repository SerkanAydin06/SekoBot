using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class DeathTests
{
    private static GameState At(double ms, double? hp, bool dead = false, bool ready = false, params Point[] mobs) =>
        new(TimeSpan.FromMilliseconds(ms), 1920, 1080, hp, 1, null, 1)
        {
            Dead = dead, ReviveReady = ready, MobCandidates = mobs
        };

    [Fact]
    public void Dies_waits_clicks_revive_here_heals_then_carries_on()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        brain.Tick(At(0, 1));
        brain.Tick(At(100, 1, mobs: new Point(1500, 540))); // walking right

        // HP runs out and the death dialog shows: let go of everything, don't stop, don't press Esc.
        var died = brain.Tick(At(200, 0, dead: true));
        Assert.Equal(BotPhase.Dead, brain.Phase);
        Assert.Contains(died, a => a is KeyUpAction u && u.Key == p.Keys.MoveRight);
        Assert.DoesNotContain(died, a => a is StopAction);
        Assert.DoesNotContain(died, a => a is KeyTapAction t && t.Key == p.Keys.CloseWindow);
        // The camera key G was being held for the setup: let go of it too.
        Assert.Contains(died, a => a is KeyUpAction u && u.Key == p.Keys.CameraTop);

        // Button still locked: just wait.
        var waiting = new List<BotAction>();
        for (double t = 300; t < 5000; t += 100)
            waiting.AddRange(brain.Tick(At(t, 0, dead: true)));
        Assert.DoesNotContain(waiting, a => a is ClickAction);
        Assert.DoesNotContain(waiting, a => a is StopAction);

        // Unlocked: click "Burada yeniden başla" (top middle), once per 3 s.
        var click = brain.Tick(At(5000, 0, dead: true, ready: true));
        var c = Assert.Single(click.OfType<ClickAction>());
        Assert.True(c.Slow); // a UI button: real cursor on it, hover, hold the click
        Assert.Equal(960, c.Client.X);
        Assert.InRange(c.Client.Y, 140, 170);
        Assert.Empty(brain.Tick(At(5100, 0, dead: true, ready: true)).OfType<ClickAction>());

        // Alive again with little HP: drink potions, no walking or attacking yet.
        var healing = new List<BotAction>();
        for (double t = 5200; t < 9000; t += 100)
            healing.AddRange(brain.Tick(At(t, 0.3, mobs: new Point(1500, 540))));
        Assert.Equal(BotPhase.Recovering, brain.Phase);
        Assert.True(healing.OfType<KeyTapAction>().Count(a => a.Key == p.Keys.HpPotion) >= 2);
        Assert.DoesNotContain(healing, a => a is KeyDownAction d && (d.Key == p.Keys.MoveRight || d.Key == p.Keys.Attack));

        // Healed: back to hunting.
        var hunting = brain.Tick(At(9000, 0.95, mobs: new Point(1500, 540))).ToList();
        hunting.AddRange(brain.Tick(At(9100, 0.95, mobs: new Point(1500, 540))));
        Assert.Contains(hunting, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void Clicks_revive_again_a_little_higher_and_lower_while_still_dead()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var clicks = new List<ClickAction>();
        for (double t = 0; t < 10_000; t += 100)
            clicks.AddRange(brain.Tick(At(t, 0, dead: true, ready: true)).OfType<ClickAction>());
        Assert.True(clicks.Count >= 3);
        Assert.All(clicks, c => Assert.True(c.Slow));
        Assert.Equal(3, clicks.Take(3).Select(c => c.Client.Y).Distinct().Count());
        Assert.All(clicks, c => Assert.Equal(960, c.Client.X));
    }

    [Fact]
    public void Gives_up_healing_after_the_time_limit()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        brain.Tick(At(0, 1));
        brain.Tick(At(100, 0, dead: true));
        brain.Tick(At(200, 0.2)); // revived
        Assert.Equal(BotPhase.Recovering, brain.Phase);
        brain.Tick(At(200 + p.ReviveHealMaxMs + 100, 0.2));
        Assert.NotEqual(BotPhase.Recovering, brain.Phase);
    }
}
