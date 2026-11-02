using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Simple, robust farm loop. Pure logic: takes a <see cref="GameState"/>, returns actions.
///
/// Fighting → attack key held for FightMs (longer while a target is still alive, if the
///            target bar is calibrated)
/// Looting  → pickup key pressed LootPresses times
/// Turning  → every N cycles (or when no target is left) rotate the camera / walk forward
/// Any time → HP/MP potions with cooldown, stop on critical HP. A low reading must last
///            BarConfirmMs, so one bad frame (loading screen, menu, overlap) is ignored.
/// </summary>
internal sealed class Brain
{
    private static readonly TimeSpan Never = TimeSpan.FromDays(-1);

    private readonly Func<BotProfile> _profile;

    private TimeSpan _phaseSince, _lastLoot, _turnDuration;
    private TimeSpan _lastAttackDown = Never, _lastHpPotion = Never, _lastMpPotion = Never;
    // When each low-bar condition was first seen; a single bad frame must not trigger anything.
    private TimeSpan _hpCriticalSince = Never, _hpLowSince = Never, _mpLowSince = Never;
    private int _lootPresses;

    public Brain(Func<BotProfile> profile) => _profile = profile;

    public BotPhase Phase { get; private set; } = BotPhase.Stopped;
    public int Cycles { get; private set; }
    public int Potions { get; private set; }

    public void Start(TimeSpan now)
    {
        _lastAttackDown = Never;
        _hpCriticalSince = _hpLowSince = _mpLowSince = Never;
        Enter(BotPhase.Fighting, now);
    }

    public void Stop() => Phase = BotPhase.Stopped;

    public IReadOnlyList<BotAction> Tick(GameState state)
    {
        if (Phase == BotPhase.Stopped)
            return [];

        var p = _profile();
        var now = state.Time;
        var actions = new List<BotAction>();

        var confirm = Ms(Math.Max(0, p.BarConfirmMs));
        double? hp = state.Hp;
        if (Confirmed(hp < p.StopHpBelow, ref _hpCriticalSince, now, confirm))
        {
            Phase = BotPhase.Stopped;
            return [new KeyUpAction(p.Keys.Attack), new StopAction($"HP kritik (%{hp!.Value * 100:0})")];
        }
        if (Confirmed(hp < p.PotionHpBelow, ref _hpLowSince, now, confirm) &&
            now - _lastHpPotion >= Ms(p.PotionCooldownMs))
        {
            actions.Add(new KeyTapAction(p.Keys.HpPotion));
            _lastHpPotion = now;
            Potions++;
        }
        if (Confirmed(state.Mp < p.PotionMpBelow, ref _mpLowSince, now, confirm) &&
            now - _lastMpPotion >= Ms(p.PotionCooldownMs))
        {
            actions.Add(new KeyTapAction(p.Keys.MpPotion));
            _lastMpPotion = now;
            Potions++;
        }

        switch (Phase)
        {
            case BotPhase.Fighting:
                Fight(state, p, actions);
                break;
            case BotPhase.Looting:
                Loot(state, p, actions);
                break;
            case BotPhase.Turning:
                if (now - _phaseSince >= _turnDuration)
                    Enter(BotPhase.Fighting, now);
                break;
        }
        return actions;
    }

    private void Fight(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        // Re-send key-down every second: harmless for the game and restores the
        // attack if the key was released while the game lost focus.
        if (now - _lastAttackDown >= Ms(1000))
        {
            actions.Add(new KeyDownAction(p.Keys.Attack));
            _lastAttackDown = now;
        }

        var elapsed = now - _phaseSince;
        if (elapsed < Ms(p.FightMs))
            return;
        bool targetAlive = state.TargetHp != null && state.HasTarget;
        if (targetAlive && elapsed < Ms(p.FightMaxMs))
            return;

        actions.Add(new KeyUpAction(p.Keys.Attack));
        _lastAttackDown = Never;
        _lootPresses = 0;
        _lastLoot = Never;
        Enter(BotPhase.Looting, now);
    }

    private void Loot(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (_lootPresses < p.LootPresses)
        {
            if (now - _lastLoot >= Ms(350))
            {
                actions.Add(new KeyTapAction(p.Keys.Pickup));
                _lastLoot = now;
                _lootPresses++;
            }
            return;
        }
        if (now - _lastLoot < Ms(250))
            return;

        Cycles++;
        // With a calibrated target bar we know when the area around us is empty.
        bool nothingAround = state.TargetHp != null && !state.HasTarget;
        bool turn = nothingAround || (p.RotateEveryCycles > 0 && Cycles % p.RotateEveryCycles == 0);
        bool move = p.MoveEveryCycles > 0 && Cycles % p.MoveEveryCycles == 0;

        _turnDuration = TimeSpan.Zero;
        if (turn)
        {
            actions.Add(new KeyHoldAction(p.Keys.RotateCamera, p.RotateHoldMs));
            _turnDuration += Ms(p.RotateHoldMs + 150);
        }
        if (move)
        {
            actions.Add(new KeyHoldAction(p.Keys.MoveForward, p.MoveHoldMs));
            _turnDuration += Ms(p.MoveHoldMs + 150);
        }
        Enter(turn || move ? BotPhase.Turning : BotPhase.Fighting, now);
    }

    /// <summary>True once <paramref name="condition"/> has held without interruption for <paramref name="needed"/>.
    /// A missing reading (null → false) resets it.</summary>
    private static bool Confirmed(bool condition, ref TimeSpan since, TimeSpan now, TimeSpan needed)
    {
        if (!condition)
        {
            since = Never;
            return false;
        }
        if (since == Never)
            since = now;
        return now - since >= needed;
    }

    private static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private void Enter(BotPhase phase, TimeSpan now)
    {
        Phase = phase;
        _phaseSince = now;
    }
}
