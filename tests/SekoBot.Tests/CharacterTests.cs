using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Each character keeps its own settings and what it learned, whichever SekoBot it plays in.</summary>
[Collection("CharacterFolder")]
public class CharacterTests
{
    [Fact]
    public void A_character_brings_its_own_fight_and_loot_settings_but_not_the_bots_keys_or_telegram()
    {
        var strong = new BotProfile { PullMaxMobs = 15, PotionHpBelow = 0.6, LootYang = false, LootPick = [1, 2], TelegramToken = "a" };
        strong.PullLearned["c3"] = 13;
        string json = CharacterSettings.ToJson(strong);
        Assert.DoesNotContain("Telegram", json);
        Assert.DoesNotContain("\"Keys\"", json);

        // The other SekoBot, set up for a weaker character, gets the strong one.
        var bot2 = new BotProfile { PullMaxMobs = 6, PotionHpBelow = 0.7, LootYang = true, TelegramToken = "b" };
        bot2.Keys.HpPotion = Keys.D3;
        CharacterSettings.Apply(json, bot2);
        Assert.Equal(15, bot2.PullMaxMobs);
        Assert.Equal(0.6, bot2.PotionHpBelow);
        Assert.False(bot2.LootYang);
        Assert.Equal([1, 2], bot2.LootPick);
        Assert.Equal(13, bot2.PullLearned["c3"]);
        Assert.Equal("b", bot2.TelegramToken);      // stays with the SekoBot
        Assert.Equal(Keys.D3, bot2.Keys.HpPotion);
    }

    [Fact]
    public void A_character_without_a_farm_spot_clears_the_other_ones()
    {
        var none = new BotProfile();
        var other = new BotProfile { FarmSpot = new FarmSpot { MapName = "c3" } };
        CharacterSettings.Apply(CharacterSettings.ToJson(none), other);
        Assert.Null(other.FarmSpot);
    }

    [Fact]
    public void Saved_and_loaded_by_name()
    {
        string folder = Path.Combine(Path.GetTempPath(), "sekobot-char-" + Guid.NewGuid().ToString("N"));
        string old = CharacterSettings.Folder;
        CharacterSettings.Folder = folder;
        try
        {
            Assert.False(CharacterSettings.Load("UNiCORN06", new BotProfile()));
            CharacterSettings.Save("UNiCORN06", new BotProfile { PullMaxMobs = 6 });
            var profile = new BotProfile { PullMaxMobs = 15 };
            Assert.True(CharacterSettings.Load("UNiCORN06", profile));
            Assert.Equal(6, profile.PullMaxMobs);
        }
        finally
        {
            CharacterSettings.Folder = old;
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    private static GameState Fight(double ms, int attackers, double hp, string map)
    {
        var center = new Point(960, 540);
        var targets = Enumerable.Range(0, attackers)
            .Select(i => new TargetInfo(100 + i, new Point(center.X + 40 + i, center.Y), 90, 130, 365, 400, 585, 56, false, AttacksMe: true)).ToArray();
        return new GameState(TimeSpan.FromMilliseconds(ms), 1920, 1080, hp, 1, 0, 1) { Targets = targets, Walking = true, AttackPower = 189, MapName = map };
    }

    [Fact]
    public void How_many_mobs_it_can_take_is_kept_per_map_for_the_next_start()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PullGroups = true, PullMaxMobs = 15 };
        p.PullLearned["c2"] = 12;     // learned earlier
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        void Run(double from, double to, int attackers, double hp, string map = "c2")
        {
            for (double t = from; t < to; t += 100)
                brain.Tick(Fight(t, attackers, hp, map));
        }
        Run(0, 2000, 9, 0.8);
        Run(2000, 3000, 9, 0.2);       // 9 on us, HP down to 20 %
        Run(3000, 3500, 0, 0.5);
        Assert.Equal(7, p.PullLearned["c2"]);
        Assert.True(brain.ProfileChanged);

        // Next time (a new start, maybe in the other SekoBot): 7 from the start on c2; a map not
        // learned yet starts safe.
        var again = new Brain(() => p);
        again.Start(TimeSpan.Zero);
        again.Tick(Fight(0, 0, 1, "c2"));
        Assert.Equal(7, again.PullCap(p));
        again.Tick(Fight(100, 0, 1, "c3"));
        Assert.Equal(Brain.PullStartMobs, again.PullCap(p));
    }

    [Fact]
    public void A_strong_character_climbs_quickly_from_the_safe_start()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PullGroups = true, PullMaxMobs = 15 };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        double t = 0;
        for (int fight = 0; fight < 5; fight++)
        {
            int cap = brain.PullCap(p);
            for (double end = t + 1500; t < end; t += 100)
                brain.Tick(Fight(t, cap, 0.95, "c3"));      // all of them on us, HP hardly moves
            for (double end = t + 500; t < end; t += 100)
                brain.Tick(Fight(t, 0, 0.95, "c3"));
        }
        Assert.Equal(15, brain.PullCap(p));               // 6 → 8 → 10 → 12 → 14 → 15
        Assert.Equal(15, p.PullLearned["c3"]);
    }

    [Fact]
    public void A_new_character_starts_fresh_but_keeps_what_the_player_picks_up()
    {
        var other = new BotProfile { PullMaxMobs = 6, PotionHpBelow = 0.7, OnlyMobs = "Ayı", LootYang = false, LootPick = [5] };
        other.PullLearned["c2"] = 4;
        other.FarmSpot = new FarmSpot { MapName = "c2" };
        CharacterSettings.Fresh(other);
        var defaults = new BotProfile();
        Assert.Equal(defaults.PullMaxMobs, other.PullMaxMobs);
        Assert.Equal(defaults.PotionHpBelow, other.PotionHpBelow);
        Assert.Equal("", other.OnlyMobs);
        Assert.Empty(other.PullLearned);
        Assert.Null(other.FarmSpot);
        Assert.False(other.LootYang);                      // the player's choice stays
        Assert.Equal([5], other.LootPick);
    }
}

/// <summary>Skills used the way each one works best.</summary>
public class SkillChoiceTests
{
    private const int W = 1920, H = 1080;

    private static TargetInfo Mob(int id, double distance) =>
        new(id, new Point(W / 2 + (int)(distance / 5), H / 2), distance, 130, 103, 305, 305, 56, true, true);

    private static List<Keys> Taps(Brain brain, SkillSlot[] skills, int mp, double from, double to)
    {
        TargetInfo[] five = [Mob(1, 80), Mob(2, 100), Mob(3, 120), Mob(4, 140), Mob(5, 160)];
        var keys = new List<Keys>();
        for (double t = from; t < to; t += 100)
        {
            var state = new GameState(TimeSpan.FromMilliseconds(t), W, H, 1, 1, 0, 1)
                { Skills = skills, MpPoints = mp, Targets = five, AttackPower = 189, Walking = true };
            keys.AddRange(brain.Tick(state).OfType<KeyTapAction>().Select(k => k.Key).Where(k => k is >= Keys.F1 and <= Keys.F5));
        }
        return keys;
    }

    private static Brain NewBrain()
    {
        var p = new BotProfile { LeashMeters = 0, PullGroups = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        return brain;
    }

    [Fact]
    public void A_skill_that_pushes_the_mobs_away_comes_after_the_other_area_attack()
    {
        SkillSlot[] skills =
        [
            new(Keys.F2, "Kılıç darbesi", SkillKind.Area, 46, 17, 0, 400, SkillTraits.Knockback | SkillTraits.Stun),
            new(Keys.F3, "Güçlü vuruş", SkillKind.Area, 80, 21, 0, 400, SkillTraits.Stun),
        ];
        var keys = Taps(NewBrain(), skills, 1000, 0, 5_000);
        Assert.Equal([Keys.F3, Keys.F2], keys.Take(2));
    }

    [Fact]
    public void Attacks_leave_the_mp_for_a_buff_that_is_about_to_run_out()
    {
        SkillSlot[] skills =
        [
            new(Keys.F1, "Güçlü beden", SkillKind.Buff, 159, 80, 30),
            new(Keys.F3, "Ruh vuruşu", SkillKind.Single, 66, 13, 0, 170, SkillTraits.Front),
        ];
        var brain = NewBrain();
        Assert.Contains(Keys.F1, Taps(brain, skills, 1000, 0, 3_000));   // the buff goes on (lasts 30 s)
        // 200 MP: an attack (66) would leave too little for the buff due in under 10 s.
        Assert.DoesNotContain(Keys.F3, Taps(brain, skills, 200, 22_000, 24_000));
    }
}
