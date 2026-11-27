namespace SekoBot.Bot;

internal static class PopupWatch
{
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
