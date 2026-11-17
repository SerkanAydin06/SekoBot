using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// What bonuses ("efsun") the game puts on which items, learned from the items seen in the shops:
/// for each bonus, the item groups it comes on (necklaces, shoes, shields, swords…) and the values
/// it takes there (EXP bonus: necklaces, shoes and shields only, 6 / 8 / 10 / 12 / 15 — never +1).
/// The game itself doesn't say (that table is on its server; the client only has the names), so
/// every scan adds to it; each listing counts once (seller, item, bonuses). Kept in
/// efsun-bilgisi.json, shared by every SekoBot.
/// </summary>
internal sealed partial class BonusBook
{
    public sealed class Data
    {
        /// <summary>Bonus name → item group → value → how many listings had it.</summary>
        public Dictionary<string, Dictionary<string, Dictionary<int, int>>> Bonuses { get; set; } = [];
        /// <summary>The listings already counted (seller|item|bonuses).</summary>
        public HashSet<string> Seen { get; set; } = [];
    }

    /// <summary>A value seen this few times may be a misread: not used to rank.</summary>
    public const int Enough = 2;

    /// <summary>The item groups by the game's sub type, in Turkish (what the user calls them).</summary>
    public static readonly IReadOnlyDictionary<string, string> GroupNames = new Dictionary<string, string>
    {
        ["ARMOR_BODY"] = "Zırh", ["ARMOR_HEAD"] = "Kask", ["ARMOR_SHIELD"] = "Kalkan", ["ARMOR_WRIST"] = "Bilezik",
        ["ARMOR_FOOTS"] = "Ayakkabı", ["ARMOR_NECK"] = "Kolye", ["ARMOR_EAR"] = "Küpe",
        ["WEAPON_SWORD"] = "Kılıç", ["WEAPON_TWO_HANDED"] = "Çift el", ["WEAPON_DAGGER"] = "Hançer", ["WEAPON_BOW"] = "Yay",
        ["WEAPON_BELL"] = "Çan", ["WEAPON_FAN"] = "Yelpaze", ["COSTUME_BODY"] = "Kostüm", ["COSTUME_HAIR"] = "Saç",
    };

    public static BonusBook Shared => _shared.Value;
    private static readonly Lazy<BonusBook> _shared = new(() => Load(FilePath, MarketFolder));

    private Data _data;
    private readonly object _gate = new();
    private readonly List<(string Key, string Group, List<(string Name, int Value)> Bonuses)> _news = [];

    public BonusBook(Data? data = null) => _data = data ?? new Data();

    public static string FilePath => Path.Combine(ProfileStore.Directory, "efsun-bilgisi.json");
    public static string MarketFolder => Path.Combine(ProfileStore.Directory, "pazar");

    /// <summary>The item group of an item (its sub type), or null when the item isn't known / has no group.</summary>
    public static string? GroupOf(int vnum) =>
        ItemBook.Get(vnum) is { } info && GroupNames.ContainsKey(info.SubType) ? info.SubType : null;

    public static string GroupName(string group) => GroupNames.GetValueOrDefault(group, group);

    [GeneratedRegex(@"^(.+?) \+?(-?\d+)%?$")]
    private static partial Regex OneBonus();

    /// <summary>"EXP bonusu +15, Max. HP +1000" → (name, value) pairs (also the old "+-12").</summary>
    public static List<(string Name, int Value)> Parse(string bonuses)
    {
        var list = new List<(string, int)>();
        foreach (var part in bonuses.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            if (OneBonus().Match(part.Trim()) is { Success: true } m && int.TryParse(m.Groups[2].Value, out int v))
                list.Add((m.Groups[1].Value, v));
        return list;
    }

    /// <summary>A listing seen in a shop: its bonuses go into the book (once per listing).</summary>
    public void Learn(string seller, int vnum, string bonuses)
    {
        if (bonuses.Length == 0 || GroupOf(vnum) is not string group)
            return;
        string key = $"{seller}|{vnum}|{bonuses}";
        var parsed = Parse(bonuses);
        if (parsed.Count == 0)
            return;
        lock (_gate)
        {
            if (!Add(_data, key, group, parsed))
                return;
            _news.Add((key, group, parsed));
        }
    }

    private static bool Add(Data data, string key, string group, List<(string Name, int Value)> bonuses)
    {
        if (!data.Seen.Add(key))
            return false;
        foreach (var (name, value) in bonuses)
        {
            if (!data.Bonuses.TryGetValue(name, out var groups))
                data.Bonuses[name] = groups = [];
            if (!groups.TryGetValue(group, out var values))
                groups[group] = values = [];
            values[value] = values.GetValueOrDefault(value) + 1;
        }
        return true;
    }

    /// <summary>The values a bonus was seen with on a group (only those seen often enough), smallest first.</summary>
    public IReadOnlyList<int> Values(string bonus, string group)
    {
        lock (_gate)
            return _data.Bonuses.TryGetValue(bonus, out var groups) && groups.TryGetValue(group, out var values)
                ? [.. values.Where(v => v.Value >= Enough).Select(v => v.Key).Order()]
                : [];
    }

    /// <summary>Where a value stands among those seen on the group: 1 = the lowest … Of = the highest; null when not known.</summary>
    public (int Rank, int Of)? Tier(string bonus, string group, int value)
    {
        var values = Values(bonus, group);
        int i = values is { Count: >= 2 } ? IndexOf(values, value) : -1;
        return i < 0 ? null : (i + 1, values.Count);
    }

    private static int IndexOf(IReadOnlyList<int> values, int value)
    {
        for (int i = 0; i < values.Count; i++)
            if (values[i] == value)
                return i;
        return -1;
    }

    /// <summary>How good a value is for the group: 0 = the lowest seen … 1 = the highest; null when not known.
    /// A weapon's own damage rows (any number from minus to plus) by where the value lies between the extremes.</summary>
    public double? Quality(string bonus, string group, int value)
    {
        var values = Values(bonus, group);
        if (values.Count < 2)
            return null;
        if (values.Count > 8)
            return Math.Clamp((value - values[0]) / (double)(values[^1] - values[0]), 0, 1);
        int i = IndexOf(values, value);
        return i < 0 ? null : i / (double)(values.Count - 1);
    }

    /// <summary>An item's bonuses all together: the sum of how good each is (5 bonuses all at the top = 5).</summary>
    public double Score(int vnum, string bonuses)
    {
        if (bonuses.Length == 0 || GroupOf(vnum) is not string group)
            return 0;
        return Parse(bonuses).Sum(b => Quality(b.Name, group, b.Value) ?? 0);
    }

    /// <summary>"EXP bonusu +15 ★ (en yüksek), Max. HP +1000 (4 değerden 2.)" for a tooltip.</summary>
    public string Describe(int vnum, string bonuses)
    {
        if (bonuses.Length == 0)
            return "";
        if (GroupOf(vnum) is not string group)
            return bonuses;
        return string.Join("\n", Parse(bonuses).Select(b =>
        {
            string text = $"{b.Name} {(b.Value < 0 ? "-" : "+")}{Math.Abs(b.Value)}";
            var values = Values(b.Name, group);
            if (values.Count < 2)
                return text;
            if (values.Count > 8)
                return text + $"  (görülen {values[0]} … {values[^1]})";
            int i = IndexOf(values, b.Value);
            return i < 0 ? text + "  (bu değer başka hiç görülmedi)"
                 : i == values.Count - 1 ? text + "  ★ en yüksek"
                 : text + $"  ({values.Count} değerden {i + 1}.: {string.Join(" / ", values)})";
        }));
    }

    /// <summary>The value of one bonus on a listing (null when it hasn't it).</summary>
    public static int? ValueOf(string bonuses, string bonus) =>
        Parse(bonuses).Where(b => b.Name.Equals(bonus, StringComparison.CurrentCultureIgnoreCase)).Select(b => (int?)b.Value).FirstOrDefault();

    /// <summary>The bonus names known, most often seen first (only those seen on <paramref name="group"/> when given).</summary>
    public List<string> Names(string? group = null)
    {
        lock (_gate)
            return [.. _data.Bonuses
                .Where(b => group == null || b.Value.ContainsKey(group))
                .OrderByDescending(b => group == null ? b.Value.Values.Sum(v => v.Values.Sum()) : b.Value[group].Values.Sum())
                .Select(b => b.Key)];
    }

    /// <summary>The item groups bonuses were seen on (armour first, then weapons).</summary>
    public List<string> Groups()
    {
        lock (_gate)
            return [.. _data.Bonuses.Values.SelectMany(g => g.Keys).Distinct()
                .OrderBy(g => g.StartsWith("ARMOR") ? 0 : g.StartsWith("WEAPON") ? 1 : 2).ThenBy(GroupName)];
    }

    /// <summary>A bag item: its bonuses are learned too (once per character, item and bonuses).</summary>
    public void LearnBag(string character, InventoryItem item)
    {
        if (item.Bonuses is { Length: > 0 } bonuses && bonuses.Any(b => b.Type > 0))
            Learn("çanta:" + character, item.Vnum, ItemBonuses.Describe(bonuses));
    }

    /// <summary>
    /// Whether an item can carry a bonus of at least <paramref name="min"/>, by what was seen: false when
    /// the bonus is known but never came on the item's group, or never that high there; true when it did,
    /// and when nothing is known yet (the item or the bonus not seen: it may well be).
    /// </summary>
    public bool CanCarry(int vnum, string bonus, int min)
    {
        if (GroupOf(vnum) is not string group)
            return true;
        lock (_gate)
        {
            if (!_data.Bonuses.TryGetValue(bonus, out var groups) || groups.Values.Sum(v => v.Values.Sum()) < 10)
                return true; // too little known about it
            if (!groups.TryGetValue(group, out var values))
                return false;
            return values.Keys.Max() >= min;
        }
    }

    /// <summary>"Kolye, Ayakkabı, Kalkan · 6 / 8 / 10 / 12 / 15" — where a bonus comes and with which values.</summary>
    public string WhereText(string bonus)
    {
        lock (_gate)
        {
            if (!_data.Bonuses.TryGetValue(bonus, out var groups) || groups.Count == 0)
                return "henüz görülmedi";
            var values = groups.Values.SelectMany(v => v.Where(x => x.Value >= Enough).Select(x => x.Key)).Distinct().Order().ToList();
            string where = string.Join(", ", groups.OrderByDescending(g => g.Value.Values.Sum()).Select(g => GroupName(g.Key)));
            return values.Count is > 0 and <= 8 ? $"{where} · {string.Join(" / ", values)}" :
                   values.Count > 8 ? $"{where} · {values[0]} … {values[^1]}" : where;
        }
    }

    /// <summary>How many listings were counted.</summary>
    public int Listings
    {
        get { lock (_gate) return _data.Seen.Count; }
    }

    /// <summary>A copy of everything known (for the report page).</summary>
    public Dictionary<string, Dictionary<string, Dictionary<int, int>>> Snapshot()
    {
        lock (_gate)
            return _data.Bonuses.ToDictionary(b => b.Key, b => b.Value.ToDictionary(g => g.Key, g => new Dictionary<int, int>(g.Value)));
    }

    /// <summary>Every listing of a saved market list goes into the book.</summary>
    public void LearnFrom(string marketJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(marketJson);
            if (!doc.RootElement.TryGetProperty("shops", out var shops))
                return;
            foreach (var shop in shops.EnumerateArray())
            {
                string owner = shop.TryGetProperty("owner", out var o) ? o.GetString() ?? "" : "";
                if (!shop.TryGetProperty("items", out var items))
                    continue;
                foreach (var item in items.EnumerateArray())
                    if (item.TryGetProperty("bonuses", out var b) && b.GetString() is { Length: > 0 } bonuses &&
                        item.TryGetProperty("vnum", out var v))
                        Learn(owner, v.GetInt32(), bonuses);
            }
        }
        catch (JsonException) { }
    }

    public static BonusBook Load(string path, string? marketFolder = null)
    {
        // The item groups come from the item book (opened before the game is even connected).
        if (marketFolder != null && ItemBook.FilePath == null)
            ItemBook.Load(Path.Combine(ProfileStore.Directory, "esya-bilgisi.json"));
        BonusBook book;
        try
        {
            book = File.Exists(path) && JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) is { } data
                ? new BonusBook(data)
                : new BonusBook();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            book = new BonusBook();
        }
        // The first time: everything scanned so far.
        if (book.Listings == 0 && marketFolder != null && Directory.Exists(marketFolder))
        {
            foreach (var file in Directory.GetFiles(marketFolder, "pazar-*.json"))
                try { book.LearnFrom(File.ReadAllText(file)); }
                catch (IOException) { }
            book.Save(path);
        }
        return book;
    }

    /// <summary>Shares: this SekoBot's new listings go into the file, the others' come back.</summary>
    public void Save(string? path = null)
    {
        List<(string Key, string Group, List<(string Name, int Value)> Bonuses)> news;
        lock (_gate)
        {
            news = [.. _news];
            _news.Clear();
        }
        var all = SharedFile.Update<Data>(path ?? FilePath, d => news.ForEach(n => Add(d, n.Key, n.Group, n.Bonuses)));
        lock (_gate)
        {
            if (all == null)
            {
                _news.InsertRange(0, news);
                return;
            }
            foreach (var n in _news)
                Add(all, n.Key, n.Group, n.Bonuses);
            _data = all;
        }
    }

    /// <summary>A page with the whole book: each bonus, the groups it comes on and the values there.</summary>
    public string Html()
    {
        var all = Snapshot();
        var groups = all.Values.SelectMany(g => g.Keys).Distinct()
            .OrderBy(g => g.StartsWith("ARMOR") ? 0 : g.StartsWith("WEAPON") ? 1 : 2).ThenBy(GroupName).ToList();
        var html = new StringBuilder();
        html.Append("""
            <!doctype html><html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Efsun rehberi</title><style>
            body{font:14px Segoe UI,sans-serif;background:#14161c;color:#e6e6e6;margin:0;padding:16px}
            h1{font-size:20px;margin:0 0 4px}p{color:#9aa0aa;margin:0 0 12px}
            input{background:#1e2129;color:#e6e6e6;border:1px solid #333a46;border-radius:6px;padding:6px 10px;width:260px;margin-bottom:10px}
            .wrap{overflow:auto}table{border-collapse:collapse;min-width:100%}
            th,td{border:1px solid #2a2f3a;padding:4px 6px;text-align:center;white-space:nowrap}
            th{background:#1e2129;position:sticky;top:0}td.n{text-align:left;font-weight:600}
            td.x{color:#3a3f4a}.v{display:inline-block;margin:1px;padding:1px 5px;border-radius:4px;background:#263040}
            .top{background:#2f5d3a}.few{opacity:.45}
            </style></head><body><h1>Efsun rehberi</h1>
            """);
        html.Append($"<p>Pazarda görülen {Listings:N0} efsunlu ilandan öğrenildi. Her hücre: o efsunun o eşya grubunda görülen değerleri " +
                    "(yeşil = en yüksek; soluk = yalnız bir kez görüldü, okuma hatası olabilir). Boş hücre: o grupta hiç görülmedi.</p>");
        html.Append("<input id=q placeholder=\"Efsun ara…\" oninput=\"for(const r of document.querySelectorAll('tbody tr'))r.style.display=r.cells[0].textContent.toLowerCase().includes(this.value.toLowerCase())?'':'none'\">");
        html.Append("<div class=wrap><table><thead><tr><th>Efsun</th><th>İlan</th>");
        foreach (var g in groups)
            html.Append("<th>").Append(Esc(GroupName(g))).Append("</th>");
        html.Append("</tr></thead><tbody>");
        foreach (var (name, byGroup) in all.OrderByDescending(b => b.Value.Values.Sum(v => v.Values.Sum())))
        {
            html.Append("<tr><td class=n>").Append(Esc(name)).Append("</td><td>").Append(byGroup.Values.Sum(v => v.Values.Sum())).Append("</td>");
            foreach (var g in groups)
            {
                if (!byGroup.TryGetValue(g, out var values))
                {
                    html.Append("<td class=x>·</td>");
                    continue;
                }
                int max = values.Where(v => v.Value >= Enough).Select(v => v.Key).DefaultIfEmpty(int.MinValue).Max();
                html.Append("<td>");
                foreach (var (value, count) in values.OrderBy(v => v.Key))
                    html.Append("<span class=\"v").Append(value == max ? " top" : "").Append(count < Enough ? " few" : "")
                        .Append("\" title=\"").Append(count).Append(" ilan\">").Append(value).Append("</span>");
                html.Append("</td>");
            }
            html.Append("</tr>");
        }
        html.Append("</tbody></table></div></body></html>");
        return html.ToString();
    }

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
