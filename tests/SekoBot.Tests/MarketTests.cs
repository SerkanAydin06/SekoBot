using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>The market scan: which shop is next, and the lists written from what was seen.</summary>
public class MarketTests
{
    private static readonly OpenShop Ros = new(143217, "ROS21", 23, "",
    [
        new ShopItem(55, 50096, 30, 1_500_000, []),
        new ShopItem(79, 13049, 1, 55_555_555, [new ItemBonus(1, 6)]),
        new ShopItem(49, 30277, 200, 1_222_222, []),
    ]);

    private static readonly OpenShop Ali = new(4279, "Ali", 40, "Ucuz;taş", [new ShopItem(1, 30277, 100, 300_000, [])]);

    [Fact]
    public void The_nearest_shop_not_seen_yet_is_next()
    {
        MarketShop[] shops = [new(1, 1000, 0, null), new(2, 300, 0, null), new(3, -500, 0, null)];
        Assert.Equal(2, MarketBook.Next(shops, 0, 0, _ => false)!.Id);
        Assert.Equal(3, MarketBook.Next(shops, 0, 0, id => id == 2)!.Id);
        Assert.Null(MarketBook.Next(shops, 0, 0, _ => true));
    }

    [Fact]
    public void Every_item_is_listed_with_its_seller_and_price_per_piece()
    {
        var book = new MarketBook("shop", new DateTime(2026, 11, 16, 14, 0, 0));
        book.Add(Ros, new MarketShop(143217, 21400, 31300, null));
        book.Add(Ali, null);
        Assert.Equal(2, book.Shops);
        Assert.Equal(4, book.Items);
        var lines = book.Csv().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);                                     // the header and four items
        var piece = Assert.Single(lines, l => l.Contains(";200;1222222;6111;"));   // 1 222 222 for 200: 6 111 each
        Assert.Contains(";ROS21;23;143217;;214;313", piece);
        Assert.Contains(lines, l => l.Contains(";Ali;40;4279;Ucuz,taş;"));  // a ';' in the title doesn't break the table
    }

    [Fact]
    public void The_summary_shows_the_cheapest_seller_of_each_item()
    {
        var book = new MarketBook("shop", new DateTime(2026, 11, 16, 14, 0, 0));
        book.Add(Ros, null);
        book.Add(Ali, null);
        string line = Assert.Single(book.Summary().Split('\n'), l => l.StartsWith(ItemBook.NameOf(30277) + ":"));
        Assert.Contains("2 dükkanda, 300 adet", line);
        Assert.Contains("en ucuz 3.000 (Ali)", line);                     // 300 000 for 100
        Assert.Contains("en pahalı 6.111", line);
    }

    [Fact]
    public void A_shop_opened_again_counts_once_with_its_newest_items()
    {
        var book = new MarketBook("shop", DateTime.Now);
        book.Add(Ros, null);
        book.Add(Ros with { Items = [Ros.Items[0]] }, null);
        Assert.Equal(1, book.Shops);
        Assert.Equal(1, book.Items);
        Assert.True(book.Has(143217));
    }
}
