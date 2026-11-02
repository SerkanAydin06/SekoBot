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

    private static GameState State(double ms, double? target, IReadOnlyList<Point>? mobs = null, Point? spot = null) =>
        new(TimeSpan.FromMilliseconds(ms), 800, 600, 1, 1, target, 1)
        {
            MobCandidates = mobs ?? Array.Empty<Point>(),
            TargetSpot = spot
        };

    private static (Brain Brain, BotProfile Profile) Started(bool targetBarKnown = true)
    {
        var profile = new BotProfile { AutoTarget = true, NavigateMode = false };
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
    public void Clicks_a_moving_mob_lets_the_character_fight_without_space_then_searches_again()
    {
        var (brain, p) = Started();
        Assert.Equal(BotPhase.Searching, brain.Phase);
        var search = Run(brain, 0, 1000, t => State(t, 0, Mob));
        var find = Assert.Single(search.OfType<FindTargetAction>());
        Assert.Equal(Mob, find.Candidates);

        // Mob selected and its HP keeps going down: no attack key, no extra clicks.
        var fight = Run(brain, 1000, 1000 + p.FightMs + 3000, t => State(t, 0.9 - (t - 1000) / 20000, spot: Mob[0]));
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.DoesNotContain(fight, a => a is KeyDownAction);
        Assert.DoesNotContain(fight, a => a is ClickAction);

        double t0 = 1000 + p.FightMs + 3000;
        var after = Run(brain, t0, t0 + 3000, t => State(t, 0, Mob));
        Assert.Equal(p.LootPresses, after.OfType<KeyTapAction>().Count(a => a.Key == p.Keys.Pickup));
        Assert.Single(after.OfType<FindTargetAction>());
        Assert.Equal(BotPhase.Searching, brain.Phase);
        Assert.Equal(1, brain.Cycles);
    }

    [Fact]
    public void Clicks_the_mob_again_when_its_hp_stops_dropping_and_gives_up_after_three_tries()
    {
        var (brain, p) = Started();
        Run(brain, 0, 1000, t => State(t, 0, Mob));
        var spot = new Point(330, 260);
        var stuck = Run(brain, 1000, 1000 + 4 * p.StallMs + 500, t => State(t, 0.7, spot: spot));
        var clicks = stuck.OfType<ClickAction>().ToList();
        Assert.Equal(3, clicks.Count);
        Assert.All(clicks, c => Assert.Equal(spot, c.Client));
        Assert.NotEqual(BotPhase.Fighting, brain.Phase);
        Assert.DoesNotContain(stuck, a => a is KeyDownAction);
    }

    [Fact]
    public void A_target_bar_that_never_changes_is_ignored_and_the_bot_keeps_clicking_mobs()
    {
        var (brain, p) = Started();
        Run(brain, 0, 1000, t => State(t, 0, Mob));
        // Stuck: the "target" stays at 70 % whatever happens, and its position is unknown.
        var stuck = Run(brain, 1000, 1000 + 4 * p.StallMs + 500, t => State(t, 0.7, Mob));
        Assert.Contains(stuck, a => a is NoteAction);
        Assert.Equal(3, stuck.OfType<ClickAction>().Count()); // clicked the nearest moving thing

        // The same bar is still showing: don't fight it again, look for mobs instead.
        double t1 = 1000 + 4 * p.StallMs + 500;
        var later = Run(brain, t1, t1 + 3500, t => State(t, 0.7, Mob));
        Assert.Contains(later, a => a is FindTargetAction);
        Assert.Equal(BotPhase.Searching, brain.Phase);

        // Its value changes (a mob really gets hit): fight again.
        Run(brain, t1 + 3500, t1 + 3800, t => State(t, 0.6, Mob));
        Assert.Equal(BotPhase.Fighting, brain.Phase);
    }

    [Fact]
    public void Can_still_hold_the_attack_key_on_a_target_if_wanted()
    {
        var (brain, p) = Started();
        p.UseAttackKeyOnTarget = true;
        Run(brain, 0, 1000, t => State(t, 0, Mob));
        var fight = Run(brain, 1000, 2000, t => State(t, 0.9 - (t - 1000) / 20000, spot: Mob[0]));
        Assert.Contains(fight, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
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
    public void Turns_camera_twice_then_walks_when_nothing_is_found_without_attacking_the_air()
    {
        var (brain, p) = Started();
        var actions = Run(brain, 0, 3 * (p.SearchTimeoutMs + 200) + p.RotateHoldMs * 2 + p.MoveHoldMs + 1500, t => State(t, 0));
        Assert.Equal(2, actions.OfType<KeyHoldAction>().Count(a => a.Key == p.Keys.RotateCamera));
        Assert.Equal(1, actions.OfType<KeyHoldAction>().Count(a => a.Key == p.Keys.MoveForward));
        Assert.DoesNotContain(actions, a => a is KeyDownAction);
        Assert.NotEqual(BotPhase.Fighting, brain.Phase);
    }

    [Fact]
    public void Without_target_bar_attacks_blindly_after_one_search()
    {
        var (brain, p) = Started(targetBarKnown: false);
        Run(brain, 0, p.SearchTimeoutMs + 200, t => State(t, null));
        Assert.Equal(BotPhase.Fighting, brain.Phase);
    }
}
