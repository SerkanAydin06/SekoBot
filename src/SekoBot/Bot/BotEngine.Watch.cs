using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// While farming: measuring how long mobs take to come back (<see cref="RespawnTracker"/>), and
/// moving to another place when another player farms ours (<see cref="PlayerWatch"/>). The new
/// place is picked from the mobs seen in the last minutes, away from the other players.
/// </summary>
internal sealed partial class BotEngine
{
    private readonly RespawnTracker _respawn = RespawnTracker.Load();
    /// <summary>Trees and rocks run into, per map (kept between sessions, see <see cref="ObstacleMap"/>).</summary>
    private readonly ObstacleMap _obstacles = ObstacleMap.Load();
    private TimeSpan _obstaclesSavedAt;
    private readonly PlayerWatch _players = new();
    private static string JournalPath => Path.Combine(ProfileStore.Directory, "dusme-kaydi.json");
    private readonly DropJournal _journal = DropJournal.Load(JournalPath);
    private TimeSpan _journalSavedAt;

    /// <summary>The kinds of mobs on the current map, as the game describes them.</summary>
    public IReadOnlyList<MobInfo> MapMobs => _world?.MapMobs ?? [];

    /// <summary>What a kind of mob was seen to drop on a map (a copy, for the UI).</summary>
    public MobDrops? Drops(string map, int mob)
    {
        lock (_journal)
            return _journal.For(map, mob) is { } d ? new MobDrops { Kills = d.Kills, Yang = d.Yang, Items = new(d.Items) } : null;
    }

    /// <summary>Every map and mob something was recorded for (map, mob number).</summary>
    public List<(string Map, int Mob)> JournalEntries()
    {
        lock (_journal)
            return [.. _journal.Maps.SelectMany(m => m.Value.Keys.Select(k => (m.Key, k)))];
    }
    private readonly Dictionary<int, (WorldMob Mob, TimeSpan Seen)> _seenMobs = [];
    private bool _respawnDirty;
    private TimeSpan _respawnSavedAt, _itemsSavedAt;

    /// <summary>The measured wait for mobs of a level, when known (for the area choice).</summary>
    private double? RespawnSeconds(int level)
    {
        lock (_respawn)
            return _respawn.Seconds(level);
    }

    /// <summary>What was measured so far, for the UI: "Sv 19: 48 sn (6)".</summary>
    public string RespawnSummary()
    {
        lock (_respawn)
            return string.Join(", ", _respawn.Samples.Where(s => s.Value.Count >= 2).OrderBy(s => s.Key)
                .Select(s => $"Sv {s.Key}: {_respawn.Seconds(s.Key):0} sn ({s.Value.Count})"));
    }

    private void WatchTick(WorldSnapshot world, BotProfile profile, TimeSpan now)
    {
        lock (_respawn)
        {
            if (!_respawnHooked)
            {
                _respawn.Measured += (level, seconds) => _respawnDirty = true;
                _respawnHooked = true;
            }
            _respawn.Update(world, now);
            if (now - _itemsSavedAt > TimeSpan.FromSeconds(60))
            {
                ItemBook.SaveIfChanged();
                _itemsSavedAt = now;
            }
            if (_obstacles.Changed && now - _obstaclesSavedAt > TimeSpan.FromSeconds(60))
            {
                _obstacles.Save();
                _obstaclesSavedAt = now;
            }
            if (_respawnDirty && now - _respawnSavedAt > TimeSpan.FromSeconds(60))
            {
                _respawn.Save();
                _respawnDirty = false;
                _respawnSavedAt = now;
            }
        }

        lock (_journal)
        {
            _journal.Update(world, now);
            if (_journal.Changed && now - _journalSavedAt > TimeSpan.FromSeconds(60))
            {
                _journal.Save(JournalPath);
                _journalSavedAt = now;
            }
        }

        // A special item on the ground ("always pick up"): tell the user, once per item.
        // The game makes a new object for an item when we come back near it: told once per item and place.
        foreach (var drop in world.Drops ?? [])
            if (AlwaysLoot(drop.Vnum, profile) &&
                _announcedDrops.Add($"{drop.Vnum}:{Math.Round(drop.Position.X / 200)}:{Math.Round(drop.Position.Y / 200)}"))
            {
                string found = $"🎁 Yere düştü: {ItemBook.NameOf(drop.Vnum)} ({drop.Position.DistanceXY(world.Player) / 100:0} m) — alınıyor.";
                _log(found);
                if (profile.NotifyDrops)
                    Notify(found);
            }
        if (_announcedDrops.Count > 500)
            _announcedDrops.Clear();

        foreach (var mob in world.Mobs)
            _seenMobs[mob.Id] = (mob, now);
        foreach (var (id, (_, seen)) in _seenMobs.ToList())
            if (now - seen > TimeSpan.FromMinutes(3))
                _seenMobs.Remove(id);

        // The user's own characters (in another game next to this one) aren't strangers taking our place.
        var players = (world.Players ?? []).Where(p => !OwnCharacter(p.Name)).ToList();
        string? reason = _players.Update(now, _session.Exp, world.Player, players, profile.AvoidPlayerMinutes, profile.AvoidPlayerExpDrop);
        if (reason == null)
            return;
        var others = players.Where(p => !p.IsGm).Select(p => p.Position).ToList();
        var old = _farmSpot ?? world.Player;
        var spot = FarmSpotFinder.Best(_seenMobs.Values.Select(s => s.Mob).ToList(), profile, world.Player, world.MapName,
            world.AttackPower, respawn: RespawnSeconds,
            allowed: v => v.DistanceXY(old) > 1500 && others.All(o => o.DistanceXY(v) > 2500), level: world.Level);
        if (spot == null)
        {
            _log($"👥 {reason}; ama yakında başka uygun alan görülmedi, burada devam ediliyor.");
            return;
        }
        double metres = spot.Position.DistanceXY(world.Player) / 100;
        MoveFarmSpot(spot, profile, $"👥 {reason}. Yeni alana geçiliyor ({metres:0} m): {spot.Names}.");
    }

    /// <summary>Farms around another place from now on (the bot walks there); tells the user.</summary>
    private void MoveFarmSpot(FarmSpot spot, BotProfile profile, string message)
    {
        lock (_botGate)
        {
            _farmSpot = spot.Position;
            _botHome = spot.Position;
            profile.FarmSpot = spot;
            _brain.NewFarmSpot();
        }
        _log(message);
        Notify(message);
        AreaScanFinished?.Invoke(message); // the UI saves the profile
    }

    /// <summary>One of the user's own characters: a SekoBot has played it (it has its settings file).</summary>
    internal static bool OwnCharacter(string name)
    {
        try { return name.Length > 0 && CharacterSettings.Exists(name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// <summary>Who an item-gathering character follows: the one named, else the nearest of the
    /// user's own characters in sight (not itself).</summary>
    internal static (string Name, Vec3 Position)? FollowWho(IReadOnlyList<OtherPlayer>? players, Vec3? me, string self, string wanted)
    {
        if (players == null || players.Count == 0)
            return null;
        var names = wanted.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var pick = names.Length > 0
            ? players.Where(p => names.Any(n => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            : players.Where(p => !p.Name.Equals(self, StringComparison.OrdinalIgnoreCase) && OwnCharacter(p.Name));
        var leader = me is Vec3 at ? pick.OrderBy(p => p.Position.DistanceXY(at)).FirstOrDefault() : pick.FirstOrDefault();
        return leader == null ? null : (leader.Name, leader.Position);
    }

    private bool _respawnHooked;
    private readonly HashSet<string> _announcedDrops = [];
}
