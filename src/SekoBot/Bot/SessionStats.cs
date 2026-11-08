using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>What the bot achieved since it was started, measured in the game's memory.</summary>
/// <param name="ExpPercent">How far into the current level (0..100), or null.</param>
/// <param name="ToLevel">Estimated time until the next level at the current pace, or null.</param>
/// <param name="HpPotionsLeft">HP potions (the item on the HP potion key) left in the inventory, or null.</param>
internal sealed record SessionReport(int Kills, long Exp, int LevelsGained, long Yang, int Level, double? ExpPercent,
    TimeSpan? ToLevel, int HpPotionsUsed, int MpPotionsUsed, int? HpPotionsLeft, int? MpPotionsLeft);

/// <summary>
/// Counts kills, experience, money and potions from memory snapshots.
/// <para>
/// A kill is a mob that was alive within <see cref="KillRangeMetres"/> of the character and then
/// died (or vanished), with the character's experience going up within a few seconds of it. Mobs
/// killed by other players nearby don't give us experience, so they aren't counted.
/// </para>
/// </summary>
internal sealed class SessionStats
{
    public const double KillRangeMetres = 8;
    private static readonly TimeSpan NearMemory = TimeSpan.FromSeconds(3), ExpWindow = TimeSpan.FromSeconds(4);

    private readonly Dictionary<int, TimeSpan> _near = [];      // mob id → last seen alive near us
    private readonly Dictionary<int, TimeSpan> _died = [];      // mob id → when it died near us (waiting for exp)
    private readonly Dictionary<int, TimeSpan> _counted = [];   // already counted (ids get reused much later)
    private HashSet<int> _previousNear = [];
    private TimeSpan _lastExpUp = TimeSpan.FromDays(-1), _started;
    private long? _exp, _needExp, _yang;
    private int _level;
    private int? _hpLeft, _mpLeft;

    public int Kills { get; private set; }
    public long Exp { get; private set; }
    public int LevelsGained { get; private set; }
    public long Yang { get; private set; }
    public int HpPotionsUsed { get; private set; }
    public int MpPotionsUsed { get; private set; }

    /// <summary>Raised once when the potions of a kind run out ("HP" or "MP").</summary>
    public event Action<string>? PotionsRanOut;

    public void Reset(TimeSpan now)
    {
        _near.Clear();
        _died.Clear();
        _counted.Clear();
        _previousNear = [];
        _lastExpUp = TimeSpan.FromDays(-1);
        _exp = _needExp = _yang = null;
        _hpLeft = _mpLeft = null;
        _level = 0;
        _started = now;
        Kills = LevelsGained = HpPotionsUsed = MpPotionsUsed = 0;
        Exp = Yang = 0;
    }

    /// <param name="hpPotion">The item number of the HP potion (on its quick slot key), if known.</param>
    public void Update(WorldSnapshot world, TimeSpan now, int? hpPotion, int? mpPotion)
    {
        bool expUp = TrackProgress(world, now, hpPotion, mpPotion);
        TrackKills(world, now, expUp);
    }

    /// <returns>True when the experience went up just now.</returns>
    private bool TrackProgress(WorldSnapshot world, TimeSpan now, int? hpPotion, int? mpPotion)
    {
        if (world.Progress is not { } progress)
            return false;
        bool expUp = false;
        if (_exp is long exp && _needExp is long need)
        {
            if (world.Level > _level && _level > 0)
            {
                // Level up: the rest of the old level, plus what is already in the new one.
                Exp += need + progress.Exp;
                LevelsGained += world.Level - _level;
                expUp = true;
            }
            else if (world.Level == _level && progress.Exp > exp)
            {
                Exp += progress.Exp - exp;
                expUp = true;
            }
        }
        if (_yang is long yang && progress.Yang > yang)
            Yang += progress.Yang - yang;
        _exp = progress.Exp;
        _needExp = progress.NeedExp;
        _yang = progress.Yang;
        _level = world.Level;
        if (expUp)
            _lastExpUp = now;

        _hpLeft = Potions(progress, hpPotion, _hpLeft, used => HpPotionsUsed += used, "HP");
        _mpLeft = Potions(progress, mpPotion, _mpLeft, used => MpPotionsUsed += used, "MP");
        return expUp;
    }

    private int? Potions(CharacterProgress progress, int? vnum, int? before, Action<int> used, string kind)
    {
        if (vnum is not int item)
            return before == null ? null : 0; // the quick slot became empty: none left
        int left = progress.Count(item);
        if (before is int had && left < had)
            used(had - left);
        if (left == 0 && before is > 0)
            PotionsRanOut?.Invoke(kind);
        return left;
    }

    private void TrackKills(WorldSnapshot world, TimeSpan now, bool expUp)
    {
        double range = KillRangeMetres * 100;
        var alive = new HashSet<int>();
        var nearNow = new HashSet<int>();
        foreach (var mob in world.Mobs)
        {
            alive.Add(mob.Id);
            if (mob.Position.DistanceXY(world.Player) <= range)
            {
                _near[mob.Id] = now;
                nearNow.Add(mob.Id);
            }
        }
        // Died: shown dead, or gone from the world, after being alive near us.
        foreach (var dead in world.DeadMobs ?? [])
            MarkDied(dead.Id, now);
        foreach (int id in _previousNear)
            if (!alive.Contains(id))
                MarkDied(id, now);
        _previousNear = nearNow;

        if (expUp)
        {
            foreach (var (id, when) in _died.ToList())
            {
                if (now - when > ExpWindow)
                    continue;
                Kills++;
                _counted[id] = now;
                _died.Remove(id);
            }
        }
        foreach (var (id, when) in _died.ToList())
            if (now - when > ExpWindow)
                _died.Remove(id); // no experience came: killed by someone else
        foreach (var (id, when) in _near.ToList())
            if (now - when > TimeSpan.FromSeconds(30))
                _near.Remove(id);
        foreach (var (id, when) in _counted.ToList())
            if (now - when > TimeSpan.FromMinutes(2))
                _counted.Remove(id);
    }

    private void MarkDied(int id, TimeSpan now)
    {
        if (_counted.ContainsKey(id) || _died.ContainsKey(id))
            return;
        if (_near.TryGetValue(id, out var seen) && now - seen <= NearMemory)
        {
            // Experience that came just before the death was noticed counts too.
            if (now - _lastExpUp <= TimeSpan.FromSeconds(1.5))
            {
                Kills++;
                _counted[id] = now;
                return;
            }
            _died[id] = now;
        }
    }

    public SessionReport Report(WorldSnapshot? world, TimeSpan now)
    {
        double? percent = null;
        TimeSpan? toLevel = null;
        if (world?.Progress is { } progress && progress.Exp + progress.NeedExp > 0)
        {
            percent = 100.0 * progress.Exp / (progress.Exp + progress.NeedExp);
            var running = now - _started;
            if (Exp > 0 && running >= TimeSpan.FromMinutes(1))
                toLevel = TimeSpan.FromHours(progress.NeedExp / (Exp / running.TotalHours));
        }
        return new SessionReport(Kills, Exp, LevelsGained, Yang, world?.Level ?? _level, percent, toLevel,
            HpPotionsUsed, MpPotionsUsed, _hpLeft, _mpLeft);
    }
}
