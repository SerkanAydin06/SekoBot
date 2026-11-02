using System.Text.Json;
using System.Text.Json.Serialization;

namespace SekoBot.Config;

/// <summary>Rectangle in 0..1 coordinates of the game client area (resolution independent).</summary>
internal readonly record struct NormRect(double X, double Y, double W, double H)
{
    public Rectangle ToPixels(int width, int height)
    {
        var r = Rectangle.FromLTRB(
            (int)Math.Round(X * width), (int)Math.Round(Y * height),
            (int)Math.Round((X + W) * width), (int)Math.Round((Y + H) * height));
        return Rectangle.Intersect(r, new Rectangle(0, 0, width, height));
    }

    public static NormRect FromPixels(Rectangle r, int width, int height) =>
        new((double)r.X / width, (double)r.Y / height, (double)r.Width / width, (double)r.Height / height);
}

/// <summary>A horizontal bar (HP/MP/target HP) and the colour of its filled part.</summary>
internal sealed class BarSpec
{
    public NormRect Area { get; set; }
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public int Tolerance { get; set; } = 70;
    /// <summary>Found automatically (may be widened/moved by the bot); false = marked by hand.</summary>
    public bool Auto { get; set; }
}

internal sealed class KeyBindings
{
    public Keys Attack { get; set; } = Keys.Space;
    public Keys Pickup { get; set; } = Keys.Z;
    public Keys HpPotion { get; set; } = Keys.D1;
    public Keys MpPotion { get; set; } = Keys.D2;
    public Keys RotateCamera { get; set; } = Keys.E;
    public Keys MoveForward { get; set; } = Keys.W;
}

internal sealed class BotProfile
{
    /// <summary>Title of the game window; the bot connects to it on its own.</summary>
    public string GameWindowTitle { get; set; } = "Mobile2 Global";

    public BarSpec? HpBar { get; set; }
    public BarSpec? MpBar { get; set; }
    /// <summary>Optional: the selected target's HP bar. Lets the bot keep hitting a living mob
    /// and turn around when nothing is left.</summary>
    public BarSpec? TargetBar { get; set; }

    public int PerceptionFps { get; set; } = 10;
    /// <summary>Find HP/MP/target bars on screen automatically (manual marks always win).</summary>
    public bool AutoDetectBars { get; set; } = true;

    public double PotionHpBelow { get; set; } = 0.55;
    public double PotionMpBelow { get; set; } = 0.25;
    public double StopHpBelow { get; set; } = 0.20;
    public int PotionCooldownMs { get; set; } = 1500;
    /// <summary>A low HP/MP reading must last this long before a potion or stop (filters bad frames).</summary>
    public int BarConfirmMs { get; set; } = 300;

    /// <summary>Find mobs by hovering (attack cursor) and click them; needs the target bar to learn.</summary>
    public bool AutoTarget { get; set; } = true;
    /// <summary>How long to wait for a target bar after asking for a mob search.</summary>
    public int SearchTimeoutMs { get; set; } = 6000;
    /// <summary>How long the mouse rests on each point while looking for the attack cursor.</summary>
    public int HoverDelayMs { get; set; } = 80;

    /// <summary>How long the attack key is held per cycle (blind attack, without mob search).</summary>
    public int FightMs { get; set; } = 4000;
    /// <summary>Upper limit while the target bar still shows a living target.</summary>
    public int FightMaxMs { get; set; } = 20000;
    public int LootPresses { get; set; } = 3;
    /// <summary>Rotate the camera every N cycles (0 = only when nothing is around).</summary>
    public int RotateEveryCycles { get; set; } = 3;
    public int RotateHoldMs { get; set; } = 350;
    /// <summary>Walk forward every N cycles (0 = never).</summary>
    public int MoveEveryCycles { get; set; } = 0;
    public int MoveHoldMs { get; set; } = 1000;

    /// <summary>Recording: store one PNG frame this often (potion/stop frames are always stored).</summary>
    public int RecordFrameIntervalMs { get; set; } = 1000;
    /// <summary>Recording: stop storing frames above this size; bar values keep being recorded.</summary>
    public int RecordMaxMb { get; set; } = 1500;

    public KeyBindings Keys { get; set; } = new();
}

internal static class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SekoBot");

    private static string ProfilePath => Path.Combine(Directory, "profile.json");

    public static BotProfile Load()
    {
        // Leftover from v0.4.0 (Metin stone templates); no longer used.
        try { System.IO.Directory.Delete(Path.Combine(Directory, "templates"), true); } catch { }
        try
        {
            if (File.Exists(ProfilePath))
                return JsonSerializer.Deserialize<BotProfile>(File.ReadAllText(ProfilePath), Options) ?? new BotProfile();
        }
        catch
        {
            // A broken file must never block the app; keep a copy for the user.
            try { File.Copy(ProfilePath, ProfilePath + ".bozuk", true); } catch { }
        }
        return new BotProfile();
    }

    public static void Save(BotProfile profile)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string temp = ProfilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, Options));
        File.Move(temp, ProfilePath, true);
    }
}
