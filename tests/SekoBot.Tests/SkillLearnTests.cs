using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Each character's skills are learnt from the game's own texts and used by what they do.</summary>
public class SkillLearnTests
{
    // The warrior's skills as the game describes them (read from memory, 16.11.2026).
    [Theory]
    [InlineData("HP", "Düşmanlara önden saldır. Fiziki yakın dövüş saldırısı", "Ön yüzey etkisi STR ile güçlenir", "-(2.3*atk+(4*atk+str*4+con)*k)", (int)SkillKind.Single, (int)(SkillTraits.Front))]
    [InlineData("HP", "Birden çok düşmanı dağıt. Fiziki yakın dövüş saldırısı", "Alan etkisi Bayılma olasılığı", "-(2*atk+(2*atk+2*dex+2*con+str*4)*k)", (int)SkillKind.Area, (int)(SkillTraits.Stun))]
    [InlineData("DEF_GRADE", "Savunman gelişir ve düşmezsin. Pozitif etki", "Hareket hızını düşürür Büyük üstat evresinde geri tepme yok", "(200+str*0.2+con*0.5)*k", (int)SkillKind.Buff, (int)(SkillTraits.SlowsMe))]
    [InlineData("HP", "Düşmanlara önden saldır. Fiziki uzaktan saldırı", "Alan etkisi Geri tepme Bayılma", "-(2*atk+(atk+dex*3+str*5+con)*k)", (int)SkillKind.Area, (int)(SkillTraits.Stun | SkillTraits.Knockback | SkillTraits.Ranged))]
    // Other classes: HP given back is a heal, a weakening of the enemy is an attack, not a buff.
    [InlineData("HP", "Can puanını yeniler. Pozitif etki", "", "200+int*4*k", (int)SkillKind.Heal, (int)(SkillTraits.None))]
    [InlineData("ATT_GRADE", "Düşmanın saldırısını düşürür. Negatif etki", "Alan etkisi", "-(10+int*0.3)*k", (int)SkillKind.Area, (int)(SkillTraits.None))]
    // Texts without a formula: as before.
    [InlineData("HP", "Düşmana saldır.", "Alan etkisi", "", (int)SkillKind.Area, (int)(SkillTraits.None))]
    [InlineData("MOV_SPEED", "Pozitif etki", "", "", (int)SkillKind.Buff, (int)(SkillTraits.None))]
    public void A_skill_is_told_apart_by_what_the_game_says(string type, string description, string properties, string formula, int kind, int traits)
    {
        Assert.Equal(((SkillKind)kind, (SkillTraits)traits), SkillInfo.Describe(type, description, properties, formula));
    }

    private const int W = 1920, H = 1080;
    private static readonly Point Center = new(W / 2, H / 2);

    private static readonly SkillSlot Beden = new(Keys.F1, "Güçlü beden", SkillKind.Buff, 159, 94, 184, 0, SkillTraits.SlowsMe, 17);
    private static readonly SkillSlot Vurus = new(Keys.F2, "Güçlü vuruş", SkillKind.Area, 80, 25, 0, 400, SkillTraits.Stun, 10);
    private static readonly SkillSlot Ruh = new(Keys.F3, "Ruh vuruşu", SkillKind.Single, 66, 15, 0, 165, SkillTraits.Front, 1);

    private static (Brain, BotProfile) Ready()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], PullGroups = false, PickUpDrops = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        return (brain, p);
    }

    private static TargetInfo Mob(int id, double distance, bool attacks = true) =>
        new(id, new Point(Center.X + (int)(distance / 5), Center.Y), distance, 130, 103, 305, 305, 56, attacks, attacks);

    private static GameState State(double ms, double hp, SkillSlot[] skills, TargetInfo[] mobs, string[]? off = null) =>
        new(TimeSpan.FromMilliseconds(ms), W, H, hp, 1, 0, 1)
        {
            Skills = skills, MpPoints = 1000, Targets = mobs, MobCandidates = mobs.Select(m => m.Spot).ToArray(),
            Walking = true, AttackPower = 189, SkillsOffSlots = off ?? [],
        };

    private static List<BotAction> Run(Brain brain, double from, double to, Func<double, GameState> state)
    {
        var all = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            all.AddRange(brain.Tick(state(t)));
        return all;
    }

    private static List<Keys> Taps(List<BotAction> actions) =>
        [.. actions.OfType<KeyTapAction>().Select(k => k.Key).Where(k => k is >= Keys.F1 and <= Keys.F5)];

    [Fact]
    public void A_buff_that_slows_is_put_on_in_the_fight_not_on_the_way()
    {
        var (brain, _) = Ready();
        Assert.DoesNotContain(Keys.F1, Taps(Run(brain, 0, 5_000, t => State(t, 1, [Beden], []))));      // nothing to fight
        Assert.Contains(Keys.F1, Taps(Run(brain, 5_000, 9_000, t => State(t, 1, [Beden], [Mob(1, 80)]))));
    }

    [Fact]
    public void Low_on_hp_a_stunning_area_skill_is_used_on_a_small_group()
    {
        TargetInfo[] three = [Mob(1, 80), Mob(2, 120), Mob(3, 160)];
        var (healthy, _) = Ready();
        Assert.DoesNotContain(Keys.F2, Taps(Run(healthy, 0, 4_000, t => State(t, 0.9, [Vurus], three))));   // 3 < 5: not worth it
        var (pressed, _) = Ready();
        Assert.Contains(Keys.F2, Taps(Run(pressed, 0, 4_000, t => State(t, 0.4, [Vurus], three))));
    }

    [Fact]
    public void An_area_skill_counts_mobs_as_far_as_it_reaches()
    {
        // Five mobs within 4 m, two of them beyond 3 m: the 4 m skill hits them all.
        TargetInfo[] five = [Mob(1, 80), Mob(2, 150), Mob(3, 250), Mob(4, 340), Mob(5, 380)];
        var (brain, _) = Ready();
        Assert.Contains(Keys.F2, Taps(Run(brain, 0, 4_000, t => State(t, 0.9, [Vurus], five))));
        var (near, _) = Ready();
        var short3m = Vurus with { Range = 300 };
        Assert.DoesNotContain(Keys.F2, Taps(Run(near, 0, 4_000, t => State(t, 0.9, [short3m], five))));
    }

    [Fact]
    public void A_healing_skill_only_when_hp_is_low()
    {
        var heal = new SkillSlot(Keys.F4, "İyileştirme", SkillKind.Heal, 50, 10, 0);
        var (brain, _) = Ready();
        Assert.DoesNotContain(Keys.F4, Taps(Run(brain, 0, 3_000, t => State(t, 0.9, [heal], [Mob(1, 80)]))));
        Assert.Contains(Keys.F4, Taps(Run(brain, 3_000, 6_000, t => State(t, 0.5, [heal], [Mob(1, 80)]))));
    }

    [Fact]
    public void What_each_skill_does_is_written_down_once_with_the_ones_off_the_slots()
    {
        var (brain, _) = Ready();
        var notes = Run(brain, 0, 3_000, t => State(t, 1, [Beden, Vurus, Ruh], [], ["Kılıç darbesi"]))
            .OfType<NoteAction>().Select(n => n.Text).ToList();
        var told = Assert.Single(notes, n => n.StartsWith("Beceriler öğrenildi"));
        Assert.Contains("Güçlü beden (Sv 17): güçlendirme, 184 sn sürer, 94 sn'de bir, 159 MP, yavaşlatır", told);
        Assert.Contains("Güçlü vuruş (Sv 10): çevreye alan saldırısı, 4 m, bayıltır", told);
        Assert.Contains("Ruh vuruşu (Sv 1): öndekilere saldırı, 1.7 m", told.Replace(',', '.').Replace(". ", ", "));
        Assert.Single(notes, n => n.Contains("Kılıç darbesi") && n.Contains("slot"));
    }

    [Fact]
    public void After_dying_the_buff_is_put_on_again_at_once()
    {
        var buff = Beden with { Traits = SkillTraits.None };
        var (brain, _) = Ready();
        Assert.Contains(Keys.F1, Taps(Run(brain, 0, 3_000, t => State(t, 1, [buff], []))));
        Run(brain, 3_000, 8_000, t => State(t, 0, [buff], []) with { Dead = true, ReviveReady = true });
        Assert.Contains(Keys.F1, Taps(Run(brain, 8_000, 20_000, t => State(t, 1, [buff], []))));
    }
}
