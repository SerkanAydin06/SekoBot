using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Gathering mobs before fighting them (the game spawns them in threes; hitting one brings the
/// whole group): walk to the nearest group nobody has woken yet, hit one of its mobs until the
/// group comes after us, then on to the next group — the ones behind follow. Fighting starts when
/// PullMaxMobs are after us, when HP falls below PullMinHp, or when no group is left close by
/// (15 m); then everything is fought together (area skills hit them all).
/// </summary>
internal sealed partial class Brain
{
    /// <summary>Groups this close (world units) are gathered.</summary>
    internal const double PullRange = 1500;

    private readonly Dictionary<int, TimeSpan> _pulled = [];
    private int _pullGroup;
    private TimeSpan _pullHitSince = Never;
    private bool _pulling;

    private void ResetPull()
    {
        _pulled.Clear();
        _pullGroup = 0;
        _pullHitSince = Never;
        _pulling = false;
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

        var fresh = state.Targets
            .Where(t => t.Group != 0 && t.Distance <= PullRange && !_pulled.ContainsKey(t.Group))
            .GroupBy(t => t.Group)
            .Where(g => g.All(t => !t.AttacksMe && t.Hp >= t.MaxHp))
            .ToList();
        if (fresh.Count == 0)
            return EndPull(actions, chasing > 0 ? $"Yakında başka grup yok; toplanan {chasing} mob kesiliyor." : null);

        var group = fresh.FirstOrDefault(g => g.Key == _pullGroup) ?? fresh.MinBy(g => g.Min(t => t.Distance))!;
        if (group.Key != _pullGroup)
        {
            _pullGroup = group.Key;
            _pullHitSince = Never;
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
        return false;
    }
}
