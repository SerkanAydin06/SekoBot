using SekoBot.Update;
using Xunit;

namespace SekoBot.Tests;

public class TelegramUpdateTests
{
    [Theory]
    [InlineData("/guncelle", "", true)]
    [InlineData("/güncelle", "", true)]
    [InlineData("/guncelle@pardus_bot", "", true)]
    [InlineData("güncelle", "", true)]
    [InlineData("güncelle", "oyuncu mesajı", false)]
    [InlineData("/guncelle https://example.com", "", false)]
    [InlineData("/ekran", "", false)]
    public void Recognizes_update_commands(string text, string reply, bool expected)
    {
        Assert.Equal(expected, TelegramNotifier.IsUpdateCommand(text, reply));
    }
}
