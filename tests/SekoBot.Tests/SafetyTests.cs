using SekoBot.Config;
using Xunit;

namespace SekoBot.Tests;

public class SafetyTests
{
    [Fact]
    public void The_telegram_token_is_written_encrypted_and_read_back()
    {
        var p = new BotProfile { TelegramToken = "123:ABC" };
        string json = System.Text.Json.JsonSerializer.Serialize(p);
        Assert.DoesNotContain("123:ABC", json);
        Assert.Contains("dpapi:", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<BotProfile>(json)!;
        Assert.Equal("123:ABC", back.TelegramToken);
        Assert.False(back.TokenWasPlain);
    }

    [Fact]
    public void A_plain_token_from_an_older_version_is_still_read()
    {
        var back = System.Text.Json.JsonSerializer.Deserialize<BotProfile>("{\"TelegramToken\":\"999:XYZ\"}")!;
        Assert.Equal("999:XYZ", back.TelegramToken);
        Assert.True(back.TokenWasPlain);
        Assert.Equal("", Secret.Unprotect("dpapi:bm90LXJlYWw="));   // another PC's: nothing, no crash
    }
}
