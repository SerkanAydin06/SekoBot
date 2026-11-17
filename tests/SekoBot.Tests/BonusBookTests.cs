using SekoBot.Bot;
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
}
