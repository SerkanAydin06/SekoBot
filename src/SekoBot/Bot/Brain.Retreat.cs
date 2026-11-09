using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Running low on HP potions: stop fighting, walk away from the mobs (away from the ones close
/// by, those attacking us count double) until nothing is near and nothing attacks us for 3 s,
/// then stop the bot there — so the character isn't killed once the potions run out.
/// <para>
/// Only potions going down to the limit while the bot runs count: when there were too few
/// already at the start (or the potion key isn't used), the bot just warns once.
/// </para>
/// </summary>
internal sealed partial class Brain
{
    private bool _retreating, _potionsWereEnough, _potionWarned;
    private TimeSpan _quietSince = Never;

    /// <summary>Set by the engine when the bag is full and there are items to throw away: fight
    /// what attacks us, gather nothing new, then get away from the mobs to a quiet spot.</summary>
    public bool WantsClean
    {
        get => _wantsClean;
        set
        {
            _wantsClean = value;
            if (!value)
                ReadyToClean = _cleanNoted = false;
        }
    }
    private bool _wantsClean, _cleanNoted;

    /// <summary>Quiet spot reached (nothing near, nothing attacking for 3 s): the engine can open the bag.</summary>
    public bool ReadyToClean { get; private set; }

    /// <summary>Throwing was cut short (attacked): fight, then find a quiet spot again.</summary>
    public void CleanInterrupted()
    {
        ReadyToClean = false;
        _quietSince = Never;
    }

    private void ResetRetreat()
    {
        _retreating = _potionsWereEnough = _potionWarned = false;
        _quietSince = Never;
        WantsClean = false;
    }

    /// <returns>True when this tick was spent getting to a quiet spot for throwing items away.</returns>
    private bool CleanTick(GameState state, BotProfile p, List<BotAction> actions)
    {
        if (!WantsClean || !p.NavigateMode)
            return false;
        // Whatever attacks us is fought first (the usual fighting; nothing new is gathered).
        if (state.Attackers.Count > 0 || state.Targets.Any(t => t.AttacksMe))
        {
            ReadyToClean = false;
            _quietSince = Never;
            return false;
        }
        var now = state.Time;
        bool refresh = now - _lastKeyRefresh >= Ms(1000);
        if (refresh)
            _lastKeyRefresh = now;
        _tickTime = now;
        if (!_cleanNoted)
            actions.Add(new NoteAction("Envanter boşaltılacak: moblardan uzak, sakin bir yere çekiliniyor."));
        _cleanNoted = true;
        SetAttack(false, p, actions, refresh);
        FinishFight(p, actions, now);
        if (GetAway(state, p, actions, refresh))
            ReadyToClean = true;
        return true;
    }

    /// <summary>
    /// Walks away from the mobs close by (those attacking us count double). True once nothing has
    /// been near and nothing has attacked us for 3 s (then the character stands still).
    /// </summary>
    private bool GetAway(GameState state, BotProfile p, List<BotAction> actions, bool refresh)
    {
        var now = state.Time;
        double cx = state.Width / 2.0, cy = state.Height / 2.0, danger = state.Height * 0.35;
        double ex = 0, ey = 0;
        int close = 0;
        foreach (var mob in state.MobCandidates)
        {
            double dx = cx - mob.X, dy = cy - mob.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            if (d > danger)
                continue;
            close++;
            double weight = (state.Attackers.Contains(mob) ? 2 : 1) / d;
            ex += dx / d * weight;
            ey += dy / d * weight;
        }

        if (close == 0 && state.Attackers.Count == 0)
        {
            // Nothing near, nothing hitting us: wait a moment to be sure.
            SetMoves([], p, actions, refresh);
            if (_quietSince == Never)
                _quietSince = now;
            return now - _quietSince >= Ms(3000);
        }
        _quietSince = Never;
        double length = Math.Sqrt(ex * ex + ey * ey);
        if (length > 0)
            SetMoves(Towards(ex / length * 100, ey / length * 100, 38, p), p, actions, refresh);
        else if (_heldMoves.Count == 0)
            SetMoves([p.Keys.MoveForward], p, actions, refresh); // hit from somewhere off screen: keep going
        if (Phase != BotPhase.Moving)
            Enter(BotPhase.Moving, now);
        _movingSince = Never;
        CheckBlocked(state, p, actions);
        return false;
    }

    /// <returns>True when this tick was spent getting away.</returns>
    private bool RetreatTick(GameState state, BotProfile p, List<BotAction> actions)
    {
        if (p.RetreatAtPotions <= 0 || state.HpPotionsLeft is not int left)
            return false;
        if (left > p.RetreatAtPotions)
        {
            _potionsWereEnough = true;
            _retreating = false; // refilled
            return false;
        }
        if (!_potionsWereEnough)
        {
            if (!_potionWarned)
                actions.Add(new NoteAction($"Uyarı: HP iksiri az ({left}). Bot başladığında da az olduğu için kaçma yapılmayacak."));
            _potionWarned = true;
            return false;
        }

        var now = state.Time;
        if (!_retreating)
        {
            _retreating = true;
            _quietSince = Never;
            actions.Add(new NoteAction($"HP iksiri azaldı ({left} kaldı); moblardan uzaklaşılıyor."));
        }
        if (!p.NavigateMode)
        {
            actions.Add(new StopAction($"HP iksiri azaldı ({left} kaldı)"));
            return true;
        }

        bool refresh = now - _lastKeyRefresh >= Ms(1000);
        if (refresh)
            _lastKeyRefresh = now;
        _tickTime = now;
        SetAttack(false, p, actions, refresh);
        FinishFight(p, actions, now);
        // Nothing near, nothing hitting us for 3 s: stop here.
        if (GetAway(state, p, actions, refresh))
            actions.Add(new StopAction($"HP iksiri azaldı ({left} kaldı); moblardan uzak, güvenli bir yerde durdu"));
        return true;
    }
}
