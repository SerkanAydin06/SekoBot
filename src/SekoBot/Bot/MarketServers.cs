using SekoBot.Config;

namespace SekoBot.Bot;

/// <summary>
/// The market lists of every game server apart (prices differ between servers): pazar\[server]\…. The server is
/// the game's own (m2data_C.serverName); until the game is read, the one seen last.
/// </summary>
internal static class MarketServers
{
    /// <summary>The lists from before the servers were told apart were all read on this server (the user).</summary>
    internal const string OldServer = "Yakamoz";
    private const string LastFile = "son-sunucu.txt";

    public static string Root => Path.Combine(ProfileStore.Directory, "pazar");

    private static string? _current;
    private static readonly object Gate = new();
    private static readonly HashSet<string> Moved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The server of this PARDUS's game ("Tuva", "Yakamoz", …).</summary>
    public static string Current
    {
        get
        {
            lock (Gate)
                return _current ??= LastOf(Root) ?? OldServer;
        }
    }

    /// <summary>The market folder of the server now.</summary>
    public static string Folder => FolderOf(Root, Current);

    /// <summary>The server the game says; true when it is another one than before.</summary>
    public static bool Set(string server)
    {
        server = server.Trim();
        if (server.Length == 0)
            return false;
        lock (Gate)
        {
            if (string.Equals(_current, server, StringComparison.OrdinalIgnoreCase))
                return false;
            _current = server;
        }
        try
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, LastFile), server);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return true;
    }

    internal static string? LastOf(string root)
    {
        try
        {
            string path = Path.Combine(root, LastFile);
            return File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } s ? s : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The folder of a server's lists; the lists from before (straight in pazar\) moved to the old server's first.</summary>
    internal static string FolderOf(string root, string server)
    {
        MoveOld(root);
        string name = string.Concat(server.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(root, name.Length > 0 ? name : OldServer);
    }

    private static void MoveOld(string root)
    {
        lock (Gate)
        {
            if (!Moved.Add(root) || !Directory.Exists(root))
                return;
            string old = Path.Combine(root, OldServer);
            foreach (var file in Directory.GetFiles(root))
            {
                string name = Path.GetFileName(file);
                if (name.Equals(LastFile, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    Directory.CreateDirectory(old);
                    string to = Path.Combine(old, name);
                    if (!File.Exists(to))
                        File.Move(file, to);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trouble.Tell("Pazar dosyası taşınamadı: " + name, ex); }
            }
        }
    }
}
