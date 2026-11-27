using SekoBot.UI;
using Xunit;

namespace SekoBot.Tests;

public class ActiveWorkTests
{
    [Theory]
    [InlineData(false, false, false, false, null)]
    [InlineData(true, false, false, false, "farm")]
    [InlineData(false, true, false, false, "pazar")]
    [InlineData(false, false, true, false, "balık")]
    [InlineData(false, false, false, true, "alan taraması")]
    public void Telegram_and_updates_recognize_each_running_activity(bool farm, bool market, bool fishing, bool area, string? expected)
    {
        Assert.Equal(expected, MainForm.ActiveWorkName(farm, market, fishing, area));
    }
}
