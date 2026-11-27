using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Sends a private message the way a person does in this game's message (DM) window: open it with the
/// "Mesaj" button on the left, click the conversation with the player, click the text box, type, press
/// Enter, check the message shows up as ours, and close the window again if we opened it. Works from
/// what memory shows (<see cref="DmLayout"/>). Pure logic: the engine reads the layout and sends the actions.
/// </summary>
internal sealed class WhisperSender(Func<DmLayout?>? readNow = null)
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(45);
    private static TimeSpan AfterClick => TimeSpan.FromMilliseconds(PressTime.Vary(1800));
    /// <summary>After the held keys are let go, before the first click.</summary>
    internal static readonly TimeSpan KeysSettle = TimeSpan.FromMilliseconds(1000);

    private enum Step { Open, Select, Focus, Type, Send, Check, Close }

    private Step _step;
    private TimeSpan _started, _waitUntil;
    private int _opens, _selects, _checks, _closes;
    private int _matchingBeforeSend;
    private TaskCompletionSource<bool>? _sendPending;
    private int Matching(DmLayout layout) => layout.Shown.Count(m => m.Mine && m.Message.Trim() == Text);
    private bool _openedByUs;

    public bool Active { get; private set; }
    public string To { get; private set; } = "";
    public string Text { get; private set; } = "";
    public bool Sent { get; private set; }
    /// <summary>Why it ended without sending (null: sent).</summary>
    public string? Problem { get; private set; }

    public void Start(TimeSpan now, string to, string text)
    {
        _sendPending = null;
        Active = true;
        To = to.Trim();
        Text = text.Trim();
        _step = Step.Open;
        _started = now;
        // The keys were just let go: a moment for what is still on its way to the game to arrive
        // before anything is clicked or typed.
        _waitUntil = now + KeysSettle;
        _opens = _selects = _checks = _closes = 0;
        _openedByUs = Sent = false;
        Problem = null;
    }

    public void Abort(string why)
    {
        _sendPending = null;
        Active = false;
        Problem ??= why;
    }

    private Func<bool>? When(Func<DmLayout, bool> predicate) =>
        readNow == null ? null : () => readNow() is { } live && predicate(live);

    private static bool Conversation(DmLayout dm, string recipient) =>
        dm.Open && !dm.TextBox.IsEmpty &&
        dm.Shown.Any(m => !m.Mine && string.Equals(m.Name.Trim(), recipient, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<BotAction> Tick(TimeSpan now, DmLayout? dm)
    {
        var actions = new List<BotAction>();
        if (!Active || now < _waitUntil)
            return actions;
        if (dm == null)
        {
            Abort("mesaj penceresi bellekten okunamadı");
            return actions;
        }
        if (_step != Step.Close && now - _started > GiveUpAfter)
            Close("süre doldu");

        string recipient = To;
        switch (_step)
        {
            case Step.Open:
                if (dm.Open)
                {
                    _step = Step.Select;
                    goto case Step.Select;
                }
                if (_opens >= 2 || dm.Opener is not Point opener)
                {
                    Abort("mesaj penceresi açılamadı");
                    break;
                }
                var openerAt = BagCleaner.DropClickPoint(opener, dm.OpenerBounds);
                actions.Add(new ClickAction(openerAt, Slow: true, StillWanted: When(live => !live.Open && live.OpenerBounds.Contains(openerAt))));
                _openedByUs = true;
                _opens++;
                _waitUntil = now + AfterClick;
                break;

            case Step.Select:
                if (!dm.Open)
                {
                    _step = Step.Open;
                    break;
                }
                var person = dm.People.FirstOrDefault(p => string.Equals(p.Name.Trim(), To, StringComparison.OrdinalIgnoreCase));
                if (person.Name == null)
                {
                    Close($"{To} ile konuşma yok (yalnızca sana yazmış biriyle konuşulabilir)");
                    goto case Step.Close;
                }
                if (_selects >= 2)
                {
                    _step = Step.Focus;
                    goto case Step.Focus;
                }
                var personAt = BagCleaner.DropClickPoint(Centre(person.Rect), person.Rect);
                actions.Add(new ClickAction(personAt, Slow: true, StillWanted: When(live => live.Open && live.People.Any(p =>
                    string.Equals(p.Name.Trim(), recipient, StringComparison.OrdinalIgnoreCase) && p.Rect.Contains(personAt)))));
                _selects++;
                _step = Step.Focus;
                _waitUntil = now + AfterClick;
                break;

            case Step.Focus:
                if (dm.TextBox.IsEmpty)
                {
                    Close("yazı kutusu bulunamadı");
                    goto case Step.Close;
                }
                var boxAt = BagCleaner.ClickPoint(dm.TextBox);
                actions.Add(new ClickAction(boxAt, Slow: true, StillWanted: When(live =>
                    Conversation(live, recipient) && live.TextBox.Contains(boxAt))));
                _step = Step.Type;
                _waitUntil = now + TimeSpan.FromMilliseconds(PressTime.Vary(1200));
                break;

            case Step.Type:
                actions.Add(new TypeTextAction(Text, ClearFirst: true) { StillWanted = When(live => Conversation(live, recipient) && live.TextBox == dm.TextBox) });
                _step = Step.Send;
                _waitUntil = now + TimeSpan.FromMilliseconds(PressTime.Vary(800) + Text.Length * 60);
                break;

            case Step.Send:
                if (!dm.Open || dm.TextBox.IsEmpty)
                {
                    Close("mesaj yazı alanı kapandı");
                    goto case Step.Close;
                }
                _matchingBeforeSend = Matching(dm);
                if (readNow == null)
                    actions.Add(new KeyTapAction(Keys.Enter));
                else
                {
                    var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _sendPending = pending;
                    actions.Add(new KeyTapAction(Keys.Enter) { Completion = pending, StillWanted = When(live =>
                    {
                        if (!ReferenceEquals(Volatile.Read(ref _sendPending), pending) || !Conversation(live, recipient) || live.TextBox != dm.TextBox)
                            return false;
                        _matchingBeforeSend = Matching(live);
                        return true;
                    }) });
                }
                _step = Step.Check;
                _waitUntil = now + AfterClick;
                break;

            case Step.Check:
                if (_sendPending != null)
                {
                    if (!_sendPending.Task.IsCompleted) break;
                    if (!_sendPending.Task.Result)
                    {
                        Close("Enter gönderilemedi");
                        goto case Step.Close;
                    }
                    _sendPending = null;
                }
                if ((readNow == null || Conversation(dm, recipient)) && Matching(dm) > _matchingBeforeSend)
                {
                    Sent = true;
                    Close(null);
                    goto case Step.Close;
                }
                if (++_checks >= 3)
                {
                    Close("gönderildiği görülemedi (oyunda kontrol et)");
                    goto case Step.Close;
                }
                _waitUntil = now + TimeSpan.FromSeconds(1);
                break;

            case Step.Close:
                // The window is closed afterwards (it stayed open: the bot then pressed no keys): its
                // "Mesaj" button first, then Esc.
                if (!dm.Open || _closes >= 3)
                {
                    Active = false;
                    break;
                }
                if (_closes < 2 && dm.Opener is Point button)
                {
                    var buttonAt = BagCleaner.DropClickPoint(button, dm.OpenerBounds);
                    actions.Add(new ClickAction(buttonAt, Slow: true, StillWanted: When(live => live.Open && live.OpenerBounds.Contains(buttonAt))));
                }
                else
                    actions.Add(new KeyTapAction(Keys.Escape) { StillWanted = When(live => live.Open) });
                _closes++;
                _waitUntil = now + AfterClick;
                break;
        }
        return actions;
    }

    private static Point Centre(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private void Close(string? problem)
    {
        _sendPending = null;
        _step = Step.Close;
        Problem ??= problem;
    }

    /// <summary>"Ali merhaba nasılsın" → (Ali, merhaba nasılsın); "Ali: merhaba" too.</summary>
    internal static (string To, string Text)? Parse(string text)
    {
        text = text.Trim();
        int colon = text.IndexOf(':');
        int space = text.IndexOf(' ');
        int split = colon > 0 && (space < 0 || colon < space) ? colon : space;
        if (split <= 0)
            return null;
        string to = text[..split].Trim(), message = text[(split + 1)..].Trim();
        return to.Length == 0 || message.Length == 0 ? null : (to, message);
    }

    /// <summary>The player a whisper notice was about ("💬 Fısıltı — Ali (Sv 40): …" → Ali).</summary>
    internal static string? WhisperFrom(string notice)
    {
        const string mark = "Fısıltı — ";
        int at = notice.IndexOf(mark, StringComparison.Ordinal);
        if (at < 0)
            return null;
        string rest = notice[(at + mark.Length)..];
        int end = rest.IndexOfAny([' ', ':', '\n']);
        string name = (end < 0 ? rest : rest[..end]).Trim();
        return name.Length == 0 ? null : name;
    }
}
