using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class PvpTests
{
    private static OtherPlayer P(int empire = 0, int pvp = 0, bool targetsMe = false) => new("Rakip", false, new Vec3(0, 0, 0), 50, empire, pvp, targetsMe);

    [Fact]
    public void Another_kingdom_or_pvp_on_or_targeting_us_looks_hostile()
    {
        Assert.True(BotEngine.LooksHostile(P(empire: 2), ownEmpire: 1));
        Assert.True(BotEngine.LooksHostile(P(empire: 1, pvp: 1), ownEmpire: 1));
        Assert.True(BotEngine.LooksHostile(P(targetsMe: true), ownEmpire: 1));
        Assert.False(BotEngine.LooksHostile(P(empire: 1), ownEmpire: 1));
        Assert.False(BotEngine.LooksHostile(P(empire: 2), ownEmpire: 0)); // own kingdom not known
    }
}
