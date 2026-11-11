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
        profile.NavigateMode && (world.Progress?.Inventory ?? []).Any(i => ShouldThrow(i, profile, BotPicked));

    /// <summary>The bag items the bot picked up itself (kept across restarts, per character).</summary>
    private readonly PickedItems _pickedItems = new();
    private TimeSpan _pickedSavedAt;

    private IReadOnlySet<(int Slot, int Vnum)> BotPicked
    {
        get { lock (_pickedItems) return new HashSet<(int, int)>(_pickedItems.Picked); }
    }

    /// <summary>On the perception thread: notes what came into the bag, saves it now and then.</summary>
    private void TrackPicked(WorldSnapshot world, TimeSpan now)
    {
        if (world.Progress is not { Inventory.Count: > 0 } progress)
            return;
        lock (_pickedItems)
        {
            _pickedItems.UseCharacter(_world?.CharacterName ?? "");
            var profile = Profile;
            // Wanted when it came in (yang, potions, dear equipment or good bonuses, "Topla", an event's reward): never junk.
            _pickedItems.Update(progress.Inventory, BotRunning, item => KeepPicked(item, profile));
            foreach (var item in _pickedItems.CameIn)
                if (Loot.KindOf(item.Vnum) == LootKind.Equipment && GoodBonus(item, profile))
                {
                    string text = $"⭐ Efsunlu eşya alındı: {ItemBook.NameOf(item.Vnum)} — {ItemBonuses.Describe(item.Bonuses ?? [])}";
                    _log(text);
                    Notify(text);
                }
            if (_pickedItems.Changed && now - _pickedSavedAt > TimeSpan.FromSeconds(5))
            {
                _pickedSavedAt = now;
                _pickedItems.Save();
            }
        }
    }

    /// <summary>
    /// Whether to throw this bag item away: ticked "At"; or something the bot picked up that the
    /// loot choices leave out (a potion with "İksir" off, other things with "Diğer" off); or — when weapons and armour are only
    /// picked up from a price on (Silah / zırh off, "≥" set) — a weapon or armour worth less than
    /// that the bot picked up itself (<paramref name="botPicked"/>; the user's own things stay, also
    /// after a restart; not known: nothing is thrown this way). Ticked "Topla" or on the
    /// "always pick up" names: kept. Price unknown (or 0, often a special item): kept. Worn: kept.
    /// </summary>
    internal static bool ShouldThrow(InventoryItem item, BotProfile profile, IReadOnlySet<(int Slot, int Vnum)>? botPicked)
    {
        // An event's reward (any event seen, also over or coming, also ticked "At"): never thrown.
        if (item.Slot < CharacterProgress.FirstBagSlot || AlwaysLoot(item.Vnum, profile) || Array.IndexOf(Loot.AllEventItems, item.Vnum) >= 0)
            return false;
        if (profile.LootThrow.Contains(item.Vnum))
            return true;
        if (botPicked == null || !botPicked.Contains((item.Slot, item.Vnum)))
            return false;
        // The pickup key takes the closest item, so something unwanted can come along with a wanted
        // one: what the bot picked that the loot choices don't want goes again (not yang; a
        // weapon / armour of unknown price stays, it may be special).
        var kind = Loot.KindOf(item.Vnum);
        if (kind == LootKind.Equipment)
            return !profile.LootEquipment && !GoodBonus(item, profile) &&
                   (profile.LootEquipmentMinGold > 0 || profile.LootBonuses.Count > 0) &&
                   ItemBook.Get(item.Vnum) is { Gold: > 0 } info &&
                   (profile.LootEquipmentMinGold == 0 || info.Sell < profile.LootEquipmentMinGold);
        return kind switch
        {
            LootKind.Potion => !profile.LootPotions,
            LootKind.Other => !profile.LootOther && ItemBook.Get(item.Vnum) != null,
            _ => false,
        };
    }

    /// <summary>One line for the window: free slots, room for a tall item, how many items would be thrown.</summary>
    public string BagSummary()
    {
        var progress = World?.Progress;
        if (progress?.FreeSlots() is not int free)
            return "Çanta okunamadı (oyun bağlı değil ya da yükleniyor).";
        int toThrow = progress.Inventory.Count(i => ShouldThrow(i, Profile, BotPicked));
        return $"Çanta: {free} boş yer  ·  zırh / uzun silah {(progress.TallSpace() == false ? "SIĞMIYOR" : "sığar")}  ·  atılacak {toThrow} eşya" +
               (_cleaner.Active || _brain.WantsClean ? "  ·  şimdi atılıyor" : "");
    }

    /// <summary>"🗑 Şimdi at": throw the items ticked "At" now, without waiting for the bag to fill.</summary>
    public string CleanNow()
    {
        var world = World;
        if (!BotRunning)
            return "Önce botu başlat; atma bot çalışırken yapılır.";
        if (world == null || !HasThrowables(world, Profile))
            return "Çantada atılacak eşya yok ('At' işaretli ya da botun yanlışlıkla aldığı istenmeyen eşya).";
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
        int count = (world.Progress?.Inventory ?? []).Count(i => ShouldThrow(i, profile, BotPicked));
        _log($"Envanterde yer azaldı (zırh ya da uzun silah sığmıyor olabilir); çantada atılacak {count} eşya var ('At' işaretli ya da botun yanlışlıkla aldığı istenmeyen eşya). Sakin bir yerde atılacak.");
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
        var picked = BotPicked;
        // The bag as read (with the bonuses), for each slot shown in the window.
        var full = (World?.Progress?.Inventory ?? []).Where(i => i.Slot >= CharacterProgress.FirstBagSlot)
            .GroupBy(i => (i.Slot, i.Vnum)).ToDictionary(g => g.Key, g => g.First());
        IReadOnlyList<BotAction> actions = state.Dead || state.Attackers.Count > 0 || state.Targets.Any(t => t.AttacksMe)
            ? _cleaner.Abort(_bagLayout, p, "saldırı geldi")
            : _cleaner.Tick(state.Time, _bagLayout, i => ShouldThrow(full.GetValueOrDefault((i.Slot, i.Vnum), new InventoryItem(i.Slot, i.Vnum, i.Count)), p, picked), p, new Size(state.Width, state.Height));
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
