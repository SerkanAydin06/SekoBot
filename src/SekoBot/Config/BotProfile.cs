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
    public BarSpec? HpBar { get; set; }
    public BarSpec? MpBar { get; set; }
    /// <summary>The selected target's HP bar (top of screen). Its presence means "we are fighting".</summary>
    public BarSpec? TargetBar { get; set; }
    /// <summary>Where Metin stones are searched for. Excludes the HUD.</summary>
    public NormRect SearchArea { get; set; } = new(0.03, 0.10, 0.94, 0.70);

    public double MatchThreshold { get; set; } = 0.72;
    public double[] Scales { get; set; } = [0.75, 1.0, 1.3];
    public int PerceptionFps { get; set; } = 10;

    public double PotionHpBelow { get; set; } = 0.55;
    public double PotionMpBelow { get; set; } = 0.25;
    public double StopHpBelow { get; set; } = 0.20;
    public int PotionCooldownMs { get; set; } = 1500;

    public bool UseAttackKey { get; set; } = true;
    public int ApproachTimeoutMs { get; set; } = 9000;
    public int AttackTimeoutMs { get; set; } = 120000;
    public int TargetLostGraceMs { get; set; } = 800;
    public int LootPresses { get; set; } = 4;
    public int RotateHoldMs { get; set; } = 320;
    public int RotationsBeforeMove { get; set; } = 8;
    public int MoveHoldMs { get; set; } = 1200;
    public int BlacklistMs { get; set; } = 25000;

    public KeyBindings Keys { get; set; } = new();

    [JsonIgnore]
    public bool IsReadyForBot => TargetBar != null;
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

    public static string TemplateDirectory => Path.Combine(Directory, "templates");
    private static string ProfilePath => Path.Combine(Directory, "profile.json");

    public static BotProfile Load()
    {
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
