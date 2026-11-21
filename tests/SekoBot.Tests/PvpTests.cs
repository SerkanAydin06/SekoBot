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

public class CrowdTests
{
    private static BotEngine.BotPlace At(int n, int channel, double x, string map = "c2", bool farming = true) =>
        new(n, "B" + n, map, channel, x, 1000, farming, DateTime.UtcNow);

    [Fact]
    public void Of_three_of_ours_in_one_place_the_lowest_number_stays_the_others_move()
    {
        BotEngine.BotPlace[] all = [At(2, 5, 1000), At(4, 5, 1500), At(5, 5, 2000)];
        bool Moves(BotEngine.BotPlace me) => BotEngine.ShouldMove(me, all.Where(o => o.Number != me.Number));
        Assert.False(Moves(all[0]));
        Assert.True(Moves(all[1]));
        Assert.True(Moves(all[2]));
    }

    [Fact]
    public void Another_channel_another_map_far_away_or_not_farming_is_no_crowd()
    {
        var me = At(4, 5, 1000);
        Assert.False(BotEngine.ShouldMove(me, [At(2, 3, 1000)]));
        Assert.False(BotEngine.ShouldMove(me, [At(2, 5, 1000, "c3")]));
        Assert.False(BotEngine.ShouldMove(me, [At(2, 5, 1000 + BotEngine.SamePlace + 100)]));
        Assert.False(BotEngine.ShouldMove(me, [At(2, 5, 1000, farming: false)]));
    }
}
