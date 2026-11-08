using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class HotspotTests
{
    private static readonly Vec3 Start = new(20000, 18000, 900);

    private static WorldMob Mob(double x, double y, bool metin = false) =>
        new(0, 101, "Kurt", 5, 100, 100, new Vec3(x, y, 900), IsMetin: metin);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Remembers_where_the_mobs_were_and_forgets_squares_that_emptied()
    {
        var map = new HotspotMap();
        // A pack 20 m east, seen a few times.
        for (int i = 0; i < 10; i++)
            map.Observe(Start, [Mob(22000, 18000), Mob(22200, 18100), Mob(22100, 18300)], S(i));
        // Walked 20 m west: the pack is out of sight but remembered.
        var west = Start with { X = 18000 };
        var best = map.Best(west, S(20));
        Assert.NotNull(best);
        Assert.InRange(best.Value.X, 22000, 23000);

        // Back there: the pack is gone, the square empties again.
        for (int i = 0; i < 30; i++)
            map.Observe(Start with { X = 22000 }, [], S(30 + i));
        Assert.Null(map.Best(west, S(70)));
    }

    [Fact]
    public void Prefers_a_bigger_group_and_metin_stones_but_stays_in_the_area()
    {
        var map = new HotspotMap();
        for (int i = 0; i < 10; i++)
        {
            map.Observe(Start, [Mob(21500, 18000)], S(i));                           // one wolf east
            map.Observe(Start, [Mob(18500, 18000, metin: true)], S(i));              // a Metin stone west
        }
        var best = map.Best(Start, S(10));
        Assert.InRange(best!.Value.X, 18000, 19000);

        // With a 10 m area limit around the start, the 15 m-away squares are out.
        Assert.Null(map.Best(Start, S(10), home: Start, leash: 1000));
    }

    [Fact]
    public void Walks_towards_the_hotspot_instead_of_zigzagging()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], PickUpDrops = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        // Camera looking along +X: +X is "up" on screen (W), +Y is "right" (D).
        var me = Start;
        var camera = new CameraView(me with { Z = me.Z + 1000 }, -80, 0, 0, 90);
        GameState At(double ms) => new(TimeSpan.FromMilliseconds(ms), 1920, 1080, 1, 1, 0, 1)
        {
            Position = me, Camera = camera, Walking = true, Hotspot = new Vec3(me.X, me.Y + 3000, 900), // to the "right"
        };
        var actions = new List<BotAction>();
        for (double t = 0; t < 6000; t += 100)
            actions.AddRange(brain.Tick(At(t)));
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("çok olduğu yere"));
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
    }
}
