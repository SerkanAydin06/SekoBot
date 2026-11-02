using SekoBot.Capture;
using Xunit;

namespace SekoBot.Tests;

public class WindowTests
{
    [Theory]
    [InlineData("Mobile2 Global", true)]
    [InlineData("mobile2global", true)]
    [InlineData("Mobile2 Global - Kanal 1", true)]
    [InlineData("METIN2", true)]
    [InlineData("Metin 2", true)]
    [InlineData("Google Chrome", false)]
    [InlineData("SekoBot", false)]
    public void Recognises_the_game_window(string title, bool expected) =>
        Assert.Equal(expected, WindowService.IsGameTitle(title));
}
