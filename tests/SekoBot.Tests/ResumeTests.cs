using SekoBot.UI;
using Xunit;

namespace SekoBot.Tests;

public class ResumeTests
{
    [Fact]
    public void The_game_copy_is_the_folder_holding_its_launcher()
    {
        string root = Path.Combine(Path.GetTempPath(), "sekobot-test-" + Guid.NewGuid().ToString("N"), "Mobile2Global-3");
        string bin = Path.Combine(root, "Game", "Mobile2Global", "Binaries", "Win64");
        Directory.CreateDirectory(bin);
        try
        {
            File.WriteAllText(Path.Combine(root, "Mobile2Launcher.exe"), "");
            Assert.Equal(root, MainForm.LauncherFolder(Path.Combine(bin, "mobilemmo-Win64-Shipping.exe")));
            Assert.Null(MainForm.LauncherFolder(Path.Combine(Path.GetTempPath(), "x", "game.exe")));
        }
        finally { Directory.Delete(Path.GetDirectoryName(root)!, true); }
    }
}
