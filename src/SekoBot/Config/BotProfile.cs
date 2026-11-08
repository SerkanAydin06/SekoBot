using System.Text.Json;
using System.Text.Json.Serialization;

namespace SekoBot.Config;

internal sealed class KeyBindings
{
    public Keys Attack { get; set; } = Keys.Space;
    public Keys Pickup { get; set; } = Keys.Z;
    public Keys HpPotion { get; set; } = Keys.D1;
    public Keys MpPotion { get; set; } = Keys.D2;
    public Keys RotateCamera { get; set; } = Keys.E;
    public Keys MoveForward { get; set; } = Keys.W;
    public Keys MoveLeft { get; set; } = Keys.A;
    public Keys MoveBack { get; set; } = Keys.S;
    public Keys MoveRight { get; set; } = Keys.D;
    /// <summary>Held at start: tilts the camera to look straight down.</summary>
    public Keys CameraTop { get; set; } = Keys.G;
    /// <summary>Held at start: zooms the camera out.</summary>
    public Keys ZoomOut { get; set; } = Keys.F;
    /// <summary>Buff skills, pressed in a loop (see <see cref="BotProfile.SkillEverySec"/>).</summary>
    public Keys[] Skills { get; set; } = [Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5];
    /// <summary>Closes a window opened over the game (shop advert after a level-up).</summary>
    public Keys CloseWindow { get; set; } = Keys.Escape;
}

internal sealed class BotProfile
{
    /// <summary>Current profile format; older files are upgraded once by <see cref="ProfileStore"/>.</summary>
    public const int CurrentVersion = 3;

    /// <summary>Number of skill keys (F1–F5).</summary>
    public const int SkillSlots = 5;

    /// <summary>How often each skill key (F1–F5) is pressed again, in seconds; 0 = never.</summary>
    public int[] SkillEverySec { get; set; } = [60, 60, 0, 0, 0];
    /// <summary>At least this long between two skill presses.</summary>
    public int SkillGapMs { get; set; } = 1500;
    /// <summary>Stand still this long while a skill is cast.</summary>
    public int SkillCastMs { get; set; } = 1000;

    /// <summary>0 in files written before versioning.</summary>
    public int Version { get; set; }

    /// <summary>Title of the game window; the bot connects to it on its own.</summary>
    public string GameWindowTitle { get; set; } = "Mobile2 Global";

    public int PerceptionFps { get; set; } = 10;

    public double PotionHpBelow { get; set; } = 0.55;
    public double PotionMpBelow { get; set; } = 0.25;
    public int PotionCooldownMs { get; set; } = 1500;
    /// <summary>A low HP/MP reading must last this long before a potion or stop (filters bad frames).</summary>
    public int BarConfirmMs { get; set; } = 300;

    /// <summary>
    /// Walk mode (default): camera to top-down + zoomed out (G, F), walk with W/A/S/D to the
    /// nearest mob (from memory), hold Space when it is next to the character. Off: click mode.
    /// </summary>
    public bool NavigateMode { get; set; } = true;
    /// <summary>Walk into the biggest nearby group of mobs instead of to the single nearest one.</summary>
    public bool PreferCrowds { get; set; } = true;
    /// <summary>A group needs at least this many mobs.</summary>
    public int CrowdMinMobs { get; set; } = 3;
    /// <summary>Mobs within this share of the screen height of each other form a group.</summary>
    public double CrowdRadius { get; set; } = 0.12;

    /// <summary>Stay within this many metres of the starting point (0 = no limit). The position
    /// comes from memory.</summary>
    public double LeashMeters { get; set; } = 40;
    /// <summary>Walking keys held but the character not moving for this long = stuck against something.</summary>
    public int BlockedMs { get; set; } = 700;
    /// <summary>How long to step sideways around an obstacle.</summary>
    public int SidestepMs { get; set; } = 700;

    /// <summary>How long G and F are held at start to set up the camera.</summary>
    public int CameraSetupHoldMs { get; set; } = 1500;
    /// <summary>Press G and F again this often, in case the camera moved (0 = only at start).</summary>
    public int CameraRefreshMs { get; set; } = 30000;
    /// <summary>A mob counts as "next to the character" within this part of the screen height.</summary>
    public double AttackRadius { get; set; } = 0.05;
    /// <summary>After dying: wait until "Burada yeniden başla" can be clicked, click it, drink HP
    /// potions up to <see cref="ReviveHealTo"/>, then carry on. While on, low HP doesn't stop the bot.</summary>
    public bool ReviveOnDeath { get; set; } = true;
    public double ReviveHealTo { get; set; } = 0.90;
    /// <summary>Stop healing after this long even if HP isn't there yet.</summary>
    public int ReviveHealMaxMs { get; set; } = 30000;

    /// <summary>Close windows that pop up over the game (Esc once, again after 3 s if still open).</summary>
    public bool ClosePopups { get; set; } = true;
    /// <summary>While attacking, press the pickup key this often.</summary>
    public int LootEveryMs { get; set; } = 1500;
    /// <summary>No mob on screen: walk forward this long, then turn the camera, and look again.</summary>
    public int ExploreWalkMs { get; set; } = 1200;
    /// <summary>Walking to the same mob longer than this means stuck: try exploring instead.</summary>
    public int MoveStuckMs { get; set; } = 8000;

    /// <summary>Click mode: click the mobs (from memory) and check in memory that one got selected.</summary>
    public bool AutoTarget { get; set; } = true;
    /// <summary>How long to look for a mob before turning the camera.</summary>
    public int SearchTimeoutMs { get; set; } = 6000;
    /// <summary>With a selected mob, also hold the attack key (off: the click alone makes the character attack).</summary>
    public bool UseAttackKeyOnTarget { get; set; }
    /// <summary>Click the mob again when its HP has not gone down for this long.</summary>
    public int StallMs { get; set; } = 3000;

    /// <summary>How long the attack key is held per cycle (blind attack, without mob search).</summary>
    public int FightMs { get; set; } = 4000;
    /// <summary>Upper limit while the selected mob is still alive.</summary>
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
            {
                var loaded = JsonSerializer.Deserialize<BotProfile>(File.ReadAllText(ProfilePath), Options);
                if (loaded != null)
                {
                    Normalize(loaded);
                    if (Upgrade(loaded))
                        try { Save(loaded); } catch { }
                    return loaded;
                }
            }
        }
        catch
        {
            // A broken file must never block the app; keep a copy for the user.
            try { File.Copy(ProfilePath, ProfilePath + ".bozuk", true); } catch { }
        }
        return new BotProfile { Version = BotProfile.CurrentVersion };
    }

    /// <summary>Profiles written by older versions lack the skill lists or have them shorter.</summary>
    internal static void Normalize(BotProfile profile)
    {
        var defaults = new BotProfile();
        if (profile.SkillEverySec is not { Length: BotProfile.SkillSlots })
        {
            var every = new int[BotProfile.SkillSlots];
            for (int i = 0; i < every.Length; i++)
                every[i] = profile.SkillEverySec is { } old && i < old.Length ? Math.Max(0, old[i]) : defaults.SkillEverySec[i];
            profile.SkillEverySec = every;
        }
        if (profile.Keys.Skills is not { Length: BotProfile.SkillSlots })
            profile.Keys.Skills = defaults.Keys.Skills;
    }

    /// <summary>
    /// Brings an older profile up to date. Every setting is saved to the file, so a changed
    /// default would never reach existing users; values that still equal the old default are
    /// moved to the new one, values the user picked are kept.
    /// </summary>
    /// <returns>True when something changed.</returns>
    internal static bool Upgrade(BotProfile profile)
    {
        if (profile.Version >= BotProfile.CurrentVersion)
            return false;
        // v2: attacking at 10 % of the screen height started too far from the mob.
        if (Math.Abs(profile.AttackRadius - 0.10) < 0.0001)
            profile.AttackRadius = 0.05;
        // v3: everything from memory; the area limit is in metres now (the old one was a share of the big map).
        if (profile.Version < 3 && profile.LeashMeters <= 0)
            profile.LeashMeters = 40;
        profile.Version = BotProfile.CurrentVersion;
        return true;
    }

    public static void Save(BotProfile profile)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string temp = ProfilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, Options));
        File.Move(temp, ProfilePath, true);
    }
}
