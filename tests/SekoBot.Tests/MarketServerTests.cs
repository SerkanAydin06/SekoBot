using SekoBot.Bot;
using Xunit;

namespace SekoBot.Tests;

public class MarketServerTests
{
    [Fact]
    public void OldListsMoveToYakamozAndEveryServerHasItsOwnFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "pardus-servers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "pazar-shop.json"), "{}");
            File.WriteAllText(Path.Combine(root, "pazar-gecmis-shop.json"), "{}");
            string tuva = MarketServers.FolderOf(root, "Tuva");
            Assert.Equal(Path.Combine(root, "Tuva"), tuva);
            Assert.True(File.Exists(Path.Combine(root, "Yakamoz", "pazar-shop.json")));
            Assert.True(File.Exists(Path.Combine(root, "Yakamoz", "pazar-gecmis-shop.json")));
            Assert.False(File.Exists(Path.Combine(root, "pazar-shop.json")));
            Assert.Equal(Path.Combine(root, "Yakamoz"), MarketServers.FolderOf(root, "Yakamoz"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BotsOnAnotherServerAreNotOnTheSameChannel()
    {
        var me = new BotEngine.BotPlace(2, "B", "shop", 5, 0, 0, true, DateTime.UtcNow, "Tuva");
        var other = new BotEngine.BotPlace(1, "A", "shop", 5, 0, 0, true, DateTime.UtcNow, "Yakamoz");
        Assert.False(BotEngine.ShouldMove(me, [other]));
        Assert.True(BotEngine.ShouldMove(me, [other with { Server = "Tuva" }]));
    }
}
