using System.Text.Json;
using System.Text.Json.Serialization;

namespace SekoBot.Config;

/// <summary>What to do with Metin stones.</summary>
internal enum MetinMode
{
    /// <summary>Never go for them.</summary>
    Skip,
    /// <summary>Like any other mob.</summary>
    Normal,
    /// <summary>Go for them first (they give more experience and items).</summary>
    Prefer,
}

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
    /// <summary>Opens and closes the inventory (used when throwing items away).</summary>
    public Keys Inventory { get; set; } = Keys.I;
    /// <summary>Opens and closes the M map (read once per map for the whole map's NPCs).</summary>
    public Keys Map { get; set; } = Keys.M;
}

internal sealed class BotProfile
{
    /// <summary>Current profile format; older files are upgraded once by <see cref="ProfileStore"/>.</summary>
    public const int CurrentVersion = 8;

    /// <summary>Number of skill keys (F1–F5).</summary>
    public const int SkillSlots = 5;

    /// <summary>How often each skill key (F1–F5) is pressed again, in seconds; 0 = never.</summary>
    public int[] SkillEverySec { get; set; } = [60, 60, 0, 0, 0];
    /// <summary>Use the skills on the quick slots by what the game says they do (buffs kept up,
    /// area attacks on groups, single attacks while fighting). Off: F1–F5 every N seconds.</summary>
    public bool AutoSkills { get; set; } = true;
    /// <summary>An area skill is used when at least this many mobs are around.</summary>
    public int AreaSkillMinMobs { get; set; } = 5;
    /// <summary>With this many HP potions left (or fewer), get away from the mobs and stop in a quiet
    /// spot, so the character isn't killed without potions (0 = off).</summary>
    public int RetreatAtPotions { get; set; } = 5;
    /// <summary>HP below this (0..1) with 3 or more mobs on us: stop fighting and get away while
    /// drinking, until HP is back above <see cref="FleeUntilHp"/> (0 = off).</summary>
    public double FleeAtHp { get; set; } = 0.30;
    public double FleeUntilHp { get; set; } = 0.60;
    /// <summary>At least this long between two skill presses.</summary>
    public int SkillGapMs { get; set; } = 1500;
    /// <summary>Learned for the character: how much longer than SkillGapMs its skills need between
    /// presses (a long casting animation cuts off the next skill). Kept with the character.</summary>
    public int SkillGapLearnedMs { get; set; }
    /// <summary>Stand still this long while a skill is cast.</summary>
    public int SkillCastMs { get; set; } = 1000;

    /// <summary>0 in files written before versioning.</summary>
    public int Version { get; set; }

    /// <summary>Title of the game window; the bot connects to it on its own.</summary>
    public string GameWindowTitle { get; set; } = "Mobile2 Global";

    /// <summary>Decisions per second (everything comes from memory, so this is cheap).</summary>
    public int PerceptionFps { get; set; } = 20;

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
    /// <summary>Walk into the biggest nearby group of mobs on screen (old; replaced by <see cref="PullGroups"/>).</summary>
    public bool PreferCrowds { get; set; }
    /// <summary>Gather mobs first: hit one mob of each nearby spawn group (the group follows), then
    /// fight them all together.</summary>
    public bool PullGroups { get; set; } = true;
    /// <summary>Stop gathering once this many mobs are after us.</summary>
    public int PullMaxMobs { get; set; } = 15;
    /// <summary>After the first group, only groups within this many metres of it are gathered
    /// (so the bot gathers one area instead of being led further and further away).</summary>
    public double PullAreaMetres { get; set; } = 8;
    /// <summary>Learned per map: how many mobs this character can take at once without its HP
    /// falling under 40 % (lowered after a hard fight, raised after easy ones). Kept with the character.</summary>
    public Dictionary<string, int> PullLearned { get; set; } = [];
    /// <summary>Learned: how the fights with each kind of mob went for this character (see <see cref="Bot.MobFit"/>).</summary>
    public Dictionary<string, MobFights> MobFights { get; set; } = [];
    /// <summary>Hunt only mobs that suit the character, and walk to a better place on the map
    /// when there is one (after a level-up, when this one runs dry). Kept with the character.</summary>
    public bool AutoArea { get; set; } = true;
    /// <summary>Only gather while HP is at least this (0..1).</summary>
    public double PullMinHp { get; set; } = 0.7;
    /// <summary>A group needs at least this many mobs.</summary>
    public int CrowdMinMobs { get; set; } = 3;
    /// <summary>Mobs within this share of the screen height of each other form a group.</summary>
    public double CrowdRadius { get; set; } = 0.12;

    /// <summary>The best place to farm, found with "En iyi alanı bul"; null until then.</summary>
    public FarmSpot? FarmSpot { get; set; }
    /// <summary>The place the user saved with "Bu alanı kaydet": started at whenever it is on this map
    /// (before <see cref="FarmSpot"/>, the one the bot found); never moved by the bot.</summary>
    public FarmSpot? MySpot { get; set; }
    /// <summary>Walk to <see cref="FarmSpot"/> first when the bot starts (off: start where the character stands).</summary>
    public bool UseFarmSpot { get; set; }

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
    public double AttackRadius { get; set; } = 0.10;
    /// <summary>After dying: wait until "Burada yeniden başla" can be clicked, click it, drink HP
    /// potions up to <see cref="ReviveHealTo"/>, then carry on. While on, low HP doesn't stop the bot.</summary>
    public bool ReviveOnDeath { get; set; } = true;
    public double ReviveHealTo { get; set; } = 0.90;
    /// <summary>Stop healing after this long even if HP isn't there yet.</summary>
    public int ReviveHealMaxMs { get; set; } = 30000;

    /// <summary>Off: the bot attacks nothing (not even mobs that attack it), uses no skills and only picks up
    /// what lies on the ground around it (someone else kills, this character gathers). Kept per character.</summary>
    public bool Attack { get; set; } = true;
    /// <summary>Attack off: the character followed to pick up what it kills drop (empty: the nearest of the
    /// user's own characters in sight). Kept per character.</summary>
    public string FollowName { get; set; } = "";
    /// <summary>Mobs that are after the character are fought first.</summary>
    public bool AttackersFirst { get; set; } = true;
    /// <summary>Leave mobs above this level alone (0 = no limit); ones that attack us are still fought.</summary>
    public int MaxMobLevel { get; set; }
    /// <summary>Only go for mobs whose name contains one of these (comma separated; empty = all).</summary>
    public string OnlyMobs { get; set; } = "";
    /// <summary>Never go for mobs whose name contains one of these (comma separated).</summary>
    public string SkipMobs { get; set; } = "";
    public MetinMode Metins { get; set; } = MetinMode.Normal;
    /// <summary>Walk to items on the ground and pick them up (when no mob is next to the character).</summary>
    public bool PickUpDrops { get; set; } = true;
    /// <summary>Which items to walk to (the pickup key still picks up everything within reach).</summary>
    public bool LootYang { get; set; } = true;
    public bool LootPotions { get; set; } = true;
    public bool LootEquipment { get; set; }
    public bool LootOther { get; set; } = true;
    /// <summary>Only items this close (metres) are walked to.</summary>
    public double DropRangeMetres { get; set; } = 6;
    /// <summary>Stop the bot when the character ends up in a safe zone (town).</summary>
    public bool StopInSafeZone { get; set; } = true;

    /// <summary>Send messages to the user's Telegram (GM, death, level up, potions out, bot stopped).</summary>
    public bool TelegramEnabled { get; set; }
    /// <summary>The token of the user's own Telegram bot (from @BotFather).</summary>
    public string TelegramToken { get; set; } = "";
    /// <summary>The user's chat with that bot (found with "Sohbeti bul").</summary>
    public string TelegramChatId { get; set; } = "";

    /// <summary>Short breaks now and then (when nothing is fought), to act more like a person.</summary>
    public bool Breaks { get; set; } = true;
    /// <summary>About this many minutes between breaks (±20 %).</summary>
    public int BreakEveryMin { get; set; } = 30;
    /// <summary>About this long a break lasts (±25 %).</summary>
    public int BreakSeconds { get; set; } = 40;

    /// <summary>Stop the bot when a game master (GM) shows up nearby.</summary>
    public bool StopNearGm { get; set; } = true;
    /// <summary>Also pick up weapons and armour a shop pays at least this much for (the selling
    /// price, <see cref="Memory.ItemInfo.Sell"/>; 0 = off).</summary>
    public int LootEquipmentMinGold { get; set; }
    /// <summary>
    /// The bonus list ("efsun listesi"): a weapon or armour with one of these bonuses at least this
    /// big is kept (and told), whatever its price. While the list isn't empty all weapons and armour
    /// are picked up — a bonus shows only in the bag — and the rest is thrown away again.
    /// </summary>
    public List<BonusWant> LootBonuses { get; set; } = [];

    // ── Pazar taraması ──
    /// <summary>Items looked for in the market (part of the name is enough), each with the highest
    /// price per piece worth telling about (0 = any price).</summary>
    public List<MarketWant> MarketWanted { get; set; } = [];
    /// <summary>Look straight down and zoom out before the market scan (more stalls on screen).</summary>
    public bool MarketBirdsEye { get; set; } = true;
    /// <summary>Send a Telegram message when a wanted item is found at its price or cheaper.</summary>
    public bool MarketNotify { get; set; } = true;
    /// <summary>A scan stopped half way on the same map within a few hours goes on where it was.</summary>
    public bool MarketResume { get; set; } = true;

    /// <summary>Continuous market watch: a shop is read again once its reading is older than this (minutes).</summary>
    public int MarketRefreshMinutes { get; set; } = 30;

    /// <summary>Tell good deals: items without bonuses for sale well under their usual market price.</summary>
    public bool MarketDeals { get; set; } = true;

    /// <summary>A deal is at least this many percent under the usual price.</summary>
    public int MarketDealPercent { get; set; } = 40;

    /// <summary>The market window's list refreshes itself once a minute (off: only with "Yenile").</summary>
    public bool MarketAutoRefresh { get; set; }

    /// <summary>The bot was made small (one bar under its game) when last closed: it opens small again.</summary>
    public bool MiniMode { get; set; }

    /// <summary>What this SekoBot shows: 0 FARM, 1 PAZAR (the market watch), 2 BALIKÇILIK.</summary>
    public int UiMode { get; set; }
    /// <summary>Items always picked up, whatever the other loot choices (names, comma separated,
    /// part of the name is enough), e.g. "Cesaret Pelerini". A message is sent when one drops.</summary>
    public string LootAlways { get; set; } = "";
    /// <summary>Items ticked "Topla" in the item list: always picked up (item numbers).</summary>
    public List<int> LootPick { get; set; } = [];
    /// <summary>Only the weapons and armour the character's class can wear are picked up.</summary>
    public bool LootOwnClass { get; set; } = true;
    /// <summary>Rewards and specials (boxes, keys, books, quest items, scrolls…) always picked up.</summary>
    public bool LootRewards { get; set; } = true;
    /// <summary>Which loot upgrade the character's choices had (see <see cref="CharacterSettings.UpgradeLoot"/>).</summary>
    public int LootVersion { get; set; }
    /// <summary>Items ticked "Alma" in the item list: never walked to (item numbers).</summary>
    public List<int> LootSkip { get; set; } = [];
    /// <summary>Items ticked "At" in the item list: thrown away when the bag is full, never picked up (item numbers).</summary>
    public List<int> LootThrow { get; set; } = [];
    /// <summary>Bag full: step away from the mobs, throw the items ticked "At" and carry on (instead of stopping).</summary>
    public bool ThrowWhenInventoryFull { get; set; } = true;
    /// <summary>Bag full: sell at a market NPC known near on this map instead of throwing (see BotEngine.Sell).</summary>
    public bool SellToNpc { get; set; } = true;
    /// <summary>Another player staying this many minutes within 30 m while the experience drops: find another place (0 = off).</summary>
    public double AvoidPlayerMinutes { get; set; } = 3;

    /// <summary>Fishing: the quick slot key the bait is on (pressed when a cast doesn't take); None = not used.</summary>
    public System.Windows.Forms.Keys FishBaitKey { get; set; } = System.Windows.Forms.Keys.None;
    /// <summary>On the login screen (a maintenance): the login button pressed every few minutes until the server opens.</summary>
    public bool AutoLogin { get; set; } = true;
    /// <summary>Killed by a player: wait for them to leave before reviving, change channel if they stay;
    /// hit by a player: change channel.</summary>
    public bool PvpGuard { get; set; } = true;
    /// <summary>Two or more of our bots farming the same place on the same channel: all but one change channel.</summary>
    public bool CrowdGuard { get; set; } = true;
    /// <summary>Minutes between two login tries.</summary>
    public double AutoLoginMinutes { get; set; } = 3;

    /// <summary>Fishing: the mini game's upright picture is clicked by the bot.</summary>
    public bool FishAutoPick { get; set; } = true;
    /// <summary>Fishing: a Telegram message when a fish is hooked and the picture question waits.</summary>
    public bool FishNotify { get; set; } = true;

    /// <summary>Move to another farm spot when a player stays near (off: the bot stays where it farms).</summary>
    public bool MoveAwayFromPlayers { get; set; }
    /// <summary>How much the experience a minute has to drop (percent) against before the player came.</summary>
    public double AvoidPlayerExpDrop { get; set; } = 40;
    /// <summary>Answer "durum", "dur", "başla" written to the Telegram bot.</summary>
    public bool TelegramCommands { get; set; } = true;
    /// <summary>Tell (log, Telegram, sound) when someone writes a private message.</summary>
    public bool NotifyWhispers { get; set; } = true;
    /// <summary>Tell about trade / party / friend requests and the game's yes-no questions.</summary>
    public bool NotifyRequests { get; set; } = true;
    /// <summary>Tell when our name (or a GM) shows up in the chat.</summary>
    public bool NotifyMentions { get; set; } = true;
    /// <summary>Tell when an item with a bonus of the bonus list comes into the bag (the items chosen there).</summary>
    public bool NotifyBonusItems { get; set; } = true;
    /// <summary>Tell when a drop event starts.</summary>
    public bool NotifyEvents { get; set; } = true;
    /// <summary>Tell when there are stat / skill points to spend.</summary>
    public bool NotifyPoints { get; set; } = true;
    /// <summary>Send "🎁 Yere düştü" to Telegram when a "Topla" item drops (the log always gets it).</summary>
    public bool NotifyDrops { get; set; } = true;
    /// <summary>Send "⭐ Seviye atladın" to Telegram.</summary>
    public bool NotifyLevelUp { get; set; } = true;
    /// <summary>Send "💰 … satıldı" to Telegram after a trip to a market NPC (the log always gets it).</summary>
    public bool NotifySells { get; set; } = true;
    /// <summary>Pick up the item of a drop event going on now (level allowing), like "Topla".</summary>
    public bool EventLoot { get; set; } = true;
    /// <summary>Stop the bot when the inventory has no free slot left (loot would be lost).</summary>
    public bool StopWhenInventoryFull { get; set; } = true;

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

    /// <summary>profile.json for the first SekoBot, profile-2.json for a second one open at the same time.</summary>
    private static string ProfilePath => Path.Combine(Directory, Instance.FileName("profile", "json"));

    public static BotProfile Load()
    {
        // A second SekoBot starts from the first one's settings (Telegram, keys…), then keeps its own.
        string first = Path.Combine(Directory, "profile.json");
        if (!Instance.IsFirst && !File.Exists(ProfilePath) && File.Exists(first))
            try { File.Copy(first, ProfilePath); } catch { }
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
                    if (Upgrade(loaded) | CharacterSettings.UpgradeLoot(loaded)) // both, always
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
        // v8: 9 mobs never took the HP below half (watched 18 min, 2 potions): 12 (it still lowers itself on low HP).
        if (profile.Version < 8 && profile.PullMaxMobs == 9)
            profile.PullMaxMobs = 12;
        // v7: area skills were wasted on 3 mobs (and on mobs still on their way): 5 within 3 m now.
        if (profile.Version < 7 && profile.AreaSkillMinMobs == 3)
            profile.AreaSkillMinMobs = 5;
        // v6: "go to the crowd" is replaced by gathering spawn groups.
        if (profile.Version < 6)
            profile.PreferCrowds = false;
        // v5: memory reading is fast enough for 20 decisions a second (was 10).
        if (profile.Version < 5 && profile.PerceptionFps == 10)
            profile.PerceptionFps = 20;
        // v4: at 5 % the character often touched the mob without attacking ("stuck" next to it).
        if (Math.Abs(profile.AttackRadius - 0.05) < 0.0001)
            profile.AttackRadius = 0.10;
        // v3: everything from memory; the area limit is in metres now (the old one was a share of the big map).
        if (profile.Version < 3 && profile.LeashMeters <= 0)
            profile.LeashMeters = 40;
        profile.Version = BotProfile.CurrentVersion;
        return true;
    }

    private static readonly object SaveLock = new();

    public static void Save(BotProfile profile)
    {
        lock (SaveLock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            string temp = ProfilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(profile, Options));
            File.Move(temp, ProfilePath, true);
            if (Character.Length > 0)
                CharacterSettings.Save(Character, profile);
        }
    }

    /// <summary>The character this SekoBot plays now: its own settings are saved with it too.</summary>
    public static string Character { get; set; } = "";

    internal static JsonSerializerOptions JsonOptions => Options;
}

/// <summary>
/// Settings that belong to a character, not to a SekoBot window: how it fights (gathering, skills,
/// potions, fleeing), what it hunts and picks up, where it farms and what it has learned. Kept in
/// karakterler/NAME.json, so a character plays the same whichever SekoBot (1., 2., ...) it is in.
/// Keys, Telegram, breaks and window settings stay with the SekoBot.
/// </summary>
internal static class CharacterSettings
{
    internal static readonly string[] Names =
    [
        nameof(BotProfile.AutoSkills), nameof(BotProfile.SkillEverySec), nameof(BotProfile.AreaSkillMinMobs),
        nameof(BotProfile.RetreatAtPotions), nameof(BotProfile.FleeAtHp), nameof(BotProfile.FleeUntilHp),
        nameof(BotProfile.PotionHpBelow), nameof(BotProfile.PotionMpBelow), nameof(BotProfile.ReviveOnDeath),
        nameof(BotProfile.PullGroups), nameof(BotProfile.PullMaxMobs), nameof(BotProfile.PullAreaMetres),
        nameof(BotProfile.PullMinHp), nameof(BotProfile.PullLearned), nameof(BotProfile.PreferCrowds), nameof(BotProfile.CrowdMinMobs),
        nameof(BotProfile.FarmSpot), nameof(BotProfile.MySpot), nameof(BotProfile.UseFarmSpot), nameof(BotProfile.LeashMeters),
        nameof(BotProfile.AttackersFirst), nameof(BotProfile.MaxMobLevel), nameof(BotProfile.OnlyMobs),
        nameof(BotProfile.SkipMobs), nameof(BotProfile.Metins), nameof(BotProfile.PickUpDrops),
        nameof(BotProfile.LootYang), nameof(BotProfile.LootPotions), nameof(BotProfile.LootEquipment),
        nameof(BotProfile.LootOther), nameof(BotProfile.LootEquipmentMinGold), nameof(BotProfile.LootBonuses),
        nameof(BotProfile.LootAlways), nameof(BotProfile.LootPick), nameof(BotProfile.LootSkip),
        nameof(BotProfile.LootThrow), nameof(BotProfile.DropRangeMetres), nameof(BotProfile.EventLoot), nameof(BotProfile.LootVersion),
        nameof(BotProfile.LootOwnClass), nameof(BotProfile.LootRewards),
        nameof(BotProfile.SkillGapLearnedMs), nameof(BotProfile.MobFights), nameof(BotProfile.AutoArea), nameof(BotProfile.Attack), nameof(BotProfile.FollowName),
    ];

    public static string Folder { get; set; } = Path.Combine(ProfileStore.Directory, "karakterler");

    private static string PathOf(string character)
    {
        var bad = Path.GetInvalidFileNameChars();
        var name = new string(character.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return Path.Combine(Folder, (name.Length > 0 ? name : "_") + ".json");
    }

    public static bool Exists(string character) => File.Exists(PathOf(character));

    /// <summary>What a newly seen character keeps from the SekoBot it was opened in: the player's
    /// own choices about what to pick up. How it fights, hunts and where starts fresh: it learns
    /// its own (how many mobs it can take on each map) instead of playing like another character.</summary>
    internal static readonly string[] Kept =
    [
        nameof(BotProfile.PickUpDrops), nameof(BotProfile.LootYang), nameof(BotProfile.LootPotions),
        nameof(BotProfile.LootEquipment), nameof(BotProfile.LootOther), nameof(BotProfile.LootEquipmentMinGold),
        nameof(BotProfile.LootBonuses), nameof(BotProfile.LootAlways), nameof(BotProfile.LootPick),
        nameof(BotProfile.LootSkip), nameof(BotProfile.LootThrow), nameof(BotProfile.EventLoot), nameof(BotProfile.AutoSkills),
        nameof(BotProfile.LootVersion), nameof(BotProfile.LootOwnClass), nameof(BotProfile.LootRewards),
    ];

    /// <summary>
    /// Materials worth at least ~15 000 yang a piece in the market (read 2026-11-21, plain listings, 10 or more of
    /// each), stacking in the bag: picked up ("Topla") by the loot upgrade.
    /// </summary>
    internal static readonly int[] ValuableMaterials = [30005, 30006, 30007, 30008, 30009, 30010, 30011, 30014, 30015, 30016, 30017, 30018, 30019, 30021, 30022, 30023, 30025, 30026, 30027, 30030, 30031, 30034, 30035, 30037, 30038, 30039, 30040, 30042, 30044, 30045, 30046, 30047, 30048, 30049, 30050, 30051, 30052, 30053, 30055, 30056, 30057, 30058, 30059, 30060, 30061, 30067, 30070, 30071, 30072, 30073, 30074, 30075, 30076, 30077, 30078, 30079, 30080, 30081, 30082, 30083, 30085, 30086, 30087, 30088, 30089, 30090, 30091, 30092, 30102, 30139, 30150, 30151, 30158, 30165, 30166, 30167, 30168, 30179, 30190, 30192, 30193, 30194, 30195, 30196, 30197, 30198, 30220, 30221, 30222, 30223, 30224, 30227, 50701, 50705, 50706, 50707, 50708, 71123, 71129];

    /// <summary>
    /// The user's loot review of 2026-11-21: weapons / armour sold to a shop for under 4 000 yang aren't worth the
    /// walk (3 000 → 4 000), and the valuable materials are picked up. Runs once per character; what the user
    /// ticked "At" or "Alma" stays so.
    /// </summary>
    /// <returns>True when something changed.</returns>
    internal static bool UpgradeLoot(BotProfile profile)
    {
        if (profile.LootVersion >= LootUpgrade)
            return false;
        if (profile.LootEquipmentMinGold is > 0 and < 4000)
            profile.LootEquipmentMinGold = 4000;
        foreach (int vnum in ValuableMaterials)
            if (!profile.LootPick.Contains(vnum) && !profile.LootThrow.Contains(vnum) && !profile.LootSkip.Contains(vnum))
                profile.LootPick.Add(vnum);
        profile.LootVersion = LootUpgrade;
        return true;
    }

    /// <summary>The loot upgrade's number (2: the first one was marked done in the files without being applied).</summary>
    internal const int LootUpgrade = 2;

    /// <summary>A new character: everything but <see cref="Kept"/> back to the defaults.</summary>
    internal static void Fresh(BotProfile profile)
    {
        var defaults = new BotProfile();
        foreach (string name in Names.Except(Kept))
            if (typeof(BotProfile).GetProperty(name) is { } property)
                property.SetValue(profile, JsonSerializer.Deserialize(
                    JsonSerializer.Serialize(property.GetValue(defaults), property.PropertyType, ProfileStore.JsonOptions),
                    property.PropertyType, ProfileStore.JsonOptions));
        ProfileStore.Normalize(profile);
    }

    /// <summary>The character's part of the profile, as JSON.</summary>
    internal static string ToJson(BotProfile profile)
    {
        var all = JsonSerializer.SerializeToNode(profile, ProfileStore.JsonOptions)!.AsObject();
        var mine = new System.Text.Json.Nodes.JsonObject();
        foreach (string name in Names)
            mine[name] = all[name]?.DeepClone(); // null too: "no farm spot" is a setting
        return mine.ToJsonString(ProfileStore.JsonOptions);
    }

    /// <summary>Puts a character's saved settings into the profile (anything it lacks stays as it is).</summary>
    internal static void Apply(string json, BotProfile profile)
    {
        var mine = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
        if (mine == null)
            return;
        foreach (string name in Names)
        {
            if (!mine.TryGetPropertyValue(name, out var value) || typeof(BotProfile).GetProperty(name) is not { } property)
                continue;
            try
            {
                object? read = value?.Deserialize(property.PropertyType, ProfileStore.JsonOptions);
                if (read != null || name is nameof(BotProfile.FarmSpot) or nameof(BotProfile.MySpot)) // no farm spot is a setting too
                    property.SetValue(profile, read);
            }
            catch (JsonException) { }
        }
        // A character file without the loot upgrade's mark hasn't had it (whatever the SekoBot's own settings say:
        // seen, the mark came from there and the upgrade was skipped for every character).
        if (!mine.ContainsKey(nameof(BotProfile.LootVersion)))
            profile.LootVersion = 0;
        ProfileStore.Normalize(profile);
    }

    public static void Save(string character, BotProfile profile)
    {
        System.IO.Directory.CreateDirectory(Folder);
        string path = PathOf(character), temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(profile));
        File.Move(temp, path, true);
    }

    /// <returns>False when the character has no saved settings yet.</returns>
    public static bool Load(string character, BotProfile profile)
    {
        string path = PathOf(character);
        if (!File.Exists(path))
            return false;
        try
        {
            Apply(File.ReadAllText(path), profile);
            if (UpgradeLoot(profile))
                Save(character, profile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>One line of the bonus list: the game's bonus kind and the least value wanted (10 = +10 %),
/// and, when given, only for these items (names or parts of them, comma separated: "Deri Çizme, Keşiş Plaka Zırh").</summary>
public sealed class BonusWant
{
    public int Type { get; set; }
    public int Min { get; set; }
    public string Items { get; set; } = "";

    /// <summary>The item names asked for (empty: every item).</summary>
    public string[] ItemNames() => Items.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether this line is about the item: any item when no names are given, else one whose name holds one of them
    /// (any level: "Keşiş Plaka Zırh" is +0 to +9).</summary>
    public bool Applies(int vnum)
    {
        var names = ItemNames();
        if (names.Length == 0)
            return true;
        string name = Memory.ItemBook.NameOf(vnum);
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
        return names.Any(n => tr.IndexOf(name, n, System.Globalization.CompareOptions.IgnoreCase) >= 0);
    }
}

/// <summary>An item looked for in the market.</summary>
public sealed class MarketWant
{
    /// <summary>Its name or a part of it ("Kavrama Taşı" finds +0 to +4).</summary>
    public string Name { get; set; } = "";
    /// <summary>Told about only at this price per piece or cheaper (0 = any price).</summary>
    public long MaxPrice { get; set; }

    public bool Matches(string itemName) => Name.Length > 0 && itemName.Contains(Name.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
