using SekoBot.Capture;
using SekoBot.Input;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Logging in again after a maintenance: the game keeps its own name and password ("Oturumu açık tut");
/// SekoBot only presses the login button (never reads or types the password) until the server lets it in.
/// </summary>
internal sealed partial class BotEngine
{
    private InputExecutor? _loginInput;

    public LoginScreen? LoginNow()
    {
        lock (_worldGate)
            return _world?.ReadLogin();
    }

    /// <summary>The character select when it is up (its characters, the one shown, "Başla").</summary>
    public CharSelectScreen? CharSelectNow()
    {
        lock (_worldGate)
            return _world?.ReadCharSelect();
    }

    /// <summary>The game says an update is out (its version is old); null when not readable.</summary>
    public bool? GameOutdated()
    {
        lock (_worldGate)
            return _world?.ReadVersionOld();
    }

    /// <summary>Presses "Başla" on the character select (drawn at <paramref name="button"/>, client pixels).</summary>
    public void PressStart(Rectangle button)
    {
        _loginInput ??= new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        var at = BagCleaner.ClickPoint(button);
        _loginInput.Enqueue(new ClickAction(at, Slow: true, Fast: true, StillWanted: () => CharSelectNow()?.Start is { } current && current.Contains(at)));
        // Enter too (the user: "hem oraya bassın hem entere"): starts with the character shown when the click didn't take.
        _loginInput.Enqueue(new KeyTapAction(Keys.Enter) { StillWanted = () => CharSelectNow() != null });
    }

    /// <summary>
    /// The game's own small update question at its start ("Başlamadan önce ek kaynak indirmesi gerekiyor. İndirme
    /// Boyutu: 62MB" — Onayla / Çıkış; seen 2026-11-21 after v1.3.0), told from the picture (the game isn't readable
    /// yet then): a dark box in the middle, two lines of text, two buttons with a word each. Where "Onayla" is; null
    /// when the question isn't showing.
    /// </summary>
    public Point? UpdateQuestion() => WithLatestFrame(UpdateQuestionIn);

    internal static Point? UpdateQuestionIn(Frame frame) =>
        UpdateQuestionBounds(frame) is { } bounds ? BagCleaner.ClickPoint(bounds) : null;

    private static Rectangle? UpdateQuestionBounds(Frame frame)
    {
        int w = frame.Width, h = frame.Height;
        if (w < 400 || h < 300)
            return null;
        // The game's screens are drawn for a 480-high picture at the least (bigger with the window).
        double s = Math.Max(1, Math.Min(w, h) / 480.0);
        int cx = w / 2, cy = h / 2;
        int Bright(int x0, int y0, int x1, int y1)
        {
            int n = 0;
            for (int y = cy + (int)(y0 * s); y < cy + (int)(y1 * s); y++)
                for (int x = cx + (int)(x0 * s); x < cx + (int)(x1 * s); x++)
                    if (x >= 0 && y >= 0 && x < w && y < h && frame.Pixel(x, y) is var (b, g, r) && r > 200 && g > 200 && b > 200)
                        n++;
            return n;
        }
        double area = s * s;
        bool shown = Bright(-90, 22, -22, 46) >= 30 * area && Bright(22, 22, 90, 46) >= 20 * area
                     && Bright(-95, -45, 95, -2) >= 300 * area && Bright(-15, 22, 15, 46) <= 3 * area;
        if (!shown)
            return null;
        var bounds = Rectangle.FromLTRB(cx + (int)(-90 * s), cy + (int)(22 * s), cx + (int)(-22 * s), cy + (int)(46 * s));
        return bounds;
    }

    /// <summary>Presses a point of the game (client pixels), e.g. "Onayla" on its update question.</summary>
    /// <summary>A character chosen in the character select's list (its entry clicked).</summary>
    public void PickCharacter(Rectangle entry)
    {
        _loginInput ??= new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        var at = BagCleaner.ClickPoint(entry);
        _loginInput.Enqueue(new ClickAction(at, Slow: true, Fast: true, StillWanted: () => CharSelectNow() != null));
    }

    public void PressAt(Point at)
    {
        _loginInput ??= new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        _loginInput.Enqueue(new ClickAction(at, Slow: true, Fast: true,
            StillWanted: () => WithLatestFrame(frame => UpdateQuestionBounds(frame)?.Contains(at) == true)));
    }

    /// <summary>The game menu's "Oturumu kapat" (back to the login screen).</summary>
    internal const string LogoutKey = "SYSTEM_LOGOUT";

    /// <summary>The game menu's button with this text key when the menu shows.</summary>
    public Rectangle? GameMenuButton(string key)
    {
        lock (_worldGate)
            return _world?.ReadMenuButton(key);
    }

    /// <summary>Opens the game menu (the close-window key, Esc, with nothing else open).</summary>
    public void OpenGameMenu()
    {
        _loginInput ??= new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        _loginInput.Enqueue(new KeyTapAction(Profile.Keys.CloseWindow) { StillWanted = () => GameMenuButton(LogoutKey) == null });
    }

    /// <summary>Presses the game menu's button with this text key (drawn at <paramref name="button"/>).</summary>
    public void PressMenuButton(Rectangle button, string key)
    {
        _loginInput ??= new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        var at = BagCleaner.ClickPoint(button);
        _loginInput.Enqueue(new ClickAction(at, Slow: true, Fast: true, StillWanted: () => GameMenuButton(key)?.Contains(at) == true));
    }

    /// <summary>Presses the login button (drawn at <paramref name="button"/>, client pixels): the game in front a moment.</summary>
    public void PressLogin(Rectangle button)
    {
        _loginInput ??= new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        var at = BagCleaner.ClickPoint(button);
        _loginInput.Enqueue(new ClickAction(at, Slow: true, Fast: true,
            StillWanted: () => LoginNow() is { Showing: true, Waiting: false, LoggedIn: false }));
    }
}
