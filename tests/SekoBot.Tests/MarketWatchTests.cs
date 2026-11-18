using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class MarketWatchTests
{
    private static OpenShop Shop(int id, string owner, params (int Vnum, int Count, long Price)[] items) =>
        new(id, owner, 30, "", [.. items.Select((i, n) => new ShopItem(n, i.Vnum, i.Count, i.Price, []))]);

    [Fact]
    public void Every_word_has_to_match_and_the_price_range_and_seller_filter()
    {
        var book = new MarketBook("shop");
        book.SetAllShops([new MarketShop(1, 100, 100, null), new MarketShop(2, 200, 200, null)]);
        book.Add(Shop(1, "Ali", (11, 1, 5_000_000), (12, 10, 1_000)), DateTime.Now);
        book.Add(Shop(2, "Veli", (11, 1, 9_000_000)), DateTime.Now);
        string name = ItemBook.NameOf(11);
        var all = book.Find(new MarketQuery(""), new BonusBook(), 100, allowEmpty: true);
        Assert.Equal(3, all.Count);
        Assert.Empty(book.Find(new MarketQuery(""), new BonusBook()));
        var cheap = book.Find(new MarketQuery(name, MaxPrice: 6_000_000), new BonusBook());
        Assert.Single(cheap);
        Assert.Equal("Ali", cheap[0].Owner);
        Assert.Single(book.Find(new MarketQuery("", Seller: "vel"), new BonusBook()));
        Assert.Empty(book.Find(new MarketQuery(name + " yokböylekelime"), new BonusBook()));
        // A stack: the price per piece counts.
        Assert.Single(book.Find(new MarketQuery("", MinPrice: 50, MaxPrice: 200), new BonusBook()));
        Assert.Equal(2, book.ShopListings(1).Count);
        Assert.Equal(2, book.Listings(name).Count);
        Assert.Equal((2, 0), book.Coverage());
    }

    [Fact]
    public void A_shop_read_again_keeps_when_it_was_first_seen()
    {
        var book = new MarketBook("shop");
        var first = new DateTime(2026, 11, 1, 10, 0, 0);
        book.Add(Shop(1, "Ali", (11, 1, 100)), first);
        book.Add(Shop(1, "Ali", (11, 1, 90)), first.AddHours(3));
        var l = book.ShopListings(1).Single();
        Assert.Equal(first, l.First);
        Assert.Equal(first.AddHours(3), l.Seen);
        Assert.Equal(90, l.Price);
    }

    [Fact]
    public void A_deal_is_well_under_the_usual_price_of_an_item_seen_often()
    {
        var stats = new ItemStats("Kavrama Taşı+2", Shops: 10, Listings: 20, Pieces: 20, Min: 10_000, MinSeller: "x", MinShop: 1,
            Average: 100_000, Median: 90_000, Max: 1_000_000);
        Assert.True(BotEngine.IsDeal(55_000, stats, 40));
        Assert.False(BotEngine.IsDeal(70_000, stats, 40));
        Assert.False(BotEngine.IsDeal(10_000, stats with { Listings = 3 }, 40));
        Assert.False(BotEngine.IsDeal(10_000, stats with { Shops = 2 }, 40));
    }

    [Fact]
    public void The_price_history_keeps_one_entry_a_day_and_tells_the_trend()
    {
        var history = new MarketHistory();
        var day = new DateTime(2026, 11, 10, 12, 0, 0);
        ItemStats S(double avg) => new("Ruh Taşı", 5, 10, 10, avg / 2, "x", 1, avg, avg, avg * 2);
        history.Record([S(100)], day);
        history.Record([S(110)], day.AddHours(5)); // the same day: replaced
        history.Record([S(120)], day.AddDays(7));
        var days = history.Of("ruh taşı");
        Assert.Equal(2, days.Count);
        Assert.Equal(110, days[0].Average);
        Assert.Equal(120 / 110.0 - 1, history.Trend("Ruh Taşı")!.Value, 6);
        Assert.Null(history.Trend("Yok"));
        // Older than 90 days goes.
        history.Record([S(130)], day.AddDays(MarketHistory.KeepDays + 2));
        Assert.DoesNotContain(history.Of("Ruh Taşı"), d => d.Day == day.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void The_history_is_saved_and_read_back()
    {
        string folder = Path.Combine(Path.GetTempPath(), "sekobot-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            var history = new MarketHistory();
            history.Record([new ItemStats("Kılıç+9", 2, 3, 3, 1_000_000, "x", 1, 1_500_000, 1_500_000, 2_000_000)], DateTime.Now);
            history.Save(folder, "shop");
            var back = MarketHistory.Load(folder, "shop");
            Assert.Equal(1_500_000, back.Of("Kılıç+9").Single().Average);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }
}
