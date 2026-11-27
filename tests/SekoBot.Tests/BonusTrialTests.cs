using System.Runtime.CompilerServices;
using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

internal static class TrialsForTests
{
    /// <summary>The tests never read the real efsun-denemeleri.json.</summary>
    [ModuleInitializer]
    internal static void Init() => BotEngine.Trials = new BonusTrials();
}

public class BonusTrialTests
{
    [Fact]
    public void An_item_without_a_wanted_bonus_twelve_times_is_not_worth_it_any_more()
    {
        var trials = new BonusTrials();
        for (int i = 0; i < BonusTrials.GiveUpAfter - 1; i++)
            Assert.False(trials.Record(100, false));
        Assert.True(trials.Worth(100));
        Assert.True(trials.Record(100, false));
        Assert.False(trials.Worth(100));
    }

    [Fact]
    public void An_item_that_brings_a_wanted_bonus_now_and_then_stays_worth_it()
    {
        var trials = new BonusTrials();
        trials.Record(200, true);
        for (int i = 0; i < 30; i++)
            trials.Record(200, false);
        Assert.True(trials.Worth(200));
        for (int i = 0; i < 60; i++)
            trials.Record(200, false);
        Assert.False(trials.Worth(200)); // 1 in 91: under 2.5 %
    }

    [Fact]
    public void A_nearly_full_bag_picks_no_weapon_just_to_see_its_bonuses()
    {
        int sword = ItemBook.All().First(i => Loot.KindOf(i.Vnum) == LootKind.Equipment).Vnum;
        var profile = new BotProfile { LootEquipment = false, LootOwnClass = false, LootEquipmentMinGold = 0 };
        profile.LootBonuses.Add(new BonusWant { Type = 47, Min = 6 });
        var item = new GroundItem(1, default, sword);
        Assert.True(BotEngine.WantedLoot(item, profile, freeSlots: 20));
        Assert.False(BotEngine.WantedLoot(item, profile, freeSlots: BotEngine.BonusLookMinFree));
    }
}
