using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Bag full: instead of stopping, throw away the items ticked "At" in the item list. The brain
/// first fights what attacks us and walks to a quiet spot (<see cref="Brain.WantsClean"/>); then,
/// while the brain waits, <see cref="BagCleaner"/> opens the inventory and throws the items. After
/// that the brain carries on and walks back to the saved area. Attacked meanwhile: the inventory is
/// closed, the attackers fought, and it starts again. When nothing can be thrown the bot stops as before.
/// </summary>
internal sealed partial class BotEngine
{
    private const int CleanTriesMax = 5;
    /// <summary>Throwing starts with this many free slots left (not only when the bag is full).</summary>
    internal const int CleanAtFreeSlots = 2;
    private readonly BagCleaner _cleaner = new();
    private BagLayout? _bagLayout;
    private int _cleanTries;

    /// <summary>Whether a full bag holds an item ticked "At" (so throwing can free a slot).</summary>
    private bool CanThrow(WorldSnapshot world, BotProfile profile) =>
        profile.ThrowWhenInventoryFull && HasThrowables(world, profile);

    private bool HasThrowables(WorldSnapshot world, BotProfile profile) =>
        profile.NavigateMode && (world.Progress?.Inventory ?? []).Any(i => ShouldThrow(i, profile, _bagAtStart));

    /// <summary>What was in the bag (slot, item) when the bot started: the user's own things.</summary>
    private HashSet<(int Slot, int Vnum)>? _bagAtStart;

    private void RememberBagAtStart(WorldSnapshot? world) =>
        _bagAtStart = world?.Progress is { Inventory.Count: > 0 } progress
            ? [.. progress.Inventory.Select(i => (i.Slot, i.Vnum))]
            : null;

    /// <summary>
    /// Whether to throw this bag item away: ticked "At"; or — when weapons and armour are only
    /// picked up from a price on (Silah / zırh off, "≥" set) — a weapon or armour worth less than
    /// that which came into the bag while the bot ran (what was there at the start is the user's and
    /// stays; not known what was there: nothing is thrown this way). Ticked "Topla" or on the
    /// "always pick up" names: kept. Price unknown (or 0, often a special item): kept. Worn: kept.
    /// </summary>
    internal static bool ShouldThrow(InventoryItem item, BotProfile profile, IReadOnlySet<(int Slot, int Vnum)>? atStart)
    {
        if (item.Slot < CharacterProgress.FirstBagSlot || AlwaysLoot(item.Vnum, profile))
            return false;
        if (profile.LootThrow.Contains(item.Vnum))
            return true;
        return !profile.LootEquipment && profile.LootEquipmentMinGold > 0 &&
               atStart != null && !atStart.Contains((item.Slot, item.Vnum)) &&
               Loot.KindOf(item.Vnum) == LootKind.Equipment &&
               ItemBook.Get(item.Vnum) is { Gold: > 0 } info && info.Gold < profile.LootEquipmentMinGold;
    }

    /// <summary>"🗑 Şimdi at": throw the items ticked "At" now, without waiting for the bag to fill.</summary>
    public string CleanNow()
    {
        var world = World;
        if (!BotRunning)
            return "Önce botu başlat; atma bot çalışırken yapılır.";
        if (world == null || !HasThrowables(world, Profile))
            return "Çantada atılacak eşya yok ('At' işaretli ya da botun aldığı ucuz silah/zırh).";
        lock (_botGate)
        {
            if (_cleaner.Active || _brain.WantsClean)
                return "Eşyalar zaten atılıyor.";
            _cleanTries = 0;
            _brain.WantsClean = true;
        }
        return "'At' işaretli eşyalar atılacak: önce moblardan uzak, sakin bir yere geçiliyor.";
    }

    /// <summary>The bag is full: throw items away when possible. False when the bot should stop instead.</summary>
    private bool StartCleaning(WorldSnapshot world, BotProfile profile)
    {
        lock (_botGate)
        {
            if (_cleaner.Active || _brain.WantsClean)
                return true;
            if (_cleanTries >= CleanTriesMax || !CanThrow(world, profile))
                return false;
            _brain.WantsClean = true;
        }
        int count = (world.Progress?.Inventory ?? []).Count(i => ShouldThrow(i, profile, _bagAtStart));
        _log($"Envanterde yer azaldı (zırh ya da uzun silah sığmıyor olabilir); çantada atılacak {count} eşya var ('At' işaretli ya da botun aldığı ucuz silah/zırh). Sakin bir yerde atılacak.");
        return true;
    }

    /// <summary>The inventory as drawn, read only while throwing (on the perception thread).</summary>
    private void ReadBagForCleaning() =>
        _bagLayout = _cleaner.Active || _brain.ReadyToClean ? _world?.ReadBagLayout() : null;

    /// <summary>
    /// Inside <see cref="_botGate"/>: the actions for this tick while items are thrown away, or null
    /// when the brain should tick as usual.
    /// </summary>
    private IReadOnlyList<BotAction>? CleanTick(GameState state, BotProfile p)
    {
        if (!_cleaner.Active)
        {
            if (!_brain.WantsClean || !_brain.ReadyToClean)
                return null;
            _cleaner.Start(state.Time);
            _log("Sakin bir yerdeyiz; envanter açılıp 'At' işaretli eşyalar atılıyor.");
        }
        IReadOnlyList<BotAction> actions = state.Dead || state.Attackers.Count > 0 || state.Targets.Any(t => t.AttacksMe)
            ? _cleaner.Abort(_bagLayout, p, "saldırı geldi")
            : _cleaner.Tick(state.Time, _bagLayout, i => ShouldThrow(new InventoryItem(i.Slot, i.Vnum, i.Count), p, _bagAtStart), p, new Size(state.Width, state.Height));
        if (!_cleaner.Active)
            CleaningDone();
        return actions;
    }

    private void CleaningDone()
    {
        if (_cleaner.Problem == "saldırı geldi" && ++_cleanTries < CleanTriesMax)
        {
            _log("Eşya atarken saldırı geldi; envanter kapatıldı, önce savaşılıyor.");
            _brain.CleanInterrupted();
            return;
        }
        _brain.WantsClean = false;
        if (_cleaner.Thrown > 0)
        {
            _cleanTries = 0;
            _log($"🗑 {_cleaner.Thrown} eşya atıldı" + (_cleaner.Problem is { } p ? $" ({p})" : "") + "; avlanmaya devam, kayıtlı alana dönülüyor.");
        }
        else
        {
            _cleanTries++;
            _log("Eşya atılamadı: " + (_cleaner.Problem ?? "bilinmeyen neden") + ".");
        }
    }
}
