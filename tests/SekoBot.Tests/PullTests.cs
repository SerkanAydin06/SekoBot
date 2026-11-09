using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class SpawnGroupTests
{
    private static WorldMob Mob(int id, double x, double y, bool metin = false) =>
        new(id, 101, "Ayı", 5, 100, 100, new Vec3(x, y, 0), IsMetin: metin);

    [Fact]
    public void Mobs_with_neighbouring_numbers_standing_together_are_one_group()
    {
        // As seen in the game: Aç Ayı 1880–1882 together, Kaplan 1868–1870 together.
        var groups = BotEngine.SpawnGroups(
        [
            Mob(1880, 13451, 8946), Mob(1881, 13208, 8879), Mob(1882, 13378, 8602),
            Mob(1868, 13057, 8108), Mob(1869, 12362, 7756), Mob(1870, 12490, 7941),
            Mob(1883, 20000, 20000),                 // next number but far away: its own group
            Mob(1871, 13000, 8100, metin: true),     // a Metin stone: no group
        ]);
        Assert.Equal(1880, groups[1881]);
        Assert.Equal(1880, groups[1882]);
        Assert.Equal(1868, groups[1869]);
        Assert.Equal(1868, groups[1870]);
        Assert.Equal(1883, groups[1883]);
        Assert.False(groups.ContainsKey(1871));
    }
}

public class PullTests
{
    private const int W = 1920, H = 1080;
    private static readonly Point Center = new(W / 2, H / 2);

    private static TargetInfo Mob(int id, int group, int dx, double distance, bool attacks = false, int hp = 305) =>
        new(id, new Point(Center.X + dx, Center.Y), distance, 130, 103, hp, 305, 56, attacks, attacks, group);

    private static (Brain, BotProfile) Ready(int max = 6)
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PickUpDrops = false, PullMaxMobs = max };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        return (brain, p);
    }

    private static List<BotAction> Run(Brain brain, double from, double to, TargetInfo[] targets, double hp = 1)
    {
        var actions = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            actions.AddRange(brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), W, H, hp, 1, 0, 1)
                { Targets = targets, Walking = true, AttackPower = 189 }));
        return actions;
    }

    [Fact]
    public void Wakes_one_group_then_goes_for_the_next_then_fights_them_all()
    {
        var (brain, p) = Ready(max: 6);
        // Group A 1 m to the right (in reach), group B 10 m to the left.
        TargetInfo[] a = [Mob(10, 10, 40, 100), Mob(11, 10, 60, 150), Mob(12, 10, 80, 160)];
        TargetInfo[] b = [Mob(20, 20, -500, 1000), Mob(21, 20, -520, 1050), Mob(22, 20, -540, 1100)];
        var hit = Run(brain, 0, 1000, [.. a, .. b]);
        Assert.Contains(hit, x => x is NoteAction n && n.Text.Contains("toplanıyor"));
        Assert.Contains(hit, x => x is KeyDownAction d && d.Key == p.Keys.Attack);

        // Group A comes after us: off to group B (left), not attacking A.
        TargetInfo[] aComing = [Mob(10, 10, 40, 100, attacks: true), Mob(11, 10, 60, 150, attacks: true), Mob(12, 10, 80, 160, attacks: true)];
        var walk = Run(brain, 1000, 2500, [.. aComing, .. b]);
        Assert.Contains(walk, x => x is KeyDownAction d && d.Key == p.Keys.MoveLeft);
        Assert.Contains(walk, x => x is KeyUpAction u && u.Key == p.Keys.Attack);

        // Group B comes too: 6 after us = the limit → fight them all.
        TargetInfo[] bComing = [Mob(20, 20, -40, 100, attacks: true), Mob(21, 20, -60, 120, attacks: true), Mob(22, 20, -80, 140, attacks: true)];
        var fight = Run(brain, 2500, 3500, [.. aComing, .. bComing]);
        Assert.Contains(fight, x => x is NoteAction n && n.Text.Contains("6 mob toplandı"));
        Assert.Equal(BotPhase.Fighting, brain.Phase);
    }

    [Fact]
    public void Stops_gathering_when_hp_is_low()
    {
        var (brain, _) = Ready(max: 9);
        TargetInfo[] coming = [Mob(10, 10, 40, 100, attacks: true), Mob(11, 10, 60, 150, attacks: true)];
        TargetInfo[] fresh = [Mob(20, 20, -500, 1000), Mob(21, 20, -520, 1050)];
        Run(brain, 0, 500, [.. coming, .. fresh]);
        var low = Run(brain, 500, 1500, [.. coming, .. fresh], hp: 0.5);
        Assert.Contains(low, x => x is NoteAction n && n.Text.StartsWith("Can azaldı"));
        Assert.Equal(BotPhase.Fighting, brain.Phase);
    }

    [Fact]
    public void Off_means_no_gathering()
    {
        var (brain, p) = Ready();
        p.PullGroups = false;
        TargetInfo[] a = [Mob(10, 10, 40, 100), Mob(11, 10, 60, 150)];
        var actions = Run(brain, 0, 1000, a);
        Assert.DoesNotContain(actions, x => x is NoteAction n && n.Text.Contains("toplanıyor"));
    }
}
