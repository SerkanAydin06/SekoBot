using System.Text;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Mobs from memory: every living, attackable mob of the game world (see <see cref="GameWorldReader"/>),
/// put on the screen with the game camera, so the bot sees every mob, also those without a name shown.
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>What the bot sees on screen from memory: the mobs it may go for (nearest to us
    /// first), the ones to go for first, and the items close by. Null when the camera can't be read.</summary>
    internal static (List<Point> Mobs, List<Point> Priority, List<GroundSpot> Drops, bool Metins)? MemoryView(
        WorldSnapshot? world, BotProfile profile, int width, int height)
    {
        if (world?.Camera is not CameraView camera)
            return null;
        var mobs = new List<Point>();
        var attackers = new List<Point>();
        var metins = new List<Point>();
        foreach (var mob in world.ByDistance)
        {
            if (!Wanted(mob, profile) || camera.ToScreen(mob.Position, width, height) is not PointF p || !OnPlayfield(p, width, height))
                continue;
            var spot = Point.Round(p);
            mobs.Add(spot);
            if (mob.AttacksMe && profile.AttackersFirst)
                attackers.Add(spot);
            if (mob.IsMetin && profile.Metins == MetinMode.Prefer)
                metins.Add(spot);
        }
        var drops = new List<GroundSpot>();
        if (profile.PickUpDrops)
            foreach (var item in (world.Drops ?? []).OrderBy(d => d.Position.DistanceXY(world.Player)))
                if (item.Position.DistanceXY(world.Player) <= profile.DropRangeMetres * 100 &&
                    camera.ToScreen(item.Position, width, height) is PointF p && OnPlayfield(p, width, height))
                    drops.Add(new GroundSpot(item.Id, Point.Round(p)));
        // Metin stones set to "first" come before everything, also before mobs attacking us
        // (those stand around the stone and get hit too).
        return (mobs, metins.Count > 0 ? metins : attackers, drops, metins.Count > 0);
    }

    /// <summary>Whether the bot should go for this mob, by the profile's choices. A mob that is
    /// attacking us is always fought back, and a Metin stone set to "first" is always gone for
    /// (the level and name filters are for mobs).</summary>
    internal static bool Wanted(WorldMob mob, BotProfile profile)
    {
        if (mob.IsMetin)
            return profile.Metins != MetinMode.Skip &&
                   (profile.Metins == MetinMode.Prefer || WantedByFilters(mob, profile));
        if (mob.AttacksMe)
            return true;
        return WantedByFilters(mob, profile);
    }

    private static bool WantedByFilters(WorldMob mob, BotProfile profile)
    {
        if (mob.AttacksMe)
            return true;
        if (profile.MaxMobLevel > 0 && mob.Level > profile.MaxMobLevel)
            return false;
        if (Names(profile.SkipMobs).Any(n => mob.Name.Contains(n, StringComparison.CurrentCultureIgnoreCase)))
            return false;
        var only = Names(profile.OnlyMobs);
        if (only.Count > 0 && !only.Any(n => mob.Name.Contains(n, StringComparison.CurrentCultureIgnoreCase)))
            return false;
        return true;
    }

    private static List<string> Names(string list) =>
        list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Where the selected mob is on screen, from memory (null when unknown or off screen).</summary>
    private static Point? MemoryTargetSpot(WorldSnapshot? world, int width, int height) =>
        world is { Target: { Hp: > 0 } target, Camera: { } camera } &&
        camera.ToScreen(target.Position, width, height) is PointF p && OnPlayfield(p, width, height)
            ? Point.Round(p)
            : null;

    /// <summary>On screen and not under the game's buttons (a click there would hit the button).</summary>
    internal static bool OnPlayfield(PointF p, int width, int height)
    {
        double x = p.X / width, y = p.Y / height;
        if (x < 0.02 || x > 0.98 || y < 0.04 || y > 0.9)
            return false;
        if (x > 0.86 && y < 0.36) // minimap, server name, clock
            return false;
        if (x < 0.11 && y > 0.18 && y < 0.78) // quest, message, event buttons on the left
            return false;
        return true;
    }

    /// <summary>"Mobları göster": the mobs around, read from memory, as text for the log.</summary>
    public string DescribeMobs()
    {
        if (World is not { } world)
            return "Mob listesi okunamadı: " + _memoryStatus + ". SekoBot'u yönetici olarak açtığından emin ol.";
        var mobs = world.ByDistance.ToList();
        var text = new StringBuilder();
        text.Append($"Bellekten: {mobs.Count} mob, {world.OtherPlayers} başka oyuncu (harita {world.MapName}, " +
                    $"konum X={world.Player.X:0}, Y={world.Player.Y:0}).");
        if (world.Target is { } target)
            text.Append($" Seçili hedef: {target.Name} (Sv {target.Level}) HP {target.Hp}/{target.MaxHp}.");
        if (world.Stats is { } stats)
            text.Append($" Karakter: Sv {world.Level}, HP {stats.Hp}/{stats.MaxHp}, MP {stats.Mp}/{stats.MaxMp}{(world.Dead ? ", ÖLÜ" : "")}.");
        if (world.Camera == null)
            text.Append(" Kamera okunamadı: moblar ekrana yerleştirilemiyor.");
        foreach (var mob in mobs.Take(12))
            text.Append($"\n  • {mob.Name} (Sv {mob.Level})  HP {mob.Hp}/{mob.MaxHp}  {mob.Position.DistanceXY(world.Player) / 100:0.0} m");
        if (mobs.Count > 12)
            text.Append($"\n  … ve {mobs.Count - 12} mob daha");
        return text.ToString();
    }
}
