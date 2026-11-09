using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class DropJournalTests
{
    private static readonly Vec3 Me = new(13000, 10000, 1800);
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    private static WorldMob Mob(int id, int vnum, double x) =>
        new(id, vnum, vnum == 114 ? "Kaplan" : "Siyah Ayı", 18, 585, 585, new Vec3(Me.X + x, Me.Y, Me.Z - 60));

    private static GroundItem Drop(long id, double x, int vnum, int count = 1) => new(id, new Vec3(Me.X + x, Me.Y + 30, Me.Z), vnum, count);

    private static WorldSnapshot World(WorldMob[] mobs, GroundItem[] drops) => new(Me, null, mobs, 0, "c2", Drops: drops);

    [Fact]
    public void Items_falling_where_a_mob_died_are_its_drops()
    {
        var journal = new DropJournal();
        journal.Update(World([Mob(1, 114, 100), Mob(2, 112, 500)], [Drop(90, 3000, 1, 50)]), S(0)); // old yang far off
        journal.Update(World([Mob(2, 112, 500)], []), S(1));                                          // the Kaplan died
        journal.Update(World([Mob(2, 112, 500)], [Drop(91, 110, 1, 70), Drop(92, 90, 30038)]), S(2)); // yang + Kaplan Postu
        journal.Update(World([], [Drop(91, 110, 1, 70), Drop(92, 90, 30038)]), S(3));                  // the bear died
        journal.Update(World([], [Drop(93, 510, 14001)]), S(4));                                       // its bracelet
        journal.Update(World([], [Drop(94, 1500, 50300)]), S(5));                                      // far from both: nobody's

        var tiger = journal.For("c2", 114)!;
        Assert.Equal(1, tiger.Kills);
        Assert.Equal(70, tiger.Yang);
        Assert.Equal(1, tiger.Items[30038]);
        var bear = journal.For("c2", 112)!;
        Assert.Equal(1, bear.Kills);
        Assert.Equal([14001], bear.Items.Keys);
    }

    [Fact]
    public void Late_items_and_far_deaths_do_not_count()
    {
        var journal = new DropJournal();
        journal.Update(World([Mob(1, 114, 100), Mob(2, 112, 2500)], []), S(0));
        journal.Update(World([], []), S(1));                                     // both gone; the bear was 25 m off
        journal.Update(World([], [Drop(95, 100, 30038)]), S(9));                 // 8 s later: not the Kaplan's
        Assert.Equal(1, journal.For("c2", 114)!.Kills);
        Assert.Empty(journal.For("c2", 114)!.Items);
        Assert.Null(journal.For("c2", 112));
    }

    [Fact]
    public void Shares_are_shown_after_twenty_kills()
    {
        ItemBook.Learn(new ItemInfo(95038, "Kaplan Postu", "ITEM_MATERIAL", "MATERIAL_LEATHER", 300));
        var drops = new MobDrops { Kills = 40, Items = { [95038] = 4 } };
        Assert.Equal("Kaplan Postu ×4 (%10)", DropJournal.Describe(drops));
        Assert.Equal("Kaplan Postu ×4", DropJournal.Describe(new MobDrops { Kills = 5, Items = { [95038] = 4 } }));
    }
}
