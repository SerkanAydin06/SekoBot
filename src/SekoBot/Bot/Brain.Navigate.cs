using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Walk mode: the camera looks straight down (G) and is zoomed out (F), so the character is
/// always in the middle of the screen and every mob shows its red name right on top of it.
/// The bot walks with W/A/S/D towards the nearest name and holds Space once it is next to
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
    private TimeSpan _pauseUntil = Never, _lastSeen = Never, _lastLootTap = Never, _lastKeyRefresh = Never;
    private TimeSpan _movingSince = Never, _lastCameraSetup = Never, _exploreUntil = Never;
    private HashSet<Keys> _exploreKeys = [];
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
        if (now - _lastLootTap >= Ms(p.LootEveryMs))
        {
            actions.Add(new KeyTapAction(p.Keys.Pickup));
            _lastLootTap = now;
        }
        if (now < _pauseUntil)
            return; // just gave up on an unreachable mob: let the turn happen first

        double cx = state.Width / 2.0, cy = state.Height / 2.0;
        Point? target = null;
        double best = double.MaxValue;
        foreach (var m in state.MobCandidates)
        {
            double d = Math.Sqrt(Math.Pow(m.X - cx, 2) + Math.Pow(m.Y - cy, 2));
            if (d < best) { best = d; target = m; }
        }

        if (target is not Point t)
        {
            SetAttack(false, p, actions, refresh);
            FinishFight(p, actions, now);
            // Names flicker for a moment while walking; only explore when they are really gone.
            if (now - _lastSeen < Ms(800))
            {
                SetMoves([], p, actions, refresh);
                return;
            }
            if (now >= _exploreUntil)
            {
                // Walk forward a bit, then turn the camera, and so on — names that show up
                // meanwhile are picked up on the very next tick.
                _exploreStep++;
                if (_exploreStep % 2 == 1)
                {
                    _exploreKeys = [p.Keys.MoveForward];
                    _exploreUntil = now + Ms(p.ExploreWalkMs);
                }
                else
                {
                    _exploreKeys = [];
                    PressFor(p.Keys.RotateCamera, now, p.RotateHoldMs * 2);
                    _exploreUntil = now + Ms(p.RotateHoldMs * 2 + 200);
                }
            }
            SetMoves(_exploreKeys, p, actions, refresh);
            if (Phase != BotPhase.Searching)
                Enter(BotPhase.Searching, now);
            return;
        }

        _lastSeen = now;
        _exploreStep = 0;
        _exploreUntil = Never;
        double dx = t.X - cx, dy = t.Y - cy;
        double radius = p.AttackRadius * state.Height;
        // Once attacking, keep at it a bit further out so it doesn't flip between walk and attack.
        bool inRange = best <= radius || (Phase == BotPhase.Fighting && best <= radius * 1.4);
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

        // Walk towards it: W up, S down, A left, D right (both for diagonals).
        SetAttack(false, p, actions, refresh);
        FinishFight(p, actions, now);
        SetMoves(Towards(dx, dy, radius * 0.5, p), p, actions, refresh);
        if (Phase != BotPhase.Moving)
        {
            Enter(BotPhase.Moving, now);
            _movingSince = now;
        }
        if (_movingSince != Never && now - _movingSince >= Ms(p.MoveStuckMs))
        {
            // Walking into a wall or the mob can't be reached: let go and look elsewhere.
            SetMoves([], p, actions, refresh);
            actions.Add(new NoteAction("Moba ulaşılamıyor; başka yere bakılıyor."));
            PressFor(p.Keys.RotateCamera, now, p.RotateHoldMs * 2);
            _pauseUntil = now + Ms(p.RotateHoldMs * 2 + 200);
            _movingSince = Never;
            Enter(BotPhase.Searching, now);
        }
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
        }
    }
}
