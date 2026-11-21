using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Walk mode: the camera looks straight down (G) and is zoomed out (F), so the character is
/// always in the middle of the screen; the mobs come from memory, placed on screen with the camera.
/// The bot walks with W/A/S/D towards the nearest mob and holds Space once it is next to
/// the character, still steering with W/A/S/D while attacking; when nothing is left nearby
/// it picks up loot and walks to the next one. Z is tapped all the time, and G/F are held
/// again every half minute — all next to walking and attacking, nothing ever waits.
/// </summary>
internal sealed partial class Brain
{
    private readonly HashSet<Keys> _heldMoves = [];
    /// <summary>Keys pressed for a while next to everything else (camera G/F, turning E).</summary>
    private readonly List<TimedKey> _timed = [];
    private bool _attackHeld, _wasFighting;
    private TimeSpan _tickTime;
    private double _lootJitter = 1;
    private TimeSpan _pauseUntil = Never, _lastSeen = Never, _lastLootTap = Never, _lastKeyRefresh = Never;
    private TimeSpan _movingSince = Never, _lastCameraSetup = Never, _exploreUntil = Never;
    private HashSet<Keys> _exploreKeys = [];
    /// <summary>Centre of the crowd being walked to (kept between ticks so the bot doesn't dither).</summary>
    private Point? _crowdTarget, _lastCrowd;
    private int _exploreStep;

    private sealed class TimedKey(Keys key, TimeSpan start, TimeSpan end)
    {
        public Keys Key { get; } = key;
        public TimeSpan Start { get; } = start;
        public TimeSpan End { get; } = end;
        public bool Down { get; set; }
    }

    private void ResetNavigation()
    {
        _heldMoves.Clear();
        _timed.Clear();
        _attackHeld = _wasFighting = false;
        _pauseUntil = _lastSeen = _lastLootTap = _lastKeyRefresh = _movingSince = _lastCameraSetup = _exploreUntil = Never;
        _exploreKeys = [];
        _exploreStep = 0;
        _crowdTarget = _lastCrowd = null;
        _targetId = 0;
        _toHotspot = false;
        _atFarmSpot = _travelNoted = _travelLate = false;
        Traveling = false;
        _travelSince = Never;
        ResetPull();
        _dropId = 0;
        _unreachable.Clear();
        _approachId = 0;
        _approachSince = _approachLast = Never;
        _dropTrying = TimeSpan.Zero;
        _dropTriedAt = Never;
        _specialNoted = false;
        _dropSince = _lastDropTap = Never;
        _ignoredDrops.Clear();
        _sidestepUntil = _notMovingSince = _walkStartedAt = _blockCheckedAt = _attackReleasedAt = Never;
        _sidestepKeys = [];
        _sidesteps = 0;
        ResetLeash();
        ResetTerrain();
    }

    /// <summary>Press <paramref name="key"/> from <paramref name="start"/> for <paramref name="ms"/> without blocking anything else.</summary>
    private void PressFor(Keys key, TimeSpan start, int ms) => _timed.Add(new TimedKey(key, start, start + Ms(ms)));

    private void RunTimedKeys(TimeSpan now, List<BotAction> actions, bool refresh)
    {
        foreach (var t in _timed.ToList())
        {
            if (!t.Down && now >= t.Start)
            {
                actions.Add(new KeyDownAction(t.Key));
                t.Down = true;
            }
            else if (t.Down && now < t.End && refresh)
            {
                actions.Add(new KeyDownAction(t.Key));
            }
            if (t.Down && now >= t.End)
            {
                actions.Add(new KeyUpAction(t.Key));
                _timed.Remove(t);
            }
        }
    }

    /// <summary>Let go of camera/turn keys that are being held and forget the pending ones.</summary>
    private void ReleaseTimedKeys(List<BotAction> actions)
    {
        foreach (var t in _timed.Where(t => t.Down))
            actions.Add(new KeyUpAction(t.Key));
        _timed.Clear();
    }

    /// <summary>How the camera looked once it was set (angle, distance to the character); null until known.</summary>
    private (double Pitch, double Distance)? _cameraSet;
    /// <summary>The camera counts as moved beyond this much (degrees, share of the distance).</summary>
    internal const double CameraPitchTolerance = 8, CameraDistanceTolerance = 0.15;

    /// <summary>A moment after the camera keys were let go: how it looks now is how it should look.</summary>
    private void NoteCamera(GameState state, TimeSpan now)
    {
        if (_cameraSet != null || _timed.Count > 0 || now - _lastCameraSetup < Ms(4000) ||
            state.Camera is not CameraView camera || state.Position is not Vec3 me)
            return;
        _cameraSet = (camera.Pitch, camera.Location.DistanceXY(me) + Math.Abs(camera.Location.Z - me.Z));
    }

    /// <summary>The camera isn't as it was set any more (turned, tilted, zoomed in). Not known: yes, set it.</summary>
    private bool CameraMoved(GameState state)
    {
        if (_cameraSet is not { } set || state.Camera is not CameraView camera || state.Position is not Vec3 me)
            return true;
        double distance = camera.Location.DistanceXY(me) + Math.Abs(camera.Location.Z - me.Z);
        return Math.Abs(camera.Pitch - set.Pitch) > CameraPitchTolerance ||
               set.Distance > 0 && Math.Abs(distance - set.Distance) / set.Distance > CameraDistanceTolerance;
    }

    /// <summary>Look straight down (G), then zoom out (F) — while the bot keeps walking and fighting.</summary>
    private void ScheduleCamera(BotProfile p, TimeSpan now)
    {
        _timed.RemoveAll(t => !t.Down && (t.Key == p.Keys.CameraTop || t.Key == p.Keys.ZoomOut));
        PressFor(p.Keys.CameraTop, now, p.CameraSetupHoldMs);
        PressFor(p.Keys.ZoomOut, now + Ms(p.CameraSetupHoldMs), p.CameraSetupHoldMs);
        _lastCameraSetup = now;
        _cameraSet = null; // noted again once it is set
    }

    private bool _lootOnly;

    /// <summary>
    /// Attack off ("Sadece eşya topla"): nothing is hit (mobs that attack included) and no skill
    /// used; the bot stays in its area and walks to the items lying around and picks them up
    /// (the loot choices apply), else stands still. Potions, fleeing and the area limit still work.
    /// </summary>
    private void LootOnly(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        _tickTime = now;
        bool refresh = now - _lastKeyRefresh >= Ms(1000);
        if (refresh)
            _lastKeyRefresh = now;
        if (!_lootOnly)
        {
            _lootOnly = true;
            actions.Add(new NoteAction("Saldırı kapalı: hiçbir moba vurulmuyor, sadece yerdeki eşyalar toplanıyor."));
        }
        SetAttack(false, p, actions, refresh);
        FinishFight(p, actions, now);
        _pulling = false;
        bool tapAround = !p.PickUpDrops || !p.NavigateMode;
        if (tapAround && p.LootEveryMs > 0 && now - _lastLootTap >= Ms(p.LootEveryMs * _lootJitter))
        {
            actions.Add(new KeyTapAction(p.Keys.Pickup));
            _lastLootTap = now;
            _lootJitter = 0.8 + 0.4 * _random.NextDouble();
        }
        if (!p.NavigateMode)
            return;
        if (Phase == BotPhase.Setup)
        {
            ScheduleCamera(p, now);
            Enter(BotPhase.Searching, now);
        }
        RunTimedKeys(now, actions, refresh);
        if (now < _sidestepUntil)
        {
            SetMoves(_sidestepKeys, p, actions, refresh);
            return;
        }
        FollowNotes(state, actions);
        if (state.Follow == null && TravelTick(state, p, actions, refresh))
            return;
        // Following someone: they decide where we are, not the start area.
        if (state.Follow == null && LeashTick(state, p, actions, refresh, mobNextToUs: false))
            return;
        double radius = p.AttackRadius * state.Height;
        var special = state.Drops.FirstOrDefault(d => d.Special && !_ignoredDrops.ContainsKey(d.Id));
        if (special.Id != 0 && GoForDrop(state, p, actions, refresh, radius, special))
            return;
        // Their drops first, unless they have walked off: then after them (the items stay a while).
        bool farBehind = FollowDistance(state) is double away && (away > FollowFar || state.Targets.Any(t => t.AttacksMe) && away > FollowGuard);
        if (!farBehind && GoForDrop(state, p, actions, refresh, radius))
            return;
        if (FollowTick(state, p, actions, refresh))
            return;
        if (GoForDrop(state, p, actions, refresh, radius))
            return;
        SetMoves([], p, actions, refresh);
        if (Phase != BotPhase.Searching)
            Enter(BotPhase.Searching, now);
    }

    /// <summary>Following: walk up when further than this (world units, 5 m), stop within <see cref="FollowNear"/>.</summary>
    internal const double FollowStart = 500, FollowNear = 250;
    /// <summary>This far behind (15 m), catching up comes before the items.</summary>
    internal const double FollowFar = 1500;
    /// <summary>Mobs on us: this close to the one followed (1.5 m).</summary>
    internal const double FollowGuard = 150;
    private bool _guardTold;
    private bool _following;
    private string? _followTold;

    private static double? FollowDistance(GameState state) =>
        state.Follow is { } f && state.Position is Vec3 me ? f.Position.DistanceXY(me) : null;

    /// <summary>Walks after the character followed when it got away (it kills, we pick up behind it).</summary>
    private bool FollowTick(GameState state, BotProfile p, List<BotAction> actions, bool refresh)
    {
        if (state.Follow is not { } f || state.Position is not Vec3 me || state.Camera is not CameraView camera)
        {
            _following = false;
            return false;
        }
        double away = f.Position.DistanceXY(me);
        // Mobs on us: right next to the one followed, who kills them (we don't hit back).
        bool hit = state.Targets.Any(t => t.AttacksMe);
        if (away > (hit ? FollowGuard : FollowStart))
            _following = true;
        else if (away < (hit ? FollowGuard * 0.6 : FollowNear))
            _following = false;
        if (hit && _following && !_guardTold)
        {
            _guardTold = true;
            actions.Add(new NoteAction($"Moblar saldırıyor; {f.Name}'ın yanına gidiliyor (o kessin)."));
        }
        else if (!hit)
            _guardTold = false;
        if (!_following)
            return false;
        var (right, up) = camera.ToScreenDirection(f.Position.X - me.X, f.Position.Y - me.Y);
        double sx = right, sy = -up; // screen y grows downwards
        double length = Math.Sqrt(sx * sx + sy * sy);
        if (length <= 0)
            return false;
        SetMoves(Around(state, sx, sy, length * 0.38, p, to: f.Position), p, actions, refresh);
        if (Phase != BotPhase.Moving)
            Enter(BotPhase.Moving, state.Time);
        _movingSince = Never;
        CheckBlocked(state, p, actions);
        return true;
    }

    private void FollowNotes(GameState state, List<BotAction> actions)
    {
        string? now = state.Follow?.Name;
        if (now == _followTold)
            return;
        if (now != null)
            actions.Add(new NoteAction($"{now} takip ediliyor; onun kestiklerinden düşenler toplanıyor."));
        else if (_followTold != null)
            actions.Add(new NoteAction($"{_followTold} görünmüyor; bulunulan yerde yerdeki eşyalar toplanıyor."));
        _followTold = now;
    }

    private void Navigate(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        _tickTime = now;
        // Re-send held keys every second: restores them if the game lost focus meanwhile.
        bool refresh = now - _lastKeyRefresh >= Ms(1000);
        if (refresh)
            _lastKeyRefresh = now;

        // Nothing here ever stops the bot: camera keys, turning and loot run alongside.
        if (Phase == BotPhase.Setup)
        {
            ScheduleCamera(p, now);
            Enter(BotPhase.Searching, now);
        }
        else if (p.CameraRefreshMs > 0 && now - _lastCameraSetup >= Ms(p.CameraRefreshMs))
        {
            // The camera can drift (a stray key, a cutscene): set again only when it really did (read
            // from memory), not every half minute (the keys ended up in the message box: "ggggg").
            if (CameraMoved(state))
                ScheduleCamera(p, now);
            else
                _lastCameraSetup = now;
        }
        NoteCamera(state, now);
        RunTimedKeys(now, actions, refresh);
        // Z every few seconds picks up anything around (also what the loot choices leave out), so
        // only when the bot doesn't walk to the items itself: then Z is pressed on the item.
        if (!p.PickUpDrops && p.LootEveryMs > 0 && now - _lastLootTap >= Ms(p.LootEveryMs * _lootJitter))
        {
            actions.Add(new KeyTapAction(p.Keys.Pickup));
            _lastLootTap = now;
            _lootJitter = 0.8 + 0.4 * _random.NextDouble(); // not like clockwork
        }
        if (now < _pauseUntil)
            return; // casting a skill: stand still a moment

        // Stepping aside around something we walked into.
        if (now < _sidestepUntil)
        {
            SetAttack(false, p, actions, refresh);
            SetMoves(_sidestepKeys, p, actions, refresh);
            return;
        }

        double cx = state.Width / 2.0, cy = state.Height / 2.0;
        Point? target = null;
        double best = double.MaxValue;
        // Mobs attacking us (or Metin stones, when preferred) come first.
        bool priority = state.PriorityMobs.Count > 0;
        // With the mobs from memory: the one worth most per second, and real distances.
        // Mobs outside the area aren't walked to at all (we'd only turn back halfway); those
        // attacking us are still fought.
        bool fromMemory = state.Targets.Count > 0;
        // Nor to mobs on a ledge or behind a spot we got stuck at before.
        _walkGoal = null;
        // A mob left as "can't get to it" that is next to us now was reachable after all (recorded:
        // the bot stood 6 s beside a sleeping mob of a group it had given up on).
        foreach (var (id, until) in _unreachable.ToList())
            if (now >= until || state.Targets.Any(t => t.Id == id && t.Distance <= t.Reach * ReachableAgain))
                _unreachable.Remove(id);
        if (fromMemory)
            state = state with { Targets = state.Targets.Where(t => InArea(t, p) && Walkable(t, state, now) &&
                                                                    (t.AttacksMe && !t.Ranged || t.Distance <= t.Reach || !_unreachable.ContainsKey(t.Id) && !AtNoGo(state, t))).ToList() };
        var chosen = state.Targets.Count > 0 ? ChooseTarget(state) : null;
        if (chosen != null)
        {
            target = chosen.Spot;
            best = Math.Sqrt(Math.Pow(chosen.Spot.X - cx, 2) + Math.Pow(chosen.Spot.Y - cy, 2));
            priority = chosen.Priority;
        }
        else if (!fromMemory)
        {
            foreach (var m in priority ? state.PriorityMobs : state.MobCandidates)
            {
                double d = Math.Sqrt(Math.Pow(m.X - cx, 2) + Math.Pow(m.Y - cy, 2));
                if (d < best) { best = d; target = m; }
            }
        }
        double radius = p.AttackRadius * state.Height;
        bool nextToUs = chosen != null ? chosen.Distance <= chosen.Reach : best <= radius;
        bool closeBy = chosen != null ? chosen.Distance <= chosen.Reach * 2 : best <= radius * 2;

        // A saved farm spot: walk there first (the area limit starts from there).
        if (TravelTick(state, p, actions, refresh))
            return;

        // A special item on the ground ("Topla"): straight to it, even with mobs hitting us — it is
        // worth more than the fight, and they follow anyway.
        if (state.Drops.FirstOrDefault(d => d.Special && !_ignoredDrops.ContainsKey(d.Id)) is { Id: not 0 } special &&
            GoForDrop(state, p, actions, refresh, radius, special))
        {
            if (!_specialNoted)
                actions.Add(new NoteAction("Özel eşya yerde: saldırı bırakılıp alınıyor."));
            _specialNoted = true;
            FinishFight(p, actions, now);
            return;
        }
        _specialNoted = false;

        // Stay in the area we started in (position from memory): walking back when too far.
        // Mobs hitting us close by are fought first: they would only follow and hit us on the way.
        bool hitClose = state.Targets.Any(m => m.AttacksMe && m.Distance <= HitCloseRange);
        if (LeashTick(state, p, actions, refresh, mobNextToUs: nextToUs || hitClose))
            return;

        // Loot lying close by first, when nothing is after us and no gathering is going on: walking
        // off to the next group left it behind (seen: yang 1 m away never picked up).
        if (!_pulling && !state.Targets.Any(t => t.AttacksMe) && GoForDrop(state, p, actions, refresh, radius))
        {
            FinishFight(p, actions, now);
            return;
        }

        // Gather the spawn groups around first, then fight them together.
        if (PullTick(state, p, actions, refresh, radius))
            return;

        if (target is not Point t)
        {
            SetAttack(false, p, actions, refresh);
            FinishFight(p, actions, now);
            if (GoForDrop(state, p, actions, refresh, radius))
                return;
            // Names flicker for a moment while walking; only explore when they are really gone.
            if (now - _lastSeen < Ms(800))
            {
                SetMoves([], p, actions, refresh);
                return;
            }
            // Where the mobs were seen most (from memory): go there instead of zigzagging.
            if (state.Hotspot is Vec3 spot && state.Position is Vec3 me && state.Camera is CameraView camera)
            {
                var (right, up) = camera.ToScreenDirection(spot.X - me.X, spot.Y - me.Y);
                double length = Math.Sqrt(right * right + up * up);
                if (length > 0)
                {
                    if (!_toHotspot)
                        actions.Add(new NoteAction("Etrafta mob yok; mobların çok olduğu yere gidiliyor."));
                    _toHotspot = true;
                    SetMoves(Around(state, right / length * 100, -up / length * 100, 38, p, to: spot), p, actions, refresh);
                    CheckBlocked(state, p, actions);
                    if (Phase != BotPhase.Searching)
                        Enter(BotPhase.Searching, now);
                    return;
                }
            }
            _toHotspot = false;
            if (now >= _exploreUntil)
            {
                // Zigzag forward (W, D, W, A, ...) to look around; names that show up meanwhile
                // are picked up on the very next tick. The camera is never turned (E): the
                // area check relies on its direction staying the same.
                _exploreStep++;
                var keys = new[] { p.Keys.MoveForward, p.Keys.MoveRight, p.Keys.MoveForward, p.Keys.MoveLeft };
                _exploreKeys = [keys[(_exploreStep - 1) % keys.Length]];
                _exploreUntil = now + Ms(p.ExploreWalkMs);
            }
            SetMoves(_exploreKeys, p, actions, refresh);
            if (CheckBlocked(state, p, actions))
                _exploreUntil = now; // pick another direction next time
            if (Phase != BotPhase.Searching)
                Enter(BotPhase.Searching, now);
            return;
        }

        _lastSeen = now;
        _toHotspot = false;
        _exploreStep = 0;
        _exploreUntil = Never;
        double dx = t.X - cx, dy = t.Y - cy;
        // Once attacking, keep at it a bit further out so it doesn't flip between walk and attack.
        // From memory: walk in to a little inside the reach, keep hitting up to 1.4 × the reach.
        bool inRange = chosen != null
            ? chosen.Distance <= chosen.Reach - 10 || (Phase == BotPhase.Fighting && chosen.Distance <= chosen.Reach * 1.4)
            : best <= radius || (Phase == BotPhase.Fighting && best <= radius * 1.4);
        if (inRange)
        {
            // Next to the character: attack. Walking while hitting halves the damage (measured: 96
            // HP/s walking vs 172 standing, the mob in reach), so the keys are let go while the mob
            // is within reach; steered onto it only when it is at the edge or no damage is done.
            SetMoves(SteerInReach(state, chosen, now) ? Towards(dx, dy, radius * 0.35, p) : [], p, actions, refresh);
            SetAttack(true, p, actions, refresh);
            PickUpUnderFeet(state, p, actions, radius);
            if (Phase != BotPhase.Fighting)
                Enter(BotPhase.Fighting, now);
            _wasFighting = true;
            _movingSince = Never;
            return;
        }

        // Not next to anything: first the loot lying close by, then the next mob.
        if (!priority)
        {
            SetAttack(false, p, actions, refresh);
            if (GoForDrop(state, p, actions, refresh, radius))
            {
                FinishFight(p, actions, now);
                return;
            }
        }

        // Head for the crowd if there is one worth it, else the nearest.
        _crowdTarget = null;
        if (!priority && p.PreferCrowds && !closeBy && PickCrowd(state, p, cx, cy) is Point crowd)
        {
            _crowdTarget = crowd;
            dx = crowd.X - cx;
            dy = crowd.Y - cy;
        }

        // Not getting any closer to it (behind a wall, a rock, on a ledge): leave it for a while.
        if (chosen != null && !chosen.AttacksMe && NotGettingCloser(chosen, now))
        {
            _unreachable[chosen.Id] = now + Ms(UnreachableForMs);
            GaveUpAt(state, chosen.Position);
            StuckHere(state);
            actions.Add(new NoteAction($"{(chosen.Priority ? "Metin taşına" : "Moba")} {ApproachGiveUpMs / 1000} sn'dir yaklaşılamıyor; bir süre bırakıldı."));
            return;
        }

        // Walk towards it: W up, S down, A left, D right (both for diagonals).
        _walkGoal = chosen?.Position;
        SetAttack(false, p, actions, refresh);
        FinishFight(p, actions, now);
        // Around the other mobs on the way (the one gone for doesn't count).
        int goal = chosen?.Id ?? 0;
        SetMoves(Around(state, dx, dy, radius * 0.5, p, m => m.Id == goal, chosen?.Position), p, actions, refresh);
        if (Phase != BotPhase.Moving)
        {
            Enter(BotPhase.Moving, now);
            _movingSince = now;
        }
        if (CheckBlocked(state, p, actions, mobClose: closeBy))
            return;
        if (_movingSince != Never && now - _movingSince >= Ms(p.MoveStuckMs))
        {
            // The mob can't be reached: walk the other way for a moment and look elsewhere.
            StuckHere(state);
            if (chosen != null)
                GaveUpAt(state, chosen.Position);
            var away = Towards(-dx, -dy, radius * 0.5, p);
            actions.Add(new NoteAction("Moba ulaşılamıyor; başka yere bakılıyor."));
            StepAside(away, now, 1000);
            SetMoves(away, p, actions, refresh);
            _movingSince = Never;
            Enter(BotPhase.Searching, now);
        }
    }

    /// <summary>The farm spot was moved (another player took the old one): walk to the new one.</summary>
    internal void NewFarmSpot()
    {
        _atFarmSpot = _travelNoted = _travelLate = false;
        Traveling = false;
        _travelSince = Never;
        ResetLeash();
        ResetPull();
    }

    /// <summary>Walking to a mob or stone without getting 1 m closer for this long: it can't be reached.</summary>
    internal const int ApproachGiveUpMs = 8000;
    /// <summary>One that couldn't be reached is left alone this long.</summary>
    internal const int UnreachableForMs = 120_000;
    private readonly Dictionary<int, TimeSpan> _unreachable = [];
    /// <summary>A mob left as unreachable this close (× its reach) is reachable again.</summary>
    internal const double ReachableAgain = 1.5;
    private int _approachId;
    private double _approachBest;
    private TimeSpan _approachSince = Never, _approachLast = Never;

    /// <summary>
    /// Measured on the real distance (not on the walk timers, which a step aside resets: seen walking
    /// 70 s around a rock towards a Metin stone): no metre closer to it in 8 s.
    /// </summary>
    private bool NotGettingCloser(TargetInfo target, TimeSpan now)
    {
        // Another one, or not walked to just now (fighting meanwhile): start measuring again.
        bool fresh = target.Id != _approachId || _approachSince == Never || now - _approachLast > Ms(1500);
        _approachLast = now;
        if (fresh || target.Distance < _approachBest - 100)
        {
            _approachId = target.Id;
            _approachBest = Math.Min(fresh ? double.MaxValue : _approachBest, target.Distance);
            _approachSince = now;
            return false;
        }
        if (now - _approachSince < Ms(ApproachGiveUpMs))
            return false;
        _approachId = 0;
        _approachSince = Never;
        return true;
    }

    private int _targetId;
    private bool _toHotspot;
    private bool _atFarmSpot, _travelNoted;
    private TimeSpan _travelSince = Never;
    private double _travelLimitMs;
    private bool _travelLate;

    /// <summary>On the way to the saved farm spot (the engine doesn't open the M map meanwhile).</summary>
    internal bool Traveling { get; private set; }

    /// <summary>The walk to the saved farm spot isn't over yet: nothing else comes first.</summary>
    private bool TravelPending(GameState state, BotProfile p) =>
        p.NavigateMode && !_atFarmSpot && state.FarmSpot is Vec3 && state.Position is Vec3 && state.Follow == null;

    /// <summary>How long walking to the farm spot may take: 2 minutes, and a minute more for every 100 m.</summary>
    internal static double TravelLimitMs(double distance) => 120_000 + distance / 10000 * 60_000;

    /// <summary>
    /// Walks to the saved farm spot before farming starts (mobs met on the way follow and are
    /// fought there). Whatever happens — attacked, HP low, a long way — it goes on until within 5 m of it
    /// (the user: "ne olursa olsun kayıtlı farm alanına gidecek"); running away, breaks, emptying the bag
    /// and waiting for HP wait until then (see <see cref="TravelPending"/>). Potions are still drunk on the way.
    /// </summary>
    /// <returns>True while walking there.</returns>
    private bool TravelTick(GameState state, BotProfile p, List<BotAction> actions, bool refresh)
    {
        if (_atFarmSpot || state.FarmSpot is not Vec3 spot || state.Position is not Vec3 me || state.Camera is not CameraView camera)
            return false;
        var now = state.Time;
        double distance = me.DistanceXY(spot);
        if (_travelSince == Never)
        {
            _travelSince = now;
            _travelLimitMs = TravelLimitMs(distance);
        }
        if (distance <= 500)
        {
            _atFarmSpot = true;
            Traveling = false;
            if (_travelNoted)
                actions.Add(new NoteAction("Farm alanına varıldı; farm başlıyor."));
            return false;
        }
        if (!_travelLate && now - _travelSince > Ms(_travelLimitMs))
        {
            _travelLate = true;
            actions.Add(new NoteAction($"Farm alanına {_travelLimitMs / 60_000:0} dakikadır gidiliyor, {distance / 100:0} m kaldı; gitmeye devam ediliyor."));
        }
        Traveling = true;
        if (!_travelNoted)
        {
            _travelNoted = true;
            actions.Add(new NoteAction($"Kayıtlı farm alanına gidiliyor ({distance / 100:0} m)."));
        }
        var (right, up) = camera.ToScreenDirection(spot.X - me.X, spot.Y - me.Y);
        double length = Math.Max(1e-6, Math.Sqrt(right * right + up * up));
        SetAttack(false, p, actions, refresh);
        SetMoves(Around(state, right / length * 100, -up / length * 100, 38, p, to: spot), p, actions, refresh);
        if (Phase != BotPhase.Moving)
            Enter(BotPhase.Moving, now);
        _movingSince = Never;
        CheckBlocked(state, p, actions);
        return true;
    }
    /// <summary>Mobs attacking us this close (world units, 5 m) are fought before walking back into the area.</summary>
    internal const double HitCloseRange = 500;
    /// <summary>Mobs within this (world units, 8 m) are chosen before any further away.</summary>
    internal const double NearbyRange = 800;

    /// <summary>
    /// Picks the mob worth most per second: the experience it gives ÷ (time to walk there + time
    /// to kill it + 1 s). A damaged mob dies sooner, so it scores higher; the one being fought is
    /// finished first when it's hurt and close; another one has to be clearly better (×1.3) to
    /// switch to. With low HP, mobs that hit hard count less. Mobs attacking us (or preferred
    /// Metin stones) are chosen among themselves first.
    /// </summary>
    private TargetInfo ChooseTarget(GameState state)
    {
        var pool = state.Targets.Any(t => t.Priority) ? state.Targets.Where(t => t.Priority).ToList() : state.Targets.ToList();
        // Mobs close by come first: walking across the field for a slightly better one lost more
        // than it won (seen in the game: 15 s of walking past mobs). Far ones only when none is near.
        var nearby = pool.Where(t => t.Distance <= NearbyRange).ToList();
        if (nearby.Count > 0)
            pool = nearby;
        double dps = Math.Max(50, state.AttackPower); // roughly: the attack value per second
        double Score(TargetInfo t)
        {
            // Walking runs at ~350 units/s (measured), but with stops and mobs moving it's about 200.
            double walk = Math.Max(0, t.Distance - t.Reach) / 200;
            double kill = t.Hp / dps;
            double exp = t.Exp > 0 ? t.Exp : t.MaxHp * 0.3;
            double score = exp / (walk + kill + 1);
            if (state.Hp is < 0.4 && t.DamageMax > 0)
                score /= 1 + t.DamageMax / 100.0;
            return score;
        }
        var current = pool.FirstOrDefault(t => t.Id == _targetId);
        // Finish what we started — but a hurt mob knocked back out of reach (area skills push
        // mobs away; seen in the game) isn't walked after while another one can be hit right here:
        // it comes back by itself.
        bool otherInReach = pool.Any(t => t.Id != _targetId && t.Distance <= t.Reach);
        if (current != null && current.Hp < current.MaxHp &&
            (current.Distance <= current.Reach * 1.4 || current.Distance <= current.Reach * 2.5 && !otherInReach))
            return current;
        if (current != null && current.Distance > current.Reach * 1.4 && otherInReach)
        {
            var near = pool.Where(t => t.Distance <= t.Reach).MaxBy(Score)!;
            _targetId = near.Id;
            return near;
        }
        // Mobs we can hit right here come first: one a little better behind them made the bot
        // walk around the others to it (they block the way), hitting nothing meanwhile.
        var inReach = pool.Where(t => t.Distance <= t.Reach).ToList();
        if (inReach.Count > 0)
        {
            pool = inReach;
            if (current != null && !inReach.Contains(current))
                current = null;
        }
        // Nothing to hit here: an archer attacking us is walked to first. The ones fighting up
        // close come to us by themselves; an archer stays 3 m off and shoots (recorded: two thirds of
        // the last mobs of a fight were archers the bot ended up walking after one by one).
        else if (pool.Where(t => t.Ranged && t.AttacksMe).ToList() is { Count: > 0 } archers)
        {
            pool = archers;
            if (current != null && !archers.Contains(current))
                current = null;
        }
        var best = pool.MaxBy(Score)!;
        if (current != null && Score(best) < Score(current) * 1.3)
            best = current;
        _targetId = best.Id;
        return best;
    }

    private long _dropId;
    private TimeSpan _dropSince = Never, _lastDropTap = Never, _dropTriedAt = Never;
    private bool _specialNoted;
    /// <summary>How long an item has been gone for (only the ticks spent on it count).</summary>
    private TimeSpan _dropTrying;
    private readonly Dictionary<long, TimeSpan> _ignoredDrops = [];

    /// <summary>Going for an item this long without getting it: left for DropRetryMs, then tried again.</summary>
    internal const int DropGiveUpMs = 8000, DropRetryMs = 20000;
    /// <summary>
    /// An item not reached twice is left for good (seen: an item inside a tree, the bot back at it every 20 s,
    /// stuck on the tree); where it lies is a place items aren't gone for (<see cref="BadDropSpotMs"/>, within
    /// <see cref="BadDropSpotRange"/>): another item falling into the same tree is left at once.
    /// </summary>
    internal const int DropTries = 2, BadDropSpotMs = 30 * 60_000;
    internal const double BadDropSpotRange = 150;
    private readonly Dictionary<long, int> _dropFails = [];
    private readonly List<(Vec3 At, TimeSpan Until)> _badDropSpots = [];

    /// <summary>The item couldn't be reached: left for a while, or for good the second time (its place noted).</summary>
    private void DropFailed(GroundSpot drop, TimeSpan now, List<BotAction> actions)
    {
        int fails = _dropFails[drop.Id] = _dropFails.GetValueOrDefault(drop.Id) + 1;
        if (fails < DropTries)
        {
            _ignoredDrops[drop.Id] = now + Ms(DropRetryMs);
            actions.Add(new NoteAction("Yerdeki eşyaya ulaşılamadı; biraz sonra bir kez daha denenecek."));
            return;
        }
        _ignoredDrops[drop.Id] = now + Ms(BadDropSpotMs);
        if (drop.At is Vec3 at)
            _badDropSpots.Add((at, now + Ms(BadDropSpotMs)));
        actions.Add(new NoteAction("Yerdeki eşyaya iki kez ulaşılamadı (ağacın, kayanın içinde olabilir); bırakıldı, o noktaya düşenler de alınmayacak."));
    }

    /// <summary>Items lying where one couldn't be reached before are left at once.</summary>
    private void LeaveBadDropSpots(GameState state)
    {
        var now = state.Time;
        _badDropSpots.RemoveAll(s => now >= s.Until);
        if (_badDropSpots.Count == 0)
            return;
        foreach (var d in state.Drops)
            if (d.At is Vec3 at && !_ignoredDrops.ContainsKey(d.Id) && _badDropSpots.Any(s => s.At.DistanceXY(at) <= BadDropSpotRange))
                _ignoredDrops[d.Id] = now + Ms(BadDropSpotMs);
        if (_dropFails.Count > 200)
            _dropFails.Clear();
    }
    /// <summary>An item the game said isn't ours is left this long (by then it is everyone's, or gone).</summary>
    internal const int NotOursMs = 180_000;
    /// <summary>Gathering only (attack off): someone else's item (the group's killer) is free for everyone
    /// after a while in the game; tried again this long after it fell, then every <see cref="NotOursRetryMs"/>,
    /// for at most <see cref="NotOursWaitMaxMs"/>.</summary>
    internal const int NotOursFreeMs = 32_000, NotOursRetryMs = 10_000, NotOursWaitMaxMs = 180_000;
    /// <summary>When each item on the ground was first seen (its age tells when it is free).</summary>
    private readonly Dictionary<long, TimeSpan> _dropSeenAt = [];
    private readonly HashSet<long> _waitTold = [];

    private void NoteDropAges(GameState state)
    {
        foreach (var d in state.Drops)
            _dropSeenAt.TryAdd(d.Id, state.Time);
        if (_dropSeenAt.Count > 200)
            foreach (var id in _dropSeenAt.Keys.Where(id => !state.Drops.Any(d => d.Id == id)).ToList())
            {
                _dropSeenAt.Remove(id);
                _waitTold.Remove(id);
            }
    }

    /// <summary>The game's own chat lines since the last tick (set from the memory loop).</summary>
    private int _notOursHeard, _skillWaitsHeard;

    /// <summary>The game said (in the chat) an item isn't ours <paramref name="notOurs"/> times and
    /// "WAIT!" (a skill pressed too soon) <paramref name="skillWaits"/> times.</summary>
    public void GameSaid(int notOurs, int skillWaits)
    {
        if (notOurs > 0)
            Interlocked.Add(ref _notOursHeard, notOurs);
        if (skillWaits > 0)
            Interlocked.Add(ref _skillWaitsHeard, skillWaits);
    }

    private void HearGame(GameState state, List<BotAction> actions)
    {
        var now = state.Time;
        if (Interlocked.Exchange(ref _notOursHeard, 0) > 0 && now - _lastDropTap < Ms(4000))
        {
            // The pickup key takes the item closest to the character: that one is someone else's.
            double cx = state.Width / 2.0, cy = state.Height / 2.0;
            var theirs = state.Drops.Where(d => !_ignoredDrops.ContainsKey(d.Id))
                .OrderBy(d => Math.Pow(d.Spot.X - cx, 2) + Math.Pow(d.Spot.Y - cy, 2)).FirstOrDefault();
            if (theirs.Id != 0)
            {
                if (theirs.Id == _dropId)
                    _dropId = 0;
                var fell = _dropSeenAt.TryGetValue(theirs.Id, out var seen) ? seen : now;
                if (!_profile().Attack && now - fell < Ms(NotOursWaitMaxMs))
                {
                    // Gathering behind the group's killer: wait until it is free, then take it.
                    var free = fell + Ms(NotOursFreeMs);
                    _ignoredDrops[theirs.Id] = free > now + Ms(2000) ? free : now + Ms(NotOursRetryMs);
                    if (_waitTold.Add(theirs.Id))
                        actions.Add(new NoteAction($"Yerdeki eşya kesenin (oyun \"toplayamazsın\" dedi); herkese açılınca " +
                                                   $"(yaklaşık {Math.Max(2, (free - now).TotalSeconds):0} sn) alınacak."));
                }
                else
                {
                    _ignoredDrops[theirs.Id] = now + Ms(NotOursMs);
                    actions.Add(new NoteAction("Yerdeki eşya başkasının (oyun \"toplayamazsın\" dedi); bırakıldı."));
                }
            }
        }
        if (Interlocked.Exchange(ref _skillWaitsHeard, 0) > 0)
            SkillTooSoon(now, actions);
    }

    /// <summary>
    /// Walks to the nearest item on the ground and presses the pickup key on it. An item that
    /// can't be picked up within 5 s (someone else's, or the inventory is full) is left alone
    /// for a minute.
    /// </summary>
    /// <returns>True when this tick was spent on loot.</returns>
    /// <summary>Fighting with a wanted item lying right at our feet: keep hitting and press the
    /// pickup key now and then, so it is taken without stopping the fight.</summary>
    private void PickUpUnderFeet(GameState state, BotProfile p, List<BotAction> actions, double radius)
    {
        var now = state.Time;
        if (!p.PickUpDrops || now - _lastDropTap < Ms(700))
            return;
        double cx = state.Width / 2.0, cy = state.Height / 2.0;
        bool close = state.Drops.Any(d => !_ignoredDrops.ContainsKey(d.Id) && CleanPick(state, d) &&
                                          Math.Sqrt(Math.Pow(d.Spot.X - cx, 2) + Math.Pow(d.Spot.Y - cy, 2)) <= radius * 0.6);
        if (!close)
            return;
        actions.Add(new KeyTapAction(p.Keys.Pickup));
        _lastDropTap = now;
    }

    private bool GoForDrop(GameState state, BotProfile p, List<BotAction> actions, bool refresh, double radius, GroundSpot? only = null)
    {
        var now = state.Time;
        foreach (var (id, until) in _ignoredDrops.ToList())
            if (now >= until)
                _ignoredDrops.Remove(id);
        var drop = only ?? state.Drops.FirstOrDefault(d => !_ignoredDrops.ContainsKey(d.Id));
        if (!p.PickUpDrops || drop.Id == 0)
            return false;
        // Only the time actually spent going for it counts (a fight in between doesn't use it up);
        // a special item gets longer (it may be far, mobs may be in the way).
        if (drop.Id != _dropId)
        {
            // Behind a wall or a rock with no way round (the map's obstacles): not walked to at all.
            if (!DropReachable(state, drop))
            {
                _ignoredDrops[drop.Id] = now + Ms(DropRetryMs);
                actions.Add(new NoteAction("Yerdeki eşyaya yol yok (duvar ya da kaya arkasında); bırakıldı."));
                return false;
            }
            _dropId = drop.Id;
            _dropSince = now;
            _dropTrying = TimeSpan.Zero;
        }
        else if (now - _dropTriedAt < Ms(1000))
            _dropTrying += now - _dropTriedAt;
        _dropTriedAt = now;
        // Someone else's item is told by the game ("toplayamazsın", see HearGame). One that still
        // isn't taken after a while (something in the way): left for a little, then tried again.
        if (_dropTrying > Ms(drop.Special ? 20000 : DropGiveUpMs))
        {
            DropFailed(drop, now, actions);
            return false;
        }
        // Nothing to hit here: let go of the attack key, straight to the item.
        SetAttack(false, p, actions, refresh);
        double dx = drop.Spot.X - state.Width / 2.0, dy = drop.Spot.Y - state.Height / 2.0;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        // An unwanted item lying closer would be taken instead: step right onto the wanted one first
        // (standing on it, it is pressed anyway).
        bool junkCloser = JunkCloser(state, drop.Spot);
        if (junkCloser && (_junkStepDrop != drop.Id || _junkStepSince == Never))
        {
            _junkStepDrop = drop.Id;
            _junkStepSince = now;
        }
        else if (!junkCloser)
            _junkStepSince = Never;
        // Couldn't get closer to it than the unwanted one, or junk lies right on it: a special item is taken with
        // the junk (thrown away later); an ordinary one is left — not worth filling the bag with junk (the user:
        // daggers, fans and bows came in this way, and the bag was emptied every 7 minutes).
        if (junkCloser && now - _junkStepSince > Ms(JunkStepMaxMs) || !junkCloser && JunkOnTop(state, drop.Spot))
        {
            if (!drop.Special)
            {
                _ignoredDrops[drop.Id] = now + Ms(DropRetryMs);
                _junkStepSince = Never;
                SetMoves([], p, actions, refresh);
                return false;
            }
            junkCloser = false;
        }
        if (junkCloser && distance > radius * 0.08)
        {
            SetMoves(Towards(dx, dy, radius * 0.04, p), p, actions, refresh);
        }
        else if (distance <= radius * 0.5)
        {
            SetMoves([], p, actions, refresh);
            if (now - _lastDropTap >= Ms(400))
            {
                actions.Add(new KeyTapAction(p.Keys.Pickup));
                _lastDropTap = now;
            }
        }
        else
        {
            SetMoves(Around(state, dx, dy, radius * 0.25, p, to: drop.At), p, actions, refresh);
        }
        if (Phase != BotPhase.Looting)
            Enter(BotPhase.Looting, now);
        _movingSince = Never;
        return true;
    }

    /// <summary>
    /// The best group of mobs to walk into: for every mob, the mobs within CrowdRadius of it
    /// form a group; groups with at least CrowdMinMobs score count ÷ (1 + distance / ¼ screen),
    /// so a big group close by wins and a far one has to be much bigger. The group chosen last
    /// time is kept unless another scores clearly better (×1.3), so the bot doesn't zigzag.
    /// </summary>
    /// <returns>The group's centre, or null when no group is big enough.</returns>
    private Point? PickCrowd(GameState state, BotProfile p, double cx, double cy)
    {
        var mobs = state.MobCandidates;
        double reach = p.CrowdRadius * state.Height, scale = state.Height * 0.25;
        Point? bestCentre = null, keptCentre = null;
        double bestScore = 0, keptScore = 0;
        foreach (var m in mobs)
        {
            var group = mobs.Where(o => Math.Sqrt(Math.Pow(o.X - m.X, 2) + Math.Pow(o.Y - m.Y, 2)) <= reach).ToList();
            if (group.Count < p.CrowdMinMobs)
                continue;
            var centre = new Point((int)group.Average(o => o.X), (int)group.Average(o => o.Y));
            double distance = Math.Sqrt(Math.Pow(centre.X - cx, 2) + Math.Pow(centre.Y - cy, 2));
            double score = group.Count / (1 + distance / scale);
            if (score > bestScore) { bestScore = score; bestCentre = centre; }
            if (_lastCrowd is Point last && Math.Abs(centre.X - last.X) + Math.Abs(centre.Y - last.Y) < reach && score > keptScore)
            {
                keptScore = score;
                keptCentre = centre;
            }
        }
        var chosen = keptCentre is not null && keptScore * 1.3 >= bestScore ? keptCentre : bestCentre;
        _lastCrowd = chosen;
        return chosen;
    }

    /// <summary>W up, S down, A left, D right (two keys for diagonals); nothing inside the dead zone.</summary>
    /// <summary>A mob this close to the way ahead (world units, 2.5 m) is walked around.</summary>
    internal const double AvoidClearance = 250;
    /// <summary>How far ahead mobs in the way are looked for (world units, 7 m).</summary>
    internal const double AvoidAhead = 700;

    /// <summary>
    /// The direction to walk (screen) bent around the mobs standing in the way, so they aren't
    /// woken by walking into them (seen: the bot walked right into a mob on its way back to the
    /// area). Every mob position is known from memory: one within 2.5 m of the straight line ahead
    /// (up to 7 m) pushes the way sideways, away from it, the more the closer it is. Mobs already
    /// after us and <paramref name="goingFor"/> (the mobs being walked to) don't count.
    /// </summary>
    internal static (double X, double Y) AroundMobs(GameState state, double dx, double dy, Func<TargetInfo, bool>? goingFor = null,
        Func<int, int, int>? side = null)
    {
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6 || state.Targets.Count == 0)
            return (dx, dy);
        double ux = dx / length, uy = dy / length;
        double cx = state.Width / 2.0, cy = state.Height / 2.0;
        double sideX = 0, sideY = 0;
        foreach (var mob in state.Targets)
        {
            if (mob.AttacksMe || goingFor?.Invoke(mob) == true || mob.Distance < 1)
                continue;
            double mx = mob.Spot.X - cx, my = mob.Spot.Y - cy;
            double screen = Math.Sqrt(mx * mx + my * my);
            if (screen < 1)
                continue;
            double perUnit = screen / mob.Distance; // screen pixels per world unit for this mob
            double ahead = (mx * ux + my * uy) / perUnit, across = (mx * -uy + my * ux) / perUnit;
            if (ahead <= 0 || ahead > AvoidAhead || Math.Abs(across) >= AvoidClearance)
                continue;
            // Away from the mob's side (straight ahead: to the right); stronger the closer it is.
            double away = across > 0 ? -1 : 1;
            // The side chosen for a mob is kept for a moment (else it flips left / right as we walk).
            if (side != null)
                away = side(mob.Id, (int)away);
            double weight = (1 - Math.Abs(across) / AvoidClearance) * (1 - ahead / AvoidAhead * 0.5) * 1.6;
            sideX += -uy * away * weight;
            sideY += ux * away * weight;
        }
        if (sideX == 0 && sideY == 0)
            return (dx, dy);
        return ((ux + sideX) * length, (uy + sideY) * length);
    }

    private HashSet<Keys> Around(GameState state, double dx, double dy, double dead, BotProfile p, Func<TargetInfo, bool>? goingFor = null, Vec3? to = null)
    {
        var now = state.Time;
        // A wall, a house or a rock between us and where we go (the map's obstacles): head for the way round.
        (dx, dy) = TowardsCorner(state, to, dx, dy);
        var (x, y) = AroundMobs(state, dx, dy, goingFor, (id, wanted) =>
        {
            if (_avoidSide.TryGetValue(id, out var kept) && now < kept.Until)
                return kept.Side;
            _avoidSide[id] = (wanted, now + Ms(AvoidSideKeepMs));
            return wanted;
        });
        if (state.Camera is CameraView camera && state.Position is Vec3 me && KnownObstacles(state) is { Count: > 0 } known)
            (x, y) = AroundObstacles(known, me, camera, x, y);
        return Steady(x, y, dead, p);
    }

    /// <summary>Which way a mob in the way is passed (kept this long so the bot doesn't zigzag).</summary>
    internal const int AvoidSideKeepMs = 2000;
    private readonly Dictionary<int, (int Side, TimeSpan Until)> _avoidSide = [];

    /// <summary>
    /// Like <see cref="Towards"/>, but a direction key already held is kept until the way is clearly
    /// inside the dead zone (recorded: keys pressed and let go several times a second at the edge).
    /// </summary>
    private HashSet<Keys> Steady(double dx, double dy, double dead, BotProfile p)
    {
        var keys = Towards(dx, dy, dead, p);
        double keep = dead * 0.4;
        void Hold(Keys key, bool stillThatWay) { if (_heldMoves.Contains(key) && stillThatWay) keys.Add(key); }
        Hold(p.Keys.MoveRight, dx > keep);
        Hold(p.Keys.MoveLeft, dx < -keep);
        Hold(p.Keys.MoveBack, dy > keep);
        Hold(p.Keys.MoveForward, dy < -keep);
        return keys;
    }

    private static HashSet<Keys> Towards(double dx, double dy, double dead, BotProfile p)
    {
        var keys = new HashSet<Keys>();
        if (dx > dead) keys.Add(p.Keys.MoveRight);
        if (dx < -dead) keys.Add(p.Keys.MoveLeft);
        if (dy > dead) keys.Add(p.Keys.MoveBack);
        if (dy < -dead) keys.Add(p.Keys.MoveForward);
        return keys;
    }

    /// <summary>The fight just ended (no mob next to us any more): grab the loot, count it.</summary>
    private int _reachTarget, _reachHp;
    private TimeSpan _reachHpAt = Never, _steerUntil = Never;
    /// <summary>No damage for this long with the mob in reach: step onto it a moment (maybe facing away).</summary>
    internal const int NoDamageSteerMs = 1500;

    /// <summary>
    /// Whether to keep walking onto the mob being hit: not while it is in reach (from memory) and
    /// its HP goes down; yes at the edge of the reach, without memory, or after 1.5 s without damage.
    /// </summary>
    private bool SteerInReach(GameState state, TargetInfo? mob, TimeSpan now)
    {
        if (mob == null || mob.Distance > mob.Reach)
            return true;
        if (mob.Id != _reachTarget || mob.Hp < _reachHp)
        {
            _reachTarget = mob.Id;
            _reachHp = mob.Hp;
            _reachHpAt = now;
            return now < _steerUntil;
        }
        if (now - _reachHpAt > Ms(NoDamageSteerMs))
        {
            _steerUntil = now + Ms(400);
            _reachHpAt = now;
        }
        return now < _steerUntil;
    }

    /// <summary>
    /// Whether an unwanted item lies closer to the character than this wanted one (screen pixels
    /// from the middle; the camera looks down): the pickup key would take that one.
    /// </summary>
    internal static bool JunkCloser(GameState state, Point wanted)
    {
        if (state.Junk.Count == 0)
            return false;
        double cx = state.Width / 2.0, cy = state.Height / 2.0;
        double mine = Math.Sqrt(Math.Pow(wanted.X - cx, 2) + Math.Pow(wanted.Y - cy, 2));
        // One lying on top of the wanted item can't be stepped past (the bot circled there, seen in
        // the game): both are taken, the unwanted one is thrown away later.
        double onTop = JunkOnTopPx * state.Height / 1080.0;
        return state.Junk.Any(j => Math.Sqrt(Math.Pow(j.X - wanted.X, 2) + Math.Pow(j.Y - wanted.Y, 2)) > onTop &&
                                   Math.Sqrt(Math.Pow(j.X - cx, 2) + Math.Pow(j.Y - cy, 2)) <= mine + 4);
    }

    /// <summary>An unwanted item lies right on the wanted one: the pickup key would take it too.</summary>
    internal static bool JunkOnTop(GameState state, Point wanted)
    {
        double onTop = JunkOnTopPx * state.Height / 1080.0;
        return state.Junk.Any(j => Math.Sqrt(Math.Pow(j.X - wanted.X, 2) + Math.Pow(j.Y - wanted.Y, 2)) <= onTop);
    }

    /// <summary>
    /// The pickup key may be pressed for this item: nothing unwanted closer, and — for an ordinary item (yang,
    /// potions, a weapon picked to look at its bonuses) — nothing unwanted lying on it either. A special one
    /// ("Topla", rewards, valuable materials) is worth taking some junk along (thrown away later).
    /// </summary>
    internal static bool CleanPick(GameState state, GroundSpot drop) =>
        !JunkCloser(state, drop.Spot) && (drop.Special || !JunkOnTop(state, drop.Spot));

    /// <summary>An unwanted item this close to the wanted one (screen pixels at 1080 high) lies on top of it.</summary>
    internal const double JunkOnTopPx = 20;
    /// <summary>Stepping onto a wanted item this long with junk still closer: the key is pressed anyway.</summary>
    internal const int JunkStepMaxMs = 2500;
    private TimeSpan _junkStepSince = Never;
    private long _junkStepDrop;

    /// <summary>A wanted item lies around this tick (on the screen, within the loot range) and no unwanted one closer.</summary>
    private bool _wantedDropsAround;

    private void FinishFight(BotProfile p, List<BotAction> actions, TimeSpan now)
    {
        if (!_wasFighting)
            return;
        _wasFighting = false;
        Cycles++;
        // The pickup key takes whatever lies closest, also what the loot choices leave out (seen:
        // cheap armour taken with "only items worth ≥ 5000"): pressed only with a wanted item around.
        // The bot walks to farther items itself.
        if (!p.PickUpDrops || _wantedDropsAround)
        {
            actions.Add(new KeyTapAction(p.Keys.Pickup));
            actions.Add(new KeyTapAction(p.Keys.Pickup));
        }
        _lastLootTap = now;
    }

    private void SetMoves(HashSet<Keys> wanted, BotProfile p, List<BotAction> actions, bool refresh)
    {
        foreach (var key in _heldMoves.Where(k => !wanted.Contains(k)).ToList())
        {
            actions.Add(new KeyUpAction(key));
            _heldMoves.Remove(key);
        }
        foreach (var key in wanted)
            if (_heldMoves.Add(key) || refresh)
                actions.Add(new KeyDownAction(key));
    }

    private void SetAttack(bool on, BotProfile p, List<BotAction> actions, bool refresh)
    {
        if (on && (!_attackHeld || refresh))
        {
            actions.Add(new KeyDownAction(p.Keys.Attack));
            _attackHeld = true;
        }
        else if (!on && _attackHeld)
        {
            actions.Add(new KeyUpAction(p.Keys.Attack));
            _attackHeld = false;
            _attackReleasedAt = _tickTime;
        }
    }
}
