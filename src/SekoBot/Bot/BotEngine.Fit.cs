using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// The right mobs and place for the character, by itself ("Güçlü moblara gitme"):
/// <list type="bullet">
/// <item>every fight is recorded for the character (which kinds, how its HP held: <see cref="MobFit"/>),
/// and kinds too strong for it are no longer gone for (they are still fought when they attack);</item>
/// <item>when nothing seen on the map suits the character, the user is told to go to an easier map
/// (the bot never changes maps itself) and the bot goes on with what there is.</item>
/// </list>
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>How often the place is compared with the others seen.</summary>
    internal static readonly TimeSpan AreaCheckEvery = TimeSpan.FromMinutes(3);

    private readonly FightWatch _fights = new();
    /// <summary>Nothing seen on the map suits the character: every mob is gone for (better than standing).</summary>
    private bool _fitRelaxed;
    private string _fitToldMap = "";
    private TimeSpan? _fitCheckedAt;
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

        // Walking to a better place by itself is off for now: it moved too often (every few
        // minutes, once even for a single strong mob). The user picks the place.
    }


    /// <summary>A new start: the fight record starts over (what was learned stays in the profile).</summary>
    private void ResetFit()
    {
        _fights.Reset();
        _fitRelaxed = false;
        _fitCheckedAt = null;
        _fitLevel = 0;
    }
}
