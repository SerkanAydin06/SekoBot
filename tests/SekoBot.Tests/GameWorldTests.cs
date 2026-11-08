using System.Text;
using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class GameWorldTests
{
    private const long Module = 0x1_4000_0000;
    private const long Heap = 0x2000_0000;
    /// <summary>The readable part of the fake exe (around the two fixed places).</summary>
    private const long ModuleStart = 0x4CE0000, ModuleSize = 0x4D90000;

    /// <summary>A tiny Unreal Engine 4.27 game in fake memory, laid out like Mobile2.</summary>
    private sealed class FakeGame
    {
        public readonly FakeMemory Memory = new();
        private long _next = Heap + 0x100;
        private long _nameCursor;
        private readonly long _names;
        private readonly Dictionary<string, int> _nameIndex = [];

        public FakeGame()
        {
            Memory.Add(Module + ModuleStart, (int)(ModuleSize - ModuleStart));
            Memory.Add(Heap, 0x200000);
            _names = Alloc(0x4000);
            Memory.WriteLong(Module + GameWorldReader.NamePoolOffset + 0x10, _names);
            Name("None");
            Name("ByteProperty"); // every Unreal name table starts with these two
        }

        public long Alloc(int size)
        {
            long at = _next;
            _next += (size + 15) & ~15;
            return at;
        }

        public int Name(string text)
        {
            if (_nameIndex.TryGetValue(text, out int index))
                return index;
            index = (int)(_nameCursor / 2);
            var bytes = Encoding.ASCII.GetBytes(text);
            WriteBytes(_names + _nameCursor, BitConverter.GetBytes((ushort)(bytes.Length << 6)));
            WriteBytes(_names + _nameCursor + 2, bytes);
            _nameCursor += (2 + bytes.Length + 1) & ~1;
            _nameIndex[text] = index;
            return index;
        }

        public void WriteBytes(long address, byte[] bytes) => Memory.Write(address, bytes);

        public void Int(long address, int value) => WriteBytes(address, BitConverter.GetBytes(value));

        public void Vec(long address, float x, float y, float z)
        {
            Memory.WriteFloat(address, x);
            Memory.WriteFloat(address + 4, y);
            Memory.WriteFloat(address + 8, z);
        }

        public void FString(long address, string text)
        {
            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            long data = Alloc(bytes.Length);
            WriteBytes(data, bytes);
            Memory.WriteLong(address, data);
            Int(address + 8, text.Length + 1);
        }

        /// <param name="fields">Fields listed in the class' type information (name, offset).</param>
        public long Class(string name, params (string Name, int Offset)[] fields)
        {
            long cls = Alloc(0x100);
            Int(cls + GameWorldReader.ObjName, Name(name));
            long previous = cls + GameWorldReader.StructProperties - GameWorldReader.FieldNext;
            foreach (var (field, offset) in fields)
            {
                long property = Alloc(0x80);
                Int(property + GameWorldReader.FieldName, Name(field));
                Int(property + GameWorldReader.PropertyOffset, offset);
                Memory.WriteLong(previous + GameWorldReader.FieldNext, property);
                previous = property;
            }
            return cls;
        }

        public long Object(long cls, int size)
        {
            long obj = Alloc(size);
            Memory.WriteLong(obj + GameWorldReader.ObjClass, cls);
            return obj;
        }
    }

    private static (FakeGame Game, long Level, List<long> Actors, long NpcClass) World(string map = "c2") => World(out _, map);

    /// <param name="gameData">The game instance ("m2data_C").</param>
    private static (FakeGame Game, long Level, List<long> Actors, long NpcClass) World(out long gameData, string map = "c2")
    {
        var game = new FakeGame();
        gameData = game.Object(game.Class("m2data_C"), 0x1200);
        game.Memory.WriteLong(Module + GameWorldReader.GameInstanceOffset, gameData);
        long gm = game.Object(game.Class("gameManager"), 0xE00);
        game.Memory.WriteLong(gameData + 0x1F8, gm);
        game.Vec(gm + 0xA10, 19648.3f, 18054.3f, 963.1f);
        game.FString(gm + 0x958, map);
        long level = game.Object(game.Class("Level"), 0x200);
        game.Memory.WriteLong(gm + GameWorldReader.ObjOuter, level);
        // The NPC class lists HP and maxHP somewhere else than in the tested build.
        long npcClass = game.Class("NPC", ("HP", 0x600), ("maxHP", 0x604));
        return (game, level, [], npcClass);
    }

    private static long Npc(FakeGame game, long cls, string name, int level, int hp, int maxHp, float x, float y,
        bool player = false, bool dead = false, bool attackable = true, bool owned = false)
    {
        long npc = game.Object(cls, 0x900);
        long root = game.Object(0, 0x200);
        game.Memory.WriteLong(npc + GameWorldReader.ActorRoot, root);
        game.Vec(root + GameWorldReader.ComponentWorldLocation, x, y, 900);
        game.Int(npc + 0x348, 101); // vnum
        game.FString(npc + 0x348 + 8, name);
        game.Int(npc + 0x348 + 0x3C, level);
        game.Int(npc + 0x600, hp);
        game.Int(npc + 0x604, maxHp);
        game.Memory.Write(npc + 0x506, 1); // spawned
        game.Memory.Write(npc + 0x50C, (byte)(dead ? 1 : 0));
        game.Memory.Write(npc + 0x628, (byte)(player ? 1 : 0));
        game.Memory.Write(npc + 0x798, (byte)(attackable ? 1 : 0));
        game.Memory.Write(npc + 0x7A2, (byte)(owned ? 1 : 0));
        if (player)
            game.FString(npc + 0x640, name);
        return npc;
    }

    private static void SetActors(FakeGame game, long level, List<long> actors)
    {
        long data = game.Alloc(actors.Count * 8 + 8);
        for (int i = 0; i < actors.Count; i++)
            game.Memory.WriteLong(data + i * 8, actors[i]);
        game.Memory.WriteLong(level + GameWorldReader.LevelActors, data);
        game.Int(level + GameWorldReader.LevelActors + 8, actors.Count);
    }

    [Fact]
    public void Reads_player_living_mobs_and_camera()
    {
        var (game, level, actors, npc) = World();
        actors.Add(game.Object(game.Class("StaticMeshActor"), 0x200));
        actors.Add(Npc(game, npc, "Aç Mavi Kurt", 6, 120, 180, 19700, 18000));
        actors.Add(Npc(game, npc, "Ölü Kurt", 6, 0, 180, 19800, 18000, dead: true));
        actors.Add(Npc(game, npc, "Receoplex", 0, 1, 1, 19900, 18000, player: true, attackable: false));
        long gm = Npc(game, npc, "Ayaz", 0, 1, 1, 19950, 18000, player: true, attackable: false);
        game.Memory.Write(gm + 0x6B5, 1); // isGM
        actors.Add(gm);
        actors.Add(Npc(game, npc, "Satıcı", 1, 100, 100, 19600, 18000, attackable: false));
        actors.Add(Npc(game, npc, "Evcil", 1, 100, 100, 19600, 18100, owned: true));
        long camera = game.Object(game.Class("PlayerCameraManager"), 0x2200);
        game.Vec(camera + 0x1AF0 + GameWorldReader.CameraCachePov, 19785.9f, 18128.5f, 2019.5f);
        game.Vec(camera + 0x1AF0 + GameWorldReader.CameraCachePov + 0xC, -81.5f, -151.7f, 0);
        game.Memory.WriteFloat(camera + 0x1AF0 + GameWorldReader.CameraCachePov + 0x18, 90);
        actors.Add(camera);
        SetActors(game, level, actors);

        using var reader = new GameWorldReader(game.Memory, Module);
        var world = reader.Read();

        Assert.NotNull(world);
        Assert.Equal("c2", world.MapName);
        Assert.Equal(19648.3, world.Player.X, 1);
        Assert.Equal(18054.3, world.Player.Y, 1);
        var mob = Assert.Single(world.Mobs);
        Assert.Equal("Aç Mavi Kurt", mob.Name);
        Assert.Equal(6, mob.Level);
        Assert.Equal(120, mob.Hp);
        Assert.Equal(180, mob.MaxHp);
        Assert.Equal(19700, mob.Position.X, 1);
        Assert.Equal(2, world.OtherPlayers);
        Assert.Equal(["Receoplex", "Ayaz"], world.Players!.Select(p => p.Name));
        Assert.Equal([false, true], world.Players!.Select(p => p.LooksLikeGm));
        Assert.NotNull(world.Camera);
        Assert.Equal(90, world.Camera.Fov, 3);
        Assert.Equal(-81.5, world.Camera.Pitch, 3);
    }

    [Fact]
    public void Reads_the_selected_mob_and_its_health()
    {
        var (game, level, actors, npc) = World(out long gameData);
        long bear = Npc(game, npc, "Aç Ayı", 8, 61, 305, 19700, 18000);
        actors.Add(bear);
        SetActors(game, level, actors);
        using var reader = new GameWorldReader(game.Memory, Module);

        Assert.Null(reader.Read()!.Target);
        Assert.Equal(0, reader.Read()!.TargetHp);

        game.Memory.WriteLong(gameData + 0x1C0, bear); // selectedActor
        var world = reader.Read()!;
        Assert.Equal("Aç Ayı", world.Target!.Name);
        Assert.Equal(0.2, world.TargetHp, 3);

        game.Memory.Write(bear + 0x50C, 1); // it died: still the target, with no health
        world = reader.Read()!;
        Assert.Equal(0, world.TargetHp);
        Assert.Empty(world.Mobs);
    }

    [Fact]
    public void Reads_hp_mp_level_and_death()
    {
        var (game, level, actors, _) = World(out long gameData);
        SetActors(game, level, actors);
        long loader = game.Object(game.Class("loader"), 0x900);
        game.Memory.WriteLong(gameData + 0x1188, loader);
        game.Int(loader + 0x770 + 0x34, 801);  // HP
        game.Int(loader + 0x770 + 0x3C, 390);  // SP (MP)
        game.Int(loader + 0x770 + 0x68, 1236); // fullHP
        game.Int(loader + 0x770 + 0x88, 480);  // fullSp
        game.Int(loader + 0x770 + 0x90, 11);   // Level
        long gm = BitConverter.ToInt64(Read(game, gameData + 0x1F8, 8));
        long pawn = game.Object(game.Class("playerMove"), 0x900);
        game.Memory.WriteLong(gm + 0x380, pawn);
        using var reader = new GameWorldReader(game.Memory, Module);

        var world = reader.Read()!;
        Assert.Equal(new CharacterStats(801, 390, 1236, 480), world.Stats);
        Assert.Equal(11, world.Level);
        Assert.False(world.Dead);

        game.Memory.Write(pawn + 0x778, 1);
        Assert.True(reader.Read()!.Dead);
    }

    [Fact]
    public void Reads_experience_money_inventory_and_quick_slots()
    {
        var (game, level, actors, _) = World(out long gameData);
        SetActors(game, level, actors);
        long loader = game.Object(game.Class("loader"), 0x900);
        game.Memory.WriteLong(gameData + 0x1188, loader);
        long data = loader + 0x770;
        game.Memory.WriteLong(data + 0x70, 10538);  // Exp
        game.Memory.WriteLong(data + 0x78, 123442); // needexp (left to the next level)
        game.FString(data + 0x48, "90716");         // yang
        // Inventory: TMap slot → item, 3 elements of which the middle one is free.
        long map = data + 0xD8;
        long elements = game.Alloc(3 * 0x98);
        (int Slot, int Vnum, int Count)[] stacks = [(12, 27201, 181), (13, 99999, 5), (15, 27204, 182)];
        for (int i = 0; i < stacks.Length; i++)
        {
            game.Int(elements + i * 0x98, stacks[i].Slot);
            game.Int(elements + i * 0x98 + 8, stacks[i].Vnum);
            game.Int(elements + i * 0x98 + 8 + 0x3C, stacks[i].Count);
        }
        game.Memory.WriteLong(map, elements);
        game.Int(map + 8, 3);
        game.Int(map + 0x10, 0b101);  // allocation bits (inline)
        game.Int(map + 0x2C, 128);    // max bits: inline
        long quick = game.Alloc(16);
        game.Int(quick, 12);
        game.Int(quick + 4, 15);
        game.Int(quick + 8, -104);
        game.Memory.WriteLong(loader + 0x278, quick);
        game.Int(loader + 0x278 + 8, 3);
        using var reader = new GameWorldReader(game.Memory, Module);

        var progress = reader.Read()!.Progress!;

        Assert.Equal(10538, progress.Exp);
        Assert.Equal(123442, progress.NeedExp);
        Assert.Equal(90716, progress.Yang);
        Assert.Equal(2, progress.Inventory.Count);
        Assert.Equal(27201, progress.QuickSlotVnum(1));
        Assert.Equal(182, progress.Count(27204));
        Assert.Equal(0, progress.Count(99999)); // the free element is not an item
    }

    [Fact]
    public void Reads_attackers_metins_ground_items_and_the_safe_zone()
    {
        var (game, level, actors, npc) = World(out long gameData);
        long gm = BitConverter.ToInt64(Read(game, gameData + 0x1F8, 8));
        long pawn = game.Object(game.Class("playerMove"), 0x900);
        game.Memory.WriteLong(gm + 0x380, pawn);
        long wolf = Npc(game, npc, "Aç Alfa Kurt", 4, 145, 145, 19700, 18000);
        game.Memory.WriteLong(wolf + 0x860, pawn); // targetActorMe: after us
        actors.Add(wolf);
        long stone = Npc(game, npc, "Savaş Metini", 15, 34050, 34050, 19800, 18000);
        game.Int(stone + 0x348, 8003);
        actors.Add(stone);
        long itemClass = game.Class("droppedItem");
        foreach (bool disabled in new[] { false, true })
        {
            long item = game.Object(itemClass, 0x300);
            long root = game.Object(0, 0x200);
            game.Memory.WriteLong(item + GameWorldReader.ActorRoot, root);
            game.Vec(root + GameWorldReader.ComponentWorldLocation, 19660, 18060, 900);
            game.Memory.Write(item + 0x234, (byte)(disabled ? 1 : 0)); // picked up already
            actors.Add(item);
        }
        SetActors(game, level, actors);
        using var reader = new GameWorldReader(game.Memory, Module);

        var world = reader.Read()!;
        Assert.True(world.Mobs.Single(m => m.Name == "Aç Alfa Kurt").AttacksMe);
        Assert.True(world.Mobs.Single(m => m.Name == "Savaş Metini").IsMetin);
        Assert.False(world.Mobs.Single(m => m.Name == "Aç Alfa Kurt").IsMetin);
        Assert.Single(world.Drops!);
        Assert.False(world.InSafeZone);

        game.Memory.Write(pawn + 0x87C, 1); // isInSafe
        Assert.True(reader.Read()!.InSafeZone);
    }

    [Fact]
    public void Finds_the_fixed_places_again_after_a_game_update()
    {
        var (game, level, actors, npc) = World();
        actors.Add(Npc(game, npc, "Kurt", 3, 162, 162, 19700, 18000));
        SetActors(game, level, actors);
        using var reader = new GameWorldReader(game.Memory, Module, ModuleSize);
        reader.UseOffsets(ModuleStart + 0x100, ModuleStart + 0x200); // where they were "before the update"

        var world = reader.Read();

        Assert.NotNull(world);
        Assert.True(reader.Searched);
        Assert.Equal("Kurt", Assert.Single(world.Mobs).Name);
    }

    [Fact]
    public void No_world_without_the_game()
    {
        var memory = new FakeMemory();
        memory.Add(Module + ModuleStart, (int)(ModuleSize - ModuleStart));
        using var reader = new GameWorldReader(memory, Module, ModuleSize);
        Assert.Null(reader.Read());
        Assert.False(reader.Searched);
    }

    private static byte[] Read(FakeGame game, long address, int count)
    {
        var bytes = new byte[count];
        Assert.True(game.Memory.Read(address, bytes, count));
        return bytes;
    }

    [Fact]
    public void Camera_direction_tells_which_way_is_screen_up()
    {
        // Looking along world +Y (yaw 90): +Y is "up" on screen, +X is "left".
        var camera = new CameraView(new Vec3(0, 0, 1000), -80, 90, 0, 90);
        var (right, up) = camera.ToScreenDirection(0, 100);
        Assert.Equal(0, right, 3);
        Assert.Equal(100, up, 3);
        (right, up) = camera.ToScreenDirection(100, 0);
        Assert.Equal(-100, right, 3);
        Assert.Equal(0, up, 3);
    }

    [Fact]
    public void Camera_looking_down_puts_points_around_the_centre()
    {
        // Straight down from 1000 units up; the top of the picture is world +X.
        var camera = new CameraView(new Vec3(0, 0, 1000), -90, 0, 0, 90);
        var below = camera.ToScreen(new Vec3(0, 0, 0), 1200, 700)!.Value;
        Assert.Equal(600, below.X, 1);
        Assert.Equal(350, below.Y, 1);
        var ahead = camera.ToScreen(new Vec3(500, 0, 0), 1200, 700)!.Value;
        Assert.Equal(600, ahead.X, 1);
        Assert.Equal(350 - 300, ahead.Y, 1); // 500 units at depth 1000 with a 90° view = ¼ of the width
        var right = camera.ToScreen(new Vec3(0, 500, 0), 1200, 700)!.Value;
        Assert.Equal(600 + 300, right.X, 1);
        Assert.Null(camera.ToScreen(new Vec3(0, 0, 2000), 1200, 700)); // behind the camera
    }

    [Theory]
    [InlineData(600, 350, true)]
    [InlineData(1150, 100, false)] // minimap
    [InlineData(60, 300, false)]   // buttons on the left
    [InlineData(600, 670, false)]  // skill bar
    [InlineData(60, 600, true)]
    public void Only_clicks_on_the_playfield(float x, float y, bool expected) =>
        Assert.Equal(expected, BotEngine.OnPlayfield(new PointF(x, y), 1204, 688));
}
