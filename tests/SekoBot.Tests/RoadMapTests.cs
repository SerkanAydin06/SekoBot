using System.Collections;
using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class RoadMapTests
{
    [Fact]
    public void A_light_line_through_the_ground_is_road_a_speck_and_the_ground_not()
    {
        // 120 × 120 of dark green ground (c2-like), a light grey road across it, a light speck off it, water at the edge.
        int w = 120, h = 120;
        var px = new (byte R, byte G, byte B)[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = x > 110 ? ((byte)70, (byte)90, (byte)140) : ((byte)54, (byte)64, (byte)32);
        for (int x = 0; x < 100; x++)
            px[60 * w + x] = px[61 * w + x] = (120, 118, 105);
        px[20 * w + 20] = (120, 118, 105);
        var road = RoadMap.RoadPixels(px, w, h);
        Assert.True(road[60 * w + 50]);
        Assert.False(road[30 * w + 50]);     // ground
        Assert.False(road[20 * w + 20]);     // a speck
        Assert.False(road[60 * w + 115]);    // water
    }

    [Fact]
    public void A_long_way_keeps_to_the_road_even_when_the_straight_way_is_free()
    {
        // 10 km square, a road going north 2 km east of the start, then west to the goal.
        int n = 100;
        var bits = new BitArray(n * n);
        for (int r = 0; r < n; r++)
        {
            bits[r * n + 30] = bits[r * n + 31] = true;           // x 3000..3200 north–south
            if (r is 80 or 81)
                for (int c = 0; c <= 31; c++) bits[r * n + c] = true; // y 8000..8200 east–west
        }
        var roads = new RoadMap("t", 0, 0, n, n, bits);
        var t = new MapTerrain("t", [], DateTime.Now) { Roads = roads };
        Vec3 from = new(3100, 1000, 0), to = new(500, 8100, 0);
        var way = t.WayTo(from, to, roads: true)!;
        Assert.NotNull(way);
        // Along the road: north first (x stays near 3100 up to y ~8000), not across the field.
        Assert.Contains(way, p => Math.Abs(p.X - 3100) < 300 && p.Y > 7000);
        Assert.Null(new MapTerrain("t", [], DateTime.Now).WayTo(from, to, roads: true) is { Count: > 1 } ? "corners" : null); // no roads: straight
    }
}
