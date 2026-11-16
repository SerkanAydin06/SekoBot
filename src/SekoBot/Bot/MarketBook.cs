using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>Prices of one item over the shops that sell it (per piece: stacks divided by their size).</summary>
/// <param name="Average">The average without the odd prices at both ends (a tenth each side from 5
/// listings on: troll prices like 1 billion for a fragment don't pull it up).</param>
internal sealed record ItemStats(string Name, int Shops, int Listings, long Pieces, double Min, string MinSeller, int MinShop,
    double Average, double Median, double Max);

/// <summary>
/// What a market scan saw: every shop opened, with its items and prices, and where every shop of
/// the map stands. Saved as data (pazar-….json, also to go on with a scan stopped half way), as a
/// table of every item (….csv, opens in Excel) and as a report to look at (….html: the market
/// map, search, the cheapest seller and the prices of each item, the wanted items first).
/// </summary>
internal sealed class MarketBook
{
    private readonly Dictionary<int, Shop> _shops = [];
    private Dictionary<int, (double X, double Y)> _all = [];

    public MarketBook(string map, DateTime started)
    {
        Map = map;
        Started = started;
    }

    public string Map { get; }
    public DateTime Started { get; private set; }
    public int Shops => _shops.Count;
    public int Items => _shops.Values.Sum(s => s.Items.Count);
    public bool Has(int shopId) => _shops.ContainsKey(shopId);

    /// <summary>The game's skills by number (a skill book is named by the skill it teaches).</summary>
    public IReadOnlyDictionary<int, string> Skills { get; set; } = new Dictionary<int, string>();

    /// <summary>The items looked for (shown first in the report).</summary>
    public IReadOnlyList<MarketWant> Wanted { get; set; } = [];

    /// <summary>Every shop of the map and where it stands (for the map in the report).</summary>
    public void SetAllShops(IEnumerable<MarketShop> shops)
    {
        foreach (var s in shops)
            _all[s.Id] = (s.X, s.Y);
    }

    /// <summary>A shop was opened (again: its newest contents count).</summary>
    public void Add(OpenShop shop, MarketShop? where)
    {
        var at = where is { } w ? (w.X, w.Y) : _all.TryGetValue(shop.Id, out var known) ? known : (0.0, 0.0);
        _shops[shop.Id] = new Shop
        {
            Id = shop.Id, Owner = shop.Owner, Level = shop.Level, Title = shop.Title, X = at.Item1, Y = at.Item2,
            Items = [.. shop.Items.Select(i => new Item
            {
                Name = Name(i), Vnum = i.Vnum, Count = i.Count, Price = i.Price, Socket = i.Socket,
                Bonuses = i.Bonuses.Count == 0 ? "" : ItemBonuses.Describe(i.Bonuses),
            })],
        };
    }

    /// <summary>The wanted items this shop sells at the wanted price or cheaper.</summary>
    public List<(MarketWant Want, string Item, double UnitPrice)> WantedIn(OpenShop shop) =>
        [.. shop.Items.SelectMany(i => Wanted.Where(w => w.Matches(Name(i)) && (w.MaxPrice <= 0 || i.UnitPrice <= w.MaxPrice))
                                              .Select(w => (w, Name(i), i.UnitPrice)))];

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Its name; a skill book (or spirit stone) with the skill it is for: all books share one name.</summary>
    private string Name(ShopItem item)
    {
        string name = ItemBook.NameOf(item.Vnum);
        return (name.Contains("Kitab", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Ruh Taşı", StringComparison.OrdinalIgnoreCase)) &&
               Skills.TryGetValue(item.Socket, out var skill)
            ? $"{name}: {skill}"
            : name;
    }

    // ── Prices ──

    /// <summary>Per item name: who sells it and at what price per piece.</summary>
    public List<ItemStats> Stats() =>
        [.. _shops.Values.SelectMany(s => s.Items.Select(i => (Item: i, Shop: s)))
            .GroupBy(x => x.Item.Name)
            .Select(g =>
            {
                var prices = g.Select(x => x.Item.UnitPrice).Order().ToList();
                var cheapest = g.MinBy(x => x.Item.UnitPrice);
                return new ItemStats(g.Key, g.Select(x => x.Shop.Id).Distinct().Count(), prices.Count, g.Sum(x => (long)x.Item.Count),
                    prices[0], cheapest.Shop.Owner, cheapest.Shop.Id, TrimmedAverage(prices), prices[prices.Count / 2], prices[^1]);
            })
            .OrderBy(s => s.Name, StringComparer.Create(Turkish, true))];

    /// <summary>The average of sorted prices without a tenth at each end (from 5 prices on).</summary>
    internal static double TrimmedAverage(IReadOnlyList<double> sorted)
    {
        int cut = sorted.Count >= 5 ? Math.Max(1, sorted.Count / 10) : 0;
        return sorted.Skip(cut).Take(sorted.Count - 2 * cut).Average();
    }

    internal static string Yang(double price) => price >= 1_000_000_000
        ? (price / 1_000_000_000).ToString("0.##", Turkish) + " milyar"
        : price >= 1_000_000
            ? (price / 1_000_000).ToString("0.##", Turkish) + " milyon"
            : Math.Round(price).ToString("N0", Turkish);

    // ── Files ──

    /// <summary>Every item, one per line (semicolons: Excel opens it as a table).</summary>
    public string Csv()
    {
        var text = new StringBuilder("Eşya;Adet;Fiyat (yang);Tanesi (yang);Efsunlar;Satıcı;Satıcı Sv;Dükkan;Dükkan başlığı;X (m);Y (m)\r\n");
        foreach (var shop in _shops.Values.OrderBy(s => s.Owner, StringComparer.Create(Turkish, true)))
            foreach (var item in shop.Items.OrderBy(i => i.Name, StringComparer.Create(Turkish, true)))
                text.Append(string.Join(";", Clean(item.Name), item.Count, item.Price, Math.Round(item.UnitPrice), Clean(item.Bonuses),
                    Clean(shop.Owner), shop.Level, shop.Id, Clean(shop.Title),
                    shop.X == 0 && shop.Y == 0 ? "" : (shop.X / 100).ToString("0", CultureInfo.InvariantCulture),
                    shop.X == 0 && shop.Y == 0 ? "" : (shop.Y / 100).ToString("0", CultureInfo.InvariantCulture))).Append("\r\n");
        return text.ToString();
    }

    private static string Clean(string text) => text.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>All of it as data (what the report shows; read back to go on with a scan).</summary>
    public string ToJson() => JsonSerializer.Serialize(new Data
    {
        Map = Map, Started = Started, Saved = DateTime.Now,
        Shops = [.. _shops.Values],
        All = [.. _all.Select(kv => new[] { kv.Key, Math.Round(kv.Value.X), Math.Round(kv.Value.Y) })],
        Stats = Stats(),
        Wanted = [.. Wanted],
    }, Json);

    /// <summary>A scan saved before (to go on with it), or null.</summary>
    public static MarketBook? FromJson(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize<Data>(json, Json) is not { } data)
                return null;
            var book = new MarketBook(data.Map, data.Started);
            foreach (var shop in data.Shops)
                book._shops[shop.Id] = shop;
            foreach (var a in data.All.Where(a => a.Length == 3))
                book._all[(int)a[0]] = (a[1], a[2]);
            return book;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the data, the table and the report (pazar-[time].json / .csv / .html); returns the report's path.</summary>
    public string Save(string folder)
    {
        Directory.CreateDirectory(folder);
        string stem = Path.Combine(folder, $"pazar-{Started:yyyy-MM-dd_HH-mm}");
        string json = ToJson();
        File.WriteAllText(stem + ".json", json, new UTF8Encoding(false));
        // With the BOM Excel reads the Turkish letters right.
        File.WriteAllText(stem + ".csv", Csv(), new UTF8Encoding(true));
        File.WriteAllText(stem + ".html", MarketReport.Html(json), new UTF8Encoding(false));
        return stem + ".html";
    }

    /// <summary>The newest scan saved in <paramref name="folder"/> for this map, if it isn't older than <paramref name="maxAge"/>.</summary>
    public static MarketBook? Latest(string folder, string map, TimeSpan maxAge)
    {
        if (!Directory.Exists(folder))
            return null;
        foreach (var file in new DirectoryInfo(folder).GetFiles("pazar-*.json").OrderByDescending(f => f.LastWriteTime))
        {
            if (DateTime.Now - file.LastWriteTime > maxAge)
                return null;
            try
            {
                if (FromJson(File.ReadAllText(file.FullName)) is { } book && book.Map == map)
                    return book;
            }
            catch (IOException) { }
        }
        return null;
    }

    /// <summary>The shop to go to next: the nearest one not seen yet (shops stand in rows, so going
    /// to the nearest each time walks the rows one after another).</summary>
    internal static MarketShop? Next(IEnumerable<MarketShop> shops, double x, double y, Func<int, bool> skip) =>
        shops.Where(s => !skip(s.Id)).MinBy(s => (s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y));

    // ── What is saved ──

    internal sealed class Shop
    {
        public int Id { get; set; }
        public string Owner { get; set; } = "";
        public int Level { get; set; }
        public string Title { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public List<Item> Items { get; set; } = [];
    }

    internal sealed class Item
    {
        public string Name { get; set; } = "";
        public int Vnum { get; set; }
        public int Count { get; set; }
        public long Price { get; set; }
        public int Socket { get; set; }
        public string Bonuses { get; set; } = "";
        [JsonIgnore] public double UnitPrice => Count > 1 ? (double)Price / Count : Price;
    }

    private sealed class Data
    {
        public string Map { get; set; } = "";
        public DateTime Started { get; set; }
        public DateTime Saved { get; set; }
        public List<Shop> Shops { get; set; } = [];
        public List<double[]> All { get; set; } = [];
        public List<ItemStats> Stats { get; set; } = [];
        public List<MarketWant> Wanted { get; set; } = [];
    }
}
