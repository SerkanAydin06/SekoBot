namespace SekoBot.Bot;

/// <summary>
/// Which game windows count as "a window over the game" to close with Esc: the known ones (menus,
/// adverts), and any other window that wasn't showing when the bot started. A window that Esc
/// doesn't close is let be after a few seconds (seen: the chat history window stayed open, every
/// Esc opened and closed the game menu instead, and the bot stood still for minutes; seen too: two
/// "popup_C" adverts on a freshly opened game that Esc didn't close, and the bot never attacked).
/// Windows are counted by name (two adverts are "popup_C, popup_C"): one let be doesn't hide a new
/// one of the same kind coming later. Pure logic.
/// </summary>
internal sealed class PopupWatch
{
    /// <summary>An unknown window still there this long (Esc pressed meanwhile) is let be.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(7);
    /// <summary>A known one (Esc should close it) gets a few more tries.</summary>
    public static readonly TimeSpan KnownGiveUpAfter = TimeSpan.FromSeconds(12);

    /// <summary>How many of each window are let be (showing at the start, or Esc didn't close them).</summary>
    private readonly Dictionary<string, int> _ignored = [];
    /// <summary>Since when there have been more of a window than are let be.</summary>
    private readonly Dictionary<string, TimeSpan> _since = [];

    /// <summary>The windows already showing when the bot starts belong to the screen.</summary>
    public void Start(IEnumerable<string> showing)
    {
        _ignored.Clear();
        foreach (var group in showing.GroupBy(w => w))
            if (!BotEngine.KnownWindows.Contains(group.Key))
                _ignored[group.Key] = group.Count();
        _since.Clear();
    }

    /// <summary>The windows to close now; <paramref name="givenUp"/> gets the ones let be this time.</summary>
    public List<string> Update(IEnumerable<string> open, TimeSpan now, List<string>? givenUp = null)
    {
        var counts = open.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        // Closed meanwhile: fewer let be (a new one later is closed again).
        foreach (var (window, ignored) in _ignored.ToList())
        {
            int count = counts.GetValueOrDefault(window);
            if (count < ignored)
                _ignored[window] = count;
        }
        var popups = new List<string>();
        foreach (var (window, count) in counts)
        {
            if (BotEngine.NotWindows.Contains(window))
                continue;
            int extra = count - _ignored.GetValueOrDefault(window);
            if (extra <= 0)
            {
                _since.Remove(window);
                continue;
            }
            if (!_since.TryGetValue(window, out var since))
                _since[window] = since = now;
            // The game menu is what a stray Esc opens: it is always closed again.
            bool known = BotEngine.KnownWindows.Contains(window);
            if (window != "EscMenu" && now - since >= (known ? KnownGiveUpAfter : GiveUpAfter))
            {
                _ignored[window] = count;
                _since.Remove(window);
                givenUp?.Add(window);
                continue;
            }
            popups.AddRange(Enumerable.Repeat(window, extra));
        }
        return popups;
    }
}
