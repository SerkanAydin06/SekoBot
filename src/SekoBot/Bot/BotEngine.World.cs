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
