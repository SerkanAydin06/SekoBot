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
}
