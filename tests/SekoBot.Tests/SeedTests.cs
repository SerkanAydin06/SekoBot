using SekoBot.Bot;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Two SekoBots started a few seconds apart must not draw the same "random" break times.</summary>
public class SeedTests
{
    [Fact]
    public void Bots_started_seconds_apart_take_their_breaks_at_different_times()
    {
        int close = 0;
        for (int s = 1; s <= 200; s++)
        {
            double a = new Random(Brain.Seed(TimeSpan.FromSeconds(s), 1)).NextDouble();
            double b = new Random(Brain.Seed(TimeSpan.FromSeconds(s + 8), 2)).NextDouble();
            if (Math.Abs(a - b) < 0.01)
                close++;
        }
        Assert.True(close < 10, $"{close} of 200 pairs nearly the same");
        Assert.Equal(Brain.Seed(TimeSpan.FromSeconds(5), 1), Brain.Seed(TimeSpan.FromSeconds(5), 1));   // the same start: the same numbers (tests stay repeatable)
    }
}
