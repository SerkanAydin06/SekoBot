using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// The HP / MP potion keys kept filled (the user: "potlar bitince 1 ve 2'deki yerlerine sürükleyerek kendi koysun"): a
/// key that stays empty while the bag holds that potion (or another of its kind) gets it dragged on — at a calm moment
/// (nothing attacking), one bot at a time with the screen, see <see cref="PotionRefill"/>.
/// </summary>
internal sealed partial class BotEngine
{
    private readonly PotionRefill _refill = new();
    private IReadOnlyList<Rectangle?>? _quickRects;
    /// <summary>Since when each kind's key reads empty (the game empties it for a moment when it moves to the next stack).</summary>
    private readonly Dictionary<string, TimeSpan> _keyEmptySince = [];
    /// <summary>Not tried again before (after a failure).</summary>
    private readonly Dictionary<string, TimeSpan> _refillPause = [];

    /// <summary>A key read empty this long is really empty.</summary>
    internal static readonly TimeSpan KeyEmptyFor = TimeSpan.FromSeconds(4);
    /// <summary>After a failed try, the same key is left this long.</summary>
    internal static readonly TimeSpan RefillPauseAfterFail = TimeSpan.FromMinutes(10);

    /// <summary>The quick slot number of a key (1 = key "1", 10 = "0"); 0 when it isn't one.</summary>
    internal static int QuickNumber(Keys key) => key switch
    {
        >= Keys.D1 and <= Keys.D9 => key - Keys.D0,
        Keys.D0 => 10,
        >= Keys.NumPad1 and <= Keys.NumPad9 => key - Keys.NumPad0,
        _ => 0,
    };

    /// <summary>The potions of a kind by name, as the game calls them ("Kırmızı İksir(K)": HP, "Mavi İksir(B)": MP).</summary>
    internal static bool PotionOfKind(string? name, string kind) =>
        name != null && name.StartsWith(kind == "HP" ? "Kırmızı İksir" : "Mavi İksir", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A potion key to fill: its kind, number and the potion to put on it — the one it held before when the bag still has
    /// it, else the potion of its kind the bag has most of. Only a key that is really empty (not one holding a skill or
    /// another item the user put there).
    /// </summary>
    private (string Kind, int Key, int Vnum)? RefillWanted(WorldSnapshot world, BotProfile p, TimeSpan now)
    {
        if (world.Progress is not { } progress)
            return null;
        foreach (var (kind, key) in new[] { ("HP", p.Keys.HpPotion), ("MP", p.Keys.MpPotion) })
        {
            int number = QuickNumber(key);
            if (number == 0 || number > progress.QuickSlots.Count)
                continue;
            int pointsAt = progress.QuickSlots[number - 1];
            bool empty = pointsAt == -1 || pointsAt >= 0 && progress.QuickSlotVnum(number) == null;
            if (!empty)
            {
                _keyEmptySince.Remove(kind);
                continue;
            }
            if (!_keyEmptySince.TryGetValue(kind, out var since))
                _keyEmptySince[kind] = since = now;
            if (now - since < KeyEmptyFor || _refillPause.TryGetValue(kind, out var until) && now < until)
                continue;
            int? seen;
            lock (_potionSeen)
                seen = _potionSeen.TryGetValue(key, out int v) ? v : p.PotionVnums.TryGetValue(kind, out int kept) ? kept : null;
            int? want = seen is int s && progress.Count(s) > 0 ? s
                : progress.Inventory.Where(i => i.Slot >= CharacterProgress.FirstBagSlot && i.Count > 0 && PotionOfKind(ItemBook.Get(i.Vnum)?.Name, kind))
                    .GroupBy(i => i.Vnum).OrderByDescending(g => g.Sum(i => i.Count)).Select(g => (int?)g.Key).FirstOrDefault();
            if (want is int vnum)
                return (kind, number, vnum);
        }
        return null;
    }

    /// <summary>Inside <see cref="_botGate"/>: the refill going on, or started when a key is empty; null when nothing to do.</summary>
    private IReadOnlyList<BotAction>? RefillTick(GameState state, BotProfile p)
    {
        bool attacked = state.Dead || state.Attackers.Count > 0 || state.Targets.Any(t => t.AttacksMe);
        if (_refill.Active)
        {
            if (attacked)
                _refill.Abort("saldırı geldi");
            var actions = _refill.Tick(state.Time, _bagLayout, _quickRects, World?.Progress?.QuickSlotVnum(_refill.Key), p.Keys, ReadBagNow);
            if (!_refill.Active)
                RefillDone(state.Time);
            return actions;
        }
        // Also on the way to the farm area (the user: "alanın dışında olsa bile önce potlarını kontrol etsin").
        if (attacked || _cleaner.Active || _selling || _tidying || _brain.WantsClean || _whisper.Active
            || World is not { } world || !BagRead(world) || RefillWanted(world, p, state.Time) is not var (kind, key, vnum))
            return null;
        if (!FrontTurn.Shared.TryTake(_world?.CharacterName is { Length: > 0 } me ? me : "başka bir PARDUS"))
            return null; // another bot has the screen: fought on, tried again later
        if (GrowForWork())
            return [];
        _refill.Start(state.Time, kind, key, vnum);
        _log($"🧪 {kind} iksiri tuşu ({key}) boş; çantadaki {ItemBook.NameOf(vnum)} oraya sürükleniyor.");
        // Walking (to the farm area): stopped for it.
        var stop = new List<BotAction> { new KeyUpAction(p.Keys.Attack) };
        foreach (var move in new[] { p.Keys.MoveForward, p.Keys.MoveBack, p.Keys.MoveLeft, p.Keys.MoveRight })
            stop.Add(new KeyUpAction(move));
        return stop;
    }

    private void RefillDone(TimeSpan now)
    {
        if (_refill.Succeeded)
        {
            _refillPause.Remove(_refill.Kind);
            _log($"🧪 {ItemBook.NameOf(_refill.Vnum)} {_refill.Key} tuşuna konuldu; {_refill.Kind} iksiri yine içiliyor.");
        }
        else
        {
            _refillPause[_refill.Kind] = now + RefillPauseAfterFail;
            string text = $"🧪 {_refill.Kind} iksiri {_refill.Key} tuşuna konamadı ({_refill.Problem ?? "bilinmeyen neden"}); " +
                          $"{RefillPauseAfterFail.TotalMinutes:0} dakika sonra yeniden denenecek. Elle de sürüklenebilir.";
            _log(text);
            if (_refill.Problem != "saldırı geldi")
                Notify(text);
            else
                _refillPause[_refill.Kind] = now + TimeSpan.FromSeconds(30);
        }
        _keyEmptySince.Remove(_refill.Kind);
        StartTidy(); // nothing left open, then the turn goes
    }
}
