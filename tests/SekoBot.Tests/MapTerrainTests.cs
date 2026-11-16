using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>The map's obstacles: outlines, heights, the way around them, what was walked through, files.</summary>
public class MapTerrainTests
{
    private static MapBlock Box(double x0, double y0, double x1, double y1, float bottom = 0, float top = 400, string name = "a1-038-wall-lin2") =>
        new() { Name = name, Points = MapBlock.Hull([(x0, y0), (x1, y0), (x1, y1), (x0, y1)]), Bottom = bottom, Top = top };

    // A wall from y = -2000 to 2000 at x = 1000..1100; the character stands at height 90 (feet on 0).
    private static MapTerrain Wall(params MapBlock[] more) => new("test", [Box(1000, -2000, 1100, 2000), .. more], DateTime.Now);

    private static readonly Vec3 Me = new(0, 0, 90);

    [Fact]
    public void An_outline_is_the_hull_seen_from_above()
    {
        var b = new MapBlock { Points = MapBlock.Hull([(0, 0), (100, 0), (100, 100), (0, 100), (50, 50), (100, 0)]) };
        Assert.Equal(4, b.Corners);
        Assert.Equal(10_000, b.Area, 1);
        Assert.True(b.Contains(50, 50));
        Assert.Equal(0, b.Distance(10, 10));
        Assert.Equal(50, b.Distance(150, 50), 3);
        Assert.Equal(100, b.Width, 3);
        var wall = new MapBlock { Points = MapBlock.Hull([(0, 0), (1000, 1000), (1070.7, 929.3), (70.7, -70.7)]) };   // turned 45°, 100 thick
        Assert.Equal(100, wall.Width, 0);
    }

    [Fact]
    public void A_wall_blocks_the_straight_way_and_the_way_round_goes_past_its_end()
    {
        var map = Wall();
        var goal = new Vec3(2000, 0, 90);
        Assert.False(map.LineClear(Me, goal));
        var way = map.WayTo(Me, goal);
        Assert.NotNull(way);
        Assert.Equal(goal, way![^1]);
        Assert.True(way.Count >= 2);
        Assert.Contains(way, p => Math.Abs(p.Y) >= 2000);                 // round one end of the wall
        var here = Me;
        foreach (var p in way)
        {
            Assert.True(map.LineClear(here, p, MapTerrain.Margin * 0.8));  // every leg is free
            here = p;
        }
        Assert.InRange(MapTerrain.Length(Me, way), 4000, 6500);
    }

    [Fact]
    public void A_free_way_is_one_straight_leg()
    {
        var map = Wall();
        var goal = new Vec3(-2000, 500, 90);
        Assert.True(map.LineClear(Me, goal));
        Assert.Equal([goal], map.WayTo(Me, goal));
    }

    [Fact]
    public void Things_deep_under_the_feet_or_over_the_head_are_no_obstacle()
    {
        var goal = new Vec3(2000, 0, 90);
        Assert.True(Wall().LineClear(Me, goal) is false);
        Assert.True(new MapTerrain("t", [Box(1000, -2000, 1100, 2000, -2000, -900)], DateTime.Now).LineClear(Me, goal));  // deep under the ground
        Assert.True(new MapTerrain("t", [Box(100, -2000, 200, 2000, 400, 500)], DateTime.Now).LineClear(Me, goal));       // a bridge just above us
    }

    [Fact]
    public void A_place_closed_all_round_has_no_way()
    {
        var map = new MapTerrain("t", [Box(-500, -500, 500, -400), Box(-500, 400, 500, 500), Box(-500, -500, -400, 500), Box(400, -500, 500, 500)], DateTime.Now);
        Assert.Null(map.WayTo(Me, new Vec3(3000, 0, 90)));
    }

    [Fact]
    public void What_the_character_walked_through_is_no_obstacle_from_then_on()
    {
        var gate = Box(1000, -2000, 1100, 2000, name: "wall-door");
        var map = new MapTerrain("t", [gate], DateTime.Now);
        Assert.Equal(1, map.Walked(new Vec3(1050, 0, 90)));
        Assert.True(gate.Passable);
        Assert.True(map.Changed);
        Assert.True(map.LineClear(Me, new Vec3(2000, 0, 90)));
        // Read again later: still known as passable.
        var again = MapTerrain.Renew("t", [Box(1000, -2000, 1100, 2000, name: "wall-door")], map, DateTime.Now);
        Assert.True(again.Blocks[0].Passable);
    }

    [Fact]
    public void Aim_heads_for_the_furthest_corner_in_sight()
    {
        var map = Wall();
        var way = map.WayTo(Me, new Vec3(2000, 0, 90))!;
        var aim = map.Aim(way, Me);
        Assert.True(map.LineClear(Me, aim));
        Assert.NotEqual(way[^1], aim);
    }

    [Fact]
    public void The_map_is_kept_with_its_picture_and_read_back()
    {
        string folder = Path.Combine(Path.GetTempPath(), "harita-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var map = Wall(Box(-3000, -3000, -2900, -2900, name: "SM_Big_Rock_02"));
            map.Blocks[0].Passable = true;
            string picture = map.Save(folder, [new Vec3(5, 5, 0)], Me);
            Assert.True(File.Exists(picture));
            string html = File.ReadAllText(picture);
            Assert.StartsWith("<!doctype html>", html);
            Assert.Contains("SM_Big_Rock_02", html);
            Assert.DoesNotContain("__DATA__", html);
            var back = MapTerrain.Load(folder, "test")!;
            Assert.Equal(2, back.Blocks.Count);
            Assert.True(back.Blocks[0].Passable);
            Assert.Null(MapTerrain.Load(folder, "başka"));
            var book = new TerrainBook(folder);
            Assert.Equal(2, book.Of("test")!.Blocks.Count);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData("a1-038-wall-lin2_duel", "Duvar")]
    [InlineData("SM_Big_Rock_02", "Kaya")]
    [InlineData("AfricanBaobab_RT", "Ağaç")]
    [InlineData("obj_ob-7-12-building02_Object012", "Yapı")]
    [InlineData("obj_ob-b1-005-woodbarrel_woodbarrel2", "Eşya")]
    public void Obstacles_are_named_by_kind(string mesh, string kind) => Assert.Equal(kind, MapReport.Kind(mesh));

    [Fact]
    public void The_engine_turns_a_mesh_the_way_the_game_does()
    {
        // Yaw 90: the X axis turns into Y.
        var p = GameWorldReader.Rotate(new Vec3(0, 90, 0), new Vec3(100, 0, 0));
        Assert.Equal(0, p.X, 3);
        Assert.Equal(100, p.Y, 3);
        // A quaternion of 90° about Z, moved and scaled.
        double s = Math.Sqrt(0.5);
        var t = new GameWorldReader.Transform(0, 0, s, s, new Vec3(1000, 0, 0), new Vec3(2, 2, 2));
        var q = t.Apply(new Vec3(100, 0, 0));
        Assert.Equal(1000, q.X, 3);
        Assert.Equal(200, q.Y, 3);
    }

    [Fact]
    public void The_market_page_draws_the_walls()
    {
        var map = Wall();
        string walls = MapReport.Outlines(map);
        Assert.StartsWith("[[", walls);
        string html = MarketReport.Html("{\"map\":\"shop\",\"saved\":\"2026-11-16T14:00:00\",\"shops\":[],\"all\":[],\"wanted\":[]}", walls);
        Assert.Contains("const W = [[", html);
        Assert.Equal("[]", MapReport.Outlines(null));
    }
}
