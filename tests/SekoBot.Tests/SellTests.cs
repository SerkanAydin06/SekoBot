using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Selling at a market NPC: what goes, what stays, and the item menu's "Sat".</summary>
public class SellTests
{
    private const int Sword = 990201, Armour = 990202, Necklace = 990203, Quest = 990204, Junk = 990205, Free = 990206;

    private static void Items()
    {
        ItemBook.Learn(new ItemInfo(Sword, "Test Kılıç+0", "ITEM_WEAPON", "WEAPON_SWORD", 5000));
        ItemBook.Learn(new ItemInfo(Armour, "Test Zırh+0", "ITEM_ARMOR", "ARMOR_BODY", 15000));
        ItemBook.Learn(new ItemInfo(Necklace, "Test Kolye+0", "ITEM_ARMOR", "ARMOR_NECK", 3000));
        ItemBook.Learn(new ItemInfo(Quest, "Test Görev Kağıdı", "ITEM_QUEST", "", 100));
        ItemBook.Learn(new ItemInfo(Junk, "Test Taş", "ITEM_MATERIAL", "", 200));
        ItemBook.Learn(new ItemInfo(Free, "Test Bedava", "ITEM_MATERIAL", "", 0));
    }

    [Fact]
    public void Only_what_the_bot_picked_and_nobody_asked_for_is_sold()
    {
        Items();
        var p = new BotProfile { LootEquipmentMinGold = 3000, LootBonuses = [new BonusWant { Type = 47, Min = 10 }], LootPick = [Junk + 1] };
        InventoryItem I(int slot, int vnum, ItemBonus[]? bonuses = null) => new(slot, vnum, 1, bonuses);
        var sword = I(20, Sword);
        var armour = I(21, Armour);                                  // kept by the price limit: sold too
        var expNecklace = I(22, Necklace, [new ItemBonus(47, 12)]);  // a bonus asked for: kept
        var quest = I(23, Quest);
        var junk = I(24, Junk);
        var free = I(25, Free);                                      // worth nothing in a shop
        var mine = I(26, Sword);                                     // the user's own
        HashSet<(int, int)> picked = [(20, Sword), (21, Armour), (22, Necklace), (23, Quest), (24, Junk), (25, Free)];
        Assert.True(BotEngine.ShouldSell(sword, p, picked));
        Assert.True(BotEngine.ShouldSell(armour, p, picked));
        Assert.True(BotEngine.ShouldSell(junk, p, picked));
        Assert.False(BotEngine.ShouldSell(expNecklace, p, picked));
        Assert.False(BotEngine.ShouldSell(quest, p, picked));
        Assert.False(BotEngine.ShouldSell(free, p, picked));
        Assert.False(BotEngine.ShouldSell(mine, p, picked));
        p.LootPick = [Junk];                                         // ticked "Topla": kept
        Assert.False(BotEngine.ShouldSell(junk, p, picked));
    }

    [Fact]
    public void Selling_clicks_the_item_then_its_Sat_and_counts_it_when_it_is_gone()
    {
        var cleaner = new BagCleaner { Selling = true };
        var p = new BotProfile();
        var item = new BagSlotView(20, Sword, 1, 0, new Rectangle(700, 200, 40, 40));
        var window = new Rectangle(662, 20, 520, 599);
        BagLayout Bag(bool menu, bool itemThere) => new(true, window, 0, [], itemThere ? [item] : [], null, MenuKnown: true,
            DropButton: menu ? new Point(900, 300) : null, MenuSlot: menu ? 20 : -1, SellButton: menu ? new Point(900, 340) : null, NpcShop: 9001);
        cleaner.Start(TimeSpan.Zero);
        Assert.Equal([new ClickAction(new Point(720, 220), Slow: true, Fast: true)], cleaner.Tick(TimeSpan.Zero, Bag(false, true), _ => true, p, new Size(1204, 688)));
        // The menu: "Sat", not "Yere at".
        Assert.Equal([new ClickAction(new Point(900, 340), Slow: true, Fast: true)], cleaner.Tick(TimeSpan.FromSeconds(1), Bag(true, true), _ => true, p, new Size(1204, 688)));
        // No question; the item is gone: sold.
        cleaner.Tick(TimeSpan.FromSeconds(2), Bag(false, false), _ => true, p, new Size(1204, 688));
        Assert.Equal(1, cleaner.Thrown);
    }
}
