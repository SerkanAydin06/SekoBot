using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>The market list: kept up to date, closed shops out, prices, wanted items, search, files.</summary>
public class MarketTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 14, 0, 0);

    private static readonly OpenShop Ros = new(143217, "ROS21", 23, "",
    [
        new ShopItem(55, 50096, 30, 1_500_000, []),
        new ShopItem(79, 13049, 1, 55_555_555, [new ItemBonus(1, 6)]),
        new ShopItem(49, 30277, 200, 1_222_222, []),
    ]);

    private static readonly OpenShop Ali = new(4279, "Ali", 40, "Ucuz;taş", [new ShopItem(1, 30277, 100, 300_000, [])]);

    private static readonly MarketShop[] Map =
        [new(143217, 21400, 31300, null), new(4279, 21700, 30800, null), new(9, 22000, 30000, null)];

    private static MarketBook Book()
    {
        var book = new MarketBook("shop");
        book.SetAllShops(Map);
        book.Add(Ros, Now);
        book.Add(Ali, Now);
        return book;
    }

    [Fact]
    public void The_nearest_shop_not_seen_yet_is_next()
    {
        MarketShop[] shops = [new(1, 1000, 0, null), new(2, 300, 0, null), new(3, -500, 0, null)];
        Assert.Equal(2, MarketBook.Next(shops, 0, 0, _ => false)!.Id);
        Assert.Equal(3, MarketBook.Next(shops, 0, 0, id => id == 2)!.Id);
        Assert.Null(MarketBook.Next(shops, 0, 0, _ => true));
    }

    [Fact]
    public void A_closed_shop_goes_out_of_the_list_at_once()
    {
        var book = Book();
        Assert.Equal(0, book.SetAllShops(Map));
        Assert.Equal(1, book.SetAllShops([Map[0], Map[2]]));       // Ali closed his shop
        Assert.False(book.Has(4279));
        Assert.Equal(1, book.Shops);
        Assert.Empty(book.Find("Ucuz"));
        Assert.Equal(0, book.SetAllShops([]));                       // nothing read (loading screen): nothing goes out
        Assert.Equal(1, book.Shops);
    }

    [Fact]
    public void A_shop_read_lately_is_fresh_and_opening_it_again_updates_it()
    {
        var book = Book();
        Assert.True(book.Fresh(4279, TimeSpan.FromHours(2), Now.AddMinutes(30)));
        Assert.False(book.Fresh(4279, TimeSpan.FromHours(2), Now.AddHours(3)));
        Assert.False(book.Fresh(9, TimeSpan.FromHours(2), Now));    // never read
        book.Add(Ros with { Items = [Ros.Items[0]] }, Now.AddHours(1));
        Assert.Equal(2, book.Shops);
        Assert.Equal(2, book.Items);                                  // one item left in ROS21's shop, one in Ali's
    }

    [Fact]
    public void An_item_is_found_cheapest_first_with_its_shop_and_place()
    {
        string name = ItemBook.NameOf(30277);
        var found = Book().Find(name);
        Assert.Equal(["Ali", "ROS21"], found.Select(l => l.Owner));
        Assert.Equal(3000, found[0].UnitPrice);
        Assert.Equal((4279, 21700.0, 30800.0), (found[0].ShopId, found[0].X, found[0].Y));
        Assert.Empty(Book().Find("  "));
    }

    [Fact]
    public void Every_item_is_listed_with_its_seller_place_and_price_per_piece()
    {
        var lines = Book().Csv().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);                                              // the header and four items
        var piece = Assert.Single(lines, l => l.Contains(";200;1222222;6111;"));   // 1 222 222 for 200: 6 111 each
        Assert.Contains(";ROS21;23;143217;;214;313;16.11.2026 14:00", piece);
        Assert.Contains(lines, l => l.Contains(";Ali;40;4279;Ucuz,taş;"));         // a ';' in the title doesn't break the table
    }

    [Fact]
    public void Each_item_gets_its_cheapest_seller_average_and_middle_price()
    {
        var stats = Assert.Single(Book().Stats(), s => s.Name == ItemBook.NameOf(30277));
        Assert.Equal(2, stats.Shops);
        Assert.Equal(300, stats.Pieces);
        Assert.Equal(3000, stats.Min);                       // 300 000 for 100
        Assert.Equal("Ali", stats.MinSeller);
        Assert.Equal(6111.11, stats.Max, 1);
    }

    [Fact]
    public void The_average_leaves_out_the_odd_prices_at_both_ends()
    {
        double[] prices = [60_000, 85_000, 88_000, 90_000, 90_000, 90_000, 92_000, 95_000, 99_000, 100_000, 1_111_111_111];
        Assert.InRange(MarketBook.TrimmedAverage(prices), 85_000, 100_000);
        Assert.Equal(15, MarketBook.TrimmedAverage([10, 20]));
    }

    [Fact]
    public void A_wanted_item_is_found_only_at_its_price_or_cheaper()
    {
        var book = Book();
        string name = ItemBook.NameOf(30277);
        book.Wanted = [new MarketWant { Name = name[..Math.Min(name.Length, 6)].ToUpperInvariant(), MaxPrice = 5_000 }];
        Assert.Single(book.WantedIn(Ali));                   // 3 000 each
        Assert.Empty(book.WantedIn(Ros));                    // 6 111 each: too dear
        book.Wanted = [new MarketWant { Name = name }];
        Assert.Single(book.WantedIn(Ros));
    }

    [Fact]
    public void One_list_per_map_is_kept_and_read_back()
    {
        string folder = Path.Combine(Path.GetTempPath(), "pazar-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var book = Book();
            string report = book.Save(folder);
            Assert.Equal(MarketBook.Stem(folder, "shop") + ".html", report);
            book.Add(Ali, Now.AddHours(1));
            book.Save(folder);                                                      // the same files, not new ones
            Assert.Equal(3, Directory.GetFiles(folder).Length);
            var again = MarketBook.Load(folder, "shop");
            Assert.Equal(2, again.Shops);
            Assert.True(again.Fresh(4279, TimeSpan.FromMinutes(1), Now.AddHours(1)));
            Assert.Equal(book.Csv(), again.Csv());
            Assert.Equal(0, MarketBook.Load(folder, "başka").Shops);              // another map: its own list
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void The_report_page_holds_the_data()
    {
        string html = MarketReport.Html(Book().ToJson());
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("\"owner\":\"ROS21\"", html);
        Assert.DoesNotContain("__DATA__", html);
    }

    [Fact]
    public void Skill_books_are_told_apart_by_the_skill_they_teach()
    {
        int book = ItemBook.All().FirstOrDefault(i => i.Name == "Beceri Kitabı")?.Vnum ?? 50300;
        ItemBook.Learn(new ItemInfo(book, "Beceri Kitabı", "", "", 0));
        var shop = new OpenShop(1, "Ali", 40, "", [new ShopItem(1, book, 1, 40_000, [], Socket: 4), new ShopItem(2, book, 1, 90_000, [], Socket: 3)]);
        var market = new MarketBook("shop") { Skills = new Dictionary<int, string> { [3] = "Güçlü vuruş", [4] = "Güçlü beden" } };
        market.Add(shop, Now);
        var names = market.Stats().Select(s => s.Name).ToList();
        Assert.Contains("Beceri Kitabı: Güçlü beden", names);
        Assert.Contains("Beceri Kitabı: Güçlü vuruş", names);
    }

    [Fact]
    public void Prices_over_a_billion_are_said_in_billions()
    {
        Assert.Equal("1,11 milyar", MarketBook.Yang(1_111_111_111));
        Assert.Equal("56,5 milyon", MarketBook.Yang(56_500_000));
        Assert.Equal("249.999", MarketBook.Yang(249_999));
    }
}
