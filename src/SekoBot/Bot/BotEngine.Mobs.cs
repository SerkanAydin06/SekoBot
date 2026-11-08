using System.Text;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Mobs from memory: every living, attackable mob of the game world (see <see cref="GameWorldReader"/>),
/// put on the screen with the game camera, so the bot sees every mob, also those without a name shown.
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>Where the mobs from memory are on screen (client pixels, nearest to us first),
    /// or null when the camera can't be read.</summary>
    private static List<Point>? MemoryMobs(WorldSnapshot? world, int width, int height)
    {
        if (world?.Camera is not CameraView camera)
            return null;
        var points = new List<Point>();
        foreach (var mob in world.ByDistance)
            if (camera.ToScreen(mob.Position, width, height) is PointF p && OnPlayfield(p, width, height))
                points.Add(Point.Round(p));
        return points;
    }

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
