namespace SekoBot.Config;

/// <summary>
/// Problems that used to be swallowed silently (a file that could not be saved or read): written to the log, each
/// at most once every 10 minutes. Told before the window is up are kept and given to it when it listens.
/// </summary>
internal static class Trouble
{
    private static readonly object Gate = new();
    private static readonly List<string> Waiting = [];
    private static readonly Dictionary<string, DateTime> ToldAt = [];
    private static Action<string>? _listener;

    /// <summary>Where problems go (the window's log); the ones told before are given at once.</summary>
    public static void Listen(Action<string> listener)
    {
        string[] waiting;
        lock (Gate)
        {
            _listener = listener;
            waiting = [.. Waiting];
            Waiting.Clear();
        }
        foreach (string text in waiting)
            listener(text);
    }

    public static void Tell(string what, Exception? ex = null)
    {
        string text = "⚠ " + what + (ex != null ? $" ({ex.Message})" : "");
        Action<string>? listener;
        lock (Gate)
        {
            if (ToldAt.TryGetValue(what, out var at) && DateTime.Now - at < TimeSpan.FromMinutes(10))
                return;
            ToldAt[what] = DateTime.Now;
            listener = _listener;
            if (listener == null)
            {
                if (Waiting.Count < 50)
                    Waiting.Add(text);
                return;
            }
        }
        try { listener(text); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { }
    }

    /// <summary>Writes a file whole or not at all (a crash mid-way leaves the old one, never half a file).</summary>
    public static void WriteSafely(string path, string text)
    {
        string temp = path + ".yaziliyor";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
