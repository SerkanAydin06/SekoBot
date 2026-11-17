using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// The right mobs and place for the character, by itself ("Uygun alanı kendisi seç"):
/// <list type="bullet">
/// <item>every fight is recorded for the character (which kinds, how its HP held: <see cref="MobFit"/>),
/// and kinds too strong for it are no longer gone for (they are still fought when they attack);</item>
/// <item>every few minutes and right after a level-up the place is scored against the others seen
/// on the map (experience an hour from the mobs that suit it): one clearly better is walked to;</item>
/// <item>when nothing seen on the map suits the character, the user is told to go to an easier map
/// (the bot never changes maps itself) and the bot goes on with what there is.</item>
/// </list>
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>How often the place is compared with the others seen.</summary>
    internal static readonly TimeSpan AreaCheckEvery = TimeSpan.FromMinutes(3);
    /// <summary>After a move, this long before the next one (no going back and forth).</summary>
    internal static readonly TimeSpan MoveRest = TimeSpan.FromMinutes(5);
    /// <summary>A place counts as better only with this much more experience an hour.</summary>
    internal const double BetterBy = 1.3;
    /// <summary>A better place closer than this (world units, 15 m) isn't worth a move.</summary>
    internal const double MoveAtLeast = 1500;

    private readonly FightWatch _fights = new();
    /// <summary>Nothing seen on the map suits the character: every mob is gone for (better than standing).</summary>
    private bool _fitRelaxed;
    private string _fitToldMap = "";
    private TimeSpan? _fitCheckedAt, _movedAt;
    private int _fitLevel;

    internal void FitTick(WorldSnapshot world, BotProfile profile, TimeSpan now)
    {
        if (_fights.Update(world.Mobs, world.Stats?.HpFraction, now) is { } fight)
        {
            profile.MobFights = MobFit.Record(profile.MobFights, fight.Kinds, fight.Most, fight.LowestHp);
            try { ProfileStore.Save(profile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (!profile.AutoArea)
        {
            _fitRelaxed = false;
            return;
        }
        bool levelUp = _fitLevel > 0 && world.Level > _fitLevel;
        if (world.Level > 0)
            _fitLevel = world.Level;
        if (!levelUp && _fitCheckedAt is { } checkedAt && now - checkedAt < AreaCheckEvery)
            return;
        _fitCheckedAt = now;

        var seen = _seenMobs.Values.Select(s => s.Mob).Where(m => !m.IsMetin && m.Level > 0).ToList();
        if (seen.Count < 5)
            return;
        string name = CharacterName.Length > 0 ? CharacterName : "Karakter";
        bool anyFit = profile.MaxMobLevel > 0 || seen.Any(m => !MobFit.TooHard(m, profile, world.Level));
        if (!anyFit)
        {
            if (!_fitRelaxed)
            {
                _fitRelaxed = true;
                if (_fitToldMap != world.MapName)
                {
                    _fitToldMap = world.MapName;
                    string message = $"⚠ {world.MapName} haritasında {name} (Sv {world.Level}) için uygun mob görülmedi: buradakiler Sv " +
                                     $"{seen.Min(m => m.Level)}–{seen.Max(m => m.Level)}. Bot buradakilerle devam ediyor, ama daha kolay bir haritaya " +
                                     "geçmen çok daha verimli olur.";
                    _log(message);
                    Notify(message);
                }
            }
            return;
        }
        if (_fitRelaxed)
        {
            _fitRelaxed = false;
            _log($"Bu karaktere uygun moblar görüldü; çok güçlü olanlara artık gidilmiyor.");
        }

        if (_movedAt is { } movedAt && now - movedAt < MoveRest)
            return;
        var here = _botHome ?? world.Player;
        double hereScore = FarmSpotFinder.Score(seen, profile, here, world.AttackPower, RespawnSeconds, world.Level, fit: true);
        var others = (world.Players ?? []).Where(p => !p.IsGm).Select(p => p.Position).ToList();
        var spot = FarmSpotFinder.Best(seen, profile, world.Player, world.MapName, world.AttackPower, out double best,
            respawn: RespawnSeconds, allowed: v => v.DistanceXY(here) > MoveAtLeast && others.All(o => o.DistanceXY(v) > 2500),
            level: world.Level, fit: true);
        if (spot == null || best <= 0 || best < hereScore * BetterBy)
            return;
        _movedAt = now;
        double metres = spot.Position.DistanceXY(world.Player) / 100;
        string why = levelUp ? $"Sv {world.Level} oldu" : hereScore <= 0 ? "Burada uygun mob kalmadı" : "Burası daha az verimli";
        MoveFarmSpot(spot, profile, $"🧭 {why}: {metres:0} m ötede {name} için daha iyi alan (tahmini saatte {Short(best)} EXP, " +
                                    $"burada {Short(hereScore)}): {spot.Names}. Oraya geçiliyor.");
    }

    private static string Short(double exp) => exp >= 1_000_000 ? $"{exp / 1_000_000:0.#} milyon" : exp >= 1000 ? $"{exp / 1000:0} bin" : $"{exp:0}";

    /// <summary>A new start: the fight record starts over (what was learned stays in the profile).</summary>
    private void ResetFit()
    {
        _fights.Reset();
        _fitRelaxed = false;
        _fitCheckedAt = null;
        _fitLevel = 0;
    }
}
