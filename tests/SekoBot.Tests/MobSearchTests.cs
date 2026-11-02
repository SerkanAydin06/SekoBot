using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class MotionDetectorTests
{
    private const int W = 800, H = 600;

    /// <summary>Textured ground shifted by <paramref name="scroll"/> px, plus textured 30×40 "mobs".</summary>
    private static Frame Scene(int scroll = 0, params Point[] mobs)
    {
        var data = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int i = (y * W + x) * 4;
            int sx = x + scroll;
            // Deterministic texture with strong local contrast (like grass/stones).
            byte v = (byte)(60 + ((sx * 37 + y * 91) ^ (sx * y)) % 120);
            data[i] = (byte)(v / 2);
            data[i + 1] = v;
            data[i + 2] = (byte)(v * 3 / 4);
        }
        // Mobs carry their own (non-periodic) texture, so their body changes as they move.
        foreach (var m in mobs)
            for (int ly = 0; ly < 40; ly++)
            for (int lx = 0; lx < 30; lx++)
            {
                int i = ((m.Y - 20 + ly) * W + (m.X - 15 + lx)) * 4;
                int t = 40 + ((lx * 73856093) ^ (ly * 19349663)) % 140;
                data[i] = (byte)t; data[i + 1] = (byte)(t / 3); data[i + 2] = (byte)(t / 2);
            }
        return Frame.FromArray(W, H, data);
    }

    private static IReadOnlyList<Point> Feed(MotionDetector detector, Func<int, Frame> frameAt, int frames)
    {
        IReadOnlyList<Point> result = [];
        for (int i = 0; i < frames; i++)
            using (var f = frameAt(i))
                result = detector.Update(f, TimeSpan.FromMilliseconds(i * 100));
        return result;
    }

    [Fact]
    public void Moving_mob_becomes_a_candidate()
    {
        var detector = new MotionDetector();
        // A mob wandering left and right at (250, 220).
        var found = Feed(detector, i => Scene(0, new Point(250 + (i % 2 == 0 ? 0 : 8), 220)), 6);
        Assert.NotEmpty(found);
        Assert.InRange(found[0].X, 220, 280);
        Assert.InRange(found[0].Y, 195, 245);
    }

    [Fact]
    public void Nearest_mob_comes_first()
    {
        var detector = new MotionDetector();
        var found = Feed(detector, i =>
        {
            int d = i % 2 == 0 ? 0 : 8;
            return Scene(0, new Point(120 + d, 150), new Point(520 + d, 360));
        }, 6);
        Assert.Equal(2, found.Count);
        Assert.True(found[0].X > 400, "the mob near the centre should be first");
    }

    [Fact]
    public void Static_scene_has_no_candidates()
    {
        var detector = new MotionDetector();
        Assert.Empty(Feed(detector, _ => Scene(0, new Point(250, 220)), 6));
    }

    [Fact]
    public void Camera_movement_is_not_mistaken_for_mobs()
    {
        var detector = new MotionDetector();
        Assert.Empty(Feed(detector, i => Scene(i * 6), 6));
    }

    [Fact]
    public void Own_character_and_hud_are_ignored()
    {
        var detector = new MotionDetector();
        // Moving thing in the middle (character) and at the bottom (HUD).
        var found = Feed(detector, i =>
        {
            int d = i % 2 == 0 ? 0 : 8;
            return Scene(0, new Point(400 + d, 300), new Point(300 + d, 560));
        }, 6);
        Assert.Empty(found);
    }

    [Fact]
    public void Missed_spot_is_skipped_for_a_while()
    {
        var detector = new MotionDetector();
        Frame At(int i) => Scene(0, new Point(250 + (i % 2 == 0 ? 0 : 8), 220));
        var found = Feed(detector, At, 6);
        detector.MarkMiss(found[0], TimeSpan.FromMilliseconds(500));
        using (var f = At(6))
            Assert.Empty(detector.Update(f, TimeSpan.FromMilliseconds(600)));
        // After the memory expires it is tried again.
        using (var f = At(7))
            Assert.NotEmpty(detector.Update(f, TimeSpan.FromSeconds(20)));
    }
}

public class MobSearchBrainTests
{
    private static readonly Point[] Mob = [new(300, 250)];

    private static GameState State(double ms, double? target, IReadOnlyList<Point>? mobs = null) =>
        new(TimeSpan.FromMilliseconds(ms), 800, 600, 1, 1, target, 1) { MobCandidates = mobs ?? Array.Empty<Point>() };

    private static (Brain Brain, BotProfile Profile) Started(bool targetBarKnown = true)
    {
        var profile = new BotProfile { AutoTarget = true };
        if (targetBarKnown)
            profile.TargetBar = new BarSpec();
        var brain = new Brain(() => profile);
        brain.Start(TimeSpan.Zero);
        return (brain, profile);
    }

    private static List<BotAction> Run(Brain brain, double from, double to, Func<double, GameState> state)
    {
        var actions = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            actions.AddRange(brain.Tick(state(t)));
        return actions;
    }

    [Fact]
    public void Clicks_a_moving_mob_fights_until_it_dies_then_searches_again()
    {
        var (brain, p) = Started();
        Assert.Equal(BotPhase.Searching, brain.Phase);
        var search = Run(brain, 0, 1000, t => State(t, 0, Mob));
        var find = Assert.Single(search.OfType<FindTargetAction>());
        Assert.Equal(Mob, find.Candidates);
        Assert.DoesNotContain(search, a => a is KeyDownAction);

        var fight = Run(brain, 1000, 1000 + p.FightMs + 3000, t => State(t, 0.7));
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.Contains(fight, a => a is KeyDownAction d && d.Key == p.Keys.Attack);

        double t0 = 1000 + p.FightMs + 3000;
        var after = Run(brain, t0, t0 + 3000, t => State(t, 0, Mob));
        Assert.Contains(after, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.Equal(p.LootPresses, after.OfType<KeyTapAction>().Count(a => a.Key == p.Keys.Pickup));
        Assert.Single(after.OfType<FindTargetAction>());
        Assert.Equal(BotPhase.Searching, brain.Phase);
        Assert.Equal(1, brain.Cycles);
    }

    [Fact]
    public void Waits_for_motion_instead_of_clicking_blindly()
    {
        var (brain, _) = Started();
        var actions = Run(brain, 0, 3000, t => State(t, 0));
        Assert.DoesNotContain(actions, a => a is FindTargetAction);
        Assert.Equal(BotPhase.Searching, brain.Phase);
    }

    [Fact]
    public void Retries_with_fresh_candidates_while_searching()
    {
        var (brain, _) = Started();
        var actions = Run(brain, 0, 5500, t => State(t, 0, Mob));
        Assert.Equal(2, actions.OfType<FindTargetAction>().Count());
    }

    [Fact]
    public void Turns_camera_when_nothing_is_found_and_attacks_blindly_after_three_misses()
    {
        var (brain, p) = Started();
        var actions = Run(brain, 0, 3 * (p.SearchTimeoutMs + p.RotateHoldMs + 400), t => State(t, 0));
        Assert.Equal(2, actions.OfType<KeyHoldAction>().Count(a => a.Key == p.Keys.RotateCamera));
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.Contains(actions, a => a is KeyDownAction);
    }

    [Fact]
    public void Without_target_bar_attacks_blindly_after_one_search()
    {
        var (brain, p) = Started(targetBarKnown: false);
        Run(brain, 0, p.SearchTimeoutMs + 200, t => State(t, null));
        Assert.Equal(BotPhase.Fighting, brain.Phase);
    }
}
