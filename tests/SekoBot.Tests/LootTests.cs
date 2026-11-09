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
