using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Acting a little more like a person: every BreakEveryMin minutes (±20 %), when nothing is being
/// fought and nothing attacks, the bot lets go of everything and stands still for BreakSeconds
/// (±25 %). A mob attacking ends the break at once.
/// </summary>
internal sealed partial class Brain
{
    /// <summary>Seeded from the start time: different every session, the same when a recording is replayed.</summary>
    private Random _random = new(0);
    private TimeSpan _nextBreak = Never, _breakUntil = Never;

    private void ResetBreaks(TimeSpan now, BotProfile p)
    {
        _breakUntil = Never;
        _nextBreak = now + Ms(p.BreakEveryMin * 60_000 * (0.8 + 0.4 * _random.NextDouble()));
    }

    /// <returns>True while on a break (nothing else happens this tick).</returns>
    private bool BreakTick(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (!p.Breaks || p.BreakEveryMin <= 0)
            return false;
        if (_nextBreak == Never)
            ResetBreaks(now, p);
        if (_breakUntil != Never)
        {
            if (state.Attackers.Count > 0)
            {
                actions.Add(new NoteAction("Mola bitti: bir mob saldırıyor."));
                ResetBreaks(now, p);
                return false;
            }
            if (now < _breakUntil)
                return true;
            actions.Add(new NoteAction("Mola bitti; devam."));
            ResetBreaks(now, p);
            return false;
        }
        bool busy = Phase == BotPhase.Fighting || state.Attackers.Count > 0 || state.HasTarget;
        if (now < _nextBreak || busy)
            return false;
        double seconds = p.BreakSeconds * (0.75 + 0.5 * _random.NextDouble());
        _breakUntil = now + Ms(seconds * 1000);
        if (p.NavigateMode)
        {
            SetMoves([], p, actions, refresh: false);
            SetAttack(false, p, actions, refresh: false);
            ReleaseTimedKeys(actions);
        }
        else
            actions.Add(new KeyUpAction(p.Keys.Attack));
        actions.Add(new NoteAction($"Kısa mola ({seconds:0} sn)."));
        return true;
    }
}
