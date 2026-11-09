using System.Collections.Concurrent;
using System.Text.Json;

namespace SekoBot.Memory;

/// <summary>What the game says about an item: its name, type ("ITEM_WEAPON", "ITEM_USE", …),
/// sub-type ("USE_POTION", "ARMOR_EAR", …) and what a shop pays for it.</summary>
internal sealed record ItemInfo(int Vnum, string Name, string Type, string SubType, int Gold);

/// <summary>
/// Items the bot has learned about. The game keeps no item list in memory that could be read, but
/// every item in the bag carries its name, type and price (seen: "Kırmızı İksir(O)", ITEM_USE /
/// USE_POTION, 96). So each item is learned the first time it is in the bag and remembered for
/// good: after that, the same item lying on the ground is known before it is picked up.
/// </summary>
internal static class ItemBook
{
    private static readonly ConcurrentDictionary<int, ItemInfo> Items = new();
    private static volatile bool _dirty;

    public static ItemInfo? Get(int vnum) => Items.TryGetValue(vnum, out var info) ? info : null;

    public static bool Knows(int vnum) => Items.ContainsKey(vnum);

    public static int Count => Items.Count;

    public static IEnumerable<ItemInfo> All() => Items.Values;

    public static void Learn(ItemInfo info)
    {
        if (info.Vnum <= 1 || info.Name.Length == 0)
            return;
        if (Items.TryGetValue(info.Vnum, out var known) && known == info)
            return;
        Items[info.Vnum] = info;
        _dirty = true;
    }

    /// <summary>The name for the log: "Gümüş Küpe+3", else "eşya #17043".</summary>
    public static string NameOf(int vnum) => vnum == 1 ? "yang" : Get(vnum)?.Name ?? $"eşya #{vnum}";

    // ── Saved between sessions ──

    public static string? FilePath { get; set; }

    public static void Load(string path)
    {
        FilePath = path;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<List<ItemInfo>>(File.ReadAllText(path)) is { } list)
                foreach (var info in list)
                    Items[info.Vnum] = info;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public static void SaveIfChanged()
    {
        if (!_dirty || FilePath == null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Items.Values.OrderBy(i => i.Vnum).ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
