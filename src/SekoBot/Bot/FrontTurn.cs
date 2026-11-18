using SekoBot.Config;

namespace SekoBot.Bot;

/// <summary>
/// One SekoBot at a time brings its game to the front and uses the mouse (emptying the bag, a trip to
/// a market NPC): the others wait for their turn (seen: two bots emptying their bags at once, the
/// games flipping in front of each other and the clicks mixed up). The turn is a file kept open
/// without sharing (sira.lock): only one SekoBot can hold it, and Windows lets it go when that
/// SekoBot closes or crashes, so nobody waits for ever. Its holder's name is written next to it (sira.lock.kimde).
/// </summary>
internal sealed class FrontTurn(string? path = null)
{
    /// <summary>Held longer than this: let go anyway (something went wrong).</summary>
    public static readonly TimeSpan MaxHold = TimeSpan.FromMinutes(8);

    public static FrontTurn Shared { get; } = new();

    private readonly string _path = path ?? Path.Combine(ProfileStore.Directory, "sira.lock");
    private readonly object _gate = new();
    private FileStream? _held;
    private DateTime _since;

    public bool Mine
    {
        get
        {
            lock (_gate)
            {
                if (_held != null && DateTime.UtcNow - _since > MaxHold)
                    LetGoLocked();
                return _held != null;
            }
        }
    }

    /// <summary>Takes the turn when nobody has it (or this SekoBot already does). False: someone else has it.</summary>
    public bool TryTake(string who)
    {
        lock (_gate)
        {
            if (_held != null)
                return true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var file = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                _held = file;
                try { File.WriteAllText(_path + ".kimde", who); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                _since = DateTime.UtcNow;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Who has the turn now (another SekoBot), when it can be told; empty otherwise.</summary>
    public string Holder()
    {
        lock (_gate)
            if (_held != null)
                return "";
        try
        {
            return File.ReadAllText(_path + ".kimde").Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    public void LetGo()
    {
        lock (_gate)
            LetGoLocked();
    }

    private void LetGoLocked()
    {
        _held?.Dispose();
        _held = null;
    }
}
