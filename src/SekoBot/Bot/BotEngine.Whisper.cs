using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Answering a private message from Telegram: the text is queued; when no mob is on us (or after 30 s
/// at the latest) the brain waits, every held key is let go, and <see cref="WhisperSender"/> types it in
/// the game's message window. The result goes to the log and Telegram.
/// </summary>
internal sealed partial class BotEngine
{
    private static readonly TimeSpan WhisperWaitForFight = TimeSpan.FromSeconds(30);
    private readonly WhisperSender _whisper = new();
    private (string To, string Text, TimeSpan Since)? _pendingWhisper;
    private DmLayout? _dmLayout;

    /// <summary>Queues a message for a player (from Telegram). Returns what to answer there.</summary>
    public string QueueWhisper(string to, string text)
    {
        if (!BotRunning)
            return "Bot çalışmıyor; mesaj yalnızca bot çalışırken gönderilebilir.";
        lock (_botGate)
        {
            if (_whisper.Active || _pendingWhisper != null)
                return "Bir mesaj zaten gönderiliyor; birazdan tekrar dene.";
            _pendingWhisper = (to, text, _clock.Elapsed);
        }
        _log($"Telegram'dan mesaj: {to} ← {text}");
        return $"✉ {to} kişisine yazılacak: \"{text}\" (dövüş bitince, en geç 30 sn içinde).";
    }

    /// <summary>The DM window, read only while a message is being sent (on the perception thread).</summary>
    private void ReadDmForWhisper() =>
        _dmLayout = _whisper.Active ? _world?.ReadDmWindow() : null;

    /// <summary>Inside <see cref="_botGate"/>: this tick's actions while a message is sent, or null.</summary>
    private IReadOnlyList<BotAction>? WhisperTick(GameState state, BotProfile p)
    {
        var actions = new List<BotAction>();
        if (!_whisper.Active)
        {
            if (_pendingWhisper is not { } pending || _cleaner.Active)
                return null;
            bool fighting = state.Attackers.Count > 0 || state.Targets.Any(t => t.AttacksMe);
            if (fighting && state.Time - pending.Since < WhisperWaitForFight)
                return null;
            _pendingWhisper = null;
            _whisper.Start(state.Time, pending.To, pending.Text);
            lock (_social)
                _social.ExpectOwn(pending.To, DateTime.Now);
            // Nothing may be held while typing (a held W would be typed into the box).
            actions.Add(new KeyUpAction(p.Keys.Attack));
            foreach (var key in new[] { p.Keys.MoveForward, p.Keys.MoveBack, p.Keys.MoveLeft, p.Keys.MoveRight })
                actions.Add(new KeyUpAction(key));
            return actions;
        }
        if (state.Dead || state.Hp is < 0.35)
            _whisper.Abort(state.Dead ? "karakter öldü" : "can çok düştü");
        else
            actions.AddRange(_whisper.Tick(state.Time, _dmLayout));
        if (!_whisper.Active)
        {
            string result = _whisper.Sent
                ? $"✉ Mesaj gönderildi: {_whisper.To} ← \"{_whisper.Text}\""
                : $"✉ Mesaj gönderilemedi ({_whisper.To}): {_whisper.Problem}";
            _log(result);
            Notify(result);
        }
        return actions;
    }
}
