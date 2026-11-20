using System.Drawing;

namespace SekoBot.Memory;

/// <summary>
/// The game's login screen (found 2026-11-20 during the maintenance): m2data_C.loginMain (loginScreen_C).
/// The name and password are the game's own (saved by "Oturumu açık tut"); SekoBot never reads them.
/// Its login button is the third <c>c_Button</c> field (+0x350): pressed, it hides until the server answers
/// ("Sunucu henüz açılmadı." while closed). A small game window cuts the button off at the bottom.
/// Logged in: loader.loginok / m2data_C.loginok, then the character select (charSelect / charSelectNew).
/// </summary>
/// <param name="Button">The login button where it is drawn (client pixels); null when not shown.</param>
/// <param name="Waiting">The button is hidden: a login is going on.</param>
/// <param name="LoggedIn">Through: the character select is up (or loading into the game).</param>
internal sealed record LoginScreen(bool Showing, Rectangle? Button, bool Waiting, bool LoggedIn);

internal sealed partial class GameWorldReader
{
    /// <summary>Which of the login screen's <c>c_Button</c> fields is the login button (0-based).</summary>
    internal const int LoginButtonIndex = 2;

    public LoginScreen? ReadLogin()
    {
        try
        {
            if (!_located)
                return null;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return null;
            long loader = Pointer(instance + Offset(instanceClass, "m2data_C", "loader"));
            bool loggedIn = Byte(instance + Offset(instanceClass, "m2data_C", "loginok")) is byte a && a != 0
                            || MemoryReads.IsPointer(loader) && Byte(loader + Offset(Pointer(loader + ObjClass), "loader", "loginok")) is byte b && b != 0
                            || MemoryReads.IsPointer(Pointer(instance + Offset(instanceClass, "m2data_C", "charSelectNew")))
                            || MemoryReads.IsPointer(Pointer(instance + Offset(instanceClass, "m2data_C", "charSelect")));
            long screen = Pointer(instance + Offset(instanceClass, "m2data_C", "loginMain"));
            if (!MemoryReads.IsPointer(screen) || ClassName(Pointer(screen + ObjClass)) != "loginScreen_C" || !Showing(screen))
                return new LoginScreen(false, null, false, loggedIn);
            long button = 0;
            if (NthProperty(Pointer(screen + ObjClass), "c_Button", LoginButtonIndex) is int at)
                button = Pointer(screen + at);
            if (!MemoryReads.IsPointer(button))
                return new LoginScreen(true, null, false, loggedIn);
            bool hidden = !Showing(button);
            return new LoginScreen(true, hidden ? null : DrawnAt(button), hidden, loggedIn);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }

    /// <summary>The offset of the n-th field called <paramref name="field"/> (a class can have several with one name).</summary>
    private int? NthProperty(long cls, string field, int n)
    {
        var found = new List<int>();
        for (int depth = 0; depth < 8 && MemoryReads.IsPointer(cls); depth++, cls = Pointer(cls + StructSuper))
        {
            long property = Pointer(cls + StructProperties);
            for (int i = 0; i < 2000 && MemoryReads.IsPointer(property); i++, property = Pointer(property + FieldNext))
                if (Int32(property + FieldName) is int name && Name(name) == field && Int32(property + PropertyOffset) is int o and >= 0 and < 0x100000)
                    found.Add(o);
        }
        found.Sort();
        return n < found.Count ? found[n] : null;
    }
}
