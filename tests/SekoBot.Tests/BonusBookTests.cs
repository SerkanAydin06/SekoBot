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
}
