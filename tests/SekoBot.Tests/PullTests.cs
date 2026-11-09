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

    // Mobs with world positions: the character stands at x = 0; screen x follows world x.
    private static TargetInfo At(int id, int group, double x, double y, int exp = 103, int hp = 305, bool attacks = false) =>
        new(id, new Point(Center.X + (int)(x / 5), Center.Y + (int)(y / 5)), Math.Sqrt(x * x + y * y), 130, exp, hp, hp, 56,
            attacks, attacks, group, new Vec3(x, y, 0));

    [Fact]
    public void Starts_with_the_group_worth_most_then_only_gathers_around_it()
    {
        var (brain, p) = Ready(max: 9);
        p.PullAreaMetres = 8;
        // Cheap bears 3 m to the right; black bears (384 EXP) 10 m to the left.
        TargetInfo[] cheap = [At(10, 10, 300, 0), At(11, 10, 320, 50), At(12, 10, 340, -50)];
        TargetInfo[] black = [At(20, 20, -1000, 0, 384, 624), At(21, 20, -1020, 50, 384, 624), At(22, 20, -1040, -50, 384, 624)];
        var first = Run(brain, 0, 1000, [.. cheap, .. black]);
        Assert.Contains(first, x => x is KeyDownAction d && d.Key == p.Keys.MoveLeft);   // to the black bears
        Assert.DoesNotContain(first, x => x is KeyDownAction d && d.Key == p.Keys.MoveRight);

        // Black bears woken (we're next to them now at x = -900). The cheap bears are 12 m from them:
        // outside the 8 m area, so they're not gathered — fight what came.
        TargetInfo[] blackComing = [At(20, 20, -100, 0, 384, 624, attacks: true), At(21, 20, -120, 50, 384, 624, attacks: true),
            At(22, 20, -140, -50, 384, 624, attacks: true)];
        TargetInfo[] cheapFar = [At(10, 10, 1200, 0), At(11, 10, 1220, 50), At(12, 10, 1240, -50)];
        var next = Run(brain, 1000, 2000, [.. blackComing, .. cheapFar]);
        Assert.Contains(next, x => x is NoteAction n && n.Text.Contains("Bu bölgede başka grup yok"));
        Assert.DoesNotContain(next, x => x is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void Gathers_the_groups_close_by_even_with_a_mob_already_on_us()
    {
        var (brain, p) = Ready(max: 9);
        // One mob walked into us and hits; a fresh group 5 m to the left, another 12 m to the right.
        TargetInfo[] onUs = [At(1, 1, 60, 0, attacks: true)];
        TargetInfo[] left = [At(20, 20, -500, 0), At(21, 20, -520, 50), At(22, 20, -540, -50)];
        TargetInfo[] farRight = [At(30, 30, 1200, 0), At(31, 30, 1220, 50), At(32, 30, 1240, -50)];
        var actions = Run(brain, 0, 1000, [.. onUs, .. left, .. farRight]);
        Assert.Contains(actions, x => x is NoteAction n && n.Text.Contains("toplanıyor"));
        Assert.Contains(actions, x => x is KeyDownAction d && d.Key == p.Keys.MoveLeft);   // to the close group
        Assert.DoesNotContain(actions, x => x is KeyDownAction d && d.Key == p.Keys.MoveRight);

        // Only far groups: the mob on us is fought, no gathering.
        var (brain2, p2) = Ready(max: 9);
        var fight = Run(brain2, 0, 1000, [.. onUs, .. farRight]);
        Assert.DoesNotContain(fight, x => x is NoteAction n && n.Text.Contains("toplanıyor"));
        Assert.Contains(fight, x => x is KeyDownAction d && d.Key == p2.Keys.Attack);
    }

    [Fact]
    public void Gives_up_a_group_it_cannot_get_to()
    {
        var (brain, _) = Ready(max: 9);
        // A group 5 m away that we never get closer to (behind a wall).
        TargetInfo[] walled = [At(20, 20, -500, 0), At(21, 20, -520, 50), At(22, 20, -540, -50)];
        var actions = Run(brain, 0, 8000, walled);
        Assert.Contains(actions, x => x is NoteAction n && n.Text.Contains("ulaşılamadı"));
        // Then the normal fight takes over (walking to the nearest mob), no gathering note again soon.
        var after = Run(brain, 8000, 12000, walled);
        Assert.DoesNotContain(after, x => x is NoteAction n && n.Text.Contains("ulaşılamadı"));
    }

    [Fact]
    public void Stops_gathering_when_hp_is_low()
    {
        var (brain, _) = Ready(max: 9);
        TargetInfo[] first = [Mob(10, 10, 40, 100), Mob(11, 10, 60, 150)];
        TargetInfo[] coming = [Mob(10, 10, 40, 100, attacks: true), Mob(11, 10, 60, 150, attacks: true)];
        TargetInfo[] fresh = [Mob(20, 20, -500, 1000), Mob(21, 20, -520, 1050)];
        Run(brain, 0, 500, [.. first, .. fresh]);          // gathering starts
        Run(brain, 500, 1000, [.. coming, .. fresh]);      // the first group comes, on to the next
        var low = Run(brain, 1000, 2000, [.. coming, .. fresh], hp: 0.5);
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
