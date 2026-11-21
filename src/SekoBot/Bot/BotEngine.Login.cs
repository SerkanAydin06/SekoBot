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
        var at = new Point(button.X + button.Width / 2, button.Y + button.Height / 2);
        _loginInput.Enqueue(new ClickAction(at, Slow: true, Fast: true, StillWanted: () => CharSelectNow() is { Start: not null }));
    }

    /// <summary>Presses the login button (drawn at <paramref name="button"/>, client pixels): the game in front a moment.</summary>
    public void PressLogin(Rectangle button)
    {
        _loginInput ??= new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        var at = new Point(button.X + button.Width / 2, button.Y + button.Height / 2);
        _loginInput.Enqueue(new ClickAction(at, Slow: true, Fast: true,
            StillWanted: () => LoginNow() is { Showing: true, Waiting: false, LoggedIn: false }));
    }
}
