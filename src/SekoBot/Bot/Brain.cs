using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Simple, robust farm loop. Pure logic: takes a <see cref="GameState"/>, returns actions.
///
/// Fighting → attack key held for FightMs (longer while the selected mob is still alive)
/// Looting  → pickup key pressed LootPresses times
/// Turning  → every N cycles (or when no target is left) rotate the camera / walk forward
/// Any time → HP/MP potions with cooldown, stop on critical HP. A low reading must last
///            BarConfirmMs, so one bad frame (loading screen, menu, overlap) is ignored.
/// </summary>
internal sealed partial class Brain
{
    private static readonly TimeSpan Never = TimeSpan.FromDays(-1);

    private readonly Func<BotProfile> _profile;

    private TimeSpan _phaseSince, _lastLoot, _turnDuration;
    private TimeSpan _lastAttackDown = Never, _lastHpPotion = Never, _lastMpPotion = Never, _lastEsc = Never;
    private TimeSpan _lastReviveClick = Never, _recoverSince = Never;
    // When each low-bar condition was first seen; a single bad frame must not trigger anything.
    private TimeSpan _hpLowSince = Never, _mpLowSince = Never;
    private TimeSpan _targetGoneSince = Never, _searchSentAt = Never, _lastProgress = Never;
    private double _targetHpBest = 1;
    /// <summary>A target HP given up on (stuck or unreachable mob); ignored until the target
    /// changes or its HP does.</summary>
    private double? _ignoredTargetHp;
    private int _reclicks;
    private int _lootPresses, _failedSearches;
    private bool _searchSent, _blindFight;

    public Brain(Func<BotProfile> profile) => _profile = profile;

    public BotPhase Phase { get; private set; } = BotPhase.Stopped;
    public int Cycles { get; private set; }
    public int Potions { get; private set; }
    public int Deaths { get; private set; }

    public void Start(TimeSpan now)
    {
        Cycles = Potions = Deaths = 0;
        _lastAttackDown = Never;
        _hpLowSince = _mpLowSince = _lastEsc = Never;
        ResetSkills();
        _failedSearches = 0;
        _blindFight = false;
        if (_profile().NavigateMode)
        {
            ResetNavigation();
            Enter(BotPhase.Setup, now);
            return;
        }
        Enter(_profile().AutoTarget ? BotPhase.Searching : BotPhase.Fighting, now);
    }

    public void Stop() => Phase = BotPhase.Stopped;

    public IReadOnlyList<BotAction> Tick(GameState state)
    {
        if (Phase == BotPhase.Stopped)
            return [];

        var p = _profile();
        var now = state.Time;
        var actions = new List<BotAction>();

        // Dead: wait for "Burada yeniden başla", click it; then heal before fighting again.
        if (p.ReviveOnDeath && state.Dead)
        {
            HandleDeath(state, p, actions);
            return actions;
        }
        if (Phase == BotPhase.Dead)
        {
            actions.Add(new NoteAction("Yeniden doğuldu; can dolduruluyor."));
            Enter(BotPhase.Recovering, now);
            _recoverSince = now;
        }
        if (Phase == BotPhase.Recovering && Recover(state, p, actions))
            return actions;

        // Low HP never stops the bot: stopping would let the mobs finish us off anyway. Potions
        // below, and if we die anyway the death handling above revives us.
        var confirm = Ms(Math.Max(0, p.BarConfirmMs));
        double? hp = state.Hp;
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

        // A window over the game (advert after a level-up): let go of everything and close it
        // with one Esc; only press again if it is still there 3 s later (a second Esc with
        // nothing open would open the game menu instead).
        if (p.ClosePopups && state.PopupOpen)
        {
            if (p.NavigateMode)
            {
                SetMoves([], p, actions, refresh: false);
                SetAttack(false, p, actions, refresh: false);
            }
            if (now - _lastEsc >= Ms(3000))
            {
                actions.Add(new KeyTapAction(p.Keys.CloseWindow));
                actions.Add(new NoteAction("Açılır pencere görüldü; Esc ile kapatılıyor."));
                _lastEsc = now;
            }
            return actions;
        }

        CastSkills(state, p, actions);

        if (p.NavigateMode)
        {
            Navigate(state, p, actions);
            return actions;
        }

        if (!state.HasTarget)
            _ignoredTargetHp = null;

        switch (Phase)
        {
            case BotPhase.Searching:
                Search(state, p, actions);
                break;
            case BotPhase.Fighting:
                Fight(state, p, actions);
                break;
            case BotPhase.Looting:
                Loot(state, p, actions);
                break;
            case BotPhase.Turning:
                if (now - _phaseSince >= _turnDuration)
                    Enter(NextAfterLoot(p), now);
                break;
        }
        return actions;
    }

    private void Fight(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        var elapsed = now - _phaseSince;
        bool targeted = p.AutoTarget && state.TargetHp != null && !_blindFight;

        if (!targeted || p.UseAttackKeyOnTarget)
        {
            // Re-send key-down every second: harmless for the game and restores the
            // attack if the key was released while the game lost focus.
            if (now - _lastAttackDown >= Ms(1000))
            {
                actions.Add(new KeyDownAction(p.Keys.Attack));
                _lastAttackDown = now;
            }
        }

        if (targeted)
        {
            // A clicked mob: the character runs to it and attacks on its own (no Space, which
            // would swing at the air or walk). Keep going until its bar is gone.
            bool gone = Confirmed(!state.HasTarget, ref _targetGoneSince, now, Ms(500));
            if (gone && elapsed >= Ms(800))
            {
                EndFight(p, actions, now);
                return;
            }
            if (state.TargetHp is double targetHp && targetHp < _targetHpBest - 0.005)
            {
                _targetHpBest = targetHp;
                _lastProgress = now;
            }
            // The mob's HP stopped going down: the character isn't hitting it. Click it again.
            if (state.HasTarget && now - _lastProgress >= Ms(p.StallMs))
            {
                if (_reclicks >= 3)
                {
                    // Unreachable/stuck: ignore it and look for mobs.
                    _ignoredTargetHp = state.TargetHp;
                    actions.Add(new NoteAction("Hedefin HP'si düşmüyor; bu hedef bırakıldı, başka mob aranıyor."));
                    EndFight(p, actions, now);
                    return;
                }
                if ((state.TargetSpot ?? state.MobCandidates.Cast<Point?>().FirstOrDefault()) is Point spot)
                    actions.Add(new ClickAction(spot));
                _reclicks++;
                _lastProgress = now;
            }
            if (elapsed >= Ms(p.FightMaxMs))
            {
                _ignoredTargetHp = state.HasTarget ? state.TargetHp : null;
                EndFight(p, actions, now);
            }
            return;
        }

        if (elapsed < Ms(p.FightMs))
            return;
        bool targetAlive = state.TargetHp != null && state.HasTarget;
        if (targetAlive && elapsed < Ms(p.FightMaxMs))
            return;
        EndFight(p, actions, now);
    }

    private void EndFight(BotProfile p, List<BotAction> actions, TimeSpan now)
    {
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
        // Knowing the selected mob, we know when the area around us is empty.
        bool nothingAround = state.TargetHp != null && !state.HasTarget;
        // With mob search, turning happens when a search finds nothing.
        bool turn = !p.AutoTarget && (nothingAround || (p.RotateEveryCycles > 0 && Cycles % p.RotateEveryCycles == 0));
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
        Enter(turn || move ? BotPhase.Turning : NextAfterLoot(p), now);
    }

    private void HandleDeath(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (Phase != BotPhase.Dead)
        {
            if (p.NavigateMode)
            {
                SetMoves([], p, actions, refresh: false);
                SetAttack(false, p, actions, refresh: false);
                ReleaseTimedKeys(actions);
                _lastCameraSetup = Never; // set the camera up again once alive
            }
            else
            {
                actions.Add(new KeyUpAction(p.Keys.Attack));
            }
            actions.Add(new NoteAction("Karakter öldü; \"Burada yeniden başla\" bekleniyor."));
            Enter(BotPhase.Dead, now);
            Deaths++;
            _lastReviveClick = Never;
        }
        if (state.ReviveReady && now - _lastReviveClick >= Ms(3000))
        {
            actions.Add(new ClickAction(DeathDetector.ReviveHereButton(state.Width, state.Height), Slow: true));
            actions.Add(new NoteAction("\"Burada yeniden başla\" tıklandı."));
            _lastReviveClick = now;
        }
    }

    /// <summary>Right after reviving: drink HP potions up to ReviveHealTo before fighting again.</summary>
    /// <returns>True while still healing (nothing else should happen this tick).</returns>
    private bool Recover(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        var waited = now - _recoverSince;
        bool done = state.Hp is double hp
            ? hp >= p.ReviveHealTo || waited >= Ms(p.ReviveHealMaxMs)
            : waited >= Ms(5000); // HP bar unknown: just give it a moment
        if (done)
        {
            actions.Add(new NoteAction("Can doldu; avlanmaya devam."));
            if (p.NavigateMode)
                Enter(BotPhase.Searching, now);
            else
                Enter(p.AutoTarget ? BotPhase.Searching : BotPhase.Fighting, now);
            return false;
        }
        if (state.Hp is not null && now - _lastHpPotion >= Ms(p.PotionCooldownMs))
        {
            actions.Add(new KeyTapAction(p.Keys.HpPotion));
            _lastHpPotion = now;
            Potions++;
        }
        return true;
    }

    /// <summary>A selected mob that is alive and not one we already gave up on.</summary>
    private bool RealTarget(GameState state) =>
        state.HasTarget && !(_ignoredTargetHp is double ignored && Math.Abs(state.TargetHp!.Value - ignored) < 0.02);

    private static BotPhase NextAfterLoot(BotProfile p) => p.AutoTarget ? BotPhase.Searching : BotPhase.Fighting;

    /// <summary>
    /// Wait for the camera to settle and moving things (mobs) to show up, then ask the input
    /// worker to click them. Retries with fresh candidates; turns the camera when nothing works.
    /// </summary>
    private void Search(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (RealTarget(state))
        {
            _failedSearches = 0;
            _blindFight = false;
            Enter(BotPhase.Fighting, now);
            Fight(state, p, actions);
            return;
        }
        // A click attempt takes up to ~1.5 s per candidate; allow fresh candidates after that.
        if (_searchSent && now - _searchSentAt >= Ms(2500))
            _searchSent = false;
        // Give the camera a moment to settle after turning.
        if (!_searchSent && now - _phaseSince >= Ms(600) && state.MobCandidates.Count > 0)
        {
            actions.Add(new FindTargetAction(state.MobCandidates.Take(3).ToArray()));
            _searchSent = true;
            _searchSentAt = now;
            return;
        }
        if (now - _phaseSince < Ms(p.SearchTimeoutMs))
            return;

        // Memory doesn't say whether something is selected: attack blindly once.
        if (state.TargetHp == null)
        {
            _blindFight = true;
            Enter(BotPhase.Fighting, now);
            return;
        }
        // Nothing here: look around twice, then walk somewhere else. No attacking the air.
        _failedSearches++;
        if (_failedSearches >= 3)
        {
            _failedSearches = 0;
            actions.Add(new KeyHoldAction(p.Keys.MoveForward, p.MoveHoldMs));
            _turnDuration = Ms(p.MoveHoldMs + 300);
        }
        else
        {
            actions.Add(new KeyHoldAction(p.Keys.RotateCamera, p.RotateHoldMs));
            _turnDuration = Ms(p.RotateHoldMs + 150);
        }
        Enter(BotPhase.Turning, now);
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
        if (phase == BotPhase.Searching)
            _searchSent = false;
        if (phase == BotPhase.Fighting)
        {
            _targetGoneSince = Never;
            _lastProgress = now;
            _targetHpBest = 1.01;
            _reclicks = 0;
        }
        if (phase == BotPhase.Looting)
            _blindFight = false;
        Phase = phase;
        _phaseSince = now;
    }
}
