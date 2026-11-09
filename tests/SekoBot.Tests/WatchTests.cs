using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Update;
using Xunit;

namespace SekoBot.Tests;

public class RespawnTrackerTests
{
    private static readonly Vec3 Me = new(13000, 10000, 1800);

    private static WorldMob Mob(int id, double x, int level = 19) =>
        new(id, 112, "Siyah Ayı", level, 624, 624, new Vec3(Me.X + x, Me.Y, Me.Z - 60), Exp: 384);

    private static WorldSnapshot World(params WorldMob[] mobs) => new(Me, null, mobs, 0, "c2");

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Measures_the_wait_from_a_death_to_a_new_mob_at_the_same_spot()
    {
        var tracker = new RespawnTracker();
        tracker.Update(World(Mob(1, 500), Mob(2, 900)), S(0));
        tracker.Update(World(Mob(1, 500), Mob(2, 900)), S(20));
        tracker.Update(World(Mob(2, 900)), S(21));                         // 1 died 5 m away
        tracker.Update(World(Mob(2, 900), Mob(7, 700)), S(66));            // a new one 2 m from it: 45 s
        tracker.Update(World(), S(70));                                    // 2 died
        tracker.Update(World(Mob(8, 950)), S(121));                        // 51 s
        Assert.Equal([45.0, 51.0], tracker.Samples[19]);
        Assert.Equal(51, tracker.Seconds(19));
        Assert.Equal(51, tracker.Seconds(21));     // nearest measured level
        Assert.Null(tracker.Seconds(30));          // nothing near
    }

    [Fact]
    public void Does_not_count_mobs_seen_at_the_start_or_far_off()
    {
        var tracker = new RespawnTracker();
        tracker.Update(World(Mob(1, 500)), S(0));
        tracker.Update(World(Mob(1, 500), Mob(2, 600)), S(5));             // first look around: not a spawn
        tracker.Update(World(Mob(2, 600)), S(20));                         // 1 died
        tracker.Update(World(Mob(2, 600), Mob(3, 3900)), S(60));           // new, but 39 m off (walked into view)
        Assert.Empty(tracker.Samples);
    }

    [Fact]
    public void A_wait_is_not_known_when_we_walked_away_meanwhile()
    {
        var tracker = new RespawnTracker();
        tracker.Update(World(Mob(1, 500)), S(0));
        tracker.Update(World(), S(20));                                    // died
        var far = new WorldSnapshot(Me with { X = Me.X + 5000 }, null, [], 0, "c2");
        tracker.Update(far, S(30));                                        // 50 m away
        tracker.Update(World(Mob(9, 600)), S(90));                         // back: a mob there, but when did it come?
        Assert.Empty(tracker.Samples);
    }
}

public class PlayerWatchTests
{
    private static readonly Vec3 Me = new(13000, 10000, 1800);
    private static readonly OtherPlayer Rival = new("Rakip", false, new Vec3(13800, 10000, 1800));

    [Fact]
    public void Moves_when_a_player_stays_and_the_experience_drops()
    {
        var watch = new PlayerWatch();
        long exp = 0;
        string? reason = null;
        // 5 min alone: 6000 EXP a minute.
        for (int s = 0; s <= 300; s += 5, exp += 500)
            Assert.Null(watch.Update(TimeSpan.FromSeconds(s), exp, Me, [], 3, 40));
        // A player comes and stays; we get only 2000 a minute.
        for (int s = 305; s <= 520 && reason == null; s += 5, exp += 166)
            reason = watch.Update(TimeSpan.FromSeconds(s), exp, Me, [Rival], 3, 40);
        Assert.NotNull(reason);
        Assert.Contains("Rakip", reason);
    }

    [Fact]
    public void Stays_when_the_experience_holds_or_the_player_only_passes()
    {
        var watch = new PlayerWatch();
        long exp = 0;
        for (int s = 0; s <= 300; s += 5, exp += 500)
            watch.Update(TimeSpan.FromSeconds(s), exp, Me, [], 3, 40);
        // The player stays 5 min but the experience is the same: nothing.
        for (int s = 305; s <= 600; s += 5, exp += 480)
            Assert.Null(watch.Update(TimeSpan.FromSeconds(s), exp, Me, [Rival], 3, 40));

        var passing = new PlayerWatch();
        exp = 0;
        for (int s = 0; s <= 300; s += 5, exp += 500)
            passing.Update(TimeSpan.FromSeconds(s), exp, Me, [], 3, 40);
        // Someone near for one minute only (then gone), little experience meanwhile: nothing.
        for (int s = 305; s <= 700; s += 5, exp += 100)
            Assert.Null(passing.Update(TimeSpan.FromSeconds(s), exp, Me, s < 365 ? [Rival] : [], 3, 40));
    }

    [Fact]
    public void Off_with_zero_minutes()
    {
        var watch = new PlayerWatch();
        long exp = 0;
        for (int s = 0; s <= 900; s += 5, exp += s < 300 ? 500 : 10)
            Assert.Null(watch.Update(TimeSpan.FromSeconds(s), exp, Me, s >= 300 ? [Rival] : [], 0, 40));
    }
}

public class MeasuredRespawnSpotTests
{
    private static readonly Vec3 Start = new(20000, 18000, 900);
    private static int _id;
    private static WorldMob Black(double x, double y) => new(++_id, 112, "Siyah Ayı", 19, 624, 624, new Vec3(x, y, 900), Exp: 384);
    private static WorldMob Boar(double x, double y) => new(++_id, 101, "Aç Yaban Domuzu", 7, 198, 198, new Vec3(x, y, 900), Exp: 61);

    [Fact]
    public void A_slow_respawn_counts_against_a_place_and_players_keep_it_out()
    {
        var mobs = new List<WorldMob>();
        for (int i = 0; i < 12; i++)
            mobs.Add(Boar(20000 + (i % 4) * 300, 18000 + (i / 4) * 300));
        mobs.AddRange([Black(24000, 18000), Black(24200, 18100), Black(23900, 17800), Black(24100, 18300)]);
        var p = new BotProfile();
        // Black bears come back in 60 s (estimate): they win.
        Assert.InRange(FarmSpotFinder.Best(mobs, p, Start, "c2", 189)!.X, 23000, 25000);
        // Measured: level 19 takes 10 minutes, level 7 30 s: the boars win.
        double? Wait(int level) => level >= 15 ? 600 : 30;
        Assert.InRange(FarmSpotFinder.Best(mobs, p, Start, "c2", 189, respawn: Wait)!.X, 19000, 21500);
        // A player at the bears: not there.
        Assert.InRange(FarmSpotFinder.Best(mobs, p, Start, "c2", 189, allowed: v => v.DistanceXY(new Vec3(24000, 18000, 900)) > 2500)!.X, 19000, 21500);
    }
}

public class TelegramCommandTests
{
    [Theory]
    [InlineData("durum", "durum")]
    [InlineData("/Durum", "durum")]
    [InlineData("  DUR ", "dur")]
    [InlineData("Başla", "basla")]
    [InlineData("BAŞLA", "basla")]
    [InlineData("/start@SekoBot", "start")]
    public void Commands_are_understood_however_they_are_written(string text, string command) =>
        Assert.Equal(command, TelegramNotifier.Command(text));
}
