using SekoBot.Config;

namespace SekoBot.Bot;

/// <summary>
/// What the bots learned about each skill, by its name, for every character and every SekoBot
/// (beceri-bilgisi.json, shared while they run): how long its casting takes (a skill pressed too
/// soon after it is cut off) and how much later than its cooldown the game really takes it again
/// ("WAIT!"). A ninja's skills learned in one SekoBot are known in all of them, for every ninja.
/// The cooldown, duration and MP come from the game itself; only what the game doesn't say is here.
/// </summary>
internal sealed class SkillLore
{
    public sealed class Entry
    {
        /// <summary>After this skill, the next one waits at least this long (ms; 0 = not learned).</summary>
        public int CastMs { get; set; }
        /// <summary>Pressed again this much later than its cooldown says (ms).</summary>
        public int ExtraMs { get; set; }
    }

    /// <summary>Learned casting time: grows by this, up to <see cref="CastMaxMs"/>.</summary>
    public const int CastStepMs = 500, CastMaxMs = 4000;
    public const int ExtraStepMs = 500, ExtraMaxMs = 3000;

    /// <summary>The one every SekoBot of this process uses (its file shared with the others).</summary>
    public static SkillLore Shared { get; } = Load();

    private Dictionary<string, Entry> _skills;
    private readonly object _gate = new();
    private readonly List<Action<Dictionary<string, Entry>>> _news = [];

    public SkillLore(Dictionary<string, Entry>? skills = null) => _skills = skills ?? [];

    public int CastMs(string name)
    {
        lock (_gate)
            return name.Length > 0 && _skills.TryGetValue(name, out var e) ? e.CastMs : 0;
    }

    public int ExtraMs(string name)
    {
        lock (_gate)
            return name.Length > 0 && _skills.TryGetValue(name, out var e) ? e.ExtraMs : 0;
    }

    /// <summary>A skill pressed <paramref name="afterMs"/> after <paramref name="name"/> was cut off by its casting.</summary>
    /// <returns>The casting time now known for it.</returns>
    public int CutOffAfter(string name, int afterMs)
    {
        if (name.Length == 0)
            return 0;
        lock (_gate)
        {
            Cut(_skills, name, afterMs);
            _news.Add(d => Cut(d, name, afterMs));
            return _skills[name].CastMs;
        }
    }

    private static void Cut(Dictionary<string, Entry> skills, string name, int afterMs)
    {
        if (!skills.TryGetValue(name, out var e))
            skills[name] = e = new Entry();
        e.CastMs = Math.Min(CastMaxMs, Math.Max(e.CastMs, afterMs) + CastStepMs);
    }

    /// <summary>The game said "WAIT!" to this skill: it needs a little longer than its cooldown.</summary>
    public int TooSoon(string name)
    {
        if (name.Length == 0)
            return 0;
        lock (_gate)
        {
            Soon(_skills, name);
            _news.Add(d => Soon(d, name));
            return _skills[name].ExtraMs;
        }
    }

    private static void Soon(Dictionary<string, Entry> skills, string name)
    {
        if (!skills.TryGetValue(name, out var e))
            skills[name] = e = new Entry();
        e.ExtraMs = Math.Min(ExtraMaxMs, e.ExtraMs + ExtraStepMs);
    }

    /// <summary>Taken by the game without "WAIT!": the extra learned for it eases off a little.</summary>
    public const int EaseMs = 100;

    public void TookFine(string name)
    {
        lock (_gate)
        {
            if (name.Length == 0 || !_skills.TryGetValue(name, out var e) || e.ExtraMs <= 0)
                return;
            Ease(_skills, name);
            _news.Add(d => Ease(d, name));
        }
    }

    private static void Ease(Dictionary<string, Entry> skills, string name)
    {
        if (skills.TryGetValue(name, out var e))
            e.ExtraMs = Math.Max(0, e.ExtraMs - EaseMs);
    }

    public static string FilePath => Path.Combine(ProfileStore.Directory, "beceri-bilgisi.json");

    public static SkillLore Load(string? path = null)
    {
        try
        {
            path ??= FilePath;
            if (File.Exists(path))
                return new SkillLore(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        return new SkillLore();
    }

    /// <summary>Shares: this SekoBot's news go into the file, and what the others learned comes back.</summary>
    public void Save(string? path = null)
    {
        List<Action<Dictionary<string, Entry>>> news;
        lock (_gate)
        {
            news = [.. _news];
            _news.Clear();
        }
        var all = SharedFile.Update<Dictionary<string, Entry>>(path ?? FilePath, d => news.ForEach(n => n(d)));
        lock (_gate)
        {
            if (all == null)
            {
                _news.InsertRange(0, news);
                return;
            }
            foreach (var n in _news)
                n(all);
            _skills = all;
        }
    }
}
