using SekoBot.Input;
using Xunit;

namespace SekoBot.Tests;

/// <summary>The user's mouse is held only while the bot uses the cursor.</summary>
public class MouseGuardTests
{
    [Fact]
    public void Held_while_the_bot_uses_the_cursor_free_right_after()
    {
        Assert.False(MouseGuard.Holding);
        var outer = MouseGuard.Hold();
        var inner = MouseGuard.Hold();          // a click inside a longer action
        Assert.True(MouseGuard.Holding);
        inner.Dispose();
        Assert.True(MouseGuard.Holding);        // the outer one still holds
        outer.Dispose();
        outer.Dispose();                        // twice: no harm
        Assert.False(MouseGuard.Holding);
    }

    [Fact]
    public void A_short_hold_meanwhile_does_not_end_the_long_one()
    {
        using var job = MouseGuard.Hold(TimeSpan.FromMinutes(1));
        using (MouseGuard.Hold(TimeSpan.FromMilliseconds(1))) { }
        Thread.Sleep(20);
        Assert.True(MouseGuard.Holding);
    }
}
