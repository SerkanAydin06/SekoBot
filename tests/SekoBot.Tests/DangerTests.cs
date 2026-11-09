using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class DangerTests
{
    private static readonly Point Center = new(960, 540);

    private static TargetInfo Mob(int id, int dx, double distance, bool attacks, bool priority = false) =>
        new(id, new Point(Center.X + dx, Center.Y), distance, 130, 365, 400, 585, 56, priority, AttacksMe: attacks);

    private static GameState State(double ms, double hp, TargetInfo[] targets) =>
        new(TimeSpan.FromMilliseconds(ms), 1920, 1080, hp, 1, 0, 1)
        {
            Targets = targets, Walking = true, AttackPower = 189, HpPotionsLeft = 500,
            MobCandidates = [.. targets.Select(t => t.Spot)], Attackers = [.. targets.Where(t => t.AttacksMe).Select(t => t.Spot)],
        };

    private static (Brain, BotProfile) Ready()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PullGroups = false, Breaks = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        return (brain, p);
    }

    [Fact]
    public void Low_hp_with_many_on_us_runs_away_then_comes_back()
    {
        var (brain, p) = Ready();
        TargetInfo[] four = [Mob(1, 60, 90, true), Mob(2, 70, 95, true), Mob(3, 80, 100, true), Mob(4, 50, 85, true)];
        for (double t = 0; t < 1000; t += 100)
            brain.Tick(State(t, 0.8, four));
        var run = new List<BotAction>();
        for (double t = 1000; t < 3000; t += 100)
            run.AddRange(brain.Tick(State(t, 0.25, four)));
        Assert.Contains(run, a => a is NoteAction n && n.Text.Contains("Tehlike"));
        Assert.Contains(run, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);      // away from them (they're on the right)
        Assert.Contains(run, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.Contains(run, a => a is KeyTapAction k && k.Key == p.Keys.HpPotion);       // still drinking
        var back = new List<BotAction>();
        for (double t = 3000; t < 4000; t += 100)
            back.AddRange(brain.Tick(State(t, 0.65, four)));
        Assert.Contains(back, a => a is NoteAction n && n.Text.Contains("toparlandı"));
        Assert.Contains(back, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
    }

    [Fact]
    public void Low_hp_against_one_or_two_keeps_fighting()
    {
        var (brain, _) = Ready();
        var actions = new List<BotAction>();
        for (double t = 0; t < 2000; t += 100)
            actions.AddRange(brain.Tick(State(t, 0.2, [Mob(1, 60, 90, true), Mob(2, 70, 95, true)])));
        Assert.DoesNotContain(actions, a => a is NoteAction n && n.Text.Contains("Tehlike"));
    }

    [Fact]
    public void A_stone_it_cannot_get_closer_to_is_left_for_a_while()
    {
        var (brain, _) = Ready();
        var stone = Mob(9, 500, 1170, false, priority: true);   // 11.7 m away, the distance never shrinks
        var notes = new List<string>();
        for (double t = 0; t < 12_000; t += 100)
            notes.AddRange(brain.Tick(State(t, 1, [stone])).OfType<NoteAction>().Select(n => n.Text));
        Assert.Contains(notes, n => n.Contains("yaklaşılamıyor; bir süre bırakıldı"));
        // Getting closer step by step is fine.
        var (brain2, _) = Ready();
        var notes2 = new List<string>();
        for (double t = 0; t < 12_000; t += 100)
            notes2.AddRange(brain2.Tick(State(t, 1, [Mob(9, 500, 1170 - t / 10, false, priority: true)])).OfType<NoteAction>().Select(n => n.Text));
        Assert.DoesNotContain(notes2, n => n.Contains("bir süre bırakıldı"));
    }

    [Fact]
    public void No_break_with_mobs_all_around()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PullGroups = false, Breaks = true, BreakEveryMin = 1 };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        TargetInfo[] around = [Mob(1, 200, 600, false), Mob(2, 220, 700, false), Mob(3, 240, 800, false)];
        var notes = new List<string>();
        for (double t = 0; t < 120_000; t += 200)
            notes.AddRange(brain.Tick(State(t, 1, around) with { Targets = around, MobCandidates = [] }).OfType<NoteAction>().Select(n => n.Text));
        Assert.DoesNotContain(notes, n => n.Contains("Kısa mola"));
    }
}
