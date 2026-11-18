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
        profile.ThrowWhenInventoryFull && (HasThrowables(world, profile) || CanSellNear(world, profile));

    /// <summary>For the brain's breaks: something to throw away now (the bag read last).</summary>
    /// <summary>What came into the bag and was thrown away during this run (window and tur-esyalari.txt).</summary>
    private readonly SessionItems _sessionItems = new();
    public SessionItems SessionItems => _sessionItems;

    private bool JunkInBag() => Profile.ThrowWhenInventoryFull && _world != null && World is { } world && HasThrowables(world, Profile);

    /// <summary>"Şimdi at" was pressed with something to sell: sold after throwing, also with room in the bag.</summary>
    private bool _sellAsked;

    /// <summary>Full, nearly full, or no room for a tall item (as when emptying starts).</summary>
    private static bool BagTight(WorldSnapshot world) =>
        world.Progress?.FreeSlots() is int free && (free <= CleanAtFreeSlots || world.Progress.TallSpace() == false);

    private bool HasThrowables(WorldSnapshot world, BotProfile profile) =>
        profile.NavigateMode && (world.Progress?.Inventory ?? []).Any(i => ShouldThrow(i, profile, BotPicked));

    /// <summary>The bag items the bot picked up itself (kept across restarts, per character).</summary>
    private readonly PickedItems _pickedItems = new();
    private TimeSpan _pickedSavedAt;
    private TimeSpan? _bagBonusesAt;

    private IReadOnlySet<(int Slot, int Vnum)> BotPicked
    {
        get { lock (_pickedItems) return new HashSet<(int, int)>(_pickedItems.Picked); }
    }

    /// <summary>Everything in the bag the bot picked up itself, wanted or not (for selling).</summary>
    private readonly PickedItems _lootedItems = new("bot-topladi-");

    private IReadOnlySet<(int Slot, int Vnum)> BotLooted
    {
        get { lock (_pickedItems) return new HashSet<(int, int)>(_lootedItems.Picked); }
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
            _lootedItems.UseCharacter(_world?.CharacterName ?? "");
            _lootedItems.Update(progress.Inventory, BotRunning, (Func<InventoryItem, bool>?)null);
            foreach (var (vnum, count) in _pickedItems.Gained)
                _sessionItems.Picked(vnum, count);
            _sessionItems.Save();
            // What the character carries teaches the bonus book too (not only the shops).
            string me = _world?.CharacterName ?? "";
            if (me.Length > 0 && (_bagBonusesAt is not { } last || now - last > TimeSpan.FromSeconds(10)))
            {
                _bagBonusesAt = now;
                foreach (var item in progress.Inventory)
                    BonusBook.Shared.LearnBag(me, item);
            }
            foreach (var item in _pickedItems.CameIn)
                if (Loot.KindOf(item.Vnum) == LootKind.Equipment && GoodBonus(item, profile))
                {
                    // The bonuses asked for are marked ✔ (the others are told too: they make its worth).
                    var wanted = profile.LootBonuses;
                    string bonuses = string.Join(", ", (item.Bonuses ?? []).Where(b => b.Type > 0).Select(b =>
                        ItemBonuses.Describe([b]) + (wanted.Any(w => ItemBonuses.Same(w.Type, b.Type) && b.Value >= w.Min && w.Applies(item.Vnum)) ? " ✔" : "")));
                    string text = $"⭐ Efsunlu eşya alındı: {ItemBook.NameOf(item.Vnum)} — {bonuses}";
                    _log(text);
                    if (profile.NotifyBonusItems)
                        Notify(text);
                }
            if (_pickedItems.Changed && now - _pickedSavedAt > TimeSpan.FromSeconds(5))
            {
                _pickedSavedAt = now;
                _pickedItems.Save();
            }
            if (_lootedItems.Changed && now - _pickedSavedAt > TimeSpan.FromSeconds(5))
            {
                _pickedSavedAt = now;
                _lootedItems.Save();
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
        bool sell = world != null && CanSellNear(world, Profile);
        if (world == null || !sell && !HasThrowables(world, Profile))
            return "Çantada atılacak ya da satılacak eşya yok (atılacak: 'At' işaretli ya da botun istemeden aldığı; satılacak: sadece fiyatı için alınan efsunsuz silah / zırh)." +
                   (world != null && Profile.SellToNpc && NearShop(world) == null ? " (Yakında bilinen market NPC'si de yok.)" : "");
        lock (_botGate)
        {
            if (_cleaner.Active || _brain.WantsClean)
                return "Eşyalar zaten atılıyor.";
            _cleanTries = 0;
            _brain.WantsClean = true;
            _sellAsked = sell;
        }
        return HasThrowables(world, Profile)
            ? "Atılacak eşyalar atılacak" + (sell ? ", sonra fiyatı için alınanlar market NPC'sine satılacak" : "") + ": önce moblardan uzak, sakin bir yere geçiliyor."
            : "Fiyatı için alınan eşyalar market NPC'sine götürülüyor: önce moblardan uzak, sakin bir yere geçiliyor.";
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

    private string _turnWaitTold = "";

    /// <summary>
    /// The turn to use the screen and mouse (see <see cref="FrontTurn"/>): taken, or another SekoBot has
    /// it — then this one waits (told once) where it is.
    /// </summary>
    private bool TakeTurn()
    {
        if (FrontTurn.Shared.TryTake(_world?.CharacterName is { Length: > 0 } me ? me : "başka bir SekoBot"))
        {
            _turnWaitTold = "";
            return true;
        }
        string who = FrontTurn.Shared.Holder();
        if (_turnWaitTold != who)
        {
            _turnWaitTold = who;
            _log($"⏳ {(who.Length > 0 ? who : "Başka bir bot")} şu an envanter / satış işlemi yapıyor; sıra bekleniyor (işlemler karışmasın).");
        }
        return false;
    }

    /// <summary>The turn is let go (the bag emptied, the trip over, the message sent).</summary>
    private void LetGoTurn()
    {
        if (!_cleaner.Active && !_selling && !_whisper.Active)
            FrontTurn.Shared.LetGo();
    }

    /// <summary>While waiting for the turn: still fight what attacks (the brain), else stand.</summary>
    private static IReadOnlyList<BotAction>? WaitingForTurn(GameState state) =>
        state.Attackers.Count > 0 || state.Targets.Any(t => t.AttacksMe) ? null : [];

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
            // One bot at a time: another one emptying its bag or selling is waited for.
            if (!TakeTurn())
                return WaitingForTurn(state);
            // What is to be thrown goes first, here; then what was picked for its price is taken to a market NPC.
            if ((World is not { } w || !HasThrowables(w, p)) && TryStartSellTrip(p) is { } letGo)
                return letGo;
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
        foreach (var (vnum, count) in _cleaner.ThrownItems)
            _sessionItems.Thrown(vnum, count);
        _cleaner.ThrownItems.Clear();
        _sessionItems.Save();
        if (!_cleaner.Active)
            CleaningDone();
        return actions;
    }

    /// <summary>Where items were last thrown away, and until when that spot is kept away from.</summary>
    private Vec3? _dumpAt;
    private TimeSpan _dumpUntil;
    internal static readonly TimeSpan DumpAvoidFor = TimeSpan.FromMinutes(1);

    private Vec3? DumpSpot(TimeSpan now) => _dumpAt is Vec3 at && now < _dumpUntil ? at : null;

    private void CleaningDone()
    {
        if (_cleaner.Problem == "saldırı geldi" && ++_cleanTries < CleanTriesMax)
        {
            _log("Eşya atarken saldırı geldi; envanter kapatıldı, önce savaşılıyor.");
            _brain.CleanInterrupted();
            LetGoTurn();
            return;
        }
        _brain.WantsClean = false;
        if (_cleaner.Thrown > 0)
        {
            _cleanTries = 0;
            // The thrown items lie here: neither they nor the mobs around are gone for for a minute
            // (seen: the bot went straight back and picked junk from the pile).
            if (World?.Player is Vec3 here)
            {
                _dumpAt = here;
                _dumpUntil = _clock.Elapsed + DumpAvoidFor;
            }
            _log($"🗑 {_cleaner.Thrown} eşya atıldı" + (_cleaner.Problem is { } p ? $" ({p})" : "") +
                 "; avlanmaya devam. Atılan yığının 10 m çevresine 1 dakika yaklaşılmıyor.");
            // Then off to sell what was picked for its price: only when the bag is still (nearly) full or
            // "Şimdi at" was pressed — not after throwing junk in a break.
            if (World is { } w && !HasThrowables(w, Profile) && (_sellAsked || BagTight(w)) && CanSellNear(w, Profile))
                _brain.WantsClean = true;
        }
        else
        {
            _cleanTries++;
            _log("Eşya atılamadı: " + (_cleaner.Problem ?? "bilinmeyen neden") + ".");
        }
        if (!_brain.WantsClean)
            LetGoTurn();
    }
}
