using System.Text.RegularExpressions;

namespace SekoBot.Bot;

/// <summary>
/// Keeps the log readable: the brain's routine notes ("Gruplar toplanıyor…", "toplanan 3 mob
/// kesiliyor") came every few seconds. A note is written when it is new; the same note again
/// (numbers aside) within <see cref="Quiet"/> is left out. Warnings (danger, death, GM…) are always written.
/// </summary>
internal sealed partial class NoteFilter
{
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, TimeSpan> _written = [];

    [GeneratedRegex(@"\d+([.,]\d+)?")]
    private static partial Regex Numbers();

    public bool ShouldWrite(string note, TimeSpan now)
    {
        if (Always(note))
            return true;
        string key = Numbers().Replace(note, "#");
        if (_written.TryGetValue(key, out var at) && now - at < Quiet)
            return false;
        _written[key] = now;
        if (_written.Count > 500)
            foreach (var old in _written.Where(w => now - w.Value >= Quiet).Select(w => w.Key).ToList())
                _written.Remove(old);
        return true;
    }

    public void Reset() => _written.Clear();

    private static bool Always(string note) =>
        note.StartsWith('⚠') || note.StartsWith("Tehlike", StringComparison.Ordinal) || note.StartsWith("Uyarı", StringComparison.Ordinal) ||
        note.Contains("öldü", StringComparison.OrdinalIgnoreCase) || note.Contains("iksiri azaldı", StringComparison.OrdinalIgnoreCase);
}
