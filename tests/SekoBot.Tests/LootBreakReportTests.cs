using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class LootFilterTests
{
    [Theory]
    [InlineData(1, "Yang")]          // seen in the game: yang is item 1
    [InlineData(27003, "Potion")]
    [InlineData(27201, "Potion")]
    [InlineData(3014, "Equipment")]  // two-handed weapon
    [InlineData(11610, "Equipment")] // armour (seen on the ground)
    [InlineData(17026, "Equipment")] // earring
    [InlineData(30028, "Other")]     // material
    [InlineData(50300, "Other")]     // skill book
    [InlineData(0, "Unknown")]
    public void Items_are_sorted_by_their_number(int vnum, string kind) => Assert.Equal(kind, Loot.KindOf(vnum).ToString());

    [Fact]
    public void Only_wanted_kinds_are_walked_to()
    {
        var p = new BotProfile(); // yang, potions, other: yes; equipment: no
        GroundItem Item(int vnum) => new(1, new Vec3(0, 0, 0), vnum, 1);
        Assert.True(BotEngine.WantedLoot(Item(1), p));
        Assert.True(BotEngine.WantedLoot(Item(27003), p));
        Assert.False(BotEngine.WantedLoot(Item(11610), p));
        Assert.True(BotEngine.WantedLoot(Item(30028), p));
        Assert.True(BotEngine.WantedLoot(Item(0), p)); // not known yet: may be anything
        p.LootYang = false;
        Assert.False(BotEngine.WantedLoot(Item(1), p));
    }
}

public class BreakTests
{
    private static GameState At(double ms, bool attacked = false) =>
        new(TimeSpan.FromMilliseconds(ms), 1920, 1080, 1, 1, 0, 1)
        {
            Walking = true,
            Attackers = attacked ? [new Point(1000, 540)] : [],
        };

    [Fact]
    public void Takes_a_short_break_now_and_then_and_a_mob_ends_it()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], BreakEveryMin = 1, BreakSeconds = 40 };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var actions = new List<BotAction>();
        for (double t = 0; t < 80_000; t += 100)
            actions.AddRange(brain.Tick(At(t)));
        Assert.Contains(actions, a => a is NoteAction n && n.Text.StartsWith("Kısa mola"));

        // A break in progress ends when something attacks.
        var p2 = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], BreakEveryMin = 1, BreakSeconds = 600 };
        var brain2 = new Brain(() => p2);
        brain2.Start(TimeSpan.Zero);
        var during = new List<BotAction>();
        for (double t = 0; t < 80_000; t += 100)
            during.AddRange(brain2.Tick(At(t)));
        Assert.Contains(during, a => a is NoteAction n && n.Text.StartsWith("Kısa mola"));
        var hit = brain2.Tick(At(80_000, attacked: true));
        Assert.Contains(hit, a => a is NoteAction n && n.Text.Contains("saldırıyor"));
    }

    [Fact]
    public void No_breaks_when_switched_off()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], BreakEveryMin = 1, Breaks = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var actions = new List<BotAction>();
        for (double t = 0; t < 120_000; t += 100)
            actions.AddRange(brain.Tick(At(t)));
        Assert.DoesNotContain(actions, a => a is NoteAction n && n.Text.StartsWith("Kısa mola"));
    }
}

public class SessionLogTests
{
    [Fact]
    public void A_run_becomes_one_line_and_a_short_summary()
    {
        var report = new SessionReport(Kills: 310, Exp: 12345, LevelsGained: 1, Yang: 4200, Level: 13, ExpPercent: 4.2,
            ToLevel: null, HpPotionsUsed: 12, MpPotionsUsed: 3, HpPotionsLeft: 300, MpPotionsLeft: 100);
        var start = new DateTime(2026, 11, 9, 14, 0, 0);
        string line = SessionLog.Line(start, start.AddMinutes(30), "c2", report, deaths: 1, reason: "envanter dolu; test");
        var cells = line.Split(';');
        Assert.Equal(15, cells.Length);
        Assert.Equal("30", cells[2]);
        Assert.Equal("310", cells[4]);
        Assert.Equal("620", cells[5]);      // per hour
        Assert.Equal("24690", cells[7]);    // EXP per hour
        Assert.Equal("envanter dolu, test", cells[14]); // ';' can't break the columns

        string summary = SessionLog.Summary(TimeSpan.FromMinutes(30), report, 1);
        Assert.Contains("310 mob", summary);
        Assert.Contains("+1 seviye", summary);
        Assert.Contains("15 iksir", summary);
    }
}
