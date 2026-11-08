using System.Text;

namespace SekoBot.Memory;

/// <summary>A point in the game world (Unreal units: 100 = 1 metre; Z is up).</summary>
internal readonly record struct Vec3(double X, double Y, double Z)
{
    public double DistanceXY(Vec3 other) => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
}

/// <summary>A living mob that can be attacked.</summary>
internal sealed record WorldMob(int Id, int Vnum, string Name, int Level, int Hp, int MaxHp, Vec3 Position);

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
}

/// <summary>One look at the game world.</summary>
/// <param name="OtherPlayers">Other players around (not mobs).</param>
/// <param name="Target">The mob selected in the game (its bar shows at the top), or null.</param>
internal sealed record WorldSnapshot(Vec3 Player, CameraView? Camera, IReadOnlyList<WorldMob> Mobs, int OtherPlayers, string MapName,
    WorldMob? Target = null)
{
    /// <summary>The selected mob's health (0..1); 0 when nothing (alive) is selected.</summary>
    public double TargetHp => Target is { MaxHp: > 0 } t ? Math.Clamp((double)t.Hp / t.MaxHp, 0, 1) : 0;

    /// <summary>Mobs nearest first.</summary>
    public IEnumerable<WorldMob> ByDistance => Mobs.OrderBy(m => m.Position.DistanceXY(Player));
}

/// <summary>
/// Reads the character's world position, the camera and the mobs around from the game's memory.
/// The game is made with Unreal Engine 4.27: everything in the world is an "actor" in the level's
/// actor list; mobs (and other players) are actors of the game class "NPC". The game's own field
/// names (HP, dead, attackable, …) are looked up in Unreal's type information, so a game update
/// that moves those fields around doesn't break the reader. Only ever reads; never writes.
/// </summary>
internal sealed class GameWorldReader : IDisposable
{
    // ── Fixed places in this game build (Unreal Engine 4.27) ──
    /// <summary>Same root as the HP pointer; the first four steps of its path lead to the game data
    /// ("m2data_C", which knows the selected mob), the fifth to "gameManager".</summary>
    internal static readonly long[] GameManagerPath = [0x18, 0x8, 0x80, 0xC8, 0x1F8];
    /// <summary>FNamePool (all object and field names); its block pointers start at +0x10.</summary>
    internal const long NamePoolOffset = 0x04D8AFC0;
    // UObject
    internal const int ObjClass = 0x10, ObjName = 0x18, ObjOuter = 0x20;
    // UStruct / UClass
    internal const int StructSuper = 0x48, StructProperties = 0x58;
    // FField / FProperty
    internal const int FieldClass = 0x08, FieldNext = 0x20, FieldName = 0x28, PropertyOffset = 0x4C;
    // ULevel: Actors (TArray)
    internal const int LevelActors = 0xA0;
    // AActor: RootComponent; USceneComponent: ComponentToWorld translation
    internal const int ActorRoot = 0x138, ComponentWorldLocation = 0x1E0;
    // FCameraCacheEntry: POV (FMinimalViewInfo: Location, Rotation, FOV)
    internal const int CameraCachePov = 0x10;
    private const int MaxActors = 50_000;

    /// <summary>The game fields the reader uses, with where they were in the tested build (fallbacks).</summary>
    internal static readonly (string Class, string Field, int Fallback)[] Fields =
    [
        ("gameManager", "playerPos", 0xA10),
        ("gameManager", "mapname", 0x958),
        ("NPC", "thismob", 0x348),
        ("NPC", "Remove", 0x502),
        ("NPC", "spawned", 0x506),
        ("NPC", "dead", 0x50C),
        ("NPC", "HP", 0x544),
        ("NPC", "maxHP", 0x548),
        ("NPC", "ID", 0x54C),
        ("NPC", "isplayer", 0x628),
        ("NPC", "attackable", 0x798),
        ("NPC", "isOwnedMob", 0x7A2),
        ("PlayerCameraManager", "CameraCachePrivate", 0x1AF0),
        ("m2data_C", "selectedActor", 0x1C0),
    ];
    // In the "mob" struct (thismob): vnum, Name (FString), Level
    private const int MobVnum = 0x0, MobName = 0x8, MobLevel = 0x3C;

    private readonly IMemorySource _memory;
    private readonly long _moduleBase;
    private readonly IDisposable? _owned;
    private readonly Dictionary<int, string> _names = [];
    private readonly Dictionary<(long Class, string Field), int> _offsets = [];
    private readonly Dictionary<long, string> _classNames = [];
    private readonly Dictionary<(int Vnum, long Text), string> _mobNames = [];
    private readonly byte[] _buffer = new byte[64];
    /// <summary>Actors that are not NPCs (trees, lights, …): their class isn't read again for a while.</summary>
    private readonly HashSet<long> _otherActors = [];
    private DateTime _otherActorsSince = DateTime.UtcNow;
    private byte[] _npc = [];

    internal GameWorldReader(IMemorySource memory, long moduleBase, IDisposable? owned = null)
    {
        _memory = memory;
        _moduleBase = moduleBase;
        _owned = owned;
    }

    public string Status { get; private set; } = "Dünya bilgisi bekleniyor";

    /// <returns>A reader, or null (with the reason) when the game can't be read.</returns>
    public static GameWorldReader? Open(uint processId, out string error)
    {
        var memory = ProcessMemory.Open(processId, out error);
        return memory == null ? null : new GameWorldReader(memory, memory.ModuleBase, memory);
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
        if (GameManager(out long gameData) is not long gm)
        {
            Status = "Oyun dünyası bulunamadı (yükleme ekranı ya da oyun güncellendi)";
            return null;
        }
        long gmClass = Pointer(gm + ObjClass);
        int posOffset = Offset(gmClass, "gameManager", "playerPos");
        if (!TryVec3(gm + posOffset, out var player))
        {
            Status = "Karakter konumu okunamadı";
            return null;
        }
        string mapName = ReadFString(gm + Offset(gmClass, "gameManager", "mapname"), 64);

        long level = Pointer(gm + ObjOuter);
        if (!MemoryReads.IsPointer(level) || !TryActors(level, out long[] actors))
        {
            Status = "Haritadaki nesne listesi okunamadı";
            return null;
        }

        CameraView? camera = null;
        var mobs = new List<WorldMob>();
        int players = 0;
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
                    case true: players++; break;
                }
            }
            else if (className == "PlayerCameraManager")
                camera ??= ReadCamera(actor + Offset(cls, className, "CameraCachePrivate") + CameraCachePov);
            else
                _otherActors.Add(actor);
        }
        WorldMob? target = null;
        long dataClass = Pointer(gameData + ObjClass);
        long selected = Pointer(gameData + Offset(dataClass, "m2data_C", "selectedActor"));
        if (MemoryReads.IsPointer(selected) && Pointer(selected + ObjClass) is long selectedClass && ClassName(selectedClass) == "NPC")
            target = ReadNpc(selected, selectedClass, selected: true) as WorldMob;
        Status = $"Bellekten: {mobs.Count} mob";
        return new WorldSnapshot(player, camera, mobs, players, mapName, target);
    }

    /// <returns>A <see cref="WorldMob"/>, true for another player, or null for anything else.</returns>
    /// <param name="selected">The selected mob: also when it just died (HP 0), so the bot sees it die.</param>
    private object? ReadNpc(long actor, long cls, bool selected = false)
    {
        // All fields in one read (much faster than one read per field).
        int span = 0;
        foreach (var f in Fields)
            if (f.Class == "NPC")
                span = Math.Max(span, Offset(cls, "NPC", f.Field));
        span = Math.Max(span + 0x100, ActorRoot + 8); // + the "mob" struct's first fields
        if (_npc.Length != span)
            _npc = new byte[span];
        if (!_memory.Read(actor, _npc, span))
            return null;
        var data = _npc;
        bool Flag(string field) => data[Offset(cls, "NPC", field)] != 0;
        int Int(string field) => BitConverter.ToInt32(data, Offset(cls, "NPC", field));

        if (Flag("isplayer"))
            return true;
        if (!Flag("attackable") || Flag("isOwnedMob") || !selected && (Flag("dead") || Flag("Remove") || !Flag("spawned")))
            return null;
        int hp = Int("HP"), maxHp = Int("maxHP");
        if (selected && (Flag("dead") || Flag("Remove")))
            hp = 0;
        if (maxHp <= 0 || hp > maxHp || hp < 0 || !selected && hp == 0)
            return null;
        long root = BitConverter.ToInt64(data, ActorRoot);
        if (!MemoryReads.IsPointer(root) || !TryVec3(root + ComponentWorldLocation, out var position))
            return null;
        int mob = Offset(cls, "NPC", "thismob");
        int vnum = BitConverter.ToInt32(data, mob + MobVnum);
        long text = BitConverter.ToInt64(data, mob + MobName);
        if (!_mobNames.TryGetValue((vnum, text), out string? name))
            _mobNames[(vnum, text)] = name = ReadFString(actor + mob + MobName, 64);
        return new WorldMob(Int("ID"), vnum, name, BitConverter.ToInt32(data, mob + MobLevel), hp, maxHp, position);
    }

    private CameraView? ReadCamera(long pov)
    {
        if (!TryVec3(pov, out var location) || Float(pov + 0xC) is not double pitch ||
            Float(pov + 0x10) is not double yaw || Float(pov + 0x14) is not double roll ||
            Float(pov + 0x18) is not double fov || fov is < 5 or > 170)
            return null;
        return new CameraView(location, pitch, yaw, roll, fov);
    }

    private long? GameManager(out long gameData)
    {
        gameData = 0;
        long cursor = Pointer(_moduleBase + CharacterStatsReader.HpBaseOffset);
        foreach (long offset in GameManagerPath)
        {
            if (!MemoryReads.IsPointer(cursor))
                return null;
            gameData = cursor;
            cursor = Pointer(cursor + offset);
        }
        return MemoryReads.IsPointer(cursor) && ClassName(Pointer(cursor + ObjClass)) == "gameManager" ? cursor : null;
    }

    private bool TryActors(long level, out long[] actors)
    {
        actors = [];
        long data = Pointer(level + LevelActors);
        int count = Int32(level + LevelActors + 8) ?? 0;
        if (!MemoryReads.IsPointer(data) || count <= 0 || count > MaxActors)
            return false;
        var bytes = new byte[count * 8];
        if (!_memory.Read(data, bytes, bytes.Length))
            return false;
        actors = new long[count];
        Buffer.BlockCopy(bytes, 0, actors, 0, bytes.Length);
        return true;
    }

    // ── Unreal type information ──

    /// <summary>Where a field of a game class is, from the class' own field list (fallback: the tested build).</summary>
    private int Offset(long cls, string className, string field)
    {
        if (_offsets.TryGetValue((cls, field), out int offset))
            return offset;
        offset = FindProperty(cls, field) ?? Fields.First(f => f.Class == className && f.Field == field).Fallback;
        _offsets[(cls, field)] = offset;
        return offset;
    }

    private int? FindProperty(long cls, string field)
    {
        for (int depth = 0; depth < 8 && MemoryReads.IsPointer(cls); depth++, cls = Pointer(cls + StructSuper))
        {
            long property = Pointer(cls + StructProperties);
            for (int i = 0; i < 2000 && MemoryReads.IsPointer(property); i++, property = Pointer(property + FieldNext))
                if (Int32(property + FieldName) is int name && Name(name) == field)
                    return Int32(property + PropertyOffset) is int o and >= 0 and < 0x100000 ? o : null;
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
        long block = Pointer(_moduleBase + NamePoolOffset + 0x10 + 8L * ((uint)index >> 16));
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
