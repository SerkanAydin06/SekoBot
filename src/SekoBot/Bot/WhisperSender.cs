using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Sends a private message the way a person does in this game's message (DM) window: open it with the
/// "Mesaj" button on the left, click the conversation with the player, click the text box, type, click
/// "Gönder", check the message shows up as ours, and close the window again if we opened it. Works from
/// what memory shows (<see cref="DmLayout"/>). Pure logic: the engine reads the layout and sends the actions.
/// </summary>
internal sealed class WhisperSender
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan AfterClick = TimeSpan.FromMilliseconds(1800);

    private enum Step { Open, Select, Focus, Type, Send, Check, Close }

    private Step _step;
    private TimeSpan _started, _waitUntil;
    private int _opens, _selects, _checks, _closes;
    private bool _openedByUs;

    public bool Active { get; private set; }
    public string To { get; private set; } = "";
    public string Text { get; private set; } = "";
    public bool Sent { get; private set; }
    /// <summary>Why it ended without sending (null: sent).</summary>
    public string? Problem { get; private set; }

    public void Start(TimeSpan now, string to, string text)
    {
        Active = true;
        To = to.Trim();
        Text = text.Trim();
        _step = Step.Open;
        _started = _waitUntil = now;
        _opens = _selects = _checks = _closes = 0;
        _openedByUs = Sent = false;
        Problem = null;
    }

    public void Abort(string why)
    {
        Active = false;
        Problem ??= why;
    }

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
                actions.Add(new ClickAction(opener, Slow: true));
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
                actions.Add(new ClickAction(Centre(person.Rect), Slow: true));
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
                actions.Add(new ClickAction(Centre(dm.TextBox), Slow: true));
                _step = Step.Type;
                _waitUntil = now + TimeSpan.FromMilliseconds(1200);
                break;

            case Step.Type:
                actions.Add(new TypeTextAction(Text));
                _step = Step.Send;
                _waitUntil = now + TimeSpan.FromMilliseconds(800 + Text.Length * 60);
                break;

            case Step.Send:
                if (dm.Send.IsEmpty)
                {
                    Close("Gönder düğmesi bulunamadı");
                    goto case Step.Close;
                }
                actions.Add(new ClickAction(Centre(dm.Send), Slow: true));
                _step = Step.Check;
                _waitUntil = now + AfterClick;
                break;

            case Step.Check:
                if (dm.Shown.Any(m => m.Mine && m.Message.Trim() == Text))
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
                // Leave the window as it was: closed again when we opened it.
                if (!_openedByUs || !dm.Open || _closes >= 2 || dm.Opener is not Point button)
                {
                    Active = false;
                    break;
                }
                actions.Add(new ClickAction(button, Slow: true));
                _closes++;
                _waitUntil = now + AfterClick;
                break;
        }
        return actions;
    }

    private static Point Centre(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private void Close(string? problem)
    {
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
