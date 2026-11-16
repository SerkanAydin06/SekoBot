using System.Globalization;
using System.Text;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// What a market scan saw: every shop opened, with its items and prices. Written as a list of
/// every item (pazar-….csv, opens in Excel) and a summary per item (pazar-….txt: how many sell it,
/// the cheapest price per piece and who sells it there, the middle price).
/// </summary>
internal sealed class MarketBook
{
    private readonly Dictionary<int, (OpenShop Shop, MarketShop? Where)> _shops = [];

    public MarketBook(string map, DateTime started)
    {
        Map = map;
        Started = started;
    }

    public string Map { get; }
    public DateTime Started { get; }
    public int Shops => _shops.Count;
    public int Items => _shops.Values.Sum(s => s.Shop.Items.Count);
    public bool Has(int shopId) => _shops.ContainsKey(shopId);

    /// <summary>A shop was opened (again: its newest contents count).</summary>
    public void Add(OpenShop shop, MarketShop? where) => _shops[shop.Id] = (shop, where);

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>The game's skills by number (a skill book is named by the skill it teaches).</summary>
    public IReadOnlyDictionary<int, string> Skills { get; init; } = new Dictionary<int, string>();

    /// <summary>Its name; a skill book (or spirit stone) with the skill it is for: all books share one name.</summary>
    private string Name(ShopItem item)
    {
        string name = ItemBook.NameOf(item.Vnum);
        return (name.Contains("Kitab", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Ruh Taşı", StringComparison.OrdinalIgnoreCase)) &&
               Skills.TryGetValue(item.Socket, out var skill)
            ? $"{name}: {skill}"
            : name;
    }

    private static string Bonuses(ShopItem item) => item.Bonuses.Count == 0 ? "" : ItemBonuses.Describe(item.Bonuses);

    /// <summary>Every item, one per line (semicolons: Excel opens it as a table).</summary>
    public string Csv()
    {
        var text = new StringBuilder("Eşya;Adet;Fiyat (yang);Tanesi (yang);Efsunlar;Satıcı;Satıcı Sv;Dükkan;Dükkan başlığı;X (m);Y (m)\r\n");
        foreach (var (shop, where) in _shops.Values.OrderBy(s => s.Shop.Owner, StringComparer.Create(Turkish, true)))
            foreach (var item in shop.Items.OrderBy(Name, StringComparer.Create(Turkish, true)))
                text.Append(string.Join(";", Clean(Name(item)), item.Count, item.Price, Math.Round(item.UnitPrice), Clean(Bonuses(item)),
                    Clean(shop.Owner), shop.Level, shop.Id, Clean(shop.Title),
                    where is { } w ? (w.X / 100).ToString("0", CultureInfo.InvariantCulture) : "",
                    where is { } v ? (v.Y / 100).ToString("0", CultureInfo.InvariantCulture) : "")).Append("\r\n");
        return text.ToString();
    }

    private static string Clean(string text) => text.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>Per item name: how many shops sell it, the cheapest piece and where, the middle price.</summary>
    public string Summary()
    {
        var text = new StringBuilder();
        text.AppendLine($"Pazar taraması: {Map}, {Started:dd.MM.yyyy HH:mm} — {Shops} dükkan, {Items} ürün.");
        text.AppendLine("Fiyatlar bir tanesi için (yığınlar adede bölündü). Efsunlu eşyaların fiyatı efsuna göre çok değişir; ayrıntı .csv dosyasında.");
        text.AppendLine();
        var all = _shops.Values.SelectMany(s => s.Shop.Items.Select(i => (Item: i, s.Shop))).ToList();
        foreach (var group in all.GroupBy(x => Name(x.Item)).OrderBy(g => g.Key, StringComparer.Create(Turkish, true)))
        {
            var prices = group.Select(x => x.Item.UnitPrice).Order().ToList();
            var cheapest = group.MinBy(x => x.Item.UnitPrice);
            double middle = prices[prices.Count / 2];
            int pieces = group.Sum(x => x.Item.Count);
            text.AppendLine($"{group.Key}: {group.Select(x => x.Shop.Id).Distinct().Count()} dükkanda, {pieces.ToString("N0", Turkish)} adet · " +
                            $"en ucuz {Yang(cheapest.Item.UnitPrice)} ({cheapest.Shop.Owner}) · orta fiyat {Yang(middle)}" +
                            (prices.Count > 1 ? $" · en pahalı {Yang(prices[^1])}" : ""));
        }
        return text.ToString();
    }

    internal static string Yang(double price) => price >= 1_000_000_000
        ? (price / 1_000_000_000).ToString("0.##", Turkish) + " milyar"
        : price >= 1_000_000
        ? (price / 1_000_000).ToString("0.##", Turkish) + " milyon"
        : Math.Round(price).ToString("N0", Turkish);

    /// <summary>Writes both files (pazar-[time].csv and .txt); returns the csv's path.</summary>
    public string Save(string folder)
    {
        Directory.CreateDirectory(folder);
        string stem = Path.Combine(folder, $"pazar-{Started:yyyy-MM-dd_HH-mm}");
        // With the BOM Excel reads the Turkish letters right.
        File.WriteAllText(stem + ".csv", Csv(), new UTF8Encoding(true));
        File.WriteAllText(stem + ".txt", Summary(), new UTF8Encoding(true));
        return stem + ".csv";
    }

    /// <summary>The shop to go to next: the nearest one not seen yet (shops stand in rows, so going
    /// to the nearest each time walks the rows one after another).</summary>
    internal static MarketShop? Next(IEnumerable<MarketShop> shops, double x, double y, Func<int, bool> skip) =>
        shops.Where(s => !skip(s.Id)).MinBy(s => (s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y));
}
