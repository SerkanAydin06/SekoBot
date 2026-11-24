using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Vision;
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

    private static GameState State(double ms, bool dead) =>
        new(TimeSpan.FromMilliseconds(ms), 1920, 1080, dead ? 0 : 1, 1, 0, 1) { Dead = dead, ReviveReady = dead };

    [Fact]
    public void Three_deaths_within_an_hour_stop_the_bot()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, Breaks = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        bool Stopped(double from, bool dead) =>
            Enumerable.Range(0, 30).SelectMany(i => brain.Tick(State(from + i * 100, dead))).OfType<StopAction>().Any();
        Assert.False(Stopped(0, true));
        Assert.False(Stopped(5_000, false));
        Assert.False(Stopped(600_000, true));
        Assert.False(Stopped(605_000, false));
        Assert.True(Stopped(1_200_000, true));
    }

    [Fact]
    public void Deaths_far_apart_do_not_stop_it()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, Breaks = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        bool any = false;
        for (int n = 0; n < 4; n++)
        {
            double at = n * 2_000_000.0;   // every ~33 minutes: never 3 within an hour
            any |= Enumerable.Range(0, 30).SelectMany(i => brain.Tick(State(at + i * 100, true))).OfType<StopAction>().Any();
            any |= Enumerable.Range(0, 30).SelectMany(i => brain.Tick(State(at + 5_000 + i * 100, false))).OfType<StopAction>().Any();
        }
        Assert.False(any);
    }
}
