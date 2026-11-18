using System.Text;
using System.Text.Json;

namespace SekoBot.Bot;

/// <summary>One day's prices of an item in the market (per piece).</summary>
internal sealed record DayPrice(string Day, double Min, double Average, int Listings);

/// <summary>
/// How an item's price went over the days: for every item name the day's cheapest and average price
/// (per piece) and how many were for sale, one entry per day (the last reading of the day counts),
/// kept 90 days. One file per map (pazar/pazar-gecmis-[map].json), written with the market list.
/// </summary>
internal sealed class MarketHistory
{
    internal const int KeepDays = 90;
    private readonly Dictionary<string, List<DayPrice>> _items = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly object _gate = new();

    public int Items { get { lock (_gate) return _items.Count; } }

    /// <summary>Today's prices from the market list now (replaces what today had).</summary>
    public void Record(IEnumerable<ItemStats> stats, DateTime now)
    {
        string day = now.ToString("yyyy-MM-dd");
        string oldest = now.AddDays(-KeepDays).ToString("yyyy-MM-dd");
        lock (_gate)
            foreach (var s in stats)
            {
                if (!_items.TryGetValue(s.Name, out var days))
                    _items[s.Name] = days = [];
                days.RemoveAll(d => d.Day == day || string.CompareOrdinal(d.Day, oldest) < 0);
                days.Add(new DayPrice(day, s.Min, s.Average, s.Listings));
            }
    }

    /// <summary>The item's days, oldest first (empty: never seen).</summary>
    public List<DayPrice> Of(string name)
    {
        lock (_gate)
            return _items.TryGetValue(name, out var days) ? [.. days.OrderBy(d => d.Day, StringComparer.Ordinal)] : [];
    }

    /// <summary>The average now against the average about a week ago (the oldest day within 8 days):
    /// +0.12 = 12 % dearer. Null when there is no earlier day.</summary>
    public double? Trend(string name)
    {
        var days = Of(name);
        if (days.Count < 2)
            return null;
        var last = days[^1];
        string from = DateTime.Parse(last.Day).AddDays(-8).ToString("yyyy-MM-dd");
        var before = days.FirstOrDefault(d => string.CompareOrdinal(d.Day, from) >= 0 && d.Day != last.Day);
        return before == null || before.Average <= 0 ? null : last.Average / before.Average - 1;
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string File(string folder, string map) =>
        Path.Combine(folder, "pazar-gecmis-" + string.Concat(map.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");

    public void Save(string folder, string map)
    {
        string json;
        lock (_gate)
            json = JsonSerializer.Serialize(_items, Json);
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File(folder, map), json, new UTF8Encoding(false));
    }

    public static MarketHistory Load(string folder, string map)
    {
        var history = new MarketHistory();
        try
        {
            string file = File(folder, map);
            if (System.IO.File.Exists(file) &&
                JsonSerializer.Deserialize<Dictionary<string, List<DayPrice>>>(System.IO.File.ReadAllText(file), Json) is { } items)
                foreach (var (name, days) in items)
                    history._items[name] = days;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        return history;
    }
}
