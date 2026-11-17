using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Mobs and places that suit the character, learned from its own fights.</summary>
public class MobFitTests
{
    private static WorldMob Mob(string name, int level, double x = 0, double y = 0, bool attacks = false, int exp = 100, int hp = 300) =>
        new(name.GetHashCode() & 0xFFFF, 100 + level, name, level, hp, hp, new Vec3(x, y, 0), AttacksMe: attacks, Exp: exp);

    [Fact]
    public void Before_any_fight_mobs_far_above_the_character_are_too_strong()
    {
        var p = new BotProfile();
        Assert.True(MobFit.TooHard(Mob("Kaplan", 18), p, 9));      // a level 9 sura on c2
        Assert.False(MobFit.TooHard(Mob("Yabani Köpek", 12), p, 9));
        Assert.False(MobFit.TooHard(Mob("Kaplan", 18), p, 22));    // nothing for a level 22
        Assert.False(MobFit.TooHard(Mob("Kaplan", 18), p, 0));     // level not known: no guess
    }

    [Fact]
    public void Fights_decide_more_than_the_level()
    {
        var p = new BotProfile();
        var kurt = Mob("Alfa Gri Kurt", 12);
        // Three fights with only three wolves that took HP under 40 %: too strong, though only 3 levels up.
        for (int i = 0; i < 3; i++)
            p.MobFights = MobFit.Record(p.MobFights, [("Alfa Gri Kurt", 12)], most: 3, lowestHp: 0.3);
        Assert.True(MobFit.TooHard(kurt, p, 9));
        // Many mobs on us is too many, not too strong: doesn't count against them.
        var q = new BotProfile();
        for (int i = 0; i < 5; i++)
            q.MobFights = MobFit.Record(q.MobFights, [("Alfa Gri Kurt", 12)], most: 9, lowestHp: 0.2);
        Assert.False(MobFit.TooHard(kurt, q, 9));
        // Beaten easily again and again: fits, even 7 levels up.
        var r = new BotProfile();
        for (int i = 0; i < 4; i++)
            r.MobFights = MobFit.Record(r.MobFights, [("Kaplan", 16)], most: 3, lowestHp: 0.9);
        Assert.False(MobFit.TooHard(Mob("Kaplan", 16), r, 9));
    }

    [Fact]
    public void A_character_that_grows_stronger_stops_avoiding_them()
    {
        var p = new BotProfile();
        for (int i = 0; i < 3; i++)
            p.MobFights = MobFit.Record(p.MobFights, [("Kaplan", 18)], most: 3, lowestHp: 0.3);
        Assert.True(MobFit.TooHard(Mob("Kaplan", 18), p, 20));
        for (int i = 0; i < 9; i++)
            p.MobFights = MobFit.Record(p.MobFights, [("Kaplan", 18)], most: 3, lowestHp: 0.9);
        Assert.False(MobFit.TooHard(Mob("Kaplan", 18), p, 20));
    }

    [Fact]
    public void Too_strong_mobs_are_not_gone_for_unless_they_attack()
    {
        var p = new BotProfile();
        Assert.False(BotEngine.Wanted(Mob("Kaplan", 18), p, 9, fit: true));
        Assert.True(BotEngine.Wanted(Mob("Kaplan", 18, attacks: true), p, 9, fit: true));
        Assert.True(BotEngine.Wanted(Mob("Kaplan", 18), p, 9, fit: false));                    // nothing suits: goes on
        Assert.True(BotEngine.Wanted(Mob("Kaplan", 18), new BotProfile { AutoArea = false }, 9, fit: true));
        Assert.True(BotEngine.Wanted(Mob("Kaplan", 18), new BotProfile { MaxMobLevel = 20 }, 9, fit: true)); // set by hand
    }

    [Fact]
    public void The_best_place_is_the_one_with_mobs_that_suit_the_character()
    {
        // Where the character stands: tigers (Sv 18, lots of EXP). 40 m away: dogs (Sv 11).
        var mobs = new List<WorldMob>();
        for (int i = 0; i < 6; i++)
            mobs.Add(Mob("Kaplan", 18, i * 100, 0, exp: 600, hp: 900) with { Id = i + 1 });
        for (int i = 0; i < 6; i++)
            mobs.Add(Mob("Yabani Köpek", 11, 4000 + i * 100, 0, exp: 120, hp: 300) with { Id = i + 100 });
        var p = new BotProfile();
        var forLevel9 = FarmSpotFinder.Best(mobs, p, new Vec3(0, 0, 0), "c2", 100, out double best, level: 9, fit: true);
        Assert.NotNull(forLevel9);
        Assert.True(forLevel9!.Position.X > 3000);                                            // to the dogs
        Assert.Equal(0, FarmSpotFinder.Score(mobs, p, new Vec3(0, 0, 0), 100, level: 9, fit: true));
        Assert.True(best > 0);
        var forLevel22 = FarmSpotFinder.Best(mobs, p, new Vec3(0, 0, 0), "c2", 300, level: 22, fit: true);
        Assert.True(forLevel22!.Position.X < 1000);                                           // the tigers pay more
    }

    [Fact]
    public void A_fight_ends_when_nothing_has_attacked_for_a_moment()
    {
        var watch = new FightWatch();
        static TimeSpan s(double v) => TimeSpan.FromSeconds(v);
        Assert.Null(watch.Update([Mob("Kaplan", 18, attacks: true), Mob("Kurt", 12, attacks: true)], 0.8, s(0)));
        Assert.Null(watch.Update([Mob("Kaplan", 18, attacks: true)], 0.35, s(2)));
        Assert.Null(watch.Update([], 0.5, s(3)));
        var fight = watch.Update([], 0.6, s(6));
        Assert.NotNull(fight);
        Assert.Equal(2, fight!.Value.Most);
        Assert.Equal(0.35, fight.Value.LowestHp);
        Assert.Contains(("Kaplan", 18), fight.Value.Kinds);
        Assert.Null(watch.Update([], 0.6, s(9)));
    }
}

public class FitTickTests
{
    [Fact]
    public void The_first_check_right_after_the_start_does_not_fail()
    {
        var engine = new BotEngine(new WindowInfo(0, 0, "test", 1920, 1080, false), new BotProfile(), _ => { });
        var world = new WorldSnapshot(new Vec3(0, 0, 0), null, [], 9, "c2");
        engine.FitTick(world, engine.Profile, TimeSpan.FromSeconds(1));
        engine.FitTick(world, engine.Profile, TimeSpan.FromMinutes(4));
    }
}
