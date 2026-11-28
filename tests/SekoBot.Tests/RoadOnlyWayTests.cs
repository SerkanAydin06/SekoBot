using System.Collections;
using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Far trips go along the roads only: to the nearest road, along it, off it nearest the goal.</summary>
public class RoadOnlyWayTests
{
    /// <summary>A road along the top (rows 10–14) bending down on the right (columns 58–62); cells of 1 m.</summary>
    private static MapTerrain Bend()
    {
        int cols = 80, rows = 80;
        var road = new BitArray(cols * rows);
        for (int c = 0; c <= 62; c++)
            for (int r = 10; r <= 14; r++)
                road[r * cols + c] = true;
        for (int r = 10; r < 70; r++)
            for (int c = 58; c <= 62; c++)
                road[r * cols + c] = true;
        return new MapTerrain("t", [], DateTime.Now) { Roads = new RoadMap("t", 0, 0, cols, rows, road) };
    }

    private static readonly Vec3 Farm = new(500, 3000, 0), Shop = new(5500, 6500, 0);

    [Fact]
    public void A_far_trip_keeps_to_the_road_round_the_bend()
    {
        var map = Bend();
        var way = map.WayTo(Farm, Shop, roads: true)!;
        Assert.NotNull(way);
        Assert.Equal(Shop, way[^1]);
        // Onto the road first (straight up to the top road), never across the field to the shop.
        Assert.True(way[0].Y < 1500, $"first corner {way[0]}");
        Assert.Contains(way, p => p.X > 5700 && p.Y < 1600); // round the bend
        // Every leg between the first and the last road corner stays on the road.
        for (int i = 1; i < way.Count - 1; i++)
            for (double t = 0; t <= 1; t += 0.05)
            {
                double x = way[i - 1].X + (way[i].X - way[i - 1].X) * t, y = way[i - 1].Y + (way[i].Y - way[i - 1].Y) * t;
                Assert.True(map.Roads!.OnRoad(x, y), $"leg {i} leaves the road at ({x:0},{y:0})");
            }
    }

    [Fact]
    public void A_mob_on_the_road_is_walked_round_on_the_road()
    {
        var map = Bend();
        var mob = new MapTerrain.Circle(3000, 1200, 150);
        var way = map.WayTo(Farm, Shop, avoid: [mob], roads: true)!;
        Assert.Contains(way, p => p.X > 5700 && p.Y < 1600);
        for (int i = 1; i < way.Count - 1; i++)
            Assert.True(map.Roads!.OnRoad(way[i].X, way[i].Y));
    }

    [Fact]
    public void Mobs_filling_the_road_are_passed_beside_it()
    {
        var map = Bend();
        var mob = new MapTerrain.Circle(3000, 1250, BotEngine.PassMobsBy);
        var way = map.WayTo(Farm, Shop, roads: true, mobs: [mob])!;
        Assert.Equal(Shop, way[^1]);
        Assert.Contains(way, p => p.X > 5700 && p.Y < 1600); // still along the road round the bend
        var prev = Farm;
        foreach (var corner in way)
        {
            for (double t = 0; t <= 1; t += 0.02)
            {
                double x = prev.X + (corner.X - prev.X) * t, y = prev.Y + (corner.Y - prev.Y) * t;
                Assert.False(mob.Holds(new Vec3(x, y, 0), 0), $"through the mobs at ({x:0},{y:0})");
                if (x > 2000 && x < 4000)
                    Assert.NotNull(map.Roads!.NearestCell(x, y, MapTerrain.OffRoadMost)); // never far from the road
            }
            prev = corner;
        }
    }

    [Fact]
    public void A_trip_the_road_does_not_help_goes_straight()
    {
        var map = Bend();
        var way = map.WayTo(new Vec3(500, 5000, 0), new Vec3(4500, 5500, 0), roads: true)!;
        Assert.DoesNotContain(way, p => p.Y < 1500);
    }
}

/// <summary>Mobs on one of two ways about as long don't send the walk to the other (they move; the choice flipped).</summary>
public class RoadChoiceTests
{
    /// <summary>A ring road (rows/columns 10–14 and 65–69 of 80): from its bottom to its top the left and right ways are as long.</summary>
    private static MapTerrain Ring()
    {
        int n = 80;
        var road = new System.Collections.BitArray(n * n);
        for (int i = 10; i <= 69; i++)
            for (int k = 0; k < 5; k++)
            {
                road[(10 + k) * n + i] = road[(65 + k) * n + i] = true;  // top, bottom rows
                road[i * n + 10 + k] = road[i * n + 65 + k] = true;      // left, right columns
            }
        return new MapTerrain("t", [], DateTime.Now) { Roads = new RoadMap("t", 0, 0, n, n, road) };
    }

    [Fact]
    public void Moving_mobs_on_the_way_dont_flip_it_to_the_other_side()
    {
        var map = Ring();
        var from = new Vec3(4000, 6700, 0);
        var to = new Vec3(4100, 1200, 0);
        var plain = map.WayTo(from, to, roads: true)!;
        bool left = plain.Any(p => p.X < 2000);
        // Mobs all along the side it takes: still that side (round them where they are, not the other way round).
        var mobs = Enumerable.Range(0, 12).Select(i => new MapTerrain.Circle(left ? 1250 : 6750, 1500 + i * 400, 300)).ToList();
        var crowded = map.WayTo(from, to, roads: true, mobs: mobs)!;
        Assert.Equal(left, crowded.Any(p => p.X < 2000));
        Assert.Equal(!left, crowded.Any(p => p.X > 6000));
    }
}
