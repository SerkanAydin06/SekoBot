using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class SessionStatsTests
{
    private const int HpPotion = 27201, MpPotion = 27204;
    private static readonly Vec3 Me = new(20000, 18000, 900);

    private static WorldMob Mob(int id, double metres) => new(id, 101, "Kurt", 3, 100, 162, Me with { X = Me.X + metres * 100 });

    private static WorldSnapshot World(long exp, int level = 12, IEnumerable<WorldMob>? alive = null,
        IEnumerable<DeadMob>? dead = null, int hpPotions = 300, int mpPotions = 100, long yang = 1000) =>
        new(Me, null, (alive ?? []).ToList(), 0, "c2", Level: level, DeadMobs: (dead ?? []).ToList(),
            Progress: new CharacterProgress(exp, 100_000 - exp, yang,
                [new InventoryItem(12, HpPotion, hpPotions), new InventoryItem(15, MpPotion, mpPotions)], [12, 15]));

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static SessionStats Started()
    {
        var stats = new SessionStats();
        stats.Reset(TimeSpan.Zero);
        return stats;
    }

    [Fact]
    public void Counts_a_mob_that_died_next_to_us_when_experience_came()
    {
        var stats = Started();
        stats.Update(World(1000, alive: [Mob(1, 2), Mob(2, 3)]), S(0), HpPotion, MpPotion);
        stats.Update(World(1000, alive: [Mob(1, 2), Mob(2, 3)]), S(1), HpPotion, MpPotion);
        // Mob 1 dies, the experience comes half a second later.
        stats.Update(World(1000, alive: [Mob(2, 3)], dead: [new DeadMob(1, Me)]), S(1.5), HpPotion, MpPotion);
        Assert.Equal(0, stats.Kills);
        stats.Update(World(1150, alive: [Mob(2, 3)], dead: [new DeadMob(1, Me)]), S(2), HpPotion, MpPotion);
        Assert.Equal(1, stats.Kills);
        Assert.Equal(150, stats.Exp);
        // Seen dead again later: not counted twice.
        stats.Update(World(1150, alive: [Mob(2, 3)], dead: [new DeadMob(1, Me)]), S(3), HpPotion, MpPotion);
        Assert.Equal(1, stats.Kills);
    }

    [Fact]
    public void Does_not_count_mobs_killed_by_others_or_far_away()
    {
        var stats = Started();
        stats.Update(World(1000, alive: [Mob(1, 2), Mob(2, 30)]), S(0), HpPotion, MpPotion);
        // Mob 1 dies next to us but no experience comes (someone else killed it).
        stats.Update(World(1000, alive: [Mob(2, 30)], dead: [new DeadMob(1, Me)]), S(1), HpPotion, MpPotion);
        stats.Update(World(1000, alive: [Mob(2, 30)]), S(6), HpPotion, MpPotion);
        // Mob 2 dies 30 m away; experience comes (from something else).
        stats.Update(World(1100, dead: [new DeadMob(2, Me)]), S(7), HpPotion, MpPotion);
        Assert.Equal(0, stats.Kills);
        Assert.Equal(100, stats.Exp);
    }

    [Fact]
    public void Several_mobs_dying_together_all_count()
    {
        var stats = Started();
        stats.Update(World(1000, alive: [Mob(1, 2), Mob(2, 2), Mob(3, 4)]), S(0), HpPotion, MpPotion);
        stats.Update(World(1300, alive: [Mob(3, 4)], dead: [new DeadMob(1, Me), new DeadMob(2, Me)]), S(1), HpPotion, MpPotion);
        Assert.Equal(2, stats.Kills);
    }

    [Fact]
    public void Experience_over_a_level_up_and_potions_used()
    {
        var stats = Started();
        string? ranOut = null;
        stats.PotionsRanOut += kind => ranOut = kind;
        stats.Update(World(99_000, hpPotions: 3), S(0), HpPotion, MpPotion);
        stats.Update(World(99_500, hpPotions: 2), S(10), HpPotion, MpPotion);
        // Level up: 500 to finish level 12, then 200 into level 13.
        stats.Update(World(200, level: 13, hpPotions: 0, yang: 1500), S(20), HpPotion, MpPotion);
        Assert.Equal(1200, stats.Exp);
        Assert.Equal(1, stats.LevelsGained);
        Assert.Equal(3, stats.HpPotionsUsed);
        Assert.Equal(500, stats.Yang);
        Assert.Equal("HP", ranOut);
        var report = stats.Report(World(200, level: 13, hpPotions: 0), S(120));
        Assert.Equal(0, report.HpPotionsLeft);
        Assert.Equal(100, report.MpPotionsLeft);
        Assert.Equal(0.2, report.ExpPercent!.Value, 3);
        Assert.NotNull(report.ToLevel);
    }

    [Fact]
    public void Free_inventory_slots_count_tall_items_twice()
    {
        // Two pages: slots 11–106. Equipment (slots 1–10) doesn't count.
        var items = new List<InventoryItem> { new(1, 3014, 1), new(2, 11214, 1) };
        for (int slot = 11; slot <= 106; slot++)
            items.Add(new InventoryItem(slot, 27003, 200));
        Assert.Equal(0, new CharacterProgress(0, 1, 0, items, [], 106).FreeSlots());

        // As found in the game: a bow, an armour and a dagger.
        var some = new CharacterProgress(0, 1, 0,
            [new(59, 2030, 1), new(61, 11800, 1), new(91, 1020, 1), new(85, 39006, 1), new(200, 27003, 1)], [], 106);
        Assert.Equal(96 - 2 * 2 - 2, some.FreeSlots()); // slot 200 is on a locked page
        Assert.Null(new CharacterProgress(0, 1, 0, [], [], 0).FreeSlots());
    }

    [Theory]
    [InlineData("Receoplex", false, false)]
    [InlineData("Receoplex", true, true)]
    [InlineData("[GM]Ayaz", false, true)]
    [InlineData("[sa]Mod", false, true)]
    public void Game_masters_are_recognised(string name, bool flag, bool gm) =>
        Assert.Equal(gm, new OtherPlayer(name, flag, Me).LooksLikeGm);

    [Fact]
    public void Quick_slots_point_at_inventory_items()
    {
        var progress = new CharacterProgress(0, 1, 0,
            [new InventoryItem(12, HpPotion, 181), new InventoryItem(14, HpPotion, 175), new InventoryItem(15, MpPotion, 182)],
            [12, 15, 0, 0, 0, -104]);
        Assert.Equal(HpPotion, progress.QuickSlotVnum(1));
        Assert.Equal(MpPotion, progress.QuickSlotVnum(2));
        Assert.Null(progress.QuickSlotVnum(3)); // inventory slot 0 is empty
        Assert.Null(progress.QuickSlotVnum(6)); // a skill
        Assert.Equal(356, progress.Count(HpPotion));
    }
}
