using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class FarmSpotTests
{
    private static readonly Vec3 Start = new(20000, 18000, 900);
    private static int _id;

    // As read in the game: Siyah Ayı 624 HP / 384 EXP, Aç Ayı 305 HP / 103 EXP.
    private static WorldMob Black(double x, double y) => new(++_id, 112, "Siyah Ayı", 19, 624, 624, new Vec3(x, y, 900), Exp: 384);
    private static WorldMob Cheap(double x, double y) => new(++_id, 180, "Aç Ayı", 12, 305, 305, new Vec3(x, y, 900), Exp: 103);

    [Fact]
    public void Picks_the_place_where_the_mobs_are_worth_most()
    {
        // Four cheap bears right here, five black bears 50 m east.
        var mobs = new List<WorldMob>
        {
            Cheap(20100, 18000), Cheap(19900, 18100), Cheap(20000, 17900), Cheap(20200, 18200),
            Black(25000, 18000), Black(25200, 18100), Black(24900, 17800), Black(25100, 18300), Black(25300, 17900),
        };
        var spot = FarmSpotFinder.Best(mobs, new BotProfile(), Start, "c2", attackPower: 189);
        Assert.NotNull(spot);
        Assert.InRange(spot.X, 24000, 26000);
        Assert.Equal("c2", spot.MapName);
        Assert.Contains("Siyah Ayı ×5", spot.Names);

        // Too far away (limit 20 m): the cheap bears here win.
        var near = FarmSpotFinder.Best(mobs, new BotProfile(), Start, "c2", 189, maxDistance: 2000)!;
        Assert.InRange(near.X, 18500, 21500);
    }

    [Fact]
    public void Nothing_wanted_means_no_spot()
    {
        var p = new BotProfile { SkipMobs = "Ayı" };
        Assert.Null(FarmSpotFinder.Best([Cheap(20100, 18000), Black(23000, 18000)], p, Start, "c2", 189));
    }

    [Fact]
    public void Walks_to_the_saved_spot_before_farming()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], PickUpDrops = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        // Camera looking along +X: +X is "up" (W). The spot is 20 m ahead; a mob is right next to us.
        var camera = new CameraView(Start with { Z = 1900 }, -80, 0, 0, 90);
        var me = Start;
        var mob = new TargetInfo(1, new Point(960 + 40, 540), 90, 130, 103, 305, 305, 56, false);
        GameState At(double ms) => new(TimeSpan.FromMilliseconds(ms), 1920, 1080, 1, 1, 0, 1)
        {
            Position = me, Camera = camera, Walking = true, FarmSpot = Start with { X = Start.X + 2000 },
            Targets = [mob], AttackPower = 189,
        };
        var walk = new List<BotAction>();
        for (double t = 0; t < 1000; t += 100)
            walk.AddRange(brain.Tick(At(t)));
        Assert.Contains(walk, a => a is NoteAction n && n.Text.Contains("farm alanına gidiliyor"));
        Assert.Contains(walk, a => a is KeyDownAction d && d.Key == p.Keys.MoveForward);
        Assert.DoesNotContain(walk, a => a is KeyDownAction d && d.Key == p.Keys.Attack);

        // Arrived: farming starts (the mob next to us is attacked).
        me = Start with { X = Start.X + 1800 };
        var arrive = new List<BotAction>();
        for (double t = 1000; t < 2000; t += 100)
            arrive.AddRange(brain.Tick(At(t)));
        Assert.Contains(arrive, a => a is NoteAction n && n.Text.Contains("varıldı"));
        Assert.Contains(arrive, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
    }
}
