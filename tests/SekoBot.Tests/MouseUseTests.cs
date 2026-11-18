using SekoBot.Input;
using Xunit;

namespace SekoBot.Tests;

public class MouseUseTests
{
    [Fact]
    public void A_mouse_job_is_marked_for_the_other_SekoBots_but_not_seen_as_someone_else()
    {
        MouseUse.Begin();
        try
        {
            Assert.True(EventWaitHandle.TryOpenExisting($@"Local\SekoBot.MouseBusy-{Instance.Number}", out var mark));
            using (mark)
                Assert.True(mark!.WaitOne(0));
            Assert.DoesNotContain(Instance.Number, MouseUse.Others());
        }
        finally
        {
            MouseUse.End();
        }
    }

    [Fact]
    public void Another_SekoBot_doing_a_mouse_job_is_seen()
    {
        int other = Instance.Number == 7 ? 6 : 7;
        using var mark = new EventWaitHandle(true, EventResetMode.ManualReset, $@"Local\SekoBot.MouseBusy-{other}");
        Assert.Contains(other, MouseUse.Others());
        mark.Reset();
        Assert.DoesNotContain(other, MouseUse.Others());
    }
}
