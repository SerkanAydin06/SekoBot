using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class LootTests
{
    [Fact]
    public void Items_seen_in_the_bag_are_known_on_the_ground()
    {
        // Before: a number in the material range is "other".
        Assert.Equal(LootKind.Other, Loot.KindOf(91001));
        // Learned from the bag (as read in the game: an earring, ITEM_ARMOR / ARMOR_EAR, 1700 yang).
        ItemBook.Learn(new ItemInfo(91001, "Gümüş Küpe+3", "ITEM_ARMOR", "ARMOR_EAR", 1700));
        Assert.Equal(LootKind.Equipment, Loot.KindOf(91001));
        Assert.Equal("Gümüş Küpe+3", ItemBook.NameOf(91001));
        Assert.Equal("eşya #91999", ItemBook.NameOf(91999));
        ItemBook.Learn(new ItemInfo(91002, "Kırmızı İksir(O)", "ITEM_USE", "USE_POTION", 96));
        Assert.Equal(LootKind.Potion, Loot.KindOf(91002));
    }

    [Fact]
    public void Equipment_worth_enough_is_picked_up_even_with_equipment_off()
    {
        ItemBook.Learn(new ItemInfo(92001, "Bakır Küpe+5", "ITEM_ARMOR", "ARMOR_EAR", 4000));
        ItemBook.Learn(new ItemInfo(92002, "Mızrak+4", "ITEM_WEAPON", "WEAPON_TWO_HANDED", 400));
        GroundItem Drop(int vnum) => new(1, new Vec3(0, 0, 0), vnum, 1);
        var p = new BotProfile { LootEquipment = false, LootEquipmentMinGold = 1000 };
        Assert.True(BotEngine.WantedLoot(Drop(92001), p));     // 4000 yang
        Assert.False(BotEngine.WantedLoot(Drop(92002), p));    // 400 yang
        p.LootEquipmentMinGold = 0;
        Assert.False(BotEngine.WantedLoot(Drop(92001), p));
    }

    [Fact]
    public void Picks_up_what_lies_around_before_gathering_the_next_group()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PickUpDrops = true, PullGroups = true };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var center = new Point(960, 540);
        // A fresh group 6 m to the right; yang lying 2 m to the left.
        TargetInfo[] group = [new(20, new Point(center.X + 120, center.Y), 600, 130, 103, 305, 305, 56, false, false, 20),
                              new(21, new Point(center.X + 130, center.Y + 10), 620, 130, 103, 305, 305, 56, false, false, 20)];
        var actions = new List<BotAction>();
        for (double t = 0; t < 1000; t += 100)
            actions.AddRange(brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), 1920, 1080, 1, 1, 0, 1)
            {
                Targets = group, Walking = true, AttackPower = 189,
                Drops = [new GroundSpot(7, new Point(center.X - 60, center.Y))],
            }));
        Assert.Contains(actions, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
        Assert.DoesNotContain(actions, a => a is NoteAction n && n.Text.Contains("toplanıyor"));
    }
}

public class TimedPickupTests
{
    private static int Taps(bool walkToDrops)
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PickUpDrops = walkToDrops, PullGroups = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        int taps = 0;
        for (double t = 0; t < 10_000; t += 100)
            taps += brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), 1920, 1080, 1, 1, 0, 1) { Walking = true })
                .Count(a => a is KeyTapAction k && k.Key == p.Keys.Pickup);
        return taps;
    }

    [Fact]
    public void No_z_every_few_seconds_while_the_bot_walks_to_the_items_itself()
    {
        Assert.Equal(0, Taps(walkToDrops: true));     // Z only on an item it chose
        Assert.InRange(Taps(walkToDrops: false), 4, 10); // the old way: every ~1.5 s
    }
}

public class LootWhileFightingTests
{
    private static readonly Point Center = new(960, 540);

    private static (Brain, BotProfile) Ready()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PickUpDrops = true, PullGroups = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        return (brain, p);
    }

    private static List<BotAction> Run(Brain brain, double from, double to, TargetInfo[] mobs, GroundSpot[] drops)
    {
        var all = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            all.AddRange(brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), 1920, 1080, 1, 1, 0, 1)
                { Targets = mobs, Drops = drops, Walking = true, AttackPower = 189 }));
        return all;
    }

    [Fact]
    public void Keeps_hitting_and_picks_up_what_lies_at_its_feet()
    {
        var (brain, p) = Ready();
        TargetInfo[] mob = [new(1, new Point(Center.X + 20, Center.Y), 80, 130, 103, 305, 305, 56, true, true)];
        var fight = Run(brain, 0, 3000, mob, [new GroundSpot(7, new Point(Center.X + 10, Center.Y + 10))]);
        Assert.Contains(fight, a => a is KeyDownAction d && d.Key == p.Keys.Attack);
        Assert.DoesNotContain(fight, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.True(fight.Count(a => a is KeyTapAction k && k.Key == p.Keys.Pickup) >= 2);
    }

    [Fact]
    public void Lets_go_of_attack_and_goes_for_the_items_when_no_mob_is_left()
    {
        var (brain, p) = Ready();
        TargetInfo[] mob = [new(1, new Point(Center.X + 20, Center.Y), 80, 130, 103, 305, 305, 56, true, true)];
        Run(brain, 0, 2000, mob, []);                                              // fighting, attack held
        var loot = Run(brain, 2000, 3000, [], [new GroundSpot(7, new Point(Center.X - 80, Center.Y))]);
        Assert.Contains(loot, a => a is KeyUpAction u && u.Key == p.Keys.Attack);
        Assert.Contains(loot, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
        // A mob far off (not attacking) and items here: items first, attack stays off.
        var (brain2, p2) = Ready();
        Run(brain2, 0, 2000, mob, []);
        TargetInfo[] far = [new(2, new Point(Center.X + 400, Center.Y), 1500, 130, 103, 305, 305, 56, false, false)];
        var loot2 = Run(brain2, 2000, 3000, far, [new GroundSpot(8, new Point(Center.X - 80, Center.Y))]);
        Assert.Contains(loot2, a => a is KeyUpAction u && u.Key == p2.Keys.Attack);
        Assert.Contains(loot2, a => a is KeyDownAction d && d.Key == p2.Keys.MoveLeft);
    }
}
