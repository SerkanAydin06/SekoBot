using System.Text.Json;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Which bag items the bot picked up itself without wanting them (slot, item) — the pickup key takes
/// the closest item — so only those are thrown and the user's own things, and what was wanted when it
/// was picked (an event's reward, also after the event), stay. Watches the bag: an item that comes in
/// while the bot runs and isn't wanted then is junk;
/// one only moved to another slot keeps what it was; what leaves the bag is forgotten. Kept in a file
/// per character, so a restart (an update) still knows what the bot picked before.
/// </summary>
internal sealed class PickedItems
{
    /// <summary>A bag read with this many fewer items is believed only when seen a few times (a read can fail).</summary>
    private const int DropLimit = 3, SameReadsToBelieve = 3;

    private readonly HashSet<(int Slot, int Vnum)> _picked = [];
    private Dictionary<(int Slot, int Vnum), int>? _last;
    private int _shrunkReads;
    private string _character = "";

    public IReadOnlySet<(int Slot, int Vnum)> Picked => _picked;

    /// <summary>Set when what the bot picked changed (to be saved).</summary>
    public bool Changed { get; set; }

    /// <summary>
    /// A new bag read. <paramref name="botRunning"/>: items coming in now are the bot's. The first read
    /// (or after a different character) only drops remembered items no longer there.
    /// </summary>
    public void Update(IReadOnlyList<InventoryItem> inventory, bool botRunning, Func<int, bool>? wanted = null) =>
        Update(inventory, botRunning, wanted == null ? null : (Func<InventoryItem, bool>)(i => wanted(i.Vnum)));

    /// <summary>Items that came into the bag at the last read while the bot ran (not moved ones).</summary>
    public IReadOnlyList<InventoryItem> CameIn { get; private set; } = [];

    public void Update(IReadOnlyList<InventoryItem> inventory, bool botRunning, Func<InventoryItem, bool>? wanted)
    {
        CameIn = [];
        var bag = inventory.Where(i => i.Slot >= CharacterProgress.FirstBagSlot && i.Count > 0)
            .GroupBy(i => (i.Slot, i.Vnum)).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
        if (_last == null)
        {
            _last = bag;
            if (_picked.RemoveWhere(k => !bag.ContainsKey(k)) > 0)
                Changed = true;
            return;
        }
        // Many items gone at once: maybe a bad read; believed when it stays so.
        if (bag.Count < _last.Count - DropLimit && ++_shrunkReads < SameReadsToBelieve)
            return;
        _shrunkReads = 0;

        var bySlot = inventory.Where(i => i.Slot >= CharacterProgress.FirstBagSlot).GroupBy(i => i.Slot).ToDictionary(g => g.Key, g => g.First());
        var cameIn = new List<InventoryItem>();
        foreach (var group in bag.Keys.Concat(_last.Keys).Select(k => k.Vnum).Distinct().ToList())
        {
            var came = bag.Keys.Where(k => k.Vnum == group && !_last.ContainsKey(k)).OrderBy(k => k.Slot).ToList();
            var gone = _last.Keys.Where(k => k.Vnum == group && !bag.ContainsKey(k)).OrderBy(k => k.Slot).ToList();
            for (int i = 0; i < came.Count; i++)
            {
                // Moved from a slot that left: same owner as before. Otherwise new: the bot's while it runs.
                var item = bySlot.GetValueOrDefault(came[i].Slot);
                if (i >= gone.Count && botRunning)
                    cameIn.Add(item);
                bool bots = i < gone.Count ? _picked.Contains(gone[i]) : botRunning && wanted?.Invoke(item) != true;
                if (bots && _picked.Add(came[i]))
                    Changed = true;
            }
            foreach (var key in gone)
                if (_picked.Remove(key))
                    Changed = true;
        }
        _last = bag;
        CameIn = cameIn;
    }

    // ---- the file (per character) ----

    private static string FileOf(string character) =>
        Path.Combine(ProfileStore.Directory, "bot-istenmeyen-" +
                     string.Concat(character.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");

    /// <summary>The character is known (or changed): load what the bot picked for it before.</summary>
    public void UseCharacter(string character)
    {
        if (character.Length == 0 || character == _character)
            return;
        _character = character;
        _picked.Clear();
        _last = null;
        _shrunkReads = 0;
        Changed = false;
        try
        {
            if (File.Exists(FileOf(character)) &&
                JsonSerializer.Deserialize<int[][]>(File.ReadAllText(FileOf(character))) is { } saved)
                foreach (var pair in saved.Where(p => p.Length == 2))
                    _picked.Add((pair[0], pair[1]));
        }
        catch { }
    }

    public void Save()
    {
        if (_character.Length == 0 || !Changed)
            return;
        Changed = false;
        try
        {
            System.IO.Directory.CreateDirectory(ProfileStore.Directory);
            File.WriteAllText(FileOf(_character), JsonSerializer.Serialize(_picked.Select(k => new[] { k.Slot, k.Vnum }).ToArray()));
        }
        catch { }
    }
}
