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
        ItemBook.Learn(new ItemInfo(Sword, "Test Kılıç+0", "ITEM_WEAPON", "WEAPON_SWORD", 20000));
        ItemBook.Learn(new ItemInfo(Armour, "Test Zırh+0", "ITEM_ARMOR", "ARMOR_BODY", 15000));
        ItemBook.Learn(new ItemInfo(Necklace, "Test Kolye+0", "ITEM_ARMOR", "ARMOR_NECK", 3000));
        ItemBook.Learn(new ItemInfo(Quest, "Test Görev Kağıdı", "ITEM_QUEST", "", 100));
        ItemBook.Learn(new ItemInfo(Junk, "Test Taş", "ITEM_MATERIAL", "", 200));
        ItemBook.Learn(new ItemInfo(Free, "Test Bedava", "ITEM_MATERIAL", "", 0));
    }

    [Fact]
    public void Only_weapons_and_armour_picked_for_their_price_are_sold()
    {
        Items();
        var p = new BotProfile { LootEquipmentMinGold = 3000, LootBonuses = [new BonusWant { Type = 47, Min = 10 }], LootPick = [Junk + 1] };
        InventoryItem I(int slot, int vnum, ItemBonus[]? bonuses = null) => new(slot, vnum, 1, bonuses);
        var sword = I(20, Sword);                                    // picked for its price: sold
        var armour = I(21, Armour);
        var expNecklace = I(22, Necklace, [new ItemBonus(47, 12)]);  // a bonus asked for: kept
        var quest = I(23, Quest);
        var junk = I(24, Junk);                                      // not weapon / armour: thrown, not sold
        var free = I(25, Free);                                      // worth nothing in a shop
        var mine = I(26, Sword);                                     // the user's own
        HashSet<(int, int)> picked = [(20, Sword), (21, Armour), (22, Necklace), (23, Quest), (24, Junk), (25, Free)];
        Assert.True(BotEngine.ShouldSell(sword, p, picked));
        Assert.True(BotEngine.ShouldSell(armour, p, picked));
        Assert.False(BotEngine.ShouldSell(junk, p, picked));
        Assert.False(BotEngine.ShouldSell(expNecklace, p, picked));
        // Cheaper than the limit (thrown as before), ticked "At", or every weapon asked for: not sold.
        Assert.False(BotEngine.ShouldSell(sword, new BotProfile { LootEquipmentMinGold = 5000 }, picked));
        Assert.False(BotEngine.ShouldSell(sword, new BotProfile { LootEquipmentMinGold = 3000, LootThrow = [Sword] }, picked));
        Assert.False(BotEngine.ShouldSell(sword, new BotProfile { LootEquipmentMinGold = 3000, LootEquipment = true }, picked));
        Assert.False(BotEngine.ShouldSell(sword, new BotProfile(), picked));
        Assert.False(BotEngine.ShouldSell(quest, p, picked));
        Assert.False(BotEngine.ShouldSell(free, p, picked));
        Assert.False(BotEngine.ShouldSell(mine, p, picked));
        p.LootPick = [Sword];                                        // ticked "Topla": kept
        Assert.False(BotEngine.ShouldSell(sword, p, picked));
    }

    [Fact]
    public void An_item_found_wanted_when_its_menu_is_up_is_not_thrown()
    {
        // Seen: an EXP +6 necklace thrown; its bonus is read again right before "Yere at".
        var cleaner = new BagCleaner();
        var p = new BotProfile();
        var item = new BagSlotView(20, Sword, 1, 0, new Rectangle(700, 200, 40, 40));
        var window = new Rectangle(662, 20, 520, 599);
        BagLayout Bag(bool menu) => new(true, window, 0, [], [item], null, MenuKnown: true,
            DropButton: menu ? new Point(900, 300) : null, MenuSlot: menu ? 20 : -1);
        cleaner.Start(TimeSpan.Zero);
        bool junk = true;
        var click = Assert.IsType<ClickAction>(Assert.Single(cleaner.Tick(TimeSpan.Zero, Bag(false), _ => junk, p, new Size(1204, 688))));
        Assert.InRange(click.Client.X, 715, 725);
        Assert.InRange(click.Client.Y, 215, 225);
        Assert.Equal(new ClickAction(click.Client, Slow: true, Fast: true), click);
        junk = false; // its bonus came through meanwhile
        var actions = cleaner.Tick(TimeSpan.FromSeconds(1), Bag(true), _ => junk, p, new Size(1204, 688));
        Assert.DoesNotContain(new ClickAction(new Point(900, 300), Slow: true, Fast: true), actions);
        Assert.Contains(new KeyTapAction(p.Keys.CloseWindow), actions);
        Assert.Equal(0, cleaner.Thrown);
    }

    [Fact]
    public void A_picked_item_found_wanted_later_is_forgotten()
    {
        var picked = new PickedItems();
        picked.Update([], true, (Func<InventoryItem, bool>?)null);
        picked.Update([new InventoryItem(20, Sword, 1)], true, (Func<InventoryItem, bool>?)(_ => false));
        Assert.Contains((20, Sword), picked.Picked);
        picked.Forget(20, Sword);
        Assert.DoesNotContain((20, Sword), picked.Picked);
    }

    private static void AssertSaleClick(IReadOnlyList<BotAction> actions, Point center, bool right = false)
    {
        var click = Assert.IsType<ClickAction>(Assert.Single(actions));
        Assert.InRange(click.Client.X, center.X - 5, center.X + 5);
        Assert.InRange(click.Client.Y, center.Y - 5, center.Y + 5);
        Assert.Equal(new ClickAction(click.Client, Slow: true, Fast: true, Right: right), click);
    }

    [Fact]
    public void Selling_right_clicks_the_item_and_says_yes_with_Enter()
    {
        var cleaner = new BagCleaner { Selling = true };
        var p = new BotProfile();
        var item = new BagSlotView(20, Sword, 1, 0, new Rectangle(700, 200, 40, 40));
        var window = new Rectangle(662, 20, 520, 599);
        var size = new Size(1204, 688);
        BagLayout Bag(bool question, bool itemThere, int shop = 9001) => new(true, window, 0, [], itemThere ? [item] : [], null,
            MenuKnown: true, NpcShop: shop, OtherConfirm: question ? new Point(600, 380) : null);
        cleaner.Start(TimeSpan.Zero);
        // The shop closed: nothing clicked.
        Assert.Empty(cleaner.Tick(TimeSpan.Zero, Bag(false, true, shop: 0), _ => true, p, size));
        AssertSaleClick(cleaner.Tick(TimeSpan.FromSeconds(0.5), Bag(false, true), _ => true, p, size), new Point(720, 220), right: true);
        // "Satılsın mı?": Enter.
        Assert.Equal([new KeyTapAction(Keys.Enter)], cleaner.Tick(TimeSpan.FromSeconds(0.7), Bag(true, true), _ => true, p, size));
        // Still showing a moment later: waited on, not clicked as well.
        Assert.Empty(cleaner.Tick(TimeSpan.FromSeconds(0.9), Bag(true, true), _ => true, p, size));
        // Gone with the item: sold.
        cleaner.Tick(TimeSpan.FromSeconds(1.1), Bag(false, false), _ => true, p, size);
        Assert.Equal(1, cleaner.Thrown);
    }

    [Fact]
    public void Selling_falls_back_to_the_menus_Sat_when_a_right_click_brings_no_question()
    {
        var cleaner = new BagCleaner { Selling = true };
        var p = new BotProfile();
        var item = new BagSlotView(20, Sword, 1, 0, new Rectangle(700, 200, 40, 40));
        var window = new Rectangle(662, 20, 520, 599);
        BagLayout Bag(bool menu, bool itemThere) => new(true, window, 0, [], itemThere ? [item] : [], null, MenuKnown: true,
            DropButton: menu ? new Point(900, 300) : null, MenuSlot: menu ? 20 : -1, SellButton: menu ? new Point(900, 340) : null, NpcShop: 9001, SellBounds: new Rectangle(890, 330, 20, 20));
        cleaner.Start(TimeSpan.Zero);
        AssertSaleClick(cleaner.Tick(TimeSpan.Zero, Bag(false, true), _ => true, p, new Size(1204, 688)), new Point(720, 220), right: true);
        // No question after the right click: the item's menu instead.
        AssertSaleClick(cleaner.Tick(TimeSpan.FromSeconds(1.5), Bag(false, true), _ => true, p, new Size(1204, 688)), new Point(720, 220));
        var t0 = 1.5;
        // The menu: "Sat", not "Yere at".
        Assert.Empty(cleaner.Tick(TimeSpan.FromSeconds(t0 + 1), Bag(true, true), _ => true, p, new Size(1204, 688))); // read once: waits
        AssertSaleClick(cleaner.Tick(TimeSpan.FromSeconds(t0 + 1.5), Bag(true, true), _ => true, p, new Size(1204, 688)), new Point(900, 340));
        // No question; the item is gone: sold.
        cleaner.Tick(TimeSpan.FromSeconds(t0 + 2), Bag(false, false), _ => true, p, new Size(1204, 688));
        Assert.Equal(1, cleaner.Thrown);
    }
}

public class BagBonusTests
{
    [Fact]
    public void The_window_bag_keeps_the_bonuses_read_from_the_character_data()
    {
        // Seen: the window's list (no bonuses) replaced the data's, and an EXP +6 necklace was thrown.
        var exp = new SekoBot.Memory.ItemBonus(47, 6);
        IReadOnlyList<SekoBot.Memory.InventoryItem> data = [new(7, 16007, 1, [new(47, 10)]), new(17, 16000, 1, [exp]), new(20, 11850, 1)];
        IReadOnlyList<SekoBot.Memory.InventoryItem> window = [new(17, 16000, 1), new(20, 11850, 1), new(21, 27001, 150)];
        var bag = SekoBot.Memory.GameWorldReader.WithBonuses(data, window);
        Assert.Equal(4, bag.Count);
        Assert.Equal([exp], bag.Single(i => i.Slot == 17).Bonuses!);
        Assert.NotNull(bag.Single(i => i.Slot == 7).Bonuses);   // worn: from the data
        Assert.Null(bag.Single(i => i.Slot == 20).Bonuses);
        Assert.Equal(150, bag.Single(i => i.Slot == 21).Count);   // the window's count
        var p = new SekoBot.Config.BotProfile { LootBonuses = [new SekoBot.Config.BonusWant { Type = 47, Min = 6 }] };
        Assert.True(SekoBot.Bot.BotEngine.GoodBonus(bag.Single(i => i.Slot == 17), p));
    }
}

public class PickedMemoryTests
{
    private const int Sword = 11, Bow = 2001;

    [Fact]
    public void A_bad_read_of_the_bag_doesnt_make_the_bot_forget_what_it_picked()
    {
        var now = new DateTime(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
        var picked = new PickedItems { Now = () => now };
        InventoryItem[] mine = [new(20, 27001, 50), new(21, 27002, 50), new(22, 27003, 50), new(23, 50001, 1), new(24, 50002, 1)];
        picked.Update(mine, true, (Func<InventoryItem, bool>?)null);
        picked.Update([.. mine, new(30, Sword, 1), new(31, Bow, 1)], true, (Func<InventoryItem, bool>?)(_ => false));
        Assert.Equal(2, picked.Picked.Count);
        // Loading a map / reconnecting: the bag reads empty, or only partly, for a few seconds.
        for (int i = 0; i < 30; i++)
        {
            now = now.AddMilliseconds(200);
            picked.Update(i % 2 == 0 ? [] : [new(20, 27001, 50)], false, (Func<InventoryItem, bool>?)null);
        }
        now = now.AddMilliseconds(200);
        picked.Update([.. mine, new(30, Sword, 1), new(31, Bow, 1)], false, (Func<InventoryItem, bool>?)null);
        Assert.Contains((30, Sword), picked.Picked);
        Assert.Contains((31, Bow), picked.Picked);
    }

    [Fact]
    public void An_item_really_gone_is_forgotten()
    {
        var picked = new PickedItems();
        picked.Update([new(20, 27001, 50)], true, (Func<InventoryItem, bool>?)null);
        picked.Update([new(20, 27001, 50), new(30, Sword, 1)], true, (Func<InventoryItem, bool>?)(_ => false));
        picked.Update([new(20, 27001, 50)], true, (Func<InventoryItem, bool>?)null);   // thrown / sold
        Assert.Empty(picked.Picked);
    }
}
