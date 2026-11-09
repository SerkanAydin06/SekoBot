namespace SekoBot.Bot;

/// <summary>
/// Which game windows count as "a window over the game" to close with Esc: the known ones (menus,
/// adverts), and any other window that wasn't showing when the bot started. An unknown window that
/// Esc doesn't close is let be after a few seconds (seen: the chat history window stayed open, every
/// Esc opened and closed the game menu instead, and the bot stood still for minutes). Pure logic.
/// </summary>
internal sealed class PopupWatch
{
    /// <summary>An unknown window still there this long (Esc pressed meanwhile) is let be.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(7);

    private readonly HashSet<string> _ignored = [];
    private readonly Dictionary<string, TimeSpan> _since = [];

    /// <summary>The windows already showing when the bot starts belong to the screen.</summary>
    public void Start(IEnumerable<string> showing)
    {
        _ignored.Clear();
        _ignored.UnionWith(showing);
        _since.Clear();
    }

    /// <summary>The windows to close now; <paramref name="givenUp"/> gets the ones let be this time.</summary>
    public List<string> Update(IEnumerable<string> open, TimeSpan now, List<string>? givenUp = null)
    {
        var windows = open.ToList();
        foreach (string gone in _since.Keys.Where(w => !windows.Contains(w)).ToList())
            _since.Remove(gone);
        var popups = new List<string>();
        foreach (string window in windows)
        {
            if (BotEngine.KnownWindows.Contains(window))
            {
                popups.Add(window);
                continue;
            }
            if (_ignored.Contains(window) || BotEngine.NotWindows.Contains(window))
                continue;
            if (!_since.TryGetValue(window, out var since))
                _since[window] = since = now;
            if (now - since >= GiveUpAfter)
            {
                _ignored.Add(window);
                givenUp?.Add(window);
                continue;
            }
            popups.Add(window);
        }
        return popups;
    }
}
