using System.Collections;
using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class RoadMiddleTests
{
    /// <summary>A road 5 m wide running east (rows 10–14), turning north at column 60 (columns 58–62).</summary>
    private static RoadMap Bend()
    {
        int cols = 80, rows = 80;
        var road = new BitArray(cols * rows);
        for (int c = 0; c <= 62; c++)
            for (int r = 10; r <= 14; r++)
                road[r * cols + c] = true;
        for (int r = 10; r < 70; r++)
            for (int c = 58; c <= 62; c++)
                road[r * cols + c] = true;
        return new RoadMap("t", 0, 0, cols, rows, road);
    }

    [Fact]
    public void The_middle_of_the_road_costs_least()
    {
        var roads = Bend();
        Assert.Equal(RoadMap.MiddleCost, roads.CostAt(2050, 1250));          // the middle row
        Assert.True(roads.CostAt(2050, 1050) > roads.CostAt(2050, 1150));    // the edge more than next to the middle
        Assert.Equal(0, roads.CostAt(2050, 3000));                           // off the road
    }

    [Fact]
    public void A_long_way_keeps_to_the_middle_and_leaves_the_road_only_at_the_end()
    {
        var map = new MapTerrain("t", [], DateTime.Now) { Roads = Bend() };
        var from = new Vec3(500, 1250, 0);
        var to = new Vec3(7500, 6500, 0);   // off the road, east of the northern stretch
        var way = map.WayTo(from, to, roads: true)!;
        Assert.NotNull(way);
        // Walked in 50-unit steps: on the road until close to the goal, and near its middle.
        var here = from;
        int onRoad = 0, all = 0, middle = 0;
        foreach (var p in way)
        {
            int steps = (int)(here.DistanceXY(p) / 50) + 1;
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / steps;
                double x = here.X + (p.X - here.X) * t, y = here.Y + (p.Y - here.Y) * t;
                all++;
                float c = map.Roads!.CostAt(x, y);
                if (c > 0) onRoad++;
                if (c is > 0 and <= RoadMap.MiddleCost + RoadMap.EdgeStep + 0.01f) middle++;
            }
            here = p;
        }
        Assert.True(onRoad > all * 0.8, $"on the road {onRoad}/{all}");
        Assert.True(middle > onRoad * 0.85, $"near the middle {middle}/{onRoad}");
    }
}
