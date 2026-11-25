using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// The stuck watch: farming with no EXP for long means something holds the bot (a wall, a window, no mobs it can
/// reach). Told after <see cref="StuckTellAfter"/>, stopped after <see cref="StuckStopAfter"/> (nothing gained by
/// going on; the user is told on Telegram either way).
/// </summary>
internal sealed partial class BotEngine
{
    internal static readonly TimeSpan StuckTellAfter = TimeSpan.FromMinutes(10), StuckStopAfter = TimeSpan.FromMinutes(20);
    private TimeSpan _stuckSince = TimeSpan.MinValue;
    private long _stuckExp = -1;
    private int _stuckKills = -1;
    private bool _stuckTold;
    /// <summary>Set when the bot is to stop for it (done by the work loop).</summary>
    private string? _stuckStop;

    private void ResetStuck()
    {
        _stuckSince = TimeSpan.MinValue;
        _stuckExp = -1;
        _stuckKills = -1;
        _stuckTold = false;
        _stuckStop = null;
    }

    private void StuckTick(WorldSnapshot world, BotProfile profile, TimeSpan now)
    {
        // Not farming for EXP (attack off, following), or busy with what gives none: selling, a channel change,
        // dead (the death watch decides): the clock starts again.
        bool waiting = !profile.Attack || _selling || _channelSince != null || world.Dead || world.InSafeZone || _stuckSince == TimeSpan.MinValue;
        // Mobs dying count as well (seen 2026-11-25 06:41: fighting on, the EXP read stood still for 10 minutes
        // around a level up — "takılmış olabilir" while it wasn't).
        if (waiting || _session.Exp != _stuckExp || _session.Kills != _stuckKills)
        {
            if (_stuckTold && !waiting)
                Report("✅ EXP yeniden geliyor; bot toparlandı.");
            _stuckSince = now;
            _stuckExp = _session.Exp;
            _stuckKills = _session.Kills;
            _stuckTold = false;
            return;
        }
        var still = now - _stuckSince;
        if (!_stuckTold && still >= StuckTellAfter)
        {
            _stuckTold = true;
            Report($"⚠ {(int)still.TotalMinutes} dakikadır hiç EXP gelmiyor; bot bir yere takılmış olabilir. " +
                   $"{(int)(StuckStopAfter - still).TotalMinutes} dakika daha böyle giderse durdurulacak.");
        }
        else if (still >= StuckStopAfter && _stuckStop == null)
            _stuckStop = $"{(int)still.TotalMinutes} dakikadır hiç EXP gelmedi (takılmış ya da yakında vurulacak mob yok)";
    }

    private void Report(string text)
    {
        _log(text);
        Notify(text);
    }
}
