using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Farm state machine. Pure logic: takes a <see cref="GameState"/>, returns actions.
/// Every waiting state has a timeout, so the bot never stays stuck on one target.
///
/// Searching   → click nearest stone                 → Approaching
/// Approaching → target HP bar appears               → Attacking   (timeout: blacklist, Searching)
/// Attacking   → target HP bar gone                  → Looting     (timeout: blacklist, Searching)
/// Looting     → pickup key pressed N times          → Searching
/// Any state   → low HP potion, critical HP          → Stop
/// </summary>
internal sealed class Brain
{
    private const double BlacklistRadius = 90;
    private const double FollowRadius = 160;

    private readonly Func<BotProfile> _profile;
    private readonly Random _random;
    private readonly List<(Point At, TimeSpan Until)> _blacklist = [];

    private TimeSpan _phaseSince, _lastClick, _lastRotate, _lastAttackKey, _lastLoot, _lastTargetSeen;
    private TimeSpan _lastHpPotion = TimeSpan.FromDays(-1), _lastMpPotion = TimeSpan.FromDays(-1);
    private Point _targetPoint;
    private int _rotations, _lootPresses;

    public Brain(Func<BotProfile> profile, Random? random = null)
    {
        _profile = profile;
        _random = random ?? new Random();
    }

    public BotPhase Phase { get; private set; } = BotPhase.Stopped;
    public int StonesDestroyed { get; private set; }
    public int TargetsSkipped { get; private set; }

    public void Start(TimeSpan now)
    {
        _blacklist.Clear();
        _rotations = 0;
        Enter(BotPhase.Searching, now);
        _lastRotate = now;
    }

    public void Stop() => Phase = BotPhase.Stopped;

    public IReadOnlyList<BotAction> Tick(GameState state)
    {
        if (Phase == BotPhase.Stopped)
            return [];

        var p = _profile();
        var now = state.Time;
        var actions = new List<BotAction>();
        _blacklist.RemoveAll(b => b.Until <= now);

        if (state.Hp is double hp)
        {
            if (hp < p.StopHpBelow)
            {
                Phase = BotPhase.Stopped;
                return [new StopAction($"HP kritik (%{hp * 100:0})")];
            }
            if (hp < p.PotionHpBelow && now - _lastHpPotion >= TimeSpan.FromMilliseconds(p.PotionCooldownMs))
            {
                actions.Add(new KeyTapAction(p.Keys.HpPotion));
                _lastHpPotion = now;
            }
        }
        if (state.Mp is double mp && mp < p.PotionMpBelow &&
            now - _lastMpPotion >= TimeSpan.FromMilliseconds(p.PotionCooldownMs))
        {
            actions.Add(new KeyTapAction(p.Keys.MpPotion));
            _lastMpPotion = now;
        }

        switch (Phase)
        {
            case BotPhase.Searching:
                Search(state, p, actions);
                break;
            case BotPhase.Approaching:
                Approach(state, p, actions);
                break;
            case BotPhase.Attacking:
                Attack(state, p, actions);
                break;
            case BotPhase.Looting:
                Loot(state, p, actions);
                break;
        }
        return actions;
    }

    private void Search(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (state.HasTarget)
        {
            // Something is already selected (e.g. the stone we just walked into).
            _lastTargetSeen = now;
            Enter(BotPhase.Attacking, now);
            return;
        }

        var stone = Nearest(state, state.ScreenCenter, double.MaxValue, skipBlacklisted: true);
        if (stone is Detection target)
        {
            _targetPoint = target.Center;
            actions.Add(new ClickAction(Jitter(target)));
            _lastClick = now;
            _rotations = 0;
            Enter(BotPhase.Approaching, now);
            return;
        }

        if (now - _lastRotate < TimeSpan.FromMilliseconds(p.RotateHoldMs + 450))
            return;
        _lastRotate = now;
        if (_rotations < p.RotationsBeforeMove)
        {
            actions.Add(new KeyHoldAction(p.Keys.RotateCamera, p.RotateHoldMs));
            _rotations++;
        }
        else
        {
            actions.Add(new KeyHoldAction(p.Keys.MoveForward, p.MoveHoldMs));
            _rotations = 0;
        }
    }

    private void Approach(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (state.HasTarget)
        {
            _lastTargetSeen = now;
            Enter(BotPhase.Attacking, now);
            return;
        }
        if (now - _phaseSince > TimeSpan.FromMilliseconds(p.ApproachTimeoutMs))
        {
            GiveUp(now, p);
            return;
        }
        // The stone moves on screen while we walk; re-click it every few seconds.
        if (now - _lastClick > TimeSpan.FromMilliseconds(2500) &&
            Nearest(state, _targetPoint, FollowRadius, skipBlacklisted: false) is Detection same)
        {
            _targetPoint = same.Center;
            actions.Add(new ClickAction(Jitter(same)));
            _lastClick = now;
        }
    }

    private void Attack(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (state.HasTarget)
        {
            _lastTargetSeen = now;
            if (now - _phaseSince > TimeSpan.FromMilliseconds(p.AttackTimeoutMs))
            {
                GiveUp(now, p);
                return;
            }
            if (p.UseAttackKey && now - _lastAttackKey > TimeSpan.FromMilliseconds(900))
            {
                actions.Add(new KeyHoldAction(p.Keys.Attack, 700));
                _lastAttackKey = now;
            }
            return;
        }
        if (now - _lastTargetSeen > TimeSpan.FromMilliseconds(p.TargetLostGraceMs))
        {
            StonesDestroyed++;
            _lootPresses = 0;
            Enter(BotPhase.Looting, now);
        }
    }

    private void Loot(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (_lootPresses >= p.LootPresses)
        {
            Enter(BotPhase.Searching, now);
            _lastRotate = now;
            return;
        }
        if (now - _lastLoot > TimeSpan.FromMilliseconds(350))
        {
            actions.Add(new KeyTapAction(p.Keys.Pickup));
            _lastLoot = now;
            _lootPresses++;
        }
    }

    private void GiveUp(TimeSpan now, BotProfile p)
    {
        _blacklist.Add((_targetPoint, now + TimeSpan.FromMilliseconds(p.BlacklistMs)));
        TargetsSkipped++;
        Enter(BotPhase.Searching, now);
        _lastRotate = now;
    }

    private Detection? Nearest(GameState state, Point from, double maxDistance, bool skipBlacklisted)
    {
        Detection? best = null;
        double bestDistance = maxDistance;
        foreach (var stone in state.Stones)
        {
            var c = stone.Center;
            if (skipBlacklisted && _blacklist.Any(b => Distance(b.At, c) < BlacklistRadius))
                continue;
            double d = Distance(from, c);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = stone;
            }
        }
        return best;
    }

    /// <summary>Click inside the middle of the box rather than the exact same pixel.</summary>
    private Point Jitter(Detection d)
    {
        int dx = Math.Max(1, d.Box.Width / 6), dy = Math.Max(1, d.Box.Height / 6);
        return new Point(d.Center.X + _random.Next(-dx, dx + 1), d.Center.Y + _random.Next(-dy, dy + 1));
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void Enter(BotPhase phase, TimeSpan now)
    {
        Phase = phase;
        _phaseSince = now;
    }
}
