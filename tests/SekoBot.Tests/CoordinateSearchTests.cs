using SekoBot.Config;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Memory made of byte arrays, for testing the searches without a game.</summary>
internal sealed class FakeMemory : IMemorySource
{
    public readonly List<(long Base, byte[] Data)> Blocks = [];

    public byte[] Add(long baseAddress, int size)
    {
        var data = new byte[size];
        Blocks.Add((baseAddress, data));
        return data;
    }

    public IReadOnlyList<MemoryRegion> Regions() => Blocks.Select(b => new MemoryRegion(b.Base, b.Data.Length)).ToList();

    public bool Read(long address, byte[] buffer, int count)
    {
        foreach (var (start, data) in Blocks)
        {
            if (address >= start && address + count <= start + data.Length)
            {
                Array.Copy(data, address - start, buffer, 0, count);
                return true;
            }
        }
        return false;
    }

    public void WriteFloat(long address, float value) => Write(address, BitConverter.GetBytes(value));
    public void WriteDouble(long address, double value) => Write(address, BitConverter.GetBytes(value));
    public void WriteLong(long address, long value) => Write(address, BitConverter.GetBytes(value));

    private void Write(long address, byte[] bytes)
    {
        foreach (var (start, data) in Blocks)
            if (address >= start && address + bytes.Length <= start + data.Length)
            {
                Array.Copy(bytes, 0, data, address - start, bytes.Length);
                return;
            }
        throw new ArgumentOutOfRangeException(nameof(address));
    }
}

public class CoordinateSearchTests
{
    private const long Heap = 0x2000_0000;

    /// <summary>
    /// A game in memory: random static values, values that change all the time (animations),
    /// and the character's (x, y, z) stored three times. The map arrow is read with a little noise
    /// and a small shift towards where the character faces.
    /// </summary>
    private sealed class Game
    {
        public readonly FakeMemory Memory = new();
        private readonly byte[] _heap;
        private readonly Random _random = new(7);
        private readonly int[] _animated;
        public readonly long[] Copies = [Heap + 0x1000, Heap + 0x40000, Heap + 0x90000];
        public readonly bool Doubles;
        public PointF Map = new(0.42f, 0.57f);
        private (double X, double Y) _facing = (0, -1);
        public bool SwappedAxes;

        public Game(bool doubles = false, bool swapped = false)
        {
            Doubles = doubles;
            SwappedAxes = swapped;
            _heap = Memory.Add(Heap, 1 << 20);
            _random.NextBytes(_heap);
            for (int i = 0; i < _heap.Length; i += 4) // mostly ordinary numbers, as in a real game
                if (_random.Next(3) == 0)
                    BitConverter.GetBytes((float)(_random.NextDouble() * 20000 - 10000)).CopyTo(_heap, i);
            _animated = Enumerable.Range(0, 20000).Select(_ => _random.Next(_heap.Length / 4) * 4).ToArray();
            WritePosition();
        }

        private void WritePosition()
        {
            // World units: 150 000 per map width, map y grows southwards like the world y.
            double wx = Map.X * 150000 - 60000, wy = Map.Y * 150000 + 20000;
            if (SwappedAxes)
                (wx, wy) = (wy, wx);
            foreach (long c in Copies)
            {
                if (Doubles)
                {
                    Memory.WriteDouble(c, wx);
                    Memory.WriteDouble(c + 8, wy);
                    Memory.WriteDouble(c + 16, 1234.5);
                }
                else
                {
                    Memory.WriteFloat(c, (float)wx);
                    Memory.WriteFloat(c + 4, (float)wy);
                    Memory.WriteFloat(c + 8, 1234.5f);
                }
            }
        }

        /// <summary>Time passes: animations change, the position doesn't.</summary>
        public void Tick()
        {
            foreach (int i in _animated)
                if (_random.Next(2) == 0)
                    BitConverter.GetBytes((float)(_random.NextDouble() * 1000)).CopyTo(_heap, i);
        }

        public void Walk(double dx, double dy)
        {
            const double step = 0.008; // about 3 px on the big map per walk
            Map = new PointF((float)(Map.X + dx * step), (float)(Map.Y + dy * step));
            _facing = (dx, dy);
            WritePosition();
            Tick();
        }

        public PointF Arrow()
        {
            double noise() => (_random.NextDouble() - 0.5) * 1.2 / 370;
            return new PointF((float)(Map.X + noise() + _facing.X * 1.0 / 370), (float)(Map.Y + noise() + _facing.Y * 1.0 / 370));
        }
    }

    private static CoordinateMatch? Run(Game game)
    {
        var search = new CoordinateSearch();
        Assert.Null(search.Begin(game.Memory, game.Arrow()));
        game.Tick();
        game.Walk(0.7, -0.7);
        Assert.Null(search.FirstMove(game.Memory, game.Arrow()));
        game.Tick();
        search.Still(game.Memory);
        (double, double)[] walks = [(0, -1), (1, 0), (1, 0), (0, 1), (-1, 0), (0, 1), (0.7, -0.7)];
        for (int i = 0; i < walks.Length; i++)
        {
            game.Walk(walks[i].Item1, walks[i].Item2);
            search.Move(game.Memory, game.Arrow());
            if (i % 2 == 1)
            {
                game.Tick();
                search.Still(game.Memory);
            }
        }
        return search.Result();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Finds_the_position_among_noise(bool doubles, bool swapped)
    {
        var game = new Game(doubles, swapped);
        var match = Run(game);
        Assert.NotNull(match);
        int size = doubles ? 8 : 4;
        Assert.Equal(size, match!.Size);
        long pair = swapped ? match.MapYAddress : match.MapXAddress;
        Assert.Contains(pair, game.Copies);
        Assert.Equal(pair + size, swapped ? match.MapXAddress : match.MapYAddress);

        // The found conversion turns memory back into the map position.
        var spec = new PositionSpec
        {
            Size = size, Ax = match.Ax, Bx = match.Bx, Ay = match.Ay, By = match.By,
            XAddress = match.MapXAddress, YAddress = match.MapYAddress
        };
        var reader = new PositionReader(game.Memory, spec, 0, absolute: true);
        var read = Assert.NotNull(reader.Read());
        Assert.InRange(read.X, game.Map.X - 0.01, game.Map.X + 0.01);
        Assert.InRange(read.Y, game.Map.Y - 0.01, game.Map.Y + 0.01);
    }

    [Theory]
    [InlineData(4.0777785e-43)] // the high half of a pointer read as a float
    [InlineData(-1.29e26)]      // the low half of a pointer
    [InlineData(double.NaN)]
    [InlineData(2e7)]
    public void Pointer_halves_and_other_non_numbers_are_not_coordinates(double value) =>
        Assert.False(CoordinateSearch.Plausible(value));

    [Theory]
    [InlineData(0)]
    [InlineData(3525.5)]
    [InlineData(-104450)]
    public void World_coordinates_are_plausible(double value) => Assert.True(CoordinateSearch.Plausible(value));

    [Fact]
    public void Nothing_found_when_nothing_follows_the_arrow()
    {
        var game = new Game();
        var search = new CoordinateSearch();
        Assert.Null(search.Begin(game.Memory, game.Arrow()));
        game.Walk(0.7, -0.7);
        Assert.Null(search.FirstMove(game.Memory, game.Arrow()));
        // The arrow moves, but the "position" in memory is frozen from now on.
        foreach (long c in game.Copies)
            game.Memory.WriteFloat(c, 1);
        for (int i = 0; i < 6; i++)
        {
            game.Map = new PointF(game.Map.X + 0.01f * (i % 2 == 0 ? 1 : -1), game.Map.Y + 0.01f);
            game.Tick();
            search.Move(game.Memory, game.Arrow());
            game.Tick();
            search.Still(game.Memory);
        }
        Assert.Null(search.Result());
    }
}

public class PointerSearchTests
{
    [Fact]
    public void Finds_a_path_through_two_pointers_and_follows_it()
    {
        var memory = new FakeMemory();
        memory.Add(0x1000_0000, 0x4000); // the character's object (a node on the HP path)
        memory.Add(0x3000_0000, 0x4000); // something it points to
        memory.Add(0x5000_0000, 0x4000); // the scene component holding the position
        memory.WriteLong(0x1000_0000 + 0x1A8, 0x3000_0000);
        memory.WriteLong(0x3000_0000 + 0x130, 0x5000_0000);
        long x = 0x5000_0000 + 0x260, y = x + 4;

        long[] nodes = [0x0900_0000, 0x1000_0000]; // the first can't be read
        var path = PointerSearch.Find(memory, nodes, x, y);
        Assert.NotNull(path);
        Assert.Equal(1, path!.Value.Node);
        Assert.Equal(new long[] { 0x1A8, 0x130 }, path.Value.Offsets);
        Assert.Equal(0x260L, path.Value.XOffset);
        Assert.Equal(0x264L, path.Value.YOffset);
        Assert.Equal((long?)0x5000_0000, PointerSearch.Resolve(memory, nodes, path.Value.Node, path.Value.Offsets));
    }
}
