using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Staying in the area the bot was started in, and not getting stuck on trees and rocks.
/// <para>
/// Area: the character's position comes from memory. The first position is "home"; when the
/// character is further than LeashMeters from it, the bot walks back (still fighting mobs that are
/// right next to it) until it is well inside again. The camera from memory tells which keys walk
/// towards home.
/// </para>
/// <para>
/// Stuck: while W/A/S/D are held the character should be moving. If it hasn't for BlockedMs,
/// something is in the way: step to the side for a moment (alternating sides) and carry on.
/// </para>
/// </summary>
internal sealed partial class Brain
{
    private Vec3? _home;
    private bool _returning;

    private TimeSpan _sidestepUntil = Never, _notMovingSince = Never, _movingOkSince = Never;
    /// <summary>When walking (re)started and when the attack key was let go: the character needs
    /// a moment to get going (it finishes its swing first), that's not being stuck.</summary>
    private TimeSpan _walkStartedAt = Never, _blockCheckedAt = Never, _attackReleasedAt = Never;
    private HashSet<Keys> _sidestepKeys = [];
    private int _sidesteps;

    /// <summary>Short text for the UI: where the bot is relative to its starting area.</summary>
    public string AreaStatus =>
        _profile().LeashMeters <= 0 || !_profile().NavigateMode ? "Alan sınırı kapalı"
        : _home == null ? "Başlangıç noktası alınıyor"
        : _returning ? "Alana geri dönülüyor"
        : "Alanın içinde";

    private void ResetLeash()
    {
        _home = null;
        _returning = false;
    }

    private void StepAside(HashSet<Keys> keys, TimeSpan now, int ms)
    {
        _sidestepKeys = keys;
        _sidestepUntil = now + Ms(ms);
    }

    /// <summary>
    /// Call right after setting the walking keys. When the character hasn't moved for BlockedMs
    /// although we walk, step sideways for SidestepMs.
    /// </summary>
    /// <param name="mobClose">A mob is right in front of us: walking into it isn't being stuck.</param>
    /// <returns>True when a side step was started.</returns>
    private bool CheckBlocked(GameState state, BotProfile p, List<BotAction> actions, bool mobClose = false)
    {
        var now = state.Time;
        if (_blockCheckedAt == Never || now - _blockCheckedAt > Ms(300))
            _walkStartedAt = now; // not walking a moment ago: just started
        _blockCheckedAt = now;
        bool settling = now - _walkStartedAt < Ms(1000) || now - _attackReleasedAt < Ms(1200);
        if (state.Walking is not bool moving || _heldMoves.Count == 0 || mobClose || settling)
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

    /// <returns>True when the area logic drove this tick (walking home).</returns>
    private bool LeashTick(GameState state, BotProfile p, List<BotAction> actions, bool refresh, bool mobNextToUs)
    {
        if (p.LeashMeters <= 0 || state.Position is not Vec3 position)
            return false;
        var now = state.Time;
        if (_home is not Vec3 home)
        {
            _home = position;
            actions.Add(new NoteAction("Başlangıç yeri kaydedildi; bu alanda kalınacak."));
            return false;
        }
        double metres = position.DistanceXY(home) / 100;
        if (metres > p.LeashMeters && !_returning)
        {
            _returning = true;
            actions.Add(new NoteAction("Alan sınırı aşıldı; başlangıç yerine dönülüyor."));
        }
        else if (metres < p.LeashMeters * 0.5 && _returning)
        {
            _returning = false;
            actions.Add(new NoteAction("Başlangıç alanına dönüldü."));
        }

        // Too far from home: walk back (a mob right next to us is fought first).
        if (!_returning || mobNextToUs || state.Camera is not CameraView camera)
            return false;
        var (right, up) = camera.ToScreenDirection(home.X - position.X, home.Y - position.Y);
        double sx = right, sy = -up; // screen y grows downwards
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
}
