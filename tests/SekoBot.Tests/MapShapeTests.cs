using SekoBot.Bot;
using Xunit;

namespace SekoBot.Tests;

public class MapShapeTests
{
    private static (byte R, byte G, byte B)[] Field(int w, int h)
    {
        var p = new (byte, byte, byte)[w * h];
        Array.Fill(p, ((byte)54, (byte)66, (byte)30)); // grass
        return p;
    }

    [Fact]
    public void Mountains_are_the_wide_grey_violet_patches()
    {
        int w = 120, h = 120;
        var p = Field(w, h);
        for (int y = 30; y < 80; y++)
            for (int x = 30; x < 80; x++)
                p[y * w + x] = (95, 95, 118);
        var m = RoadMap.MountainPixels(p, w, h);
        Assert.True(m[55 * w + 55]);
        Assert.False(m[10 * w + 10]);
        Assert.False(m[30 * w + 30]); // the foot (edge) can be walked
    }

    [Fact]
    public void A_round_grey_square_is_a_plaza_but_a_grey_line_is_not()
    {
        int w = 200, h = 200;
        var p = Field(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if ((x - 60) * (x - 60) + (y - 60) * (y - 60) <= 15 * 15)
                    p[y * w + x] = (88, 88, 93);
        for (int x = 100; x < 190; x++)
            p[150 * w + x] = p[151 * w + x] = (88, 88, 93);
        var plaza = RoadMap.Plazas(p, w, h, 1);
        Assert.True(plaza[60 * w + 60]);
        Assert.False(plaza[150 * w + 140]);
    }
}
