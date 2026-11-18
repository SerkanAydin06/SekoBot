using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Where the market NPCs stand, learned by passing them, shared by the SekoBots.</summary>
public class NpcBookTests
{
    [Fact]
    public void Market_NPCs_seen_are_kept_per_map_and_shared()
    {
        string path = Path.Combine(Path.GetTempPath(), "sekobot-npc-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var one = NpcBook.Load(path);
            var two = NpcBook.Load(path);
            var now = DateTime.Now;
            Assert.True(one.Saw("c3", 9003, new Vec3(20000, 30000, 100), now));        // new
            Assert.False(one.Saw("c3", 9003, new Vec3(20100, 30050, 100), now));       // the same one, moved a little
            Assert.False(one.Saw("c3", 9004, new Vec3(21000, 30000, 100), now));       // the bank: not a market
            Assert.True(one.Saw("c3", 9001, new Vec3(25000, 30000, 100), now));
            one.Save(path);
            two.Save(path);
            var near = two.On("c3", new Vec3(24000, 30000, 0));
            Assert.Equal([9001, 9003], near.Select(p => p.Vnum));                       // the nearest first
            Assert.Empty(two.On("c2"));
            Assert.Equal("Market", NpcBook.Shops[9003]);
        }
        finally { File.Delete(path); }
    }
}
