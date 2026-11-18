using SekoBot.Config;
using Xunit;

namespace SekoBot.Tests;

public class MySpotTests
{
    [Fact]
    public void The_users_own_spot_is_kept_with_the_character_and_can_be_cleared()
    {
        var p = new BotProfile { MySpot = new FarmSpot { MapName = "c3", X = 100, Y = 200, Z = 5 }, UseFarmSpot = true };
        var json = CharacterSettings.ToJson(p);
        var other = new BotProfile();
        CharacterSettings.Apply(json, other);
        Assert.Equal("c3", other.MySpot!.MapName);
        Assert.Equal(200, other.MySpot.Y);
        Assert.True(other.UseFarmSpot);
        // Cleared on the character: cleared when loaded too.
        CharacterSettings.Apply(CharacterSettings.ToJson(new BotProfile { UseFarmSpot = true }), other);
        Assert.Null(other.MySpot);
    }

    [Fact]
    public void A_game_tells_which_SekoBot_has_it()
    {
        const uint game = 987654321;
        Assert.Equal(int.MaxValue, Instance.BotOf(game));
        Instance.TakeGame(game);
        try { Assert.Equal(Instance.Number, Instance.BotOf(game)); }
        finally { Instance.LeaveGame(); }
        Assert.Equal(int.MaxValue, Instance.BotOf(game));
    }
}
