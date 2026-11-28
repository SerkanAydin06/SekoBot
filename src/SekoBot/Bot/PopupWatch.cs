using SekoBot.Config;

namespace SekoBot.Bot;

internal static class PopupWatch
{
    public static Keys CloseKey(string? target, KeyBindings keys) => target switch
    {
        "bigMap" => keys.Map,
        "inventory" or "equipment" => keys.Inventory,
        _ => keys.CloseWindow
    };

    // Dismiss the menu and transient dialogs before toggling a panel underneath them.
    public static string? Target(IReadOnlyList<string>? windows)
    {
        if (windows == null) return null;
        if (windows.Contains("EscMenu")) return "EscMenu";
        string? panel = null;
        foreach (string name in windows)
        {
            if (BotEngine.NotWindows.Contains(name)) continue;
            if (name is "bigMap" or "inventory" or "equipment")
            {
                if (panel == null || name == "inventory") panel = name;
            }
            else return name;
        }
        return panel;
    }
}

/// <summary>
/// A window Esc doesn't reach: Esc pressed for it, and the game's menu came up with it still listed — so it wasn't on
/// the screen (seen 2026-11-28: a closed advert left in the game's list; Esc opened the menu, then closed it, again and
/// again). Such a window is left out until it leaves the list.
/// </summary>
internal sealed class PopupGuard
{
    private string? _escFor;
    private readonly HashSet<string> _ignored = [];

    /// <summary>The windows without the ignored ones, and the one ignored just now (to be told), if any.</summary>
    public (IReadOnlyList<string>? Windows, string? NewlyIgnored) Filter(IReadOnlyList<string>? windows)
    {
        if (windows == null)
            return (null, null);
        _ignored.RemoveWhere(n => !windows.Contains(n));
        string? told = null;
        if (_escFor != null && windows.Contains("EscMenu") && windows.Contains(_escFor) && _ignored.Add(_escFor))
            told = _escFor;
        if (_escFor != null && (!windows.Contains(_escFor) || told != null))
            _escFor = null;
        return (_ignored.Count == 0 ? windows : windows.Where(n => !_ignored.Contains(n)).ToList(), told);
    }

    /// <summary>The window the bot is about to close with Esc (the game's menu itself not counted).</summary>
    public void Closing(string? target, bool withEsc)
    {
        if (target != null && target != "EscMenu" && withEsc)
            _escFor = target;
    }

    public void Reset()
    {
        _escFor = null;
        _ignored.Clear();
    }
}
