using System.Numerics;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class MobScanTests
{
    [Fact]
    public void Finds_positions_near_ours_but_not_our_own_copies()
    {
        var memory = new FakeMemory();
        memory.Add(0x4000_0000, 0x1000);
        memory.WriteFloat(0x4000_0100, 5000f); memory.WriteFloat(0x4000_0104, 9000f);   // our copy
        memory.WriteFloat(0x4000_0200, 5300f); memory.WriteFloat(0x4000_0204, 8800f);   // a mob
        memory.WriteFloat(0x4000_0300, 90000f); memory.WriteFloat(0x4000_0304, 8800f);  // far away
        var near = MobScan.FindNear(memory, memory.Regions(), 4, 5000, 9000, 1000);
        var mob = Assert.Single(near);
        Assert.Equal(0x4000_0200, mob.Address);
        Assert.Equal(300, mob.Dx, 3);
        Assert.Equal(-200, mob.Dy, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Finds_the_camera_projection_from_names_on_screen(bool mirrored)
    {
        var random = new Random(3);
        // Real projection: 0.12 px per world unit, turned by 30°.
        var scale = Complex.FromPolarCoordinates(0.12, Math.PI / 6);
        PointF Project(double dx, double dy)
        {
            var s = new Complex(dx, mirrored ? -dy : dy) * scale;
            return new PointF((float)s.Real, (float)s.Imaginary);
        }
        var frames = new List<MobScanFrame>();
        long address = 0x1000;
        for (int f = 0; f < 6; f++)
        {
            var names = new List<PointF>();
            var positions = new List<NearbyPosition>();
            for (int m = 0; m < 8; m++) // mobs: a name on screen and a position in memory
            {
                double dx = random.Next(-6000, 6000), dy = random.Next(-4000, 4000);
                var screen = Project(dx, dy);
                names.Add(new PointF(screen.X + random.Next(-10, 10), screen.Y + random.Next(-10, 10)));
                positions.Add(new NearbyPosition(address += 0x100, dx, dy));
            }
            for (int n = 0; n < 40; n++) // NPCs, other players, stale copies: positions without a name
                positions.Add(new NearbyPosition(address += 0x100, random.Next(-7000, 7000), random.Next(-7000, 7000)));
            frames.Add(new MobScanFrame(names, positions));
        }

        var match = MobScan.Match(frames);
        Assert.NotNull(match);
        Assert.Equal(mirrored, match!.Mirrored);
        Assert.True(match.Matched >= 40, $"only {match.Matched} of {match.Names} names matched");
        Assert.InRange(match.Scale.Magnitude, 0.11, 0.13);
        Assert.InRange(match.Scale.Phase, Math.PI / 6 - 0.05, Math.PI / 6 + 0.05);
    }

    [Fact]
    public void No_projection_when_positions_have_nothing_to_do_with_the_names()
    {
        var random = new Random(5);
        var frames = Enumerable.Range(0, 6).Select(_ => new MobScanFrame(
            Enumerable.Range(0, 8).Select(_ => new PointF(random.Next(-900, 900), random.Next(-500, 500))).ToList(),
            Enumerable.Range(0, 40).Select(i => new NearbyPosition(0x1000 + i * 16, random.Next(-7000, 7000), random.Next(-7000, 7000))).ToList()))
            .ToList();
        var match = MobScan.Match(frames);
        Assert.True(match == null || match.Matched < 0.4 * match.Names, $"matched {match?.Matched} by chance");
    }
}
