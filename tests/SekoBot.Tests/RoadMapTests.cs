using System.Collections;
using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class RoadMapTests
{
    [Fact]
    public void Brown_ways_on_the_map_picture_are_roads_grass_rock_and_water_not()
    {
        Assert.True(RoadMap.RoadColour(91, 76, 58));
        Assert.True(RoadMap.RoadColour(78, 66, 45));
        Assert.False(RoadMap.RoadColour(60, 57, 41));   // grass
        Assert.False(RoadMap.RoadColour(103, 99, 97));  // rock
        Assert.False(RoadMap.RoadColour(61, 81, 111));  // water
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
