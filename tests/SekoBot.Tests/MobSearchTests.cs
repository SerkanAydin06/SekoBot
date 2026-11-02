using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class MobSearchTests
{
    private static GameState State(double ms, double? target) =>
        new(TimeSpan.FromMilliseconds(ms), 800, 600, 1, 1, target, 1);

    private static (Brain Brain, BotProfile Profile) Started(bool targetBarKnown = true)
    {
        var profile = new BotProfile { AutoTarget = true };
        if (targetBarKnown)
            profile.TargetBar = new BarSpec();
        var brain = new Brain(() => profile);
        brain.Start(TimeSpan.Zero);
        return (brain, profile);
    }

    private static List<BotAction> Run(Brain brain, double from, double to, Func<double, double?> target)
    {
        var actions = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            actions.AddRange(brain.Tick(State(t, target(t))));
        return actions;
    }

    [Fact]
    public void Searches_then_fights_the_clicked_mob_until_it_dies_then_searches_again()
    {
        var (brain, p) = Started();
        Assert.Equal(BotPhase.Searching, brain.Phase);
        var search = Run(brain, 0, 1000, _ => 0);
        Assert.Single(search.OfType<FindTargetAction>());
        Assert.DoesNotContain(search, a => a is KeyDownAction);

        // Mob clicked: its bar shows; keep attacking well past FightMs while it lives.
        var fight = Run(brain, 1000, 1000 + p.FightMs + 3000, _ => 0.7);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.Contains(fight, a => a is KeyDownAction d && d.Key == p.Keys.Attack);

        // Mob dies: release attack, loot, search for the next one.
        double t0 = 1000 + p.FightMs + 3000;
        var after = Run(brain, t0, t0 + 3000, _ => 0);
        Assert.Contains(after, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.Equal(p.LootPresses, after.OfType<KeyTapAction>().Count(a => a.Key == p.Keys.Pickup));
        Assert.Single(after.OfType<FindTargetAction>());
        Assert.Equal(BotPhase.Searching, brain.Phase);
        Assert.Equal(1, brain.Cycles);
    }

    [Fact]
    public void Turns_camera_when_search_finds_nothing_and_attacks_blindly_after_three_misses()
    {
        var (brain, p) = Started();
        var actions = Run(brain, 0, 3 * (p.SearchTimeoutMs + p.RotateHoldMs + 400), _ => 0);
        Assert.Equal(2, actions.OfType<KeyHoldAction>().Count(a => a.Key == p.Keys.RotateCamera));
        Assert.Equal(3, actions.OfType<FindTargetAction>().Count());
        Assert.Equal(BotPhase.Fighting, brain.Phase);
        Assert.Contains(actions, a => a is KeyDownAction);
    }

    [Fact]
    public void Without_target_bar_attacks_blindly_after_one_search()
    {
        var (brain, p) = Started(targetBarKnown: false);
        Run(brain, 0, p.SearchTimeoutMs + 200, _ => null);
        Assert.Equal(BotPhase.Fighting, brain.Phase);
    }

    [Fact]
    public void Scan_points_start_near_the_character_and_avoid_the_hud()
    {
        var points = MobSearch.ScanPoints(800, 600);
        Assert.InRange(points.Count, 30, 120);
        double Dist(Point q) => Math.Sqrt(Math.Pow(q.X - 400, 2) + Math.Pow(q.Y - 300, 2));
        Assert.True(Dist(points[0]) < Dist(points[^1]));
        Assert.All(points, q => Assert.InRange(q.Y, 72, 492));
    }

    [Fact]
    public void Choose_prefers_learned_cursor_then_anything_but_ground()
    {
        var memory = new MobCursors();
        nint ground = 1, mob = 2, npc = 3;
        // Not enough samples yet to know the ground cursor.
        Assert.Equal(-1, MobSearch.Choose([ground, mob], memory));
        var seen = new List<nint> { ground, ground, npc, ground, ground, mob, ground, ground };
        Assert.Equal(2, MobSearch.Choose(seen, memory));

        // The NPC cursor never produces a target bar; after three misses it's ignored.
        for (int i = 0; i < 3; i++) memory.MarkFailed(npc);
        Assert.Equal(5, MobSearch.Choose(seen, memory));

        // Once the mob cursor is learned it wins immediately, even before the ground is known.
        Assert.True(memory.MarkGood(mob));
        Assert.Equal(1, MobSearch.Choose([ground, mob], memory));
        Assert.False(memory.MarkGood(mob));
    }

    [Fact]
    public void Unreadable_cursor_gives_no_pick()
    {
        var memory = new MobCursors();
        Assert.Equal(-1, MobSearch.Choose(Enumerable.Repeat((nint)0, 12).ToList(), memory, final: true));
    }
}
