using System.Text;

namespace SekoBot.Memory;

/// <summary>A point in the game world (Unreal units: 100 = 1 metre; Z is up).</summary>
internal readonly record struct Vec3(double X, double Y, double Z)
{
    public double DistanceXY(Vec3 other) => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
}

/// <summary>The character's health and mana.</summary>
internal readonly record struct CharacterStats(int Hp, int Mp, int MaxHp, int MaxMp)
{
    public double HpFraction => (double)Hp / MaxHp;
    public double MpFraction => (double)Mp / MaxMp;
}

/// <summary>A mob that can be attacked.</summary>
/// <param name="AttacksMe">The mob is after our character.</param>
/// <param name="IsMetin">A Metin stone (doesn't move or fight back, has a lot of HP).</param>
internal sealed record WorldMob(int Id, int Vnum, string Name, int Level, int Hp, int MaxHp, Vec3 Position,
    bool AttacksMe = false, bool IsMetin = false);

/// <summary>What a skill does.</summary>
internal enum SkillKind
{
    /// <summary>Hits the enemy in front.</summary>
    Single,
    /// <summary>Hits several enemies around ("Alan etkisi").</summary>
    Area,
    /// <summary>Strengthens the character for a while (e.g. "Güçlü beden").</summary>
    Buff,
}

/// <summary>One of the character's skills, as the game describes it.</summary>
/// <param name="CooldownSec">Seconds before it can be used again ("sure").</param>
/// <param name="DurationSec">How long a buff lasts (0 for attacks).</param>
internal sealed record SkillInfo(int Id, string Name, int Level, SkillKind Kind, int Mana, int CooldownSec, int DurationSec, int Distance);

/// <summary>An item lying on the ground.</summary>
internal readonly record struct GroundItem(long Id, Vec3 Position);

/// <summary>A mob that just died (it stays in the world for a moment).</summary>
internal readonly record struct DeadMob(int Id, Vec3 Position);

/// <summary>Another player around.</summary>
/// <param name="IsGm">The game marks the player as a game master.</param>
internal sealed record OtherPlayer(string Name, bool IsGm, Vec3 Position)
{
    /// <summary>A game master: marked as one, or "[GM]"-style tag in the name.</summary>
    public bool LooksLikeGm => IsGm || Name.Contains("[GM]", StringComparison.OrdinalIgnoreCase) ||
                               Name.Contains("[GA]", StringComparison.OrdinalIgnoreCase) ||
                               Name.Contains("[SA]", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A stack in the inventory: the item number (vnum) and how many.</summary>
internal readonly record struct InventoryItem(int Slot, int Vnum, int Count);

/// <summary>The character's progress: experience, money and inventory.</summary>
/// <param name="QuickSlots">What the quick slot keys 1, 2, … point at: an inventory slot
/// (0 or more), or a skill (negative).</param>
/// <param name="InventoryEnd">The last inventory slot that can be used (the locked pages come after it).</param>
internal sealed record CharacterProgress(long Exp, long NeedExp, long Yang,
    IReadOnlyList<InventoryItem> Inventory, IReadOnlyList<int> QuickSlots, int InventoryEnd = 0)
{
    /// <summary>Slots 1–10 are the equipment the character wears; the bag starts at 11.
    /// Each page is 6 columns × 8 rows.</summary>
    public const int FirstBagSlot = 11, Columns = 6;

    /// <summary>
    /// Empty bag slots. Swords, bows, two-handed weapons and armour fill two slots, one under
    /// the other; everything else one. Items this isn't sure about count as one slot,
    /// so "0 free" really means full. Null when the bag size isn't known.
    /// </summary>
    public int? FreeSlots()
    {
        if (InventoryEnd < FirstBagSlot)
            return null;
        var used = new HashSet<int>();
        foreach (var item in Inventory)
        {
            if (item.Slot < FirstBagSlot || item.Slot > InventoryEnd)
                continue;
            used.Add(item.Slot);
            if (TwoSlots(item.Vnum) && item.Slot + Columns <= InventoryEnd)
                used.Add(item.Slot + Columns);
        }
        return InventoryEnd - FirstBagSlot + 1 - used.Count;
    }

    /// <summary>Tall items, as seen in the game's inventory: swords (0–999), bows (2000–2999),
    /// two-handed (3000–3999) and armour (11000–11999). Daggers, fans, bells and the rest are
    /// counted as one slot.</summary>
    private static bool TwoSlots(int vnum) =>
        vnum is >= 0 and < 1000 or >= 2000 and < 4000 or >= 11000 and < 12000;

    /// <summary>How many of an item are in the whole inventory.</summary>
    public int Count(int vnum) => Inventory.Where(i => i.Vnum == vnum).Sum(i => i.Count);

    /// <summary>The item on quick slot key <paramref name="number"/> (1 = key "1"), or null.</summary>
    public int? QuickSlotVnum(int number)
    {
        if (number < 1 || number > QuickSlots.Count || QuickSlots[number - 1] is not (>= 0 and var slot))
            return null;
        return Inventory.FirstOrDefault(i => i.Slot == slot) is { Count: > 0 } item ? item.Vnum : null;
    }
}

/// <summary>Where the game camera is and where it looks (Unreal's FMinimalViewInfo).</summary>
internal sealed record CameraView(Vec3 Location, double Pitch, double Yaw, double Roll, double Fov)
{
    /// <summary>Where a world point shows up in a <paramref name="width"/>×<paramref name="height"/>
    /// picture of the game, or null when it is behind the camera.</summary>
    public PointF? ToScreen(Vec3 point, int width, int height)
    {
        double p = Pitch * Math.PI / 180, y = Yaw * Math.PI / 180, r = Roll * Math.PI / 180;
        double sp = Math.Sin(p), cp = Math.Cos(p), sy = Math.Sin(y), cy = Math.Cos(y), sr = Math.Sin(r), cr = Math.Cos(r);
        // Camera axes: forward, right, up.
        double fx = cp * cy, fy = cp * sy, fz = sp;
        double rx = sr * sp * cy - cr * sy, ry = sr * sp * sy + cr * cy, rz = -sr * cp;
        double ux = -(cr * sp * cy + sr * sy), uy = cy * sr - cr * sp * sy, uz = cr * cp;
        double dx = point.X - Location.X, dy = point.Y - Location.Y, dz = point.Z - Location.Z;
        double depth = dx * fx + dy * fy + dz * fz;
        if (depth < 1)
            return null;
        double right = dx * rx + dy * ry + dz * rz, up = dx * ux + dy * uy + dz * uz;
        double focal = width / 2.0 / Math.Tan(Math.Clamp(Fov, 1, 170) * Math.PI / 360);
        return new PointF((float)(width / 2.0 + right * focal / depth), (float)(height / 2.0 - up * focal / depth));
    }

    /// <summary>Turns a world direction on the ground into screen directions: (right, up) on screen.
    /// W walks "screen up", D "screen right".</summary>
    public (double Right, double Up) ToScreenDirection(double dx, double dy)
    {
        double yaw = Yaw * Math.PI / 180;
        double forwardX = Math.Cos(yaw), forwardY = Math.Sin(yaw);
        return (dx * -forwardY + dy * forwardX, dx * forwardX + dy * forwardY);
    }
}

/// <summary>One look at the game world.</summary>
/// <param name="OtherPlayers">Other players around (not mobs).</param>
/// <param name="Target">The mob selected in the game (its bar shows at the top), or null.</param>
/// <param name="Stats">The character's HP/MP, when they look valid.</param>
/// <param name="Dead">The character is dead.</param>
/// <param name="DeadMobs">Mobs that just died.</param>
/// <param name="Progress">Experience, money and inventory, when they could be read.</param>
/// <param name="Players">The other players around (with <paramref name="OtherPlayers"/> of them).</param>
/// <param name="Drops">Items lying on the ground.</param>
/// <param name="InSafeZone">The character is in a safe zone (town).</param>
/// <param name="OpenWindows">The game's windows (UI parts like "EscMenu", "inventory") that are showing.</param>
/// <param name="ReviveIn">The death window is showing: seconds until "Burada yeniden başla" can be
/// clicked (0 or less = now). Null when the window isn't showing.</param>
internal sealed record WorldSnapshot(Vec3 Player, CameraView? Camera, IReadOnlyList<WorldMob> Mobs, int OtherPlayers, string MapName,
    WorldMob? Target = null, CharacterStats? Stats = null, bool Dead = false, int Level = 0,
    IReadOnlyList<DeadMob>? DeadMobs = null, CharacterProgress? Progress = null, IReadOnlyList<OtherPlayer>? Players = null,
    IReadOnlyList<GroundItem>? Drops = null, bool InSafeZone = false, IReadOnlyList<string>? OpenWindows = null,
    double? ReviveIn = null, IReadOnlyList<SkillInfo>? Skills = null)
{
    /// <summary>Dead, and "Burada yeniden başla" can be clicked.</summary>
    public bool ReviveReady => Dead && ReviveIn is <= 0;

    /// <summary>The selected mob's health (0..1); 0 when nothing (alive) is selected.</summary>
    public double TargetHp => Target is { MaxHp: > 0 } t ? Math.Clamp((double)t.Hp / t.MaxHp, 0, 1) : 0;

    /// <summary>Mobs nearest first.</summary>
    public IEnumerable<WorldMob> ByDistance => Mobs.OrderBy(m => m.Position.DistanceXY(Player));
}

/// <summary>
/// Reads everything the bot needs from the game's memory: the character (position, HP/MP, dead),
/// the camera, the mobs around and the selected mob. The game is made with Unreal Engine 4.27:
/// the game instance ("m2data_C") is found through one fixed pointer in the game's exe, everything
/// else through the game's own field names in Unreal's type information ("gm", "loader", "HP",
/// "dead", …). When a game update moves the fixed pointer or the name table, they are searched for
/// automatically. Only ever reads; never writes.
/// </summary>
internal sealed class GameWorldReader : IDisposable
{
    // ── Fixed places in the tested game build (searched for when they don't fit) ──
    /// <summary>Static pointer to the game instance ("m2data_C").</summary>
    internal const long GameInstanceOffset = 0x04CED428;
    /// <summary>FNamePool (all object and field names); its block pointers start at +0x10.</summary>
    internal const long NamePoolOffset = 0x04D8AFC0;
    // UObject
    internal const int ObjClass = 0x10, ObjName = 0x18, ObjOuter = 0x20;
    // UStruct / UClass
    internal const int StructSuper = 0x48, StructProperties = 0x58;
    // FField / FProperty (FStructProperty: the struct's type at +0x78)
    internal const int FieldClass = 0x08, FieldNext = 0x20, FieldName = 0x28, PropertyOffset = 0x4C, StructPropertyType = 0x78;
    // ULevel: Actors (TArray)
    internal const int LevelActors = 0xA0;
    // AActor: RootComponent; USceneComponent: ComponentToWorld translation
    internal const int ActorRoot = 0x138, ComponentWorldLocation = 0x1E0;
    // FCameraCacheEntry: POV (FMinimalViewInfo: Location, Rotation, FOV)
    internal const int CameraCachePov = 0x10;
    private const int MaxActors = 50_000;
    // TMap<int, invItem> element: key (int, padded to 8), the invItem (0x88 bytes: ItemId at 0,
    // Count at 0x3C), then the set's hash links (8).
    private const int InventoryStride = 0x98, InventoryValue = 0x8, ItemVnum = 0x0, ItemCount = 0x3C;

    /// <summary>The game fields the reader uses ("a.b" = field b of struct field a), with where
    /// they were in the tested build (used only when the type information doesn't know them).</summary>
    internal static readonly (string Class, string Field, int Fallback)[] Fields =
    [
        ("m2data_C", "gm", 0x1F8),
        ("m2data_C", "loader", 0x1188),
        ("m2data_C", "selectedActor", 0x1C0),
        ("loader", "charData.HP", 0x770 + 0x34),
        ("loader", "charData.SP", 0x770 + 0x3C),
        ("loader", "charData.fullHP", 0x770 + 0x68),
        ("loader", "charData.fullSp", 0x770 + 0x88),
        ("loader", "charData.Level", 0x770 + 0x90),
        ("loader", "charData.Exp", 0x770 + 0x70),
        ("loader", "charData.needexp", 0x770 + 0x78),
        ("loader", "charData.yang", 0x770 + 0x48),
        ("loader", "charData.invitems", 0x770 + 0xD8),
        ("loader", "qSlots", 0x278),
        ("loader", "charData.invCount", 0x770 + 0xC8),
        ("loader", "playerSkills", 0x288),
        ("gameManager", "playerPos", 0xA10),
        ("gameManager", "mapname", 0x958),
        ("gameManager", "Player", 0x380),
        ("playerMove", "dead", 0x778),
        ("playerMove", "isInSafe", 0x87C),
        ("m2data_C", "uiMain", 0x1210),
        ("Widget", "Visibility", 0xCB),
        ("UI_C", "reSpawn", 0x818),
        ("reSpawn_C", "Timer", 0x2A8),
        ("reSpawn_C", "wait", 0x2AC),
        ("NPC", "thismob.vnum", 0x348),
        ("NPC", "thismob.Name", 0x348 + 0x8),
        ("NPC", "thismob.Level", 0x348 + 0x3C),
        ("NPC", "Remove", 0x502),
        ("NPC", "spawned", 0x506),
        ("NPC", "dead", 0x50C),
        ("NPC", "HP", 0x544),
        ("NPC", "maxHP", 0x548),
        ("NPC", "ID", 0x54C),
        ("NPC", "isplayer", 0x628),
        ("NPC", "attackable", 0x798),
        ("NPC", "isOwnedMob", 0x7A2),
        ("NPC", "playername", 0x640),
        ("NPC", "isGM", 0x6B5),
        ("NPC", "targetActorMe", 0x860),
        ("NPC", "thismob.Type", 0x348 + 0x28),
        ("PlayerCameraManager", "CameraCachePrivate", 0x1AF0),
    ];

    private readonly IMemorySource _memory;
    private readonly long _moduleBase, _moduleSize;
    private readonly IDisposable? _owned;
    private readonly Dictionary<int, string> _names = [];
    private readonly Dictionary<(long Class, string Field), int> _offsets = [];
    private readonly Dictionary<long, string> _classNames = [];
    private readonly Dictionary<(int Vnum, long Text), string> _mobNames = [];
    private readonly Dictionary<int, bool> _metins = [];
    /// <summary>Our character (the pawn), to tell which mobs are after us.</summary>
    private long _pawn;
    private readonly byte[] _buffer = new byte[64];
    /// <summary>Actors that are not NPCs (trees, lights, …): their class isn't read again for a while.</summary>
    private readonly HashSet<long> _otherActors = [];
    private DateTime _otherActorsSince = DateTime.UtcNow;
    private byte[] _npc = [];
    private long _namePool = NamePoolOffset, _gameInstance = GameInstanceOffset;
    private bool _located;
    private DateTime _locateAfter = DateTime.MinValue;

    /// <param name="moduleSize">Size of the game's exe in memory (to search it after a game update; 0 = don't search).</param>
    internal GameWorldReader(IMemorySource memory, long moduleBase, long moduleSize = 0, IDisposable? owned = null)
    {
        _memory = memory;
        _moduleBase = moduleBase;
        _moduleSize = moduleSize;
        _owned = owned;
    }

    public string Status { get; private set; } = "Dünya bilgisi bekleniyor";

    /// <summary>For tests: start from other fixed places (as after a game update).</summary>
    internal void UseOffsets(long namePool, long gameInstance)
    {
        _namePool = namePool;
        _gameInstance = gameInstance;
        _located = false;
    }

    /// <summary>The fixed places weren't right (game update) and were found by searching.</summary>
    public bool Searched { get; private set; }

    /// <returns>A reader, or null (with the reason) when the game can't be read.</returns>
    public static GameWorldReader? Open(uint processId, out string error)
    {
        var memory = ProcessMemory.Open(processId, out error);
        return memory == null ? null : new GameWorldReader(memory, memory.ModuleBase, memory.ModuleSize, memory);
    }

    /// <summary>Reads everything once; null when the game isn't in the world (loading, character select).</summary>
    public WorldSnapshot? Read()
    {
        try
        {
            return ReadCore();
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            Status = "Dünya okunamadı: " + ex.Message;
            return null;
        }
    }

    private WorldSnapshot? ReadCore()
    {
        if (!Locate())
            return null;
        long instance = Pointer(_moduleBase + _gameInstance);
        long instanceClass = Pointer(instance + ObjClass);
        if (ClassName(instanceClass) != "m2data_C")
        {
            _located = false;
            Status = "Oyun verisi bulunamadı (oyun yükleniyor olabilir)";
            return null;
        }
        long gm = Pointer(instance + Offset(instanceClass, "m2data_C", "gm"));
        long gmClass = Pointer(gm + ObjClass);
        if (!MemoryReads.IsPointer(gm) || ClassName(gmClass) != "gameManager")
        {
            Status = "Oyun dünyası bulunamadı (giriş ekranı ya da yükleme ekranı)";
            return null;
        }
        if (!TryVec3(gm + Offset(gmClass, "gameManager", "playerPos"), out var player))
        {
            Status = "Karakter konumu okunamadı";
            return null;
        }
        string mapName = ReadFString(gm + Offset(gmClass, "gameManager", "mapname"), 64);

        _pawn = Pointer(gm + Offset(gmClass, "gameManager", "Player"));
        long level = Pointer(gm + ObjOuter);
        if (!MemoryReads.IsPointer(level) || !TryActors(level, out long[] actors))
        {
            Status = "Haritadaki nesne listesi okunamadı";
            return null;
        }

        CameraView? camera = null;
        var mobs = new List<WorldMob>();
        var deadMobs = new List<DeadMob>();
        var players = new List<OtherPlayer>();
        var drops = new List<GroundItem>();
        if (DateTime.UtcNow - _otherActorsSince > TimeSpan.FromSeconds(10))
        {
            // Memory of removed actors gets reused: look at everything again now and then.
            _otherActors.Clear();
            _otherActorsSince = DateTime.UtcNow;
        }
        foreach (long actor in actors)
        {
            if (!MemoryReads.IsPointer(actor) || _otherActors.Contains(actor))
                continue;
            long cls = Pointer(actor + ObjClass);
            if (!MemoryReads.IsPointer(cls))
                continue;
            string className = ClassName(cls);
            if (className == "NPC")
            {
                switch (ReadNpc(actor, cls))
                {
                    case WorldMob mob: mobs.Add(mob); break;
                    case DeadMob died: deadMobs.Add(died); break;
                    case OtherPlayer other: players.Add(other); break;
                }
            }
            else if (className == "PlayerCameraManager")
                camera ??= ReadCamera(actor + Offset(cls, className, "CameraCachePrivate") + CameraCachePov);
            else if (className == "droppedItem")
            {
                // Lying on the ground (a picked-up item is removed from the world).
                if (Pointer(actor + ActorRoot) is long itemRoot && TryVec3(itemRoot + ComponentWorldLocation, out var at))
                    drops.Add(new GroundItem(actor, at));
            }
            else
                _otherActors.Add(actor);
        }

        WorldMob? target = null;
        long selected = Pointer(instance + Offset(instanceClass, "m2data_C", "selectedActor"));
        if (MemoryReads.IsPointer(selected) && Pointer(selected + ObjClass) is long selectedClass && ClassName(selectedClass) == "NPC")
            target = ReadNpc(selected, selectedClass, selected: true) as WorldMob;

        long loader = Pointer(instance + Offset(instanceClass, "m2data_C", "loader"));
        var (stats, characterLevel) = ReadStats(loader);
        var progress = ReadProgress(loader);
        // Skills change only when one is learned or levelled: every few seconds is plenty.
        if (DateTime.UtcNow - _skillsReadAt >= TimeSpan.FromSeconds(3))
        {
            _skills = ReadSkills(loader);
            _skillsReadAt = DateTime.UtcNow;
        }
        bool dead = false, safe = false;
        long pawn = _pawn;
        if (MemoryReads.IsPointer(pawn))
        {
            long pawnClass = Pointer(pawn + ObjClass);
            dead = Byte(pawn + Offset(pawnClass, "playerMove", "dead")) is byte b && b != 0;
            safe = Byte(pawn + Offset(pawnClass, "playerMove", "isInSafe")) is byte s && s != 0;
        }

        Status = Searched ? $"Bellekten: {mobs.Count} mob (adresler yeniden bulundu)" : $"Bellekten: {mobs.Count} mob";
        // Windows don't open that often: look twice a second (it takes ~100 reads).
        if (DateTime.UtcNow - _windowsReadAt >= TimeSpan.FromMilliseconds(500))
        {
            _windows = ReadWindows(Pointer(instance + Offset(instanceClass, "m2data_C", "uiMain")));
            _windowsReadAt = DateTime.UtcNow;
        }
        var windows = _windows;
        double? reviveIn = dead ? ReviveIn(Pointer(instance + Offset(instanceClass, "m2data_C", "uiMain"))) : null;
        return new WorldSnapshot(player, camera, mobs, players.Count, mapName, target, stats, dead, characterLevel, deadMobs, progress, players,
            drops, safe, windows, reviveIn, _skills);
    }

    private (CharacterStats? Stats, int Level) ReadStats(long loader)
    {
        if (!MemoryReads.IsPointer(loader))
            return (null, 0);
        long cls = Pointer(loader + ObjClass);
        int Int(string field) => Int32(loader + Offset(cls, "loader", field)) ?? -1;
        int hp = Int("charData.HP"), mp = Int("charData.SP"), maxHp = Int("charData.fullHP"), maxMp = Int("charData.fullSp");
        int level = Math.Max(0, Int("charData.Level"));
        if (maxHp is <= 0 or > 10_000_000 || maxMp is <= 0 or > 10_000_000 || hp < 0 || mp < 0 || hp > maxHp || mp > maxMp)
            return (null, level);
        return (new CharacterStats(hp, mp, maxHp, maxMp), level);
    }

    private readonly Dictionary<long, List<(string Name, int Offset)>> _widgetFields = [];
    private readonly Dictionary<long, bool> _isWidget = [];
    private List<string> _windows = [];
    private List<SkillInfo> _skills = [];
    private DateTime _skillsReadAt = DateTime.MinValue;
    private DateTime _windowsReadAt = DateTime.MinValue;

    /// <summary>
    /// The game's own windows (the "_C" widgets held by the main UI, e.g. "EscMenu", "inventory",
    /// "settings") that are showing. Unreal's ESlateVisibility: 1 Collapsed and 2 Hidden are not showing.
    /// </summary>
    private List<string> ReadWindows(long ui)
    {
        var open = new List<string>();
        if (!MemoryReads.IsPointer(ui))
            return open;
        long uiClass = Pointer(ui + ObjClass);
        if (!_widgetFields.TryGetValue(uiClass, out var fields))
            _widgetFields[uiClass] = fields = ObjectFields(uiClass);
        foreach (var (name, offset) in fields)
        {
            long widget = Pointer(ui + offset);
            if (!MemoryReads.IsPointer(widget))
                continue;
            long cls = Pointer(widget + ObjClass);
            if (!ClassName(cls).EndsWith("_C", StringComparison.Ordinal) || !IsWidget(cls))
                continue;
            if (Byte(widget + Offset(cls, "Widget", "Visibility")) is byte visibility and not (1 or 2))
                open.Add(name);
        }
        return open;
    }

    /// <summary>The object fields of a class (own and inherited, up to the engine's widget classes).</summary>
    private List<(string Name, int Offset)> ObjectFields(long cls)
    {
        var fields = new List<(string, int)>();
        for (int depth = 0; depth < 4 && MemoryReads.IsPointer(cls); depth++, cls = Pointer(cls + StructSuper))
        {
            if (ClassName(cls) is "UserWidget" or "Widget" or "Object")
                break;
            long property = Pointer(cls + StructProperties);
            for (int i = 0; i < 2000 && MemoryReads.IsPointer(property); i++, property = Pointer(property + FieldNext))
                if (Int32(Pointer(property + FieldClass)) is int type && Name(type) == "ObjectProperty" &&
                    Int32(property + FieldName) is int name && Int32(property + PropertyOffset) is int offset and >= 0)
                    fields.Add((Name(name), offset));
        }
        return fields;
    }

    private bool IsWidget(long cls)
    {
        if (_isWidget.TryGetValue(cls, out bool widget))
            return widget;
        long c = cls;
        for (int depth = 0; depth < 12 && MemoryReads.IsPointer(c) && !widget; depth++, c = Pointer(c + StructSuper))
            widget = ClassName(c) == "Widget";
        _isWidget[cls] = widget;
        return widget;
    }

    /// <summary>
    /// The death window ("reSpawn"): after it opens, "wait" counts down (the window appearing),
    /// then "Timer" counts down from 12; at 0 "Burada yeniden başla" can be clicked (seen in the game).
    /// </summary>
    private double? ReviveIn(long ui)
    {
        if (!MemoryReads.IsPointer(ui))
            return null;
        long window = Pointer(ui + Offset(Pointer(ui + ObjClass), "UI_C", "reSpawn"));
        if (!MemoryReads.IsPointer(window))
            return null;
        long cls = Pointer(window + ObjClass);
        if (Byte(window + Offset(cls, "Widget", "Visibility")) is not byte visibility || visibility is 1 or 2)
            return null;
        if (Float(window + Offset(cls, "reSpawn_C", "Timer")) is not double timer ||
            Float(window + Offset(cls, "reSpawn_C", "wait")) is not double wait)
            return null;
        return Math.Max(0, wait) + timer;
    }

    private CharacterProgress? ReadProgress(long loader)
    {
        if (!MemoryReads.IsPointer(loader))
            return null;
        long cls = Pointer(loader + ObjClass);
        long Long(string field) => _memory.TryReadInt64(loader + Offset(cls, "loader", field), out long v) ? v : -1;
        long exp = Long("charData.Exp"), need = Long("charData.needexp");
        if (exp < 0 || need <= 0)
            return null;
        long.TryParse(ReadFString(loader + Offset(cls, "loader", "charData.yang"), 32), out long yang);
        var inventory = ReadInventory(loader + Offset(cls, "loader", "charData.invitems"));
        var quick = new List<int>();
        long slots = Pointer(loader + Offset(cls, "loader", "qSlots"));
        int count = Int32(loader + Offset(cls, "loader", "qSlots") + 8) ?? 0;
        if (MemoryReads.IsPointer(slots) && count is > 0 and <= 32)
        {
            var bytes = new byte[count * 4];
            if (_memory.Read(slots, bytes, bytes.Length))
                for (int i = 0; i < count; i++)
                    quick.Add(BitConverter.ToInt32(bytes, i * 4));
        }
        int end = Int32(loader + Offset(cls, "loader", "charData.invCount")) ?? 0;
        return new CharacterProgress(exp, need, yang, inventory, quick, end is > 0 and < 1000 ? end : 0);
    }

    // TMap<int, skill> element: key (int, padded to 8), the skill struct (0xA8), the hash links (8).
    private const int SkillStride = 0xB8, SkillValue = 0x8;

    /// <summary>The character's skills (loader.playerSkills): names per grade, type, level, mana,
    /// cooldown ("sure"), duration and the description that says what it does.</summary>
    private List<SkillInfo> ReadSkills(long loader)
    {
        var skills = new List<SkillInfo>();
        if (!MemoryReads.IsPointer(loader))
            return skills;
        long map = loader + Offset(Pointer(loader + ObjClass), "loader", "playerSkills");
        long data = Pointer(map);
        int count = Int32(map + 8) ?? 0;
        if (!MemoryReads.IsPointer(data) || count <= 0 || count > 200)
            return skills;
        int maxBits = Int32(map + 0x2C) ?? 0;
        var bits = new byte[(Math.Max(count, 128) + 7) / 8];
        long bitsAt = maxBits > 128 ? Pointer(map + 0x20) : map + 0x10;
        if (!MemoryReads.IsPointer(bitsAt) || !_memory.Read(bitsAt, bits, (count + 7) / 8))
            return skills;
        var elements = new byte[count * SkillStride];
        if (!_memory.Read(data, elements, elements.Length))
            return skills;
        for (int i = 0; i < count; i++)
        {
            if ((bits[i / 8] & (1 << (i % 8))) == 0)
                continue;
            int at = i * SkillStride + SkillValue;
            long value = data + at;
            int id = BitConverter.ToInt32(elements, at);
            int level = BitConverter.ToInt32(elements, at + 0x68);
            if (id <= 0 || level <= 0)
                continue; // not learned
            // Names per grade: normal (1–19), master (20–29), grand master (30+).
            var names = new List<string>();
            long namesAt = BitConverter.ToInt64(elements, at + 0x8);
            int nameCount = BitConverter.ToInt32(elements, at + 0x10);
            for (int n = 0; n < Math.Min(nameCount, 4) && MemoryReads.IsPointer(namesAt); n++)
                names.Add(ReadFString(namesAt + n * 0x10, 64));
            string name = names.Count == 0 ? $"Beceri {id}" : names[Math.Min(names.Count - 1, level >= 30 ? 2 : level >= 20 ? 1 : 0)];
            string type = ReadFString(value + 0x58, 32);
            string properties = ReadFString(value + 0x28, 400);
            string description = ReadFString(value + 0x18, 400);
            int duration = BitConverter.ToInt32(elements, at + 0x78);
            var kind = type != "HP" || description.Contains("Pozitif", StringComparison.OrdinalIgnoreCase) ? SkillKind.Buff
                : properties.Contains("Alan etkisi", StringComparison.OrdinalIgnoreCase) ? SkillKind.Area
                : SkillKind.Single;
            skills.Add(new SkillInfo(id, name, level, kind, BitConverter.ToInt32(elements, at + 0x70),
                BitConverter.ToInt32(elements, at + 0x74), duration, BitConverter.ToInt32(elements, at + 0x6C)));
        }
        return skills;
    }

    /// <summary>The inventory map (TMap: a sparse array of slot → item, with a bit per used element).</summary>
    private List<InventoryItem> ReadInventory(long map)
    {
        var items = new List<InventoryItem>();
        long data = Pointer(map);
        int count = Int32(map + 8) ?? 0;
        if (!MemoryReads.IsPointer(data) || count <= 0 || count > 2000)
            return items;
        // Allocation bits: 128 inline bits, or a separate block when there are more.
        int maxBits = Int32(map + 0x2C) ?? 0;
        var bits = new byte[(Math.Max(count, 128) + 7) / 8];
        long bitsAt = maxBits > 128 ? Pointer(map + 0x20) : map + 0x10;
        if (!MemoryReads.IsPointer(bitsAt) || !_memory.Read(bitsAt, bits, (count + 7) / 8))
            return items;
        var elements = new byte[count * InventoryStride];
        if (!_memory.Read(data, elements, elements.Length))
            return items;
        for (int i = 0; i < count; i++)
        {
            if ((bits[i / 8] & (1 << (i % 8))) == 0)
                continue;
            int at = i * InventoryStride;
            int slot = BitConverter.ToInt32(elements, at);
            int vnum = BitConverter.ToInt32(elements, at + InventoryValue + ItemVnum);
            int amount = BitConverter.ToInt32(elements, at + InventoryValue + ItemCount);
            if (vnum > 0 && amount > 0 && slot >= 0)
                items.Add(new InventoryItem(slot, vnum, amount));
        }
        return items;
    }

    /// <returns>A <see cref="WorldMob"/>, a <see cref="DeadMob"/>, an <see cref="OtherPlayer"/>, or null for anything else.</returns>
    /// <param name="selected">The selected mob: also when it just died (HP 0), so the bot sees it die.</param>
    private object? ReadNpc(long actor, long cls, bool selected = false)
    {
        // All fields in one read (much faster than one read per field).
        int span = 0;
        foreach (var f in Fields)
            if (f.Class == "NPC")
                span = Math.Max(span, Offset(cls, "NPC", f.Field));
        span = Math.Max(span + 0x10, ActorRoot + 8);
        if (_npc.Length != span)
            _npc = new byte[span];
        if (!_memory.Read(actor, _npc, span))
            return null;
        var data = _npc;
        bool Flag(string field) => data[Offset(cls, "NPC", field)] != 0;
        int Int(string field) => BitConverter.ToInt32(data, Offset(cls, "NPC", field));

        if (Flag("isplayer"))
        {
            long playerRoot = BitConverter.ToInt64(data, ActorRoot);
            TryVec3(playerRoot + ComponentWorldLocation, out var at);
            return new OtherPlayer(ReadFString(actor + Offset(cls, "NPC", "playername"), 64), Flag("isGM"), at);
        }
        if (!Flag("attackable") || Flag("isOwnedMob"))
            return null;
        long root = BitConverter.ToInt64(data, ActorRoot);
        if (!MemoryReads.IsPointer(root) || !TryVec3(root + ComponentWorldLocation, out var position))
            return null;
        if (!selected && (Flag("dead") || Flag("Remove")))
            return new DeadMob(Int("ID"), position);
        if (!selected && !Flag("spawned"))
            return null;
        int hp = Int("HP"), maxHp = Int("maxHP");
        if (selected && (Flag("dead") || Flag("Remove")))
            hp = 0;
        // A mob's HP can be a little above its max (seen on a Metin stone: 34083 / 34050):
        // count it as full. Only nonsense (no max, negative, far above) is dropped.
        if (maxHp <= 0 || hp < 0 || hp > maxHp * 2L)
            return null;
        hp = Math.Min(hp, maxHp);
        if (!selected && hp == 0)
            return new DeadMob(Int("ID"), position);
        int vnum = Int("thismob.vnum");
        int nameAt = Offset(cls, "NPC", "thismob.Name");
        long text = BitConverter.ToInt64(data, nameAt);
        if (!_mobNames.TryGetValue((vnum, text), out string? name))
            _mobNames[(vnum, text)] = name = ReadFString(actor + nameAt, 64);
        if (!_metins.TryGetValue(vnum, out bool metin))
            _metins[vnum] = metin = vnum is >= 8000 and < 9000 || ReadFString(actor + Offset(cls, "NPC", "thismob.Type"), 8) == "2";
        long after = BitConverter.ToInt64(data, Offset(cls, "NPC", "targetActorMe"));
        bool attacksMe = _pawn != 0 && after == _pawn;
        return new WorldMob(Int("ID"), vnum, name, Int("thismob.Level"), hp, maxHp, position, attacksMe, metin);
    }

    private CameraView? ReadCamera(long pov)
    {
        if (!TryVec3(pov, out var location) || Float(pov + 0xC) is not double pitch ||
            Float(pov + 0x10) is not double yaw || Float(pov + 0x14) is not double roll ||
            Float(pov + 0x18) is not double fov || fov is < 5 or > 170)
            return null;
        return new CameraView(location, pitch, yaw, roll, fov);
    }

    private bool TryActors(long level, out long[] actors)
    {
        actors = [];
        long data = Pointer(level + LevelActors);
        int count = Int32(level + LevelActors + 8) ?? -1;
        if (count == 0)
            return true;
        if (!MemoryReads.IsPointer(data) || count < 0 || count > MaxActors)
            return false;
        var bytes = new byte[count * 8];
        if (!_memory.Read(data, bytes, bytes.Length))
            return false;
        actors = new long[count];
        Buffer.BlockCopy(bytes, 0, actors, 0, bytes.Length);
        return true;
    }

    // ── Finding the two fixed places ──

    /// <summary>Checks the name table and the game instance pointer; searches the exe for them when
    /// they don't fit (after a game update). Searching is slow, so not more often than every 15 s.</summary>
    private bool Locate()
    {
        if (_located)
            return true;
        if (IsNamePool(_moduleBase + _namePool) && IsGameInstancePointer(_moduleBase + _gameInstance))
            return _located = true;
        if (DateTime.UtcNow < _locateAfter)
            return false;
        _locateAfter = DateTime.UtcNow.AddSeconds(15);
        if (_moduleSize <= 0)
        {
            Status = "Oyun belleğinde başlangıç noktası bulunamadı";
            return false;
        }
        Status = "Oyun güncellenmiş olabilir; bellekte adresler aranıyor…";
        if (!IsNamePool(_moduleBase + _namePool))
        {
            if (Search(IsNamePoolCandidate) is not long pool)
            {
                Status = "Oyunun isim tablosu bulunamadı (oyun güncellenmiş; yeni SekoBot sürümü gerekebilir)";
                return false;
            }
            _namePool = pool - 0x10; // the found slot is the first block pointer
            _names.Clear();
            _classNames.Clear();
            _offsets.Clear();
            Searched = true;
        }
        if (!IsGameInstancePointer(_moduleBase + _gameInstance))
        {
            if (Search(IsGameInstancePointer) is not long instance)
            {
                Status = "Oyun verisi bulunamadı (karakterle oyuna girdin mi?)";
                return false;
            }
            _gameInstance = instance;
            Searched = true;
        }
        return _located = true;
    }

    private bool IsNamePoolCandidate(long address) => IsNamePool(address - 0x10);

    /// <summary>The first two names of every Unreal name table are "None" and "ByteProperty".</summary>
    private bool IsNamePool(long pool)
    {
        long block = Pointer(pool + 0x10);
        var bytes = new byte[2 + 4 + 2 + 12];
        if (!MemoryReads.IsPointer(block) || !_memory.Read(block, bytes, bytes.Length))
            return false;
        return BitConverter.ToUInt16(bytes, 0) >> 6 == 4 && Encoding.ASCII.GetString(bytes, 2, 4) == "None" &&
               BitConverter.ToUInt16(bytes, 6) >> 6 == 12 && Encoding.ASCII.GetString(bytes, 8, 12) == "ByteProperty";
    }

    private bool IsGameInstancePointer(long address)
    {
        long instance = Pointer(address);
        return MemoryReads.IsPointer(instance) && ClassName(Pointer(instance + ObjClass)) == "m2data_C";
    }

    /// <summary>Goes through every pointer-sized slot of the exe; returns the offset of the first one that passes.</summary>
    private long? Search(Func<long, bool> test)
    {
        const int chunk = 0x10000;
        var data = new byte[chunk];
        for (long offset = 0; offset < _moduleSize; offset += chunk)
        {
            int size = (int)Math.Min(chunk, _moduleSize - offset);
            if (!_memory.Read(_moduleBase + offset, data, size))
                continue;
            for (int i = 0; i + 8 <= size; i += 8)
            {
                long value = BitConverter.ToInt64(data, i);
                // Only pointers to the heap, never into the exe itself.
                if (!MemoryReads.IsPointer(value) || value >= _moduleBase && value < _moduleBase + _moduleSize)
                    continue;
                if (test(_moduleBase + offset + i))
                    return offset + i;
            }
        }
        return null;
    }

    // ── Unreal type information ──

    /// <summary>Where a field of a game class is, from the class' own field list (fallback: the tested build).</summary>
    private int Offset(long cls, string className, string field)
    {
        if (_offsets.TryGetValue((cls, field), out int offset))
            return offset;
        offset = FindPath(cls, field) ?? Fields.First(f => f.Class == className && f.Field == field).Fallback;
        _offsets[(cls, field)] = offset;
        return offset;
    }

    /// <summary>"a.b": field b of the struct field a.</summary>
    private int? FindPath(long cls, string path)
    {
        int total = 0;
        long type = cls;
        foreach (string part in path.Split('.'))
        {
            if (FindProperty(type, part) is not (int offset, long property))
                return null;
            total += offset;
            type = Pointer(property + StructPropertyType);
        }
        return total;
    }

    private (int Offset, long Property)? FindProperty(long cls, string field)
    {
        for (int depth = 0; depth < 8 && MemoryReads.IsPointer(cls); depth++, cls = Pointer(cls + StructSuper))
        {
            long property = Pointer(cls + StructProperties);
            for (int i = 0; i < 2000 && MemoryReads.IsPointer(property); i++, property = Pointer(property + FieldNext))
                if (Int32(property + FieldName) is int name && Name(name) == field)
                    return Int32(property + PropertyOffset) is int o and >= 0 and < 0x100000 ? (o, property) : null;
        }
        return null;
    }

    private string ClassName(long cls)
    {
        if (!MemoryReads.IsPointer(cls))
            return "";
        if (!_classNames.TryGetValue(cls, out string? name))
            _classNames[cls] = name = Int32(cls + ObjName) is int index ? Name(index) : "";
        return name;
    }

    /// <summary>An FName's text (FNamePool entry: 2-byte header with the length, then the characters).</summary>
    internal string Name(int index)
    {
        if (_names.TryGetValue(index, out string? text))
            return text;
        text = "";
        long block = Pointer(_moduleBase + _namePool + 0x10 + 8L * ((uint)index >> 16));
        if (MemoryReads.IsPointer(block))
        {
            long entry = block + 2L * (index & 0xFFFF);
            if (_memory.Read(entry, _buffer, 2))
            {
                int header = BitConverter.ToUInt16(_buffer, 0);
                bool wide = (header & 1) != 0;
                int length = header >> 6;
                int bytes = wide ? length * 2 : length;
                if (length is > 0 and <= 1024)
                {
                    var data = new byte[bytes];
                    if (_memory.Read(entry + 2, data, bytes))
                        text = wide ? Encoding.Unicode.GetString(data) : Encoding.Latin1.GetString(data);
                }
            }
        }
        _names[index] = text;
        return text;
    }

    // ── Plain reads ──

    private long Pointer(long address) => _memory.TryReadInt64(address, out long value) ? value : 0;

    private int? Int32(long address) => _memory.Read(address, _buffer, 4) ? BitConverter.ToInt32(_buffer, 0) : null;

    private byte? Byte(long address) => _memory.Read(address, _buffer, 1) ? _buffer[0] : null;

    private double? Float(long address) =>
        _memory.Read(address, _buffer, 4) && BitConverter.ToSingle(_buffer, 0) is float f && float.IsFinite(f) ? f : null;

    /// <summary>Three floats that look like a world position.</summary>
    private bool TryVec3(long address, out Vec3 value)
    {
        value = default;
        if (!_memory.Read(address, _buffer, 12))
            return false;
        float x = BitConverter.ToSingle(_buffer, 0), y = BitConverter.ToSingle(_buffer, 4), z = BitConverter.ToSingle(_buffer, 8);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
            Math.Abs(x) > 1e7 || Math.Abs(y) > 1e7 || Math.Abs(z) > 1e7)
            return false;
        value = new Vec3(x, y, z);
        return true;
    }

    /// <summary>An Unreal FString (pointer to UTF-16 text, length with the closing zero).</summary>
    private string ReadFString(long address, int maxLength)
    {
        long text = Pointer(address);
        int length = Int32(address + 8) ?? 0;
        if (!MemoryReads.IsPointer(text) || length <= 1 || length > maxLength)
            return "";
        var data = new byte[(length - 1) * 2];
        return _memory.Read(text, data, data.Length) ? Encoding.Unicode.GetString(data) : "";
    }

    public void Dispose() => _owned?.Dispose();
}
