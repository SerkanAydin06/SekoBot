using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class SkillTests
{
    private const int W = 1920, H = 1080;

    private static GameState At(double ms, params Point[] mobs) =>
        new(TimeSpan.FromMilliseconds(ms), W, H, 1, 1, null, 1) { MobCandidates = mobs };

    private static List<BotAction> Run(Brain brain, double from, double to, params Point[] mobs)
    {
        var actions = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            actions.AddRange(brain.Tick(At(t, mobs)));
        return actions;
    }

    private static int Count(List<BotAction> actions, Keys key) =>
        actions.OfType<KeyTapAction>().Count(a => a.Key == key);

    [Fact]
    public void Buffs_are_pressed_in_a_loop_one_at_a_time()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = [10, 20, 0, 0, 0] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var actions = Run(brain, 0, 41_000);
        // F1 every 10 s, F2 every 20 s, F3–F5 off.
        Assert.InRange(Count(actions, Keys.F1), 4, 5);
        Assert.InRange(Count(actions, Keys.F2), 2, 3);
        Assert.Equal(0, Count(actions, Keys.F3) + Count(actions, Keys.F4) + Count(actions, Keys.F5));
        // Never two skills in the same moment.
        var times = new List<double>();
        brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        for (double t = 0; t < 41_000; t += 100)
            if (brain.Tick(At(t)).OfType<KeyTapAction>().Any(a => a.Key is Keys.F1 or Keys.F2))
                times.Add(t);
        Assert.All(times.Zip(times.Skip(1)), pair => Assert.True(pair.Second - pair.First >= p.SkillGapMs));
    }

    [Fact]
    public void Stands_still_while_casting_then_walks_on()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = [0, 0, 30, 0, 0] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var mob = new Point(W / 2 + 600, H / 2);
        Run(brain, 0, 500, mob); // F3 at the start, then walking right
        var walk = Run(brain, 2000, 2500, mob);
        Assert.Contains(walk, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);

        // 30 s later F3 again: let go of D, press F3, stand still for a moment, walk on.
        var cast = Run(brain, 30_000, 30_300, mob);
        int up = cast.FindIndex(a => a is KeyUpAction u && u.Key == p.Keys.MoveRight);
        int f3 = cast.FindIndex(a => a is KeyTapAction t && t.Key == Keys.F3);
        Assert.InRange(up, 0, f3);
        var still = Run(brain, 30_300, 30_000 + p.SkillCastMs - 100, mob);
        Assert.DoesNotContain(still, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
        var again = Run(brain, 30_000 + p.SkillCastMs, 30_000 + p.SkillCastMs + 300, mob);
        Assert.Contains(again, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void All_zero_means_no_skills()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var actions = Run(brain, 0, 20_000);
        Assert.DoesNotContain(actions, a => a is KeyTapAction t && t.Key is >= Keys.F1 and <= Keys.F5);
    }

    [Fact]
    public void Old_profile_without_skills_gets_the_defaults()
    {
        var old = new BotProfile { LeashMeters = 0, SkillEverySec = [15] };
        ProfileStore.Normalize(old);
        Assert.Equal(new[] { 15, 60, 0, 0, 0 }, old.SkillEverySec);
        Assert.Equal(new[] { Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5 }, old.Keys.Skills);
    }
}
