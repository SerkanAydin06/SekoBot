namespace SekoBot;

/// <summary>
/// Several SekoBots at once, one per game window: each takes the first free number (1, 2, …) when it
/// starts. Number 1 works as before; the others keep their own settings and log (profile-2.json,
/// gunluk-2.txt), use Ctrl+F10 / Ctrl+F11, and leave Telegram commands and updates to number 1.
/// A SekoBot connected to a game marks it taken, so another one picks a different game.
/// </summary>
internal static class Instance
{
    private const int MaxInstances = 6;
    private static EventWaitHandle? _instanceMark;
    private static EventWaitHandle? _gameMark;

    /// <summary>This SekoBot's number (1 = the first one open).</summary>
    public static int Number { get; private set; } = 1;

    public static bool IsFirst => Number == 1;

    /// <summary>Taken once at start: the lowest number no other SekoBot holds.</summary>
    public static void Claim()
    {
        for (int n = 1; n <= MaxInstances; n++)
        {
            var mark = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\SekoBot-instance-{n}", out bool created);
            if (created)
            {
                _instanceMark = mark;
                Number = n;
                return;
            }
            mark.Dispose();
        }
        Number = MaxInstances + 1;
    }

    /// <summary>"profile.json" for number 1, "profile-2.json" for number 2…</summary>
    public static string FileName(string name, string extension) =>
        Number == 1 ? $"{name}.{extension}" : $"{name}-{Number}.{extension}";

    /// <summary>Whether another SekoBot is connected to this game.</summary>
    public static bool GameTaken(uint processId)
    {
        if (EventWaitHandle.TryOpenExisting($@"Local\SekoBot-game-{processId}", out var mark))
        {
            mark.Dispose();
            return _gameMark == null || !_ownGame.Equals(processId);
        }
        return false;
    }

    private static uint _ownGame;

    /// <summary>Marks the game this SekoBot connected to (the previous mark is let go).</summary>
    public static void TakeGame(uint processId)
    {
        LeaveGame();
        _gameMark = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\SekoBot-game-{processId}");
        _ownGame = processId;
    }

    public static void LeaveGame()
    {
        _gameMark?.Dispose();
        _gameMark = null;
        _ownGame = 0;
    }
}
