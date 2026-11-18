using System.Text.Json;
using System.Text.Json.Nodes;

namespace SekoBot.Config;

/// <summary>
/// The pick-up / throw / sell choices saved under a name the user gives ("esya-ayarlari/NAME.json"),
/// shared by every SekoBot: made once on one character, loaded on the others (instead of ticking the
/// same items again on each). How a character fights or where it farms is not in it.
/// </summary>
internal static class LootPresets
{
    internal static readonly string[] Names =
    [
        nameof(BotProfile.PickUpDrops), nameof(BotProfile.LootYang), nameof(BotProfile.LootPotions),
        nameof(BotProfile.LootEquipment), nameof(BotProfile.LootOther), nameof(BotProfile.LootEquipmentMinGold),
        nameof(BotProfile.LootBonuses), nameof(BotProfile.LootAlways), nameof(BotProfile.LootPick),
        nameof(BotProfile.LootSkip), nameof(BotProfile.LootThrow), nameof(BotProfile.EventLoot),
        nameof(BotProfile.DropRangeMetres), nameof(BotProfile.ThrowWhenInventoryFull),
        nameof(BotProfile.StopWhenInventoryFull), nameof(BotProfile.SellToNpc),
    ];

    public static string Folder { get; set; } = Path.Combine(ProfileStore.Directory, "esya-ayarlari");

    private static string PathOf(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        string safe = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return Path.Combine(Folder, (safe.Length > 0 ? safe : "_") + ".json");
    }

    /// <summary>The saved names, in order.</summary>
    public static List<string> All()
    {
        try
        {
            return Directory.Exists(Folder)
                ? [.. Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                       .Order(StringComparer.CurrentCultureIgnoreCase)]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static bool Exists(string name) => File.Exists(PathOf(name));

    public static void Save(string name, BotProfile profile)
    {
        var all = JsonSerializer.SerializeToNode(profile, ProfileStore.JsonOptions)!.AsObject();
        var mine = new JsonObject();
        foreach (string field in Names)
            mine[field] = all[field]?.DeepClone();
        Directory.CreateDirectory(Folder);
        string path = PathOf(name), temp = path + ".tmp";
        File.WriteAllText(temp, mine.ToJsonString(ProfileStore.JsonOptions));
        File.Move(temp, path, true);
    }

    /// <returns>False when nothing is saved under that name (or it can't be read).</returns>
    public static bool Load(string name, BotProfile profile)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(PathOf(name)))?.AsObject() is not { } mine)
                return false;
            foreach (string field in Names)
            {
                if (!mine.TryGetPropertyValue(field, out var value) || value == null || typeof(BotProfile).GetProperty(field) is not { } property)
                    continue;
                try
                {
                    if (value.Deserialize(property.PropertyType, ProfileStore.JsonOptions) is { } read)
                        property.SetValue(profile, read);
                }
                catch (JsonException) { }
            }
            ProfileStore.Normalize(profile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Delete(string name)
    {
        try { File.Delete(PathOf(name)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
