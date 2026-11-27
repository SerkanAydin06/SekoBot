using System.Text.Json;
using SekoBot.Config;

namespace SekoBot.Bot;

/// <summary>
/// What picking items up to see their bonuses brought, per item (its number): how many were looked at in the bag and how
/// many had a bonus of the list. An item that keeps coming without one isn't picked up for its bonuses any more (the user:
/// "efsununu görmek için çok eşya alıyor" — seen: 11 of 11 spears and 11 of 11 long swords picked up, then thrown away).
/// Kept for every character and every PARDUS (efsun-denemeleri.json).
/// </summary>
internal sealed class BonusTrials
{
    public sealed class Tally
    {
        public int Checked { get; set; }
        public int Hits { get; set; }
    }

    /// <summary>This many looked at without a single wanted bonus: not picked up for its bonuses any more.</summary>
    internal const int GiveUpAfter = 12;
    /// <summary>From this many on, it has to bring a wanted bonus at least this often to be worth picking up.</summary>
    internal const int RateFrom = 40;
    internal const double MinRate = 0.025;

    public static BonusTrials Shared { get; } = new(Path.Combine(ProfileStore.Directory, "efsun-denemeleri.json"));

    private readonly string? _file;
    private readonly Dictionary<int, Tally> _tallies;
    private readonly object _gate = new();
    private DateTime _savedAt = DateTime.MinValue;
    private bool _changed;

    public BonusTrials(string? file = null)
    {
        _file = file;
        _tallies = [];
        if (file != null && File.Exists(file))
        {
            try { _tallies = JsonSerializer.Deserialize<Dictionary<int, Tally>>(File.ReadAllText(file)) ?? []; }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Whether an item is still worth picking up to see its bonuses.</summary>
    public bool Worth(int vnum)
    {
        lock (_gate)
        {
            if (!_tallies.TryGetValue(vnum, out var t))
                return true;
            if (t.Hits == 0 && t.Checked >= GiveUpAfter)
                return false;
            return t.Checked < RateFrom || (double)t.Hits / t.Checked >= MinRate;
        }
    }

    /// <summary>One item looked at in the bag; true when that made it not worth picking up any more (to tell once).</summary>
    public bool Record(int vnum, bool hit)
    {
        bool gaveUp;
        lock (_gate)
        {
            bool before = Worth(vnum);
            if (!_tallies.TryGetValue(vnum, out var t))
                _tallies[vnum] = t = new Tally();
            t.Checked++;
            if (hit)
                t.Hits++;
            _changed = true;
            gaveUp = before && !Worth(vnum);
        }
        SaveSometimes(gaveUp);
        return gaveUp;
    }

    public Tally? Of(int vnum)
    {
        lock (_gate)
            return _tallies.TryGetValue(vnum, out var t) ? new Tally { Checked = t.Checked, Hits = t.Hits } : null;
    }

    private void SaveSometimes(bool now)
    {
        if (_file == null || !now && DateTime.Now - _savedAt < TimeSpan.FromMinutes(1))
            return;
        string json;
        lock (_gate)
        {
            if (!_changed)
                return;
            json = JsonSerializer.Serialize(_tallies);
            _changed = false;
            _savedAt = DateTime.Now;
        }
        Trouble.WriteSafely(_file, json);
    }
}
