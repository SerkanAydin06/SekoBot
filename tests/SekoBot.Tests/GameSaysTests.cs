using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

/// <summary>The game's own (yellow) chat lines: someone else's item, "WAIT!", the server's announcements.</summary>
public class GameSaysTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 20, 30, 0);
    private static readonly Point Center = new(960, 540);

    private static SocialSnapshot Social(ChatLine[]? chat = null, string announcement = "") =>
        new("KAiZER06", [], [], [], chat ?? [], [], 0, 0, announcement);

    [Fact]
    public void The_games_own_lines_are_counted_and_players_writing_the_same_are_not()
    {
        var watch = new SocialWatch();
        var p = new BotProfile();
        watch.Update(Social(), p, 28, Now);
        watch.Update(Social(
        [
            new(-1, "", 0, "Kazanmadığın için bu nesneyi toplayamazsın."),
            new(0, "", 0, "WAIT!"),
            new(0, "", 0, "WAIT!"),
            new(-1, "", 0, "92 Yang Kazandın."),
            new(12, "Mert", 30, "WAIT!"),                                        // a player: not the game
            new(-5, "Ece", 40, "! toplayamazsın diyor"),
        ]), p, 28, Now);
        Assert.Equal(1, watch.NotOurs);
        Assert.Equal(2, watch.SkillWaits);
        watch.Update(Social(), p, 28, Now);
        Assert.Equal(0, watch.NotOurs + watch.SkillWaits);
    }

    [Fact]
    public void A_new_announcement_is_told_once()
    {
        var watch = new SocialWatch();
        var p = new BotProfile();
        Assert.Empty(watch.Update(Social(announcement: "Eski duyuru"), p, 28, Now));          // there before: not told
        Assert.Equal("📢 Duyuru: Bakım saat 05:00'te.", Assert.Single(watch.Update(Social(announcement: "Bakım saat 05:00'te."), p, 28, Now)).Text);
        Assert.Empty(watch.Update(Social(announcement: "Bakım saat 05:00'te."), p, 28, Now));
        Assert.Empty(watch.Update(Social(announcement: "Eski duyuru"), p, 28, Now));          // repeated by the server
        Assert.Empty(watch.Update(Social(announcement: "Bakım saat 05:00'te."), p, 28, Now));
    }

    private static List<BotAction> Loot(Brain brain, double from, double to, GroundSpot[] drops)
    {
        var all = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            all.AddRange(brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), 1920, 1080, 1, 1, 0, 1)
                { Drops = drops, Walking = true, AttackPower = 189 }));
        return all;
    }

    [Fact]
    public void An_item_the_game_says_is_someone_elses_is_left_at_once()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PickUpDrops = true, PullGroups = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        GroundSpot[] drops = [new(7, new Point(Center.X + 10, Center.Y + 5)), new(8, new Point(Center.X - 200, Center.Y))];
        var first = Loot(brain, 0, 1000, drops);
        Assert.Contains(first, a => a is KeyTapAction k && k.Key == p.Keys.Pickup);        // trying the one at our feet
        brain.GameSaid(notOurs: 1, skillWaits: 0);
        var after = Loot(brain, 1000, 3000, drops);
        Assert.Contains(after, a => a is NoteAction n && n.Text.Contains("başkasının"));
        Assert.Contains(after, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);      // on to the other item
    }

    [Fact]
    public void Our_own_item_out_of_reach_is_not_called_someone_elses()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PickUpDrops = true, PullGroups = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var all = Loot(brain, 0, Brain.DropGiveUpMs + 2000, [new(7, new Point(Center.X - 300, Center.Y))]);
        Assert.DoesNotContain(all, a => a is NoteAction n && n.Text.Contains("başkasının"));
        Assert.Contains(all, a => a is NoteAction n && n.Text.Contains("bir kez daha denenecek"));
    }

    [Fact]
    public void An_item_not_reached_twice_is_left_for_good_and_so_is_its_spot()
    {
        // Seen: an item in a tree, the bot back at it every 20 s, stuck on the tree.
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false, PickUpDrops = true, PullGroups = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var tree = new Vec3(5000, 5000, 0);
        GroundSpot[] inTree = [new(7, new Point(Center.X - 300, Center.Y), At: tree)];
        double end = Brain.DropGiveUpMs + 2000 + Brain.DropRetryMs + Brain.DropGiveUpMs + 2000;
        var all = Loot(brain, 0, end, inTree);
        Assert.Contains(all, a => a is NoteAction n && n.Text.Contains("iki kez ulaşılamadı"));
        // Left for good: no more going for it.
        var later = Loot(brain, end, end + 60_000, inTree);
        Assert.DoesNotContain(later, a => a is NoteAction n && n.Text.Contains("ulaşılamadı"));
        // Another item fallen into the same tree is left at once.
        var another = Loot(brain, end + 60_000, end + 70_000, [new(8, new Point(Center.X - 300, Center.Y), At: tree with { X = tree.X + 50 })]);
        Assert.DoesNotContain(another, a => a is KeyTapAction k && k.Key == p.Keys.Pickup);
    }

    [Fact]
    public void After_wait_the_skill_is_pressed_less_often()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = [5, 0, 0, 0, 0] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var taps = new List<double>();
        for (double t = 0; t < 30_000; t += 100)
        {
            if (brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), 1920, 1080, 1, 1, null, 1)).OfType<KeyTapAction>().Any(k => k.Key == Keys.F1))
            {
                taps.Add(t);
                if (taps.Count == 1)
                    brain.GameSaid(notOurs: 0, skillWaits: 1);   // the game said the first press was too soon
            }
        }
        Assert.True(taps.Count >= 3);
        Assert.All(taps.Zip(taps.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 5000 + Brain.SkillExtraStepMs - 100));
    }
}
