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
        _atFarmSpot = _travelNoted = false;
        _travelSince = Never;
        ResetPull();
        _dropId = 0;
        _dropSince = _lastDropTap = Never;
        _ignoredDrops.Clear();
        _sidestepUntil = _notMovingSince = _walkStartedAt = _blockCheckedAt = _attackReleasedAt = Never;
        _sidestepKeys = [];
        _sidesteps = 0;
        ResetLeash();
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

    /// <summary>Look straight down (G), then zoom out (F) — while the bot keeps walking and fighting.</summary>
    private void ScheduleCamera(BotProfile p, TimeSpan now)
    {
        _timed.RemoveAll(t => !t.Down && (t.Key == p.Keys.CameraTop || t.Key == p.Keys.ZoomOut));
        PressFor(p.Keys.CameraTop, now, p.CameraSetupHoldMs);
        PressFor(p.Keys.ZoomOut, now + Ms(p.CameraSetupHoldMs), p.CameraSetupHoldMs);
        _lastCameraSetup = now;
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
            ScheduleCamera(p, now); // the camera can drift (a stray key, a cutscene)
        }
        RunTimedKeys(now, actions, refresh);
        if (now - _lastLootTap >= Ms(p.LootEveryMs * _lootJitter))
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
        if (fromMemory)
            state = state with { Targets = state.Targets.Where(t => InArea(t, p)).ToList() };
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

        // Stay in the area we started in (position from memory): walking back when too far.
        // Mobs hitting us close by are fought first: they would only follow and hit us on the way.
        bool hitClose = state.Targets.Any(m => m.AttacksMe && m.Distance <= HitCloseRange);
        if (LeashTick(state, p, actions, refresh, mobNextToUs: nextToUs || hitClose))
            return;

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
                    SetMoves(Towards(right / length * 100, -up / length * 100, 38, p), p, actions, refresh);
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
            // Next to the character: attack, and keep steering onto the mob, because every
            // swing moves the character a little and the mob moves too.
            SetMoves(Towards(dx, dy, radius * 0.35, p), p, actions, refresh);
            SetAttack(true, p, actions, refresh);
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

        // Walk towards it: W up, S down, A left, D right (both for diagonals).
        SetAttack(false, p, actions, refresh);
        FinishFight(p, actions, now);
        SetMoves(Towards(dx, dy, radius * 0.5, p), p, actions, refresh);
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
            var away = Towards(-dx, -dy, radius * 0.5, p);
            actions.Add(new NoteAction("Moba ulaşılamıyor; başka yere bakılıyor."));
            StepAside(away, now, 1000);
            SetMoves(away, p, actions, refresh);
            _movingSince = Never;
            Enter(BotPhase.Searching, now);
        }
    }

    private int _targetId;
    private bool _toHotspot;
    private bool _atFarmSpot, _travelNoted;
    private TimeSpan _travelSince = Never;

    /// <summary>
    /// Walks to the saved farm spot before farming starts (mobs met on the way follow and are
    /// fought there). Ends within 5 m of it, after 2 minutes, or when HP falls below half.
    /// </summary>
    /// <returns>True while walking there.</returns>
    private bool TravelTick(GameState state, BotProfile p, List<BotAction> actions, bool refresh)
    {
        if (_atFarmSpot || state.FarmSpot is not Vec3 spot || state.Position is not Vec3 me || state.Camera is not CameraView camera)
            return false;
        var now = state.Time;
        if (_travelSince == Never)
            _travelSince = now;
        double distance = me.DistanceXY(spot);
        string? done = distance <= 500 ? "Farm alanına varıldı; farm başlıyor."
            : state.Hp is < 0.5 ? "Can azaldı; farm burada başlıyor."
            : now - _travelSince > Ms(120_000) ? "Farm alanına 2 dakikada varılamadı; burada başlanıyor."
            : null;
        if (done != null)
        {
            _atFarmSpot = true;
            if (_travelNoted)
                actions.Add(new NoteAction(done));
            return false;
        }
        if (!_travelNoted)
        {
            _travelNoted = true;
            actions.Add(new NoteAction($"Kayıtlı farm alanına gidiliyor ({distance / 100:0} m)."));
        }
        var (right, up) = camera.ToScreenDirection(spot.X - me.X, spot.Y - me.Y);
        double length = Math.Max(1e-6, Math.Sqrt(right * right + up * up));
        SetAttack(false, p, actions, refresh);
        SetMoves(Towards(right / length * 100, -up / length * 100, 38, p), p, actions, refresh);
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
        var best = pool.MaxBy(Score)!;
        if (current != null && Score(best) < Score(current) * 1.3)
            best = current;
        _targetId = best.Id;
        return best;
    }

    private long _dropId;
    private TimeSpan _dropSince = Never, _lastDropTap = Never;
    private readonly Dictionary<long, TimeSpan> _ignoredDrops = [];

    /// <summary>
    /// Walks to the nearest item on the ground and presses the pickup key on it. An item that
    /// can't be picked up within 5 s (someone else's, or the inventory is full) is left alone
    /// for a minute.
    /// </summary>
    /// <returns>True when this tick was spent on loot.</returns>
    private bool GoForDrop(GameState state, BotProfile p, List<BotAction> actions, bool refresh, double radius)
    {
        var now = state.Time;
        foreach (var (id, until) in _ignoredDrops.ToList())
            if (now >= until)
                _ignoredDrops.Remove(id);
        var drop = state.Drops.FirstOrDefault(d => !_ignoredDrops.ContainsKey(d.Id));
        if (!p.PickUpDrops || drop.Id == 0)
            return false;
        if (drop.Id != _dropId)
        {
            _dropId = drop.Id;
            _dropSince = now;
        }
        else if (now - _dropSince > Ms(5000))
        {
            _ignoredDrops[drop.Id] = now + Ms(60000);
            actions.Add(new NoteAction("Yerdeki eşya alınamadı (başkasının olabilir); bırakıldı."));
            return false;
        }
        double dx = drop.Spot.X - state.Width / 2.0, dy = drop.Spot.Y - state.Height / 2.0;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= radius * 0.5)
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
            SetMoves(Towards(dx, dy, radius * 0.25, p), p, actions, refresh);
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
    private void FinishFight(BotProfile p, List<BotAction> actions, TimeSpan now)
    {
        if (!_wasFighting)
            return;
        _wasFighting = false;
        Cycles++;
        actions.Add(new KeyTapAction(p.Keys.Pickup));
        actions.Add(new KeyTapAction(p.Keys.Pickup));
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
