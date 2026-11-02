using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Walk mode: the camera looks straight down (G) and is zoomed out (F), so the character is
/// always in the middle of the screen and every mob shows its red name right on top of it.
/// The bot walks with W/A/S/D towards the nearest name and holds Space once it is next to
/// the character; when nothing is left nearby it picks up loot and walks to the next one.
/// </summary>
internal sealed partial class Brain
{
    private readonly HashSet<Keys> _heldMoves = [];
    private bool _attackHeld, _setupSent, _wasFighting;
    private TimeSpan _busyUntil = Never, _lastSeen = Never, _lastLootTap = Never, _lastKeyRefresh = Never;
    private TimeSpan _movingSince = Never;
    private int _exploreStep;

    private void ResetNavigation()
    {
        _heldMoves.Clear();
        _attackHeld = _setupSent = _wasFighting = false;
        _busyUntil = _lastSeen = _lastLootTap = _lastKeyRefresh = _movingSince = Never;
        _exploreStep = 0;
    }

    private void Navigate(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (Phase == BotPhase.Setup)
        {
            if (!_setupSent)
            {
                // Run one after the other by the input worker: look down, then zoom out.
                actions.Add(new KeyHoldAction(p.Keys.CameraTop, p.CameraSetupHoldMs));
                actions.Add(new KeyHoldAction(p.Keys.ZoomOut, p.CameraSetupHoldMs));
                _setupSent = true;
                _busyUntil = now + Ms(2 * p.CameraSetupHoldMs + 400);
            }
            if (now >= _busyUntil)
                Enter(BotPhase.Searching, now);
            return;
        }
        if (now < _busyUntil)
            return; // an exploring step (key hold) is still running

        // Re-send held keys every second: restores them if the game lost focus meanwhile.
        bool refresh = now - _lastKeyRefresh >= Ms(1000);
        if (refresh)
            _lastKeyRefresh = now;

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
            SetMoves([], p, actions, refresh);
            SetAttack(false, p, actions, refresh);
            FinishFight(p, actions, now);
            // Names flicker for a moment while walking; only explore when they are really gone.
            if (now - _lastSeen < Ms(800))
                return;
            _exploreStep++;
            if (_exploreStep % 2 == 1)
            {
                actions.Add(new KeyHoldAction(p.Keys.MoveForward, p.ExploreWalkMs));
                _busyUntil = now + Ms(p.ExploreWalkMs + 200);
            }
            else
            {
                actions.Add(new KeyHoldAction(p.Keys.RotateCamera, p.RotateHoldMs * 2));
                _busyUntil = now + Ms(p.RotateHoldMs * 2 + 200);
            }
            if (Phase != BotPhase.Searching)
                Enter(BotPhase.Searching, now);
            return;
        }

        _lastSeen = now;
        _exploreStep = 0;
        double dx = t.X - cx, dy = t.Y - cy;
        double radius = p.AttackRadius * state.Height;
        if (best <= radius)
        {
            // Next to the character: stand and attack, pick up loot now and then.
            SetMoves([], p, actions, refresh);
            SetAttack(true, p, actions, refresh);
            if (Phase != BotPhase.Fighting)
                Enter(BotPhase.Fighting, now);
            _wasFighting = true;
            _movingSince = Never;
            if (now - _lastLootTap >= Ms(p.LootEveryMs))
            {
                actions.Add(new KeyTapAction(p.Keys.Pickup));
                _lastLootTap = now;
            }
            return;
        }

        // Walk towards it: W up, S down, A left, D right (both for diagonals).
        SetAttack(false, p, actions, refresh);
        FinishFight(p, actions, now);
        double dead = radius * 0.5;
        var keys = new HashSet<Keys>();
        if (dx > dead) keys.Add(p.Keys.MoveRight);
        if (dx < -dead) keys.Add(p.Keys.MoveLeft);
        if (dy > dead) keys.Add(p.Keys.MoveBack);
        if (dy < -dead) keys.Add(p.Keys.MoveForward);
        SetMoves(keys, p, actions, refresh);
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
            actions.Add(new KeyHoldAction(p.Keys.RotateCamera, p.RotateHoldMs * 2));
            _busyUntil = now + Ms(p.RotateHoldMs * 2 + 200);
            _movingSince = Never;
            Enter(BotPhase.Searching, now);
        }
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
