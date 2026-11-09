using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Gathering mobs before fighting them (the game spawns them in threes; hitting one brings the
/// whole group). The first group is the one worth most (experience for the time to kill, a little
/// less for every metre away) within 15 m; the next ones only come from around that first group
/// (PullAreaMetres from it), so the bot gathers one area instead of being led further and further
/// away. A group is hit until it comes after us, then on to the next — the ones behind follow.
/// Fighting starts when PullMaxMobs are after us, when HP falls below PullMinHp, or when no group
/// is left in the area; then everything is fought together (area skills hit them all).
/// </summary>
internal sealed partial class Brain
{
    /// <summary>Groups this close (world units) are gathered.</summary>
    internal const double PullRange = 1500;

    private readonly Dictionary<int, TimeSpan> _pulled = [];
    private int _pullGroup;
    private TimeSpan _pullHitSince = Never;
    private bool _pulling;
    /// <summary>The middle of the first group gathered: the next ones come from around it.</summary>
    private Memory.Vec3? _pullAnchor;

    private void ResetPull()
    {
        _pulled.Clear();
        _pullGroup = 0;
        _pullHitSince = Never;
        _pulling = false;
        _pullAnchor = null;
    }

    private static Memory.Vec3? Centre(IEnumerable<TargetInfo> group)
    {
        var known = group.Where(t => t.Position != null).Select(t => t.Position!.Value).ToList();
        return known.Count == 0 ? null : new Memory.Vec3(known.Average(v => v.X), known.Average(v => v.Y), known.Average(v => v.Z));
    }

    /// <returns>True when this tick was spent gathering.</returns>
    private bool PullTick(GameState state, BotProfile p, List<BotAction> actions, bool refresh, double radius)
    {
        var now = state.Time;
        if (!p.PullGroups || state.Targets.Count == 0)
            return EndPull(actions, null);
        // A preferred Metin stone comes first; gathering would only get in the way.
        if (state.Targets.Any(t => t.Priority && !t.AttacksMe))
            return EndPull(actions, null);
        int chasing = state.Targets.Count(t => t.AttacksMe);
        if (state.Hp is double hp && hp < p.PullMinHp)
            return EndPull(actions, $"Can azaldı; toplanan {chasing} mob kesiliyor.");
        if (chasing >= p.PullMaxMobs)
            return EndPull(actions, $"{chasing} mob toplandı; hepsi birden kesiliyor.");
        // Mobs already after us (one walked into us): a new gathering only takes groups right
        // around us — they follow us there anyway; far groups would leave them hitting our back.
        bool startingWithChasers = !_pulling && chasing > 0;

        foreach (var (pulledGroup, when) in _pulled.ToList())
            if (now - when > Ms(60_000))
                _pulled.Remove(pulledGroup);

        // The group being pulled has come (one is after us, or hurt): on to the next.
        if (_pullGroup != 0 && state.Targets.Where(t => t.Group == _pullGroup).Any(t => t.AttacksMe || t.Hp < t.MaxHp))
        {
            _pulled[_pullGroup] = now;
            _pullGroup = 0;
            _pullHitSince = Never;
        }

        double area = Math.Max(1, p.PullAreaMetres) * 100;
        var fresh = state.Targets
            .Where(t => t.Group != 0 && t.Distance <= PullRange * 1.5 && !_pulled.ContainsKey(t.Group))
            .GroupBy(t => t.Group)
            .Where(g => g.All(t => !t.AttacksMe && t.Hp >= t.MaxHp))
            // The first group within 15 m; the next ones only around the first.
            .Where(g => _pullAnchor is not Memory.Vec3 anchor
                ? g.Min(t => t.Distance) <= (startingWithChasers ? area : PullRange)
                : Centre(g) is Memory.Vec3 c ? c.DistanceXY(anchor) <= area : g.Min(t => t.Distance) <= area)
            .ToList();
        if (fresh.Count == 0)
            return EndPull(actions, chasing > 0 ? $"Bu bölgede başka grup yok; toplanan {chasing} mob kesiliyor." : null);

        double dps = Math.Max(50, state.AttackPower);
        double Worth(IGrouping<int, TargetInfo> g) =>
            g.Sum(t => (t.Exp > 0 ? t.Exp : t.MaxHp * 0.3) / (t.Hp / dps + 1)) / (1 + g.Min(t => t.Distance) / 1000);
        // Keep the group being pulled; else the first one by worth, the next ones nearest to us.
        var group = fresh.FirstOrDefault(g => g.Key == _pullGroup)
                    ?? (_pullAnchor == null ? fresh.MaxBy(Worth)! : fresh.MinBy(g => g.Min(t => t.Distance))!);
        if (group.Key != _pullGroup)
        {
            _pullGroup = group.Key;
            _pullHitSince = Never;
            _pullAnchor ??= Centre(group);
        }
        if (!_pulling)
        {
            _pulling = true;
            actions.Add(new NoteAction("Gruplar toplanıyor (her üçlüden birine vurulup peşe takılıyor)."));
        }

        var member = group.MinBy(t => t.Distance)!;
        double cx = state.Width / 2.0, cy = state.Height / 2.0;
        double dx = member.Spot.X - cx, dy = member.Spot.Y - cy;
        if (member.Distance <= member.Reach - 10)
        {
            // Next to it: hit it until the group wakes up (a few seconds at most).
            SetMoves(Towards(dx, dy, radius * 0.35, p), p, actions, refresh);
            SetAttack(true, p, actions, refresh);
            if (_pullHitSince == Never)
                _pullHitSince = now;
            else if (now - _pullHitSince > Ms(3000))
            {
                _pulled[group.Key] = now; // didn't wake up: leave it
                _pullGroup = 0;
                _pullHitSince = Never;
            }
            if (Phase != BotPhase.Fighting)
                Enter(BotPhase.Fighting, now);
            _wasFighting = true;
        }
        else
        {
            SetAttack(false, p, actions, refresh);
            SetMoves(Towards(dx, dy, radius * 0.5, p), p, actions, refresh);
            if (Phase != BotPhase.Moving)
                Enter(BotPhase.Moving, now);
            _movingSince = Never;
            CheckBlocked(state, p, actions, mobClose: member.Distance <= member.Reach * 2);
        }
        return true;
    }

    /// <summary>Gathering is over (or not needed): the normal fight takes it from here.</summary>
    private bool EndPull(List<BotAction> actions, string? note)
    {
        if (_pulling && note != null)
            actions.Add(new NoteAction(note));
        _pulling = false;
        _pullGroup = 0;
        _pullHitSince = Never;
        _pullAnchor = null;
        return false;
    }
}
