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
    /// <summary>Since when walking to the group being pulled hasn't got us closer: one we can't get
    /// to (behind a wall, on a ledge) is given up after <see cref="PullWalkMs"/> without coming
    /// <see cref="PullProgress"/> closer (recorded: 18 of 23 gatherings in an evening gave a group up
    /// after a flat 4 s, also ones only a detour away), and after <see cref="PullWalkMaxMs"/> anyway.</summary>
    private TimeSpan _pullWalkSince = Never, _pullWalkStarted = Never;
    private double _pullClosest = double.MaxValue;
    internal const int PullWalkMs = 4000, PullWalkMaxMs = 12000;
    /// <summary>The last tick spent walking to the group; a longer gap than <see cref="PullPausedMs"/>: something else came between.</summary>
    private TimeSpan _pullWalkTickAt = Never;
    internal const int PullPausedMs = 1500;
    /// <summary>How much nearness counts when the first group is chosen (world units): a group this far is worth
    /// half as much as one next to us (was 1000 — seen by the user: off to a far group with one right beside).</summary>
    internal const double PullNearWeight = 250;
    private TimeSpan _pullHeldToldAt = Never;

    /// <summary>The least HP a group right beside us is gathered with: the setting less 25 points, never under 45 %.</summary>
    internal static double BesideMinHp(BotProfile p) => Math.Max(0.45, p.PullMinHp - 0.25);
    /// <summary>With less HP than the setting, a group beside is gathered only while this many mobs (or fewer) are after us.</summary>
    internal const int BesideMaxChasing = 3;
    internal const double PullProgress = 150;
    /// <summary>A group that couldn't be reached is left this long.</summary>
    internal const int PullUnreachableMs = 300_000;
    /// <summary>With mobs already after us, the next group is gone for only this close (world units,
    /// 10 m): recorded in the game, half the gatherings walked 5–11 s for 1–3 more mobs while the
    /// ones gathered hit our back; fighting them right away is quicker.</summary>
    internal const double PullNextWithChasers = 1000;
    /// <summary>This many mobs (or fewer) left after us and none here to hit: the next group is gone for.</summary>
    internal const int LeftoverMobs = 2;
    private bool _pulling;
    /// <summary>The middle of the first group gathered: the next ones come from around it.</summary>
    private Memory.Vec3? _pullAnchor;

    // ── How many to gather: learned from how the fights went ──

    /// <summary>A fight that took HP below this: gather fewer next time.</summary>
    internal const double PullDangerHp = 0.40;
    /// <summary>A fight that kept HP above this: one more next time (up to PullMaxMobs).</summary>
    internal const double PullSafeHp = 0.70;
    /// <summary>Never fewer than this.</summary>
    internal const int PullMinMobs = 3;
    /// <summary>A map this character hasn't learned yet starts with this many (safe for a weak
    /// character; a strong one is up to its best within a few easy fights).</summary>
    internal const int PullStartMobs = 6;
    /// <summary>A fight that kept HP above this: two more next time.</summary>
    internal const double PullVerySafeHp = 0.85;

    /// <summary>How many to gather now (0: not set yet → PullMaxMobs).</summary>
    private int _pullCap;
    /// <summary>The map _pullCap was taken from (the character's learned number for it).</summary>
    private string? _pullMap;
    private double _fightLowHp = 1;

    /// <summary>Set when something learned went into the profile (to be saved with the character).</summary>
    public bool ProfileChanged { get; set; }

    /// <summary>A new number learned for this map: kept in the profile (a new dictionary, as the
    /// window may be saving the old one at the same time).</summary>
    private void KeepPullCap(BotProfile p)
    {
        if (string.IsNullOrEmpty(_pullMap))
            return;
        p.PullLearned = new Dictionary<string, int>(p.PullLearned) { [_pullMap] = _pullCap };
        ProfileChanged = true;
    }
    private int _fightMostAttackers;

    /// <summary>How many mobs are gathered at most now (lowered after a fight that went badly).</summary>
    public int PullCap(BotProfile p) => _pullCap is > 0 ? Math.Min(_pullCap, p.PullMaxMobs) : p.PullMaxMobs;

    /// <summary>
    /// Watches each fight with several mobs on us: when it is over (nothing attacks us any more),
    /// a fight that took HP below 40 % lowers the number gathered by 2 (not under 3); one that kept
    /// HP above 70 % with about as many as allowed raises it by 1 again (up to the setting).
    /// </summary>
    private void LearnPullSize(GameState state, BotProfile p, List<BotAction> actions)
    {
        // What this character learned on this map before (another day, another SekoBot).
        if (state.MapName.Length > 0 && state.MapName != _pullMap)
        {
            _pullMap = state.MapName;
            _pullCap = p.PullLearned.TryGetValue(state.MapName, out int learned) ? learned : Math.Min(PullStartMobs, p.PullMaxMobs);
        }
        int attackers = state.Targets.Count(t => t.AttacksMe);
        if (attackers > 0)
        {
            _fightMostAttackers = Math.Max(_fightMostAttackers, attackers);
            if (state.Hp is double hp)
                _fightLowHp = Math.Min(_fightLowHp, hp);
            return;
        }
        if (_fightMostAttackers >= PullMinMobs && p.PullGroups)
        {
            int cap = PullCap(p);
            if (_fightLowHp < PullDangerHp && cap > PullMinMobs)
            {
                _pullCap = Math.Max(PullMinMobs, Math.Min(cap, _fightMostAttackers) - 2);
                KeepPullCap(p);
                actions.Add(new NoteAction($"{_fightMostAttackers} mobla can %{_fightLowHp * 100:0}'e düştü; bundan sonra en fazla {_pullCap} mob toplanacak."));
            }
            else if (_fightLowHp > PullSafeHp && cap < p.PullMaxMobs && _fightMostAttackers >= cap - 1)
            {
                _pullCap = Math.Min(p.PullMaxMobs, cap + (_fightLowHp > PullVerySafeHp ? 2 : 1));
                KeepPullCap(p);
                actions.Add(new NoteAction($"Dövüş rahat geçti (can en az %{_fightLowHp * 100:0}); artık en fazla {_pullCap} mob toplanacak."));
            }
        }
        _fightMostAttackers = 0;
        _fightLowHp = 1;
    }

    private void ResetPull()
    {
        _pullCap = 0;
        _pullMap = null;
        _fightLowHp = 1;
        _fightMostAttackers = 0;
        _pulled.Clear();
        _pullGroup = 0;
        _pullHitSince = Never;
        _pullWalkSince = Never;
        _pulling = false;
        _pullAnchor = null;
    }

    /// <summary>The group being walked to let go without counting it as unreachable (the walk home took over).</summary>
    private void DropPull(TimeSpan now)
    {
        if (_pullGroup != 0)
            _pulled[_pullGroup] = now; // not again within the minute, but not left for 5 minutes either
        _pullGroup = 0;
        _pullHitSince = _pullWalkSince = _pullWalkStarted = Never;
        _pullClosest = double.MaxValue;
    }

    /// <summary>A mob hitting us with less HP than this (of its most) is finished before gathering more.</summary>
    internal const double FinishFirstBelow = 0.4;
    /// <summary>A sleeping group this close (world units, 7 m) is gathered even while a mob hits us.</summary>
    internal const double PullBeside = 700;

    /// <summary>A whole sleeping group right beside us (not left a moment ago, inside the area).</summary>
    private bool GroupBeside(GameState state, BotProfile p) =>
        state.Targets.Where(t => t.Group != 0 && !Left(state, t.Group)).GroupBy(t => t.Group)
            .Any(g => g.All(t => !t.AttacksMe && t.Hp >= t.MaxHp) && g.Min(t => t.Distance) <= PullBeside && PullInsideArea(g, p));

    /// <summary>Groups are gathered only this far into the area (its radius), so walking to one — around
    /// whatever is in the way — doesn't take the character over the edge (and home again, half way).</summary>
    internal const double PullInArea = 0.85;

    private bool PullInsideArea(IEnumerable<TargetInfo> group, BotProfile p) =>
        p.LeashMeters <= 0 || _home is not Memory.Vec3 home || Centre(group) is not Memory.Vec3 c ||
        c.DistanceXY(home) <= p.LeashMeters * 100 * PullInArea;

    /// <summary>A group left a moment ago (it didn't wake up, or came and was fought): not gone for again for a
    /// minute — but one right beside us after <see cref="PullBesideRetryMs"/> already (seen: groups 1–7 m away
    /// skipped as "left a moment ago" while the gathering ended).</summary>
    private bool Left(GameState state, int group)
    {
        if (!_pulled.TryGetValue(group, out var when))
            return false;
        if (state.Time - when <= Ms(PullBesideRetryMs))
            return true;
        return !(state.Targets.Where(t => t.Group == group).Select(t => t.Distance).DefaultIfEmpty(double.MaxValue).Min() <= PullBeside);
    }
    internal const int PullBesideRetryMs = 15_000;

    /// <summary>For the log: how far the nearest whole sleeping group is (why it wasn't gathered can be seen).</summary>
    private string NearestSleeping(GameState state)
    {
        var near = state.Targets.Where(t => t.Group != 0).GroupBy(t => t.Group)
            .Where(g => g.All(t => !t.AttacksMe && t.Hp >= t.MaxHp)).Select(g => (g.Key, Metres: g.Min(t => t.Distance) / 100))
            .OrderBy(g => g.Metres).FirstOrDefault();
        return near.Key == 0 ? "" : $" (en yakın uyuyan grup {near.Metres:0} m{(_pulled.ContainsKey(near.Key) ? ", az önce bırakılmıştı" : "")})";
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
        if (!p.PullGroups || state.Targets.Count == 0 || WantsClean)
            return EndPull(actions, null);
        // A preferred Metin stone comes first; gathering would only get in the way.
        if (state.Targets.Any(t => t.Priority && !t.AttacksMe))
            return EndPull(actions, null);
        int chasing = state.Targets.Count(t => t.AttacksMe);
        // A group right beside us is gathered with less HP too (the user: "keep gathering what is around, leave
        // no mob near"); only the far ones need the full setting. How many is still learned from the fights.
        bool besideOnly = state.Hp is double hpNow && hpNow < p.PullMinHp && hpNow >= BesideMinHp(p) && chasing <= BesideMaxChasing && GroupBeside(state, p);
        if (state.Hp is double hp && hp < p.PullMinHp && !besideOnly)
        {
            // Not starting with too little HP — told (once a minute) when a group is right beside us, so it
            // shows why mobs are cut one by one (it didn't show before).
            if (!_pulling && now - _pullHeldToldAt > Ms(60_000) && GroupBeside(state, p))
            {
                _pullHeldToldAt = now;
                actions.Add(new NoteAction($"Yanda uyuyan grup var ama can %{hp * 100:0}; yandaki grubu toplamak için en az %{BesideMinHp(p) * 100:0} isteniyor."));
            }
            return EndPull(actions, $"Can azaldı; toplanan {chasing} mob kesiliyor.");
        }
        if (chasing >= PullCap(p))
            return EndPull(actions, $"{chasing} mob toplandı; hepsi birden kesiliyor.");
        // Mobs hitting us right here and hurt: finish them first (recorded: a gathering started
        // again while a mob at 24 % HP was being hit, and the bot walked off and came back).
        // A sleeping group right beside us is gathered all the same while the mob hitting us has much HP
        // left (the user saw a lone mob cut alone with a fine group next to it); one nearly dead is finished.
        if (!_pulling && state.Targets.Any(t => t.AttacksMe && t.Distance <= t.Reach * 1.4 && t.Hp < t.MaxHp &&
                                               (t.Hp < t.MaxHp * FinishFirstBelow || !GroupBeside(state, p))))
            return EndPull(actions, null);
        // Mobs already after us (one walked into us): a new gathering only takes groups right
        // around us — they follow us there anyway; far groups would leave them hitting our back.
        bool startingWithChasers = !_pulling && chasing > 0;
        // The last ones of a fight, none of them here to hit (1–2 left, or only archers shooting
        // from 3 m): on to the next group close by — they follow (recorded: ~11 % of the time went
        // on 1–2 last mobs with a sleeping group within 10 m).
        var after = state.Targets.Where(t => t.AttacksMe).ToList();
        bool leftovers = startingWithChasers && !after.Any(t => t.Distance <= t.Reach * 1.4) &&
                         (after.Count <= LeftoverMobs || after.All(t => t.Ranged));

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
            .Where(t => t.Group != 0 && t.Distance <= PullRange * 1.5 && !Left(state, t.Group))
            .GroupBy(t => t.Group)
            .Where(g => g.All(t => !t.AttacksMe && t.Hp >= t.MaxHp))
            .Where(g => g.Key == _pullGroup || PullInsideArea(g, p))
            .Where(g => !besideOnly || g.Min(t => t.Distance) <= PullBeside)
            // The first group within 15 m; the next ones only around the first.
            .Where(g => _pullAnchor is not Memory.Vec3 anchor
                ? g.Min(t => t.Distance) <= (leftovers ? Math.Max(area, PullNextWithChasers) : startingWithChasers ? area : PullRange)
                : g.Min(t => t.Distance) <= area || Centre(g) is Memory.Vec3 c && c.DistanceXY(anchor) <= area)
            .ToList();
        // Mobs after us: the next group only when it is close (else they are fought now).
        if (chasing > 0 && _pullGroup == 0)
            fresh = [.. fresh.Where(g => g.Min(t => t.Distance) <= PullNextWithChasers)];
        if (fresh.Count == 0)
            return EndPull(actions, chasing > 0 ? $"Bu bölgede başka grup yok; toplanan {chasing} mob kesiliyor.{NearestSleeping(state)}" : null);

        double dps = Math.Max(50, state.AttackPower);
        double Worth(IGrouping<int, TargetInfo> g) =>
            g.Sum(t => (t.Exp > 0 ? t.Exp : t.MaxHp * 0.3) / (t.Hp / dps + 1)) / (1 + g.Min(t => t.Distance) / PullNearWeight);
        // Keep the group being pulled; else the first one by worth, the next ones nearest to us.
        var group = fresh.FirstOrDefault(g => g.Key == _pullGroup)
                    ?? (_pullAnchor == null ? fresh.MaxBy(Worth)! : fresh.MinBy(g => g.Min(t => t.Distance))!);
        if (group.Key != _pullGroup)
        {
            _pullGroup = group.Key;
            _pullHitSince = Never;
            _pullWalkSince = _pullWalkStarted = now;
            _pullClosest = double.MaxValue;
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
            // Next to it: hit it until the group wakes up (a few seconds at most), standing still.
            SetMoves(SteerInReach(state, member, now) ? Towards(dx, dy, radius * 0.35, p) : [], p, actions, refresh);
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
            // Walking to this group was cut by something else (a special drop picked up, a sell trip, emptying
            // the bag, a stun): its clocks start again — the time away isn't time spent failing to get there
            // (recorded: groups given up the very second a sell trip came back).
            if (_pullWalkTickAt != Never && now - _pullWalkTickAt > Ms(PullPausedMs))
            {
                _pullWalkSince = _pullWalkStarted = now;
                _pullClosest = double.MaxValue;
            }
            _pullWalkTickAt = now;
            if (member.Distance < _pullClosest - PullProgress)
            {
                _pullClosest = member.Distance;
                _pullWalkSince = now;     // getting there: the clock starts again
            }
            if (_pullWalkSince != Never && (now - _pullWalkSince > Ms(PullWalkMs) ||
                                            _pullWalkStarted != Never && now - _pullWalkStarted > Ms(PullWalkMaxMs)))
            {
                // Can't get to it (seen in the game: 30 s walking along a wall): leave it.
                _walkGoal = member.Position;
                StuckHere(state);
                _pulled[group.Key] = now;
                // Not again in a minute (recorded on c3: 30 give-ups in 22 min, the same groups
                // tried again and again): its mobs are left for 5 minutes unless they come to us.
                foreach (var t in group)
                    _unreachable[t.Id] = now + Ms(PullUnreachableMs);
                GaveUpAt(state, member.Position);
                _pullGroup = 0;
                _pullHitSince = _pullWalkSince = _pullWalkStarted = Never;
                actions.Add(new NoteAction("Gruba ulaşılamadı; bırakıldı."));
                return true;
            }
            _walkGoal = member.Position;
            SetAttack(false, p, actions, refresh);
            // To the group, around the others (they'd come too early, from the wrong side).
            int pulling = group.Key;
            SetMoves(Around(state, dx, dy, radius * 0.5, p, m => m.Group == pulling, member.Position), p, actions, refresh);
            Did("Mob grubu toplanıyor");
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
