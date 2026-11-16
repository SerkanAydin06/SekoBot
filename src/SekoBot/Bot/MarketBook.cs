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

/// <summary>One item for sale, found in the market list: what, for how much, in which shop and where.</summary>
internal sealed record MarketListing(string Name, int Count, long Price, double UnitPrice, string Bonuses,
    int ShopId, string Owner, double X, double Y, DateTime Seen);

/// <summary>
/// The market of one map, kept up to date: every shop ever opened with its items and prices and
/// when it was read, and where every shop of the map stands. One file per map (pazar/[map].json):
/// every scan, every shop the bot or the player opens updates it, and shops that are gone from the
/// map are taken out at once. Also written as a table (….csv, Excel) and a report to look at (….html).
/// </summary>
internal sealed class MarketBook
{
    private readonly Dictionary<int, Shop> _shops = [];
    private readonly Dictionary<int, (double X, double Y)> _all = [];
    private readonly object _gate = new();

    public MarketBook(string map)
    {
        Map = map;
    }

    public string Map { get; }
    /// <summary>When something changed last.</summary>
    public DateTime Updated { get; private set; } = DateTime.Now;
    public bool Changed { get; private set; }

    public int Shops { get { lock (_gate) return _shops.Count; } }
    public int Items { get { lock (_gate) return _shops.Values.Sum(s => s.Items.Count); } }
    public bool Has(int shopId) { lock (_gate) return _shops.ContainsKey(shopId); }

    /// <summary>Read within <paramref name="within"/> (no need to open it again).</summary>
    public bool Fresh(int shopId, TimeSpan within, DateTime now)
    {
        lock (_gate)
            return _shops.TryGetValue(shopId, out var s) && now - s.Seen < within;
    }

    /// <summary>The game's skills by number (a skill book is named by the skill it teaches).</summary>
    public IReadOnlyDictionary<int, string> Skills { get; set; } = new Dictionary<int, string>();

    /// <summary>The items looked for (shown first in the report).</summary>
    public IReadOnlyList<MarketWant> Wanted { get; set; } = [];

    /// <summary>Every shop of the map now (where it stands). Shops gone from the map are taken out
    /// of the list (closed: what they sold can't be bought any more).</summary>
    /// <returns>How many known shops were gone.</returns>
    public int SetAllShops(IReadOnlyCollection<MarketShop> shops)
    {
        if (shops.Count == 0)
            return 0; // not read (a loading screen): nothing is taken out
        lock (_gate)
        {
            var now = shops.Select(s => s.Id).ToHashSet();
            int gone = 0;
            foreach (int id in _shops.Keys.Where(id => !now.Contains(id)).ToList())
            {
                _shops.Remove(id);
                gone++;
            }
            _all.Clear();
            foreach (var s in shops)
            {
                _all[s.Id] = (s.X, s.Y);
                if (_shops.TryGetValue(s.Id, out var known) && known.X == 0 && known.Y == 0)
                    (known.X, known.Y) = (s.X, s.Y);
            }
            if (gone > 0)
                Touch();
            return gone;
        }
    }

    /// <summary>Where a shop stands (null: not on the map now).</summary>
    public (double X, double Y)? Where(int shopId)
    {
        lock (_gate)
            return _all.TryGetValue(shopId, out var at) ? at : _shops.TryGetValue(shopId, out var s) && (s.X != 0 || s.Y != 0) ? (s.X, s.Y) : null;
    }

    /// <summary>A shop was opened: its newest contents replace what was known.</summary>
    public void Add(OpenShop shop, DateTime seen)
    {
        lock (_gate)
        {
            var at = _all.TryGetValue(shop.Id, out var known) ? known : _shops.TryGetValue(shop.Id, out var old) ? (old.X, old.Y) : (0.0, 0.0);
            _shops[shop.Id] = new Shop
            {
                Id = shop.Id, Owner = shop.Owner, Level = shop.Level, Title = shop.Title, X = at.Item1, Y = at.Item2, Seen = seen,
                Items = [.. shop.Items.Select(i => new Item
                {
                    Name = Name(i), Vnum = i.Vnum, Count = i.Count, Price = i.Price, Socket = i.Socket,
                    Bonuses = i.Bonuses.Count == 0 ? "" : ItemBonuses.Describe(i.Bonuses),
                })],
            };
            Touch();
        }
    }

    private void Touch()
    {
        Changed = true;
        Updated = DateTime.Now;
    }

    /// <summary>The wanted items this shop sells at the wanted price or cheaper.</summary>
    public List<(MarketWant Want, string Item, double UnitPrice)> WantedIn(OpenShop shop) =>
        [.. shop.Items.SelectMany(i => Wanted.Where(w => w.Matches(Name(i)) && (w.MaxPrice <= 0 || i.UnitPrice <= w.MaxPrice))
                                              .Select(w => (w, Name(i), i.UnitPrice)))];

    /// <summary>Every item whose name (or bonuses) holds <paramref name="query"/>, cheapest piece first.</summary>
    public List<MarketListing> Find(string query, int max = 300)
    {
        query = query.Trim();
        if (query.Length == 0)
            return [];
        lock (_gate)
            return [.. _shops.Values.SelectMany(s => s.Items
                    .Where(i => i.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                i.Bonuses.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    .Select(i => new MarketListing(i.Name, i.Count, i.Price, i.UnitPrice, i.Bonuses, s.Id, s.Owner, s.X, s.Y, s.Seen)))
                .OrderBy(l => l.UnitPrice).Take(max)];
    }

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
    public List<ItemStats> Stats()
    {
        lock (_gate)
            return [.. _shops.Values.SelectMany(s => s.Items.Select(i => (Item: i, Shop: s)))
                .GroupBy(x => x.Item.Name)
                .Select(g =>
                {
                    var prices = g.Select(x => x.Item.UnitPrice).Order().ToList();
                    var cheapest = g.MinBy(x => x.Item.UnitPrice);
                    return new ItemStats(g.Key, g.Select(x => x.Shop.Id).Distinct().Count(), prices.Count, g.Sum(x => (long)x.Item.Count),
                        prices[0], cheapest.Shop.Owner, cheapest.Shop.Id, TrimmedAverage(prices), prices[prices.Count / 2], prices[^1]);
                })
                .OrderBy(s => s.Name, StringComparer.Create(Turkish, true))];
    }

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
        var text = new StringBuilder("Eşya;Adet;Fiyat (yang);Tanesi (yang);Efsunlar;Satıcı;Satıcı Sv;Dükkan;Dükkan başlığı;X (m);Y (m);Okundu\r\n");
        lock (_gate)
            foreach (var shop in _shops.Values.OrderBy(s => s.Owner, StringComparer.Create(Turkish, true)))
                foreach (var item in shop.Items.OrderBy(i => i.Name, StringComparer.Create(Turkish, true)))
                    text.Append(string.Join(";", Clean(item.Name), item.Count, item.Price, Math.Round(item.UnitPrice), Clean(item.Bonuses),
                        Clean(shop.Owner), shop.Level, shop.Id, Clean(shop.Title),
                        shop.X == 0 && shop.Y == 0 ? "" : (shop.X / 100).ToString("0", CultureInfo.InvariantCulture),
                        shop.X == 0 && shop.Y == 0 ? "" : (shop.Y / 100).ToString("0", CultureInfo.InvariantCulture),
                        shop.Seen.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture))).Append("\r\n");
        return text.ToString();
    }

    private static string Clean(string text) => text.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>All of it as data (what the report shows, and what is read back next time).</summary>
    public string ToJson()
    {
        var stats = Stats();
        lock (_gate)
            return JsonSerializer.Serialize(new Data
            {
                Map = Map, Saved = Updated,
                Shops = [.. _shops.Values],
                All = [.. _all.Select(kv => new[] { kv.Key, Math.Round(kv.Value.X), Math.Round(kv.Value.Y) })],
                Stats = stats,
                Wanted = [.. Wanted],
            }, Json);
    }

    /// <summary>A market saved before, or null.</summary>
    public static MarketBook? FromJson(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize<Data>(json, Json) is not { } data)
                return null;
            var book = new MarketBook(data.Map) { Updated = data.Saved };
            foreach (var shop in data.Shops)
            {
                if (shop.Seen == default)
                    shop.Seen = data.Saved;
                book._shops[shop.Id] = shop;
            }
            foreach (var a in data.All.Where(a => a.Length == 3))
                book._all[(int)a[0]] = (a[1], a[2]);
            return book;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The file names of a map's market (one set per map, kept up to date).</summary>
    public static string Stem(string folder, string map) =>
        Path.Combine(folder, "pazar-" + string.Concat(map.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));

    /// <summary>Writes the data, the table and the report; returns the report's path.</summary>
    public string Save(string folder)
    {
        Directory.CreateDirectory(folder);
        string stem = Stem(folder, Map);
        string json = ToJson();
        Changed = false;
        File.WriteAllText(stem + ".json", json, new UTF8Encoding(false));
        // With the BOM Excel reads the Turkish letters right.
        File.WriteAllText(stem + ".csv", Csv(), new UTF8Encoding(true));
        File.WriteAllText(stem + ".html", MarketReport.Html(json), new UTF8Encoding(false));
        return stem + ".html";
    }

    /// <summary>The map's market as saved, or a new empty one.</summary>
    public static MarketBook Load(string folder, string map)
    {
        try
        {
            string file = Stem(folder, map) + ".json";
            if (File.Exists(file) && FromJson(File.ReadAllText(file)) is { } book && book.Map == map)
                return book;
            // Before the one list per map, every scan had its own file (pazar-[time].json): the newest one of this map starts the list.
            if (Directory.Exists(folder))
                foreach (var old in new DirectoryInfo(folder).GetFiles("pazar-2*.json").OrderByDescending(f => f.LastWriteTime))
                    if (FromJson(File.ReadAllText(old.FullName)) is { } earlier && earlier.Map == map)
                    {
                        earlier.Changed = true;
                        return earlier;
                    }
        }
        catch (IOException) { }
        return new MarketBook(map);
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
        /// <summary>When its items were read.</summary>
        public DateTime Seen { get; set; }
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
        public DateTime Saved { get; set; }
        public List<Shop> Shops { get; set; } = [];
        public List<double[]> All { get; set; } = [];
        public List<ItemStats> Stats { get; set; } = [];
        public List<MarketWant> Wanted { get; set; } = [];
    }
}
