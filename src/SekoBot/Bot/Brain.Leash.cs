using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Staying in the area the bot was started in, and not getting stuck on trees and rocks.
/// <para>
/// Area: now and then (never mid-fight) the big map is opened with M, the yellow arrow is read
/// and the map closed again. The first reading is "home". Right after it the bot walks forward
/// for a moment and reads the map again: the arrow's movement tells which way "screen up"
/// points on the map (the camera is never turned, so this stays valid). When a reading is
/// further than LeashRadius from home, the bot walks back (still fighting mobs that are
/// right next to it) until it is well inside again.
/// </para>
/// <para>
/// Stuck: while W/A/S/D are held the picture should be moving. If it hasn't for BlockedMs,
/// something is in the way: step to the side for a moment (alternating sides) and carry on.
/// </para>
/// </summary>
internal sealed partial class Brain
{
    private enum MapStage { Idle, Opening, Closing }

    private MapStage _mapStage;
    private TimeSpan _mapStageAt = Never, _lastMapCheck = Never, _calibUntil = Never;
    private bool _mapSeenOpen, _closeRetried, _mapNoteShown, _returning;
    private PointF? _home, _calibFrom, _lastPos;
    private (double X, double Y)? _up; // the map direction of "screen up" (unit vector)
    private int _calibDir, _calibFails;
    private HashSet<Keys> _calibKeys = [];

    private TimeSpan _sidestepUntil = Never, _notMovingSince = Never, _movingOkSince = Never;
    private HashSet<Keys> _sidestepKeys = [];
    private int _sidesteps;

    /// <summary>The engine reads the big map only while the bot is looking at it.</summary>
    public bool WantsMap => _mapStage != MapStage.Idle;

    private void ResetLeash()
    {
        _mapStage = MapStage.Idle;
        _mapStageAt = _lastMapCheck = _calibUntil = Never;
        _mapSeenOpen = _closeRetried = _mapNoteShown = _returning = false;
        _home = _calibFrom = _lastPos = null;
        _up = null;
        _calibDir = _calibFails = 0;
        _calibKeys = [];
    }

    private void StepAside(HashSet<Keys> keys, TimeSpan now, int ms)
    {
        _sidestepKeys = keys;
        _sidestepUntil = now + Ms(ms);
    }

    /// <summary>
    /// Call right after setting the walking keys. When the picture hasn't moved for BlockedMs
    /// although we walk, step sideways for SidestepMs.
    /// </summary>
    /// <returns>True when a side step was started.</returns>
    private bool CheckBlocked(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (state.ScreenMoving is not bool moving || _heldMoves.Count == 0)
        {
            _notMovingSince = Never;
            return false;
        }
        if (moving)
        {
            _notMovingSince = Never;
            if (_movingOkSince == Never)
                _movingOkSince = now;
            else if (now - _movingOkSince >= Ms(3000))
                _sidesteps = 0; // walking freely again
            return false;
        }
        _movingOkSince = Never;
        if (_notMovingSince == Never)
        {
            _notMovingSince = now;
            return false;
        }
        if (now - _notMovingSince < Ms(p.BlockedMs))
            return false;

        // Something is in the way: step to the side, the other side next time.
        _sidesteps++;
        bool vertical = _heldMoves.Contains(p.Keys.MoveForward) || _heldMoves.Contains(p.Keys.MoveBack);
        bool first = _sidesteps % 2 == 1;
        var side = vertical ? (first ? p.Keys.MoveRight : p.Keys.MoveLeft) : (first ? p.Keys.MoveForward : p.Keys.MoveBack);
        if (_sidesteps == 1)
            actions.Add(new NoteAction("Bir şeye takıldı; yandan dolanılıyor."));
        StepAside([side], now, p.SidestepMs);
        SetMoves([side], p, actions, refresh: false);
        _notMovingSince = Never;
        return true;
    }

    private void StartMapCheck(GameState state, BotProfile p, List<BotAction> actions)
    {
        SetMoves([], p, actions, refresh: false);
        SetAttack(false, p, actions, refresh: false);
        actions.Add(new KeyTapAction(p.Keys.Map));
        _mapStage = MapStage.Opening;
        _mapStageAt = state.Time;
        _mapSeenOpen = false;
        _lastMapCheck = state.Time;
    }

    /// <returns>True when the area logic drove this tick (map check, calibration walk, walking home).</returns>
    private bool LeashTick(GameState state, BotProfile p, List<BotAction> actions, bool refresh, bool mobNextToUs)
    {
        if (p.LeashRadius <= 0)
            return false;
        var now = state.Time;

        if (_mapStage == MapStage.Opening)
        {
            if (state.MapOpen)
                _mapSeenOpen = true;
            if (state.MapOpen && state.MapPosition is PointF pos && now - _mapStageAt >= Ms(400))
            {
                actions.Add(new KeyTapAction(p.Keys.Map)); // close it again
                _mapStage = MapStage.Closing;
                _mapStageAt = now;
                _closeRetried = false;
                OnMapReading(pos, now, p, actions);
            }
            else if (now - _mapStageAt >= Ms(2000))
            {
                if (_mapSeenOpen)
                {
                    actions.Add(new KeyTapAction(p.Keys.Map)); // open, but no arrow found: close
                    _mapStage = MapStage.Closing;
                    _mapStageAt = now;
                    _closeRetried = false;
                }
                else
                {
                    // Never opened (key missed?): don't press M again now, it would open it.
                    if (!_mapNoteShown)
                        actions.Add(new NoteAction("Harita (M) açılmadı; alan kontrolü sonra tekrar denenecek."));
                    _mapNoteShown = true;
                    _mapStage = MapStage.Idle;
                }
            }
            return true; // stand still while looking at the map
        }
        if (_mapStage == MapStage.Closing)
        {
            if (now - _mapStageAt < Ms(500))
                return true;
            if (state.MapOpen && !_closeRetried)
            {
                actions.Add(new KeyTapAction(p.Keys.Map));
                _closeRetried = true;
                _mapStageAt = now;
                return true;
            }
            _mapStage = MapStage.Idle;
        }

        // Short walk right after a reading, to learn which way "screen up" points on the map.
        if (_calibUntil != Never)
        {
            if (now < _calibUntil)
            {
                SetAttack(false, p, actions, refresh);
                SetMoves(_calibKeys, p, actions, refresh);
                return true;
            }
            _calibUntil = Never;
            StartMapCheck(state, p, actions);
            return true;
        }

        var interval = Ms(_returning ? p.LeashReturnCheckMs : p.LeashCheckMs);
        bool cameraBusy = _timed.Count > 0; // G/F still being held: the map would be in the way
        if ((_lastMapCheck == Never || now - _lastMapCheck >= interval) &&
            !cameraBusy && Phase != BotPhase.Fighting && !mobNextToUs)
        {
            StartMapCheck(state, p, actions);
            return true;
        }

        // Too far from home: walk back (a mob right next to us is fought first).
        if (_returning && !mobNextToUs && _up is (double ux, double uy) && _lastPos is PointF at && _home is PointF home)
        {
            double mx = home.X - at.X, my = home.Y - at.Y;
            double rx = -uy, ry = ux;                // map direction of "screen right"
            double sx = mx * rx + my * ry;           // along screen right
            double sy = -(mx * ux + my * uy);        // screen y grows downwards
            double length = Math.Sqrt(sx * sx + sy * sy);
            SetAttack(false, p, actions, refresh);
            FinishFight(p, actions, now);
            SetMoves(Towards(sx, sy, length * 0.38, p), p, actions, refresh);
            if (Phase != BotPhase.Moving)
                Enter(BotPhase.Moving, now);
            _movingSince = Never; // not chasing a mob, no "can't reach" give-up
            CheckBlocked(state, p, actions);
            return true;
        }
        return false;
    }

    private void OnMapReading(PointF pos, TimeSpan now, BotProfile p, List<BotAction> actions)
    {
        _lastPos = pos;
        if (_home is null)
        {
            _home = pos;
            actions.Add(new NoteAction("Başlangıç yeri kaydedildi; bu alanda kalınacak."));
            BeginCalibration(pos, now, p, 0);
            return;
        }
        if (_calibFrom is PointF from)
        {
            double vx = pos.X - from.X, vy = pos.Y - from.Y, length = Math.Sqrt(vx * vx + vy * vy);
            if (length >= 0.004)
            {
                vx /= length;
                vy /= length;
                // Map vector of the direction we walked → map vector of "screen up".
                _up = _calibDir switch
                {
                    0 => (vx, vy),    // W: up itself
                    1 => (vy, -vx),   // D: right = (-up.y, up.x)
                    2 => (-vx, -vy),  // S: down
                    _ => (-vy, vx),   // A: left
                };
                _calibFrom = null;
                actions.Add(new NoteAction("Harita yönü ayarlandı."));
            }
            else if (++_calibFails < 4)
            {
                BeginCalibration(pos, now, p, _calibFails % 4); // didn't move (blocked?): try another way
            }
            else
            {
                _calibFrom = null;
                actions.Add(new NoteAction("Harita yönü ölçülemedi; alan sınırı kullanılamıyor."));
            }
            return;
        }
        if (_home is PointF h)
        {
            double distance = Math.Sqrt(Math.Pow(pos.X - h.X, 2) + Math.Pow(pos.Y - h.Y, 2));
            if (distance > p.LeashRadius && !_returning)
            {
                _returning = true;
                actions.Add(new NoteAction("Alan sınırı aşıldı; başlangıç yerine dönülüyor."));
            }
            else if (distance < p.LeashRadius * 0.5 && _returning)
            {
                _returning = false;
                actions.Add(new NoteAction("Başlangıç alanına dönüldü."));
            }
        }
    }

    private void BeginCalibration(PointF from, TimeSpan now, BotProfile p, int direction)
    {
        _calibFrom = from;
        _calibDir = direction;
        _calibKeys = [direction switch
        {
            0 => p.Keys.MoveForward,
            1 => p.Keys.MoveRight,
            2 => p.Keys.MoveBack,
            _ => p.Keys.MoveLeft,
        }];
        // The map is being closed for the first 500 ms; walk after that.
        _calibUntil = now + Ms(500 + p.CalibrationWalkMs);
    }
}
