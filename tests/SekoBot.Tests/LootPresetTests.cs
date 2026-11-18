using SekoBot.Config;
using Xunit;

namespace SekoBot.Tests;

public class LootPresetTests
{
    [Fact]
    public void Pick_up_settings_saved_under_a_name_load_on_another_character()
    {
        string folder = Path.Combine(Path.GetTempPath(), "sekobot-presets-" + Guid.NewGuid().ToString("N"));
        string old = LootPresets.Folder;
        LootPresets.Folder = folder;
        try
        {
            var kaizer = new BotProfile
            {
                LootEquipmentMinGold = 3000, LootPick = [27001, 27002], LootSkip = [11], LootThrow = [12, 13],
                LootBonuses = [new BonusWant { Type = 47, Min = 6 }], LootAlways = "Cesaret Pelerini", SellToNpc = false,
                PullMaxMobs = 15,
            };
            LootPresets.Save("KAiZER ayarı", kaizer);
            Assert.Equal(["KAiZER ayarı"], LootPresets.All());

            var other = new BotProfile { PullMaxMobs = 6 };
            Assert.True(LootPresets.Load("KAiZER ayarı", other));
            Assert.Equal(3000, other.LootEquipmentMinGold);
            Assert.Equal([27001, 27002], other.LootPick);
            Assert.Equal([11], other.LootSkip);
            Assert.Equal([12, 13], other.LootThrow);
            Assert.Equal(47, Assert.Single(other.LootBonuses).Type);
            Assert.Equal("Cesaret Pelerini", other.LootAlways);
            Assert.False(other.SellToNpc);
            Assert.Equal(6, other.PullMaxMobs); // how it fights stays its own

            Assert.False(LootPresets.Load("yok", other));
            LootPresets.Delete("KAiZER ayarı");
            Assert.Empty(LootPresets.All());
        }
        finally
        {
            LootPresets.Folder = old;
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
