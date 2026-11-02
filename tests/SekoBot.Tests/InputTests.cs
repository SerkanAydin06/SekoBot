using SekoBot.Input;
using Xunit;

namespace SekoBot.Tests;

public class InputTests
{
    private const ushort SpaceScan = 0x39;

    [Fact]
    public void Key_down_message_carries_scan_code_and_no_previous_state()
    {
        long l = InputExecutor.KeyMessageLParam(SpaceScan, extended: false, down: true, repeat: false);
        Assert.Equal(1, l & 0xFFFF);                 // repeat count
        Assert.Equal(SpaceScan, (l >> 16) & 0xFF);  // scan code
        Assert.Equal(0, (l >> 24) & 1);             // not extended
        Assert.Equal(0, (l >> 30) & 1);             // was up before
        Assert.Equal(0, (l >> 31) & 1);             // pressing
    }

    [Fact]
    public void Held_key_repeat_marks_previous_state_down()
    {
        long l = InputExecutor.KeyMessageLParam(SpaceScan, extended: false, down: true, repeat: true);
        Assert.Equal(1, (l >> 30) & 1);
        Assert.Equal(0, (l >> 31) & 1);
    }

    [Fact]
    public void Key_up_message_sets_previous_state_and_transition()
    {
        long l = InputExecutor.KeyMessageLParam(SpaceScan, extended: true, down: false, repeat: false);
        Assert.Equal(1, (l >> 24) & 1);
        Assert.Equal(1, (l >> 30) & 1);
        Assert.Equal(1, (l >> 31) & 1);
        Assert.Equal(SpaceScan, (l >> 16) & 0xFF);
    }
}
