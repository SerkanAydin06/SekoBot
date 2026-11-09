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
    private readonly BagCleaner _cleaner = new();
    private BagLayout? _bagLayout;
    private int _cleanTries;

    /// <summary>Whether a full bag holds an item ticked "At" (so throwing can free a slot).</summary>
    private static bool CanThrow(WorldSnapshot world, BotProfile profile) =>
        profile.ThrowWhenInventoryFull && HasThrowables(world, profile);

    private static bool HasThrowables(WorldSnapshot world, BotProfile profile) =>
        profile.NavigateMode && profile.LootThrow.Count > 0 &&
        (world.Progress?.Inventory ?? []).Any(i => i.Slot >= CharacterProgress.FirstBagSlot && profile.LootThrow.Contains(i.Vnum));

    /// <summary>"🗑 Şimdi at": throw the items ticked "At" now, without waiting for the bag to fill.</summary>
    public string CleanNow()
    {
        var world = World;
        if (!BotRunning)
            return "Önce botu başlat; atma bot çalışırken yapılır.";
        if (world == null || !HasThrowables(world, Profile))
            return "Çantada 'At' işaretli eşya yok (Hedef → 📋 Eşyalar'dan işaretle).";
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
        int count = (world.Progress?.Inventory ?? []).Count(i => i.Slot >= CharacterProgress.FirstBagSlot && profile.LootThrow.Contains(i.Vnum));
        _log($"Envanter doldu; çantada 'At' işaretli {count} eşya var. Sakin bir yerde atılacak.");
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
            : _cleaner.Tick(state.Time, _bagLayout, p.LootThrow.ToHashSet(), p, new Size(state.Width, state.Height));
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
