using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Everything the bot knows about the game comes from memory (see <see cref="GameWorldReader"/>):
/// HP/MP, position, camera, dead, the mobs around and the selected mob. Read once per analysed frame.
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>The bot stops when memory can't be read for this long (loading screens are shorter).</summary>
    private static readonly TimeSpan MemoryLostStop = TimeSpan.FromSeconds(15);

    private readonly object _worldGate = new();
    private GameWorldReader? _world;
    private TimeSpan _worldRetryAt = TimeSpan.FromDays(-1);
    private WorldSnapshot? _snapshot;
    private volatile string _memoryStatus = "Bellek bekleniyor";
    private bool _searchLogged;
    private readonly List<(TimeSpan Time, Vec3 Position)> _trail = [];
    private readonly object _missGate = new();
    private readonly List<(Point Spot, TimeSpan Time)> _missed = [];

    /// <summary>What memory reading is doing, for the UI.</summary>
    public string MemoryStatus => _memoryStatus;

    /// <summary>The last look at the game world from memory (null when it can't be read).</summary>
    public WorldSnapshot? World => Volatile.Read(ref _snapshot);

    private WorldSnapshot? ReadWorld(TimeSpan now)
    {
        lock (_worldGate)
        {
            if (_world == null)
            {
                if (now < _worldRetryAt)
                    return null;
                _worldRetryAt = now + TimeSpan.FromSeconds(3);
                _world = GameWorldReader.Open(_window.ProcessId, out string error);
                if (_world == null)
                {
                    _memoryStatus = "Bellek okunamıyor: " + error;
                    Volatile.Write(ref _snapshot, null);
                    return null;
                }
            }
            var snapshot = _world.Read();
            _memoryStatus = _world.Status;
            if (_world.Searched && !_searchLogged)
            {
                _searchLogged = true;
                _log("Oyun güncellenmiş: bellekteki başlangıç noktaları kendiliğinden yeniden bulundu ✓");
            }
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
    }

    /// <summary>Whether the character moved in the last ~0.4 s (to tell walking from being stuck).</summary>
    private bool? Walking(WorldSnapshot? world, TimeSpan now)
    {
        if (world == null)
        {
            _trail.Clear();
            return null;
        }
        _trail.Add((now, world.Player));
        _trail.RemoveAll(t => now - t.Time > TimeSpan.FromSeconds(1));
        var past = _trail.FirstOrDefault(t => now - t.Time <= TimeSpan.FromMilliseconds(500));
        if (now - past.Time < TimeSpan.FromMilliseconds(250))
            return null; // not enough history yet
        return world.Player.DistanceXY(past.Position) > 15;
    }

    /// <summary>The game's windows that are closed with Esc when they show up (from memory).</summary>
    internal static readonly HashSet<string> KnownWindows =
    [
        "EscMenu", "settings", "inventory", "Character", "skills", "community", "costume", "events",
        "friendRequests", "efsunData", "oXGameUI", "pop_ayir", "pop_weaponArmor", "happyHourBoxMobile",
    ];
    /// <summary>Parts of the screen that come and go on their own (not windows to close).</summary>
    private static readonly HashSet<string> NotWindows =
    [
        "targetDetail", "dmNotifyer", "w_view", "w_viewSmall", "ustData", "data", "UIhandler", "taskbar",
        "miniMap", "chatBox", "dmBox", "happyHourBox", "closeCinematic", "WBP_QuestingInfoPanel",
    ];
    private HashSet<string> _windowsAtStart = [];
    private string _windowsSeen = "";

    /// <summary>
    /// A window is open over the game: one of the known ones, or any other game window that wasn't
    /// showing when the bot started. Logs which one, once.
    /// </summary>
    private bool PopupOpen(WorldSnapshot? world)
    {
        var open = (world?.OpenWindows ?? [])
            .Where(w => KnownWindows.Contains(w) || !_windowsAtStart.Contains(w) && !NotWindows.Contains(w))
            .ToList();
        string seen = string.Join(", ", open);
        if (seen != _windowsSeen)
        {
            _windowsSeen = seen;
            if (open.Count > 0)
                _log($"Açık pencere (bellekten): {seen}");
        }
        return open.Count > 0;
    }

    private readonly HotspotMap _hotspots = new();
    private string _hotspotMap = "";
    private Vec3? _botHome;

    /// <summary>Learns where the mobs are and says where to go when none is on screen.</summary>
    private Vec3? Hotspot(WorldSnapshot? world, BotProfile profile, TimeSpan now)
    {
        if (world == null)
            return null;
        if (world.MapName != _hotspotMap)
        {
            _hotspots.Clear(); // another map
            _hotspotMap = world.MapName;
        }
        _hotspots.Observe(world.Player, world.Mobs.Where(m => Wanted(m, profile)), now);
        return BotRunning ? _hotspots.Best(world.Player, now, _botHome, profile.LeashMeters * 100) : null;
    }

    private int _inventoryWarnedAt = int.MaxValue;
    private string? _gmSeen;

    /// <summary>
    /// Stops the bot when a game master shows up, or when the inventory is full (the loot would
    /// be lost). Warns a little before the inventory is full.
    /// </summary>
    private void CheckSafety(WorldSnapshot world, BotProfile profile)
    {
        var gm = world.Players?.FirstOrDefault(p => p.LooksLikeGm);
        if (gm == null)
            _gmSeen = null;
        else if (_gmSeen != gm.Name)
        {
            _gmSeen = gm.Name;
            double metres = gm.Position.DistanceXY(world.Player) / 100;
            _log($"⚠ GM görüldü: {gm.Name} ({metres:0} m uzakta).");
            Notify($"⚠ GM görüldü: {gm.Name} ({metres:0} m uzakta)" + (profile.StopNearGm ? "; bot durduruluyor." : "."));
            Alert();
            if (profile.StopNearGm)
                StopFor($"GM görüldü ({gm.Name})");
        }

        if (world.InSafeZone && profile.StopInSafeZone)
        {
            _log("⚠ Karakter güvenli bölgeye (şehre) girdi; bot durduruluyor.");
            Alert();
            StopFor("güvenli bölge");
            return;
        }

        if (world.Progress?.FreeSlots() is not int free)
            return;
        if (free <= 0 && profile.StopWhenInventoryFull)
        {
            _log("⚠ Envanter doldu; toplanan eşyalar kaybolmasın diye bot durduruluyor.");
            Alert();
            StopFor("envanter dolu");
            return;
        }
        if (free <= 5 && free < _inventoryWarnedAt)
        {
            _inventoryWarnedAt = free;
            _log($"Envanterde {free} boş yer kaldı.");
        }
        else if (free > 10)
            _inventoryWarnedAt = int.MaxValue;
    }

    private void StopFor(string reason)
    {
        StopBot(reason);
        BotStopped?.Invoke(reason);
        Notify("⏹ Bot durdu: " + reason);
    }

    private readonly Dictionary<string, TimeSpan> _notified = [];
    private bool _telegramFailed;
    private bool _wasDead;
    private int _lastLevel;

    /// <summary>A message to the user's Telegram, when switched on. The same message is sent at
    /// most once a minute; failures are logged once.</summary>
    public void Notify(string text)
    {
        var p = Profile;
        if (!p.TelegramEnabled || string.IsNullOrWhiteSpace(p.TelegramToken) || string.IsNullOrWhiteSpace(p.TelegramChatId))
            return;
        var now = _clock.Elapsed;
        lock (_notified)
        {
            if (_notified.TryGetValue(text, out var sent) && now - sent < TimeSpan.FromMinutes(1))
                return;
            _notified[text] = now;
        }
        string message = $"SekoBot · {_window.Title}\n{text}";
        _ = Task.Run(async () =>
        {
            string? error = await Update.TelegramNotifier.SendAsync(p.TelegramToken, p.TelegramChatId, message);
            if (error != null && !_telegramFailed)
            {
                _telegramFailed = true;
                _log(error);
            }
            else if (error == null)
                _telegramFailed = false;
        });
    }

    /// <summary>Death and level-up messages while the bot runs.</summary>
    private void NotifyEvents(WorldSnapshot world)
    {
        if (world.Dead && !_wasDead)
            Notify("☠ Karakter öldü" + (Profile.ReviveOnDeath ? "; yeniden başlatılıyor." : "."));
        _wasDead = world.Dead;
        if (_lastLevel > 0 && world.Level > _lastLevel)
            Notify($"⭐ Seviye atladın: Sv {world.Level}");
        if (world.Level > 0)
            _lastLevel = world.Level;
    }

    private static void Alert()
    {
        try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
    }

    /// <summary>Free bag slots, when known.</summary>
    public int? FreeInventorySlots => World?.Progress?.FreeSlots();

    /// <summary>HP and MP potions left in the inventory (the items on the potion keys), when known.</summary>
    public (int? Hp, int? Mp) PotionsLeft()
    {
        if (World is not { Progress: { } progress } world)
            return (null, null);
        int? Left(Keys key) => PotionVnum(world, key) is int vnum ? progress.Count(vnum) : 0;
        return (Left(Profile.Keys.HpPotion), Left(Profile.Keys.MpPotion));
    }

    /// <summary>The item on the quick slot of a number key (1–9, 0 = 10), e.g. the HP potion.</summary>
    private static int? PotionVnum(WorldSnapshot world, Keys key)
    {
        int number = key switch
        {
            >= Keys.D1 and <= Keys.D9 => key - Keys.D0,
            Keys.D0 => 10,
            >= Keys.NumPad1 and <= Keys.NumPad9 => key - Keys.NumPad0,
            _ => 0,
        };
        return number == 0 ? null : world.Progress?.QuickSlotVnum(number);
    }

    /// <summary>A click on this spot didn't select a mob a moment ago: don't try it again right away.</summary>
    private void MarkMiss(Point spot)
    {
        lock (_missGate)
            _missed.Add((spot, _clock.Elapsed));
    }

    private bool RecentlyMissed(Point spot, TimeSpan now)
    {
        lock (_missGate)
        {
            _missed.RemoveAll(m => now - m.Time > TimeSpan.FromSeconds(4));
            return _missed.Any(m => Math.Abs(m.Spot.X - spot.X) + Math.Abs(m.Spot.Y - spot.Y) < 40);
        }
    }
}
