using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// What came into the bag and what was thrown away during this run (one row per item, with
/// counts). Started fresh every time the bot starts; written to tur-esyalari.txt as it changes, so
/// the list of the last run can still be read after SekoBot is closed.
/// </summary>
internal sealed class SessionItems
{
    private readonly Dictionary<int, (int Picked, int Thrown)> _items = [];
    private readonly object _gate = new();
    private DateTime _started = DateTime.Now, _savedAt = DateTime.MinValue;
    private bool _dirty;

    /// <summary>Goes up with every change (the window redraws the list then).</summary>
    public int Version { get; private set; }

    public static string FilePath => Path.Combine(ProfileStore.Directory, Instance.FileName("tur-esyalari", "txt"));

    public void Reset()
    {
        lock (_gate)
        {
            _items.Clear();
            _started = DateTime.Now;
            _dirty = true;
            Version++;
        }
        Save(force: true);
    }

    public void Picked(int vnum, int count) => Add(vnum, count, 0);

    public void Thrown(int vnum, int count) => Add(vnum, 0, count);

    private void Add(int vnum, int picked, int thrown)
    {
        if (vnum <= 1 || picked + thrown <= 0)
            return;
        lock (_gate)
        {
            var (p, t) = _items.GetValueOrDefault(vnum);
            _items[vnum] = (p + picked, t + thrown);
            _dirty = true;
            Version++;
        }
    }

    /// <summary>The rows: item, how many picked up, how many thrown away (most picked first).</summary>
    public IReadOnlyList<(int Vnum, string Name, int Picked, int Thrown)> Rows()
    {
        lock (_gate)
            return [.. _items.Select(kv => (kv.Key, ItemBook.NameOf(kv.Key), kv.Value.Picked, kv.Value.Thrown))
                .OrderByDescending(r => r.Picked).ThenByDescending(r => r.Thrown).ThenBy(r => r.Item2, StringComparer.CurrentCulture)];
    }

    /// <summary>Written at most every few seconds (or now).</summary>
    public void Save(bool force = false)
    {
        string text;
        lock (_gate)
        {
            if (!_dirty || !force && DateTime.Now - _savedAt < TimeSpan.FromSeconds(5))
                return;
            _dirty = false;
            _savedAt = DateTime.Now;
            var lines = new List<string> { $"SekoBot · bu tur alınan ve atılan eşyalar · başlangıç {_started:dd.MM.yyyy HH:mm}", "" };
            lines.Add($"{"Eşya",-36} {"Alınan",7} {"Atılan",7}");
            foreach (var (vnum, (picked, thrown)) in _items.OrderByDescending(kv => kv.Value.Picked))
                lines.Add($"{ItemBook.NameOf(vnum),-36} {picked,7} {thrown,7}");
            text = string.Join(Environment.NewLine, lines);
        }
        try
        {
            Directory.CreateDirectory(ProfileStore.Directory);
            File.WriteAllText(FilePath, text);
        }
        catch { }
    }
}
