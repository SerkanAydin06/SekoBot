using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class PopupTests
{
    [Fact]
    public void Bot_presses_esc_once_and_only_again_if_the_window_is_still_there()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        GameState At(double ms, bool popup, params Point[] mobs) =>
            new(TimeSpan.FromMilliseconds(ms), 1920, 1080, 1, 1, null, 1) { PopupTarget = popup ? "EscMenu" : null, MobCandidates = mobs };

        brain.Tick(At(0, false));
        // Walking to a mob when the advert pops up.
        brain.Tick(At(100, false, new Point(1500, 540)));
        var first = brain.Tick(At(200, true));
        Assert.Contains(first, a => a is KeyTapAction t && t.Key == Keys.Escape);
        Assert.Contains(first, a => a is KeyUpAction u && u.Key == p.Keys.MoveRight);

        var actions = new List<BotAction>();
        for (double t = 300; t < 3300; t += 100)
            actions.AddRange(brain.Tick(At(t, true)));
        Assert.Single(actions.OfType<KeyTapAction>(), a => a.Key == Keys.Escape); // once more, after 3 s
        Assert.DoesNotContain(actions, a => a is KeyDownAction);

        // Closed: back to work.
        var after = brain.Tick(At(3400, false, new Point(1500, 540)));
        Assert.Contains(after, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Theory]
    [InlineData("bigMap", Keys.M)]
    [InlineData("inventory", Keys.I)]
    [InlineData("EscMenu", Keys.Escape)]
    [InlineData("popup_C", Keys.Escape)]
    public void Queued_close_requires_the_same_window_at_key_down(string window, Keys key)
    {
        string? live = window;
        var brain = new Brain(() => new BotProfile()) { SamePopupOpen = expected => expected == live };
        brain.Start(TimeSpan.Zero);
        var state = new GameState(TimeSpan.Zero, 1920, 1080, 1, 1, null, 1) { PopupTarget = window };
        var tap = Assert.Single(brain.Tick(state).OfType<KeyTapAction>());
        Assert.Equal(key, tap.Key);
        Assert.True(tap.StillWanted!());
        live = null;
        Assert.False(tap.StillWanted());
        Assert.Contains(brain.Tick(state with { Time = TimeSpan.FromSeconds(31) }), a => a is StopAction);
    }

    [Fact]
    public void Startup_and_stacked_windows_are_checked_in_order()
    {
        Assert.Equal("EscMenu", PopupWatch.Target(["inventory", "bigMap", "EscMenu"]));
        Assert.Equal("popup_C", PopupWatch.Target(["bigMap", "popup_C"]));
        Assert.Equal("bigMap", PopupWatch.Target(["miniMap", "bigMap"]));
        Assert.Equal("newDialog_C", PopupWatch.Target(["newDialog_C"]));
        Assert.Null(PopupWatch.Target(["miniMap", "chatBox", "taskbar"]));
    }

    [Fact]
    public void Each_blocker_gets_its_own_deadline()
    {
        var brain = new Brain(() => new BotProfile());
        brain.Start(TimeSpan.Zero);
        var state = new GameState(TimeSpan.Zero, 1920, 1080, 1, 1, null, 1) { PopupTarget = "inventory" };
        brain.Tick(state);
        state = state with { Time = TimeSpan.FromSeconds(28), PopupTarget = "bigMap" };
        brain.Tick(state);
        Assert.DoesNotContain(brain.Tick(state with { Time = TimeSpan.FromSeconds(31) }), a => a is StopAction);
        Assert.Contains(brain.Tick(state with { Time = TimeSpan.FromSeconds(58) }), a => a is StopAction);
    }

    [Theory]
    [InlineData("bigMap", Keys.M)]
    [InlineData("inventory", Keys.I)]
    [InlineData(null, Keys.Escape)]
    public void Market_rechecks_both_panel_and_shop(string? window, Keys key)
    {
        var keys = new BotProfile().Keys;
        (bool, string?, int?) live = (true, window, 17);
        var tap = BotEngine.MarketCloseAction(keys, live, () => live, default, 17);
        Assert.NotNull(tap);
        Assert.Equal(key, tap.Key);
        Assert.True(tap.StillWanted!());
        live = (true, window, 18);
        Assert.False(tap.StillWanted());
        live = (true, "EscMenu", 17);
        Assert.False(tap.StillWanted());
        Assert.Null(BotEngine.MarketCloseAction(keys, (false, null, null), () => live, default));
        Assert.Null(BotEngine.MarketCloseAction(keys, (true, null, null), () => live, default));
    }
}
