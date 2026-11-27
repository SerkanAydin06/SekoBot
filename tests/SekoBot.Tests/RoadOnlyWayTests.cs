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
