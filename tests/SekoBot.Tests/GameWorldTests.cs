using System.Text;
using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class GameWorldTests
{
    private const long Module = 0x1_4000_0000;
    private const long Heap = 0x2000_0000;

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
            Memory.Add(Module + 0x4D35000, 0x60000);
            Memory.Add(Heap, 0x200000);
            _names = Alloc(0x4000);
            Memory.WriteLong(Module + GameWorldReader.NamePoolOffset + 0x10, _names);
            Name("None");
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

    /// <param name="gameData">The game data object ("m2data_C", the step before gameManager).</param>
    private static (FakeGame Game, long Level, List<long> Actors, long NpcClass) World(out long gameData, string map = "c2")
    {
        var game = new FakeGame();
        // HP pointer root → … → game data → gameManager
        long cursor = Module + CharacterStatsReader.HpBaseOffset;
        gameData = 0;
        foreach (long offset in GameWorldReader.GameManagerPath)
        {
            long next = game.Alloc(0x400);
            game.Memory.WriteLong(cursor, next);
            cursor = next + offset;
            gameData = next;
        }
        long gm = game.Object(game.Class("gameManager"), 0xE00);
        game.Memory.WriteLong(cursor, gm);
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
        actors.Add(Npc(game, npc, "", 0, 1, 1, 19900, 18000, player: true, attackable: false));
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
        Assert.Equal(1, world.OtherPlayers);
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
    public void No_world_without_the_game_manager()
    {
        var memory = new FakeMemory();
        memory.Add(Module + 0x4D35000, 0x60000);
        using var reader = new GameWorldReader(memory, Module);
        Assert.Null(reader.Read());
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

public class MapCalibrationTests
{
    [Fact]
    public void Learns_the_big_map_from_a_few_walks()
    {
        // The map's x follows world Y, its y follows world X (turned map), with a little arrow noise.
        PointF Map(double wx, double wy, double noise) =>
            new((float)(0.00002 * wy + 0.1 + noise), (float)(-0.00002 * wx + 0.9 - noise));
        var calibration = new MapCalibration();
        (double X, double Y)[] walk = [(19600, 18000), (20000, 18400), (20300, 18100), (19900, 17700), (19500, 17900)];
        for (int i = 0; i < walk.Length; i++)
            calibration.Add(new Vec3(walk[i].X, walk[i].Y, 900), Map(walk[i].X, walk[i].Y, (i % 2 == 0 ? 1 : -1) * 0.0003));

        var spec = calibration.Fit("c2");

        Assert.NotNull(spec);
        Assert.True(spec.Swapped);
        Assert.Equal("c2", spec.MapName);
        var predicted = spec.ToMap(new Vec3(19800, 18200, 0));
        var expected = Map(19800, 18200, 0);
        Assert.Equal(expected.X, predicted.X, 2);
        Assert.Equal(expected.Y, predicted.Y, 2);
    }

    [Fact]
    public void Needs_pairs_from_different_places()
    {
        var calibration = new MapCalibration();
        Assert.True(calibration.Add(new Vec3(1000, 1000, 0), new PointF(0.5f, 0.5f)));
        Assert.False(calibration.Add(new Vec3(1010, 1005, 0), new PointF(0.5f, 0.5f))); // same place
        Assert.True(calibration.Add(new Vec3(1100, 1000, 0), new PointF(0.502f, 0.5f)));
        Assert.True(calibration.Add(new Vec3(1200, 1000, 0), new PointF(0.504f, 0.5f)));
        Assert.Null(calibration.Fit("c2")); // walked along one axis only
    }
}
