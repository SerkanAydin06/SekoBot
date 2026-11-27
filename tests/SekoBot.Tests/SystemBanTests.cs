using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class SystemBanTests
{
    [Fact]
    public void A_system_line_that_is_no_chat_ban_counts()
    {
        Assert.True(SocialWatch.IsSystemBan(new ChatLine(0, "", 0, "[SYSTEM] HILECI01 banned.")));
        Assert.False(SocialWatch.IsSystemBan(new ChatLine(0, "", 0, "[SYSTEM] TAMDEM chat blocked.")));
        Assert.False(SocialWatch.IsSystemBan(new ChatLine(0, "", 0, "WAIT!")));
        Assert.False(SocialWatch.IsSystemBan(new ChatLine(-5, "Biri", 30, "[SYSTEM] sahte")));
    }
}
