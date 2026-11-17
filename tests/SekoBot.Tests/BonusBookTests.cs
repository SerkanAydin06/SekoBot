using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Which bonuses come on which items, with which values: learned from the shops.</summary>
public class BonusBookTests
{
    private const int Necklace = 990101, Shoes = 990102, Sword = 990103, Fish = 990104;

    private static BonusBook Book()
    {
        ItemBook.Learn(new ItemInfo(Necklace, "Test Kolye+0", "ITEM_ARMOR", "ARMOR_NECK", 100));
        ItemBook.Learn(new ItemInfo(Shoes, "Test Ayakkabı+0", "ITEM_ARMOR", "ARMOR_FOOTS", 100));
        ItemBook.Learn(new ItemInfo(Sword, "Test Kılıç+0", "ITEM_WEAPON", "WEAPON_SWORD", 100));
        ItemBook.Learn(new ItemInfo(Fish, "Test Balık", "ITEM_FISH", "FISH_ALIVE", 1));
        return new BonusBook();
    }

    [Fact]
    public void Bonuses_are_read_back_from_the_list_also_negative_ones()
    {
        Assert.Equal([("EXP bonusu", 15), ("Max. HP", 1000)], BonusBook.Parse("EXP bonusu +15, Max. HP +1000"));
        Assert.Equal([("Ortalama zarar", -12)], BonusBook.Parse("Ortalama zarar +-12"));  // as written before
        Assert.Equal([("Ortalama zarar", -12)], BonusBook.Parse("Ortalama zarar -12"));
        Assert.Equal("Ortalama zarar -12", ItemBonuses.Describe([new ItemBonus(14, -12)]));
    }

    [Fact]
    public void Each_bonus_knows_its_item_groups_and_values()
    {
        var book = Book();
        foreach (var (seller, value) in new[] { ("A", 6), ("B", 10), ("C", 15), ("D", 15), ("E", 6), ("F", 10) })
            book.Learn(seller, Necklace, $"EXP bonusu +{value}, Max. HP +1000");
        book.Learn("G", Shoes, "EXP bonusu +12");
        book.Learn("H", Shoes, "EXP bonusu +12");
        book.Learn("A", Necklace, "EXP bonusu +6, Max. HP +1000");   // the same listing again: counted once
        book.Learn("X", Necklace, "EXP bonusu +7");                  // once only: maybe misread
        book.Learn("Y", Fish, "EXP bonusu +15");                     // no item group: left out
        Assert.Equal([6, 10, 15], book.Values("EXP bonusu", "ARMOR_NECK"));
        Assert.Equal([12], book.Values("EXP bonusu", "ARMOR_FOOTS"));
        Assert.Empty(book.Values("EXP bonusu", "WEAPON_SWORD"));      // never seen on swords
        Assert.Equal((3, 3), book.Tier("EXP bonusu", "ARMOR_NECK", 15)); // the highest
        Assert.Equal((1, 3), book.Tier("EXP bonusu", "ARMOR_NECK", 6));
        Assert.Null(book.Tier("EXP bonusu", "ARMOR_NECK", 7));
        Assert.Equal(9, book.Listings);
        string page = book.Html();
        Assert.Contains("Kolye", page);
        Assert.Contains("Ayakkabı", page);
        Assert.DoesNotContain("Kılıç", page);
    }

    [Fact]
    public void Shared_by_the_SekoBots_and_kept()
    {
        string path = Path.Combine(Path.GetTempPath(), "sekobot-efsun-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var one = Book();
            var two = new BonusBook();
            one.Learn("A", Necklace, "EXP bonusu +15");
            one.Learn("B", Necklace, "EXP bonusu +15");
            two.Learn("C", Shoes, "EXP bonusu +12");
            two.Learn("D", Shoes, "EXP bonusu +12");
            one.Save(path);
            two.Save(path);
            Assert.Equal([15], two.Values("EXP bonusu", "ARMOR_NECK"));
            var loaded = BonusBook.Load(path);
            Assert.Equal(4, loaded.Listings);
            Assert.Equal([12], loaded.Values("EXP bonusu", "ARMOR_FOOTS"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_market_is_searched_by_item_group_and_bonus_and_ordered_by_it()
    {
        var learned = Book();
        foreach (var v in new[] { 6, 8, 10, 12, 15 })
        {
            learned.Learn("s" + v, Necklace, $"EXP bonusu +{v}, Max. HP +1000");
            learned.Learn("t" + v, Necklace, $"EXP bonusu +{v}, Max. HP +2000");
        }
        // EXP 12 on a necklace sold cheap, EXP 15 dear, EXP 15 on shoes, a necklace without EXP.
        int exp = 47, hp = 10;
        var market = new MarketBook("shop");
        market.Add(new OpenShop(1, "Ucuzcu", 30, "", [new ShopItem(1, Necklace, 1, 1_000_000, [new ItemBonus(exp, 12), new ItemBonus(hp, 1000)])]), DateTime.Now);
        market.Add(new OpenShop(2, "Pahali", 30, "", [new ShopItem(1, Necklace, 1, 9_000_000, [new ItemBonus(exp, 15), new ItemBonus(hp, 2000)])]), DateTime.Now);
        market.Add(new OpenShop(3, "Ayakkabici", 30, "", [new ShopItem(1, Shoes, 1, 500_000, [new ItemBonus(exp, 15)])]), DateTime.Now);
        market.Add(new OpenShop(4, "Baska", 30, "", [new ShopItem(1, Necklace, 1, 100_000, [new ItemBonus(hp, 500)])]), DateTime.Now);

        var necklacesExp12 = market.Find(new MarketQuery("", "ARMOR_NECK", "EXP bonusu", 12, MarketSort.BonusValue), learned);
        Assert.Equal(["Pahali", "Ucuzcu"], necklacesExp12.Select(l => l.Owner));   // the higher value first; no shoes, no EXP-less
        Assert.Equal(15, necklacesExp12[0].BonusValue);

        var cheapest = market.Find(new MarketQuery("", "ARMOR_NECK", "EXP bonusu", 12), learned);
        Assert.Equal("Ucuzcu", cheapest[0].Owner);

        var best = market.Find(new MarketQuery("", "ARMOR_NECK", Sort: MarketSort.Score), learned);
        Assert.Equal("Pahali", best[0].Owner);                                      // both bonuses at the top: score 2
        Assert.Equal(2, best[0].Score, 3);
        Assert.Empty(market.Find(new MarketQuery(""), learned));                      // nothing asked: nothing listed
        Assert.Contains("★ en yüksek", learned.Describe(Necklace, "EXP bonusu +15"));
    }

    [Fact]
    public void With_EXP_on_the_bonus_list_only_items_EXP_comes_on_are_picked_up()
    {
        var book = Book();
        int n = 0;
        foreach (var v in new[] { 6, 8, 10, 12, 15 })
            for (int k = 0; k < 3; k++)
            {
                book.Learn("n" + n++, Necklace, $"EXP bonusu +{v}");
                book.Learn("n" + n++, Shoes, $"EXP bonusu +{v}");
            }
        book.Learn("w1", Sword, "Ortalama zarar +5");                            // swords are known, never with EXP
        book.Learn("w2", Sword, "Ortalama zarar +9");
        Assert.True(book.CanCarry(Necklace, "EXP bonusu", 10));
        Assert.False(book.CanCarry(Sword, "EXP bonusu", 10));                     // EXP never comes on swords
        Assert.False(book.CanCarry(Necklace, "EXP bonusu", 20));                  // never that high
        Assert.True(book.CanCarry(Sword, "Kritik vuruş şansı", 5));               // nothing known about it: picked up
        Assert.True(book.CanCarry(990199, "EXP bonusu", 10));                     // an item not known: picked up
        Assert.Contains("Kolye", book.WhereText("EXP bonusu"));
        Assert.Contains("6 / 8 / 10 / 12 / 15", book.WhereText("EXP bonusu"));

        var p = new BotProfile { LootEquipment = false, LootBonuses = [new BonusWant { Type = 47, Min = 10 }] };  // EXP ≥ 10
        var was = BotEngine.BonusKnowledge;
        try
        {
            BotEngine.BonusKnowledge = book;
            Assert.True(BotEngine.WantedLoot(new GroundItem(1, default, Necklace, 1), p));
            Assert.True(BotEngine.WantedLoot(new GroundItem(2, default, Shoes, 1), p));
            Assert.False(BotEngine.WantedLoot(new GroundItem(3, default, Sword, 1), p));  // not even walked to
        }
        finally { BotEngine.BonusKnowledge = was; }
    }

    [Fact]
    public void A_bonus_can_be_wanted_on_one_item_only()
    {
        var book = Book();
        ItemBook.Learn(new ItemInfo(990110, "Test Çizme+3", "ITEM_ARMOR", "ARMOR_FOOTS", 100));
        // EXP ≥ 10, but only on "Test Çizme" (any level).
        var p = new BotProfile { LootEquipment = false, LootBonuses = [new BonusWant { Type = 47, Min = 10, Items = "test çizme" }] };
        var was = BotEngine.BonusKnowledge;
        try
        {
            BotEngine.BonusKnowledge = book;
            Assert.True(BotEngine.WantedLoot(new GroundItem(1, default, 990110, 1), p));
            Assert.False(BotEngine.WantedLoot(new GroundItem(2, default, Shoes, 1), p));     // other shoes: no
            Assert.False(BotEngine.WantedLoot(new GroundItem(3, default, Necklace, 1), p));  // EXP could come, not wanted here
        }
        finally { BotEngine.BonusKnowledge = was; }
        Assert.True(BotEngine.KeepPicked(new InventoryItem(20, 990110, 1, [new ItemBonus(47, 12)]), p));
        Assert.False(BotEngine.KeepPicked(new InventoryItem(21, 990110, 1, [new ItemBonus(47, 8)]), p));  // too low
        Assert.False(BotEngine.KeepPicked(new InventoryItem(22, Shoes, 1, [new ItemBonus(47, 15)]), p));  // not that item
    }
}
