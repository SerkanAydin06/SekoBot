using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class SocialWatchTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 21, 30, 0);

    private static SocialSnapshot Social(WhisperThread[]? whispers = null, PlayerRequest[]? requests = null, ChatLine[]? chat = null,
        GameEvent[]? events = null, int stat = 0, GameQuestion[]? questions = null) =>
        new("KAiZER06", whispers ?? [], requests ?? [], questions ?? [], chat ?? [], events ?? [], stat, 0);

    [Fact]
    public void Only_new_private_messages_from_others_are_told()
    {
        var watch = new SocialWatch();
        var p = new BotProfile();
        // Already there when the bot started: not told.
        Assert.Empty(watch.Update(Social([new("Ali", 2, [new("Ali", 40, "eski mesaj"), new("KAiZER06", 20, "cevap")])]), p, 20, Now));
        var notices = watch.Update(Social([new("Ali", 4, [new("Ali", 40, "eski mesaj"), new("KAiZER06", 20, "cevap"),
            new("Ali", 40, "bot musun?"), new("KAiZER06", 20, "yok")]), new("Veli", 1, [new("Veli", 55, "selam")])]), p, 20, Now);
        Assert.Equal(["💬 Fısıltı — Ali (Sv 40): bot musun?", "💬 Fısıltı — Veli (Sv 55): selam"], notices.Select(n => n.Text));
        Assert.All(notices, n => Assert.True(n.Urgent));
        Assert.Empty(watch.Update(Social([new("Ali", 4, []), new("Veli", 1, [])]), p, 20, Now));   // nothing new
    }

    [Fact]
    public void Requests_and_questions_are_told_once_while_they_last()
    {
        var watch = new SocialWatch();
        var p = new BotProfile();
        watch.Update(Social(), p, 20, Now);
        var trade = new PlayerRequest(RequestKind.Trade, "Deniz", 33);
        Assert.Equal("🤝 Takas isteği: Deniz (Sv 33)", Assert.Single(watch.Update(Social(requests: [trade]), p, 20, Now)).Text);
        Assert.Empty(watch.Update(Social(requests: [trade]), p, 20, Now));
        watch.Update(Social(), p, 20, Now);                                                     // gone
        Assert.Single(watch.Update(Social(requests: [trade]), p, 20, Now));                     // asked again
        Assert.Contains("Oyun soruyor", Assert.Single(watch.Update(Social(questions: [new(7, "Gruba katılmak ister misin?")]), p, 20, Now)).Text);
        p.NotifyRequests = false;
        Assert.Empty(watch.Update(Social(requests: [new(RequestKind.Party, "Ece", 10)]), p, 20, Now));
    }

    [Fact]
    public void Our_name_or_a_gm_in_the_chat_is_told()
    {
        var watch = new SocialWatch();
        var p = new BotProfile();
        watch.Update(Social(), p, 20, Now);
        ChatLine[] chat =
        [
            new(-5, "Rynox", 55, "! diller günahkar"),
            new(12, "Mert", 30, "kaizer burada mısın"),
            new(-1, "", 0, "27 Yang Kazandın."),
            new(13, "KAiZER06", 20, "KAiZER06 burada"),           // our own line
            new(14, "[GM]Mira", 99, "Merhaba"),
            new(15, "Ece", 30, "bukaizerbeyden"),                 // inside another word: not us
        ];
        var notices = watch.Update(Social(chat: chat), p, 20, Now).Select(n => n.Text).ToList();
        Assert.Equal(["📣 Sohbette adın geçti — Mert (Sv 30): kaizer burada mısın", "⚠ GM sohbette yazdı — [GM]Mira: Merhaba"], notices);
    }

    [Fact]
    public void A_drop_event_for_our_level_is_told_and_its_item_picked_up()
    {
        var watch = new SocialWatch();
        var p = new BotProfile();
        GameEvent[] events =
        [
            new(1, 39028, 30, 30, true, Now.AddMinutes(-30), Now.AddMinutes(30)),   // level 30+: not for a level 20
            new(2, 33028, 500, 1, false, Now.AddMinutes(-10), Now.AddMinutes(50)),  // going on (by the clock)
            new(3, 50266, 500, 1, false, Now.AddMinutes(30), Now.AddMinutes(90)),   // later
        ];
        var notices = watch.Update(Social(events: events), p, 20, Now);
        Assert.Equal([33028], watch.ActiveEvents.Select(e => e.Item));
        Assert.Contains("Etkinlik", Assert.Single(notices).Text);
        Assert.Empty(watch.Update(Social(events: events), p, 20, Now));             // told once
        // Every event's reward is picked up, also later ones and those above our level.
        Assert.Equal([33028, 39028, 50266], watch.EventItems.Order());

        Loot.EventItems = [33028];
        try
        {
            Assert.True(BotEngine.AlwaysLoot(33028, p));
            p.EventLoot = false;
            Assert.False(BotEngine.AlwaysLoot(33028, p));
        }
        finally { Loot.EventItems = []; }
    }

    [Fact]
    public void Points_to_spend_are_told_when_they_grow()
    {
        var watch = new SocialWatch();
        var p = new BotProfile();
        Assert.Empty(watch.Update(Social(), p, 20, Now));
        Assert.Contains("3 statü", Assert.Single(watch.Update(Social(stat: 3), p, 21, Now)).Text);
        Assert.Empty(watch.Update(Social(stat: 3), p, 21, Now));
        Assert.Empty(watch.Update(Social(stat: 0), p, 21, Now));
    }
}

public class StunTests
{
    [Fact]
    public void Stunned_the_brain_waits_without_pressing_anything()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var center = new Point(960, 540);
        var actions = new List<BotAction>();
        for (double t = 0; t < 3000; t += 100)
            actions.AddRange(brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), 1920, 1080, 1, 1, 0, 1)
            {
                Stunned = true, Walking = false, MobCandidates = [new Point(center.X + 300, center.Y)],
            }));
        Assert.DoesNotContain(actions, a => a is KeyDownAction or KeyTapAction or MouseDragAction or ClickAction);
    }
}

public class PopupWatchTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void A_window_esc_does_not_close_is_let_be_after_a_few_seconds()
    {
        var watch = new PopupWatch();
        watch.Start(["chatBox", "miniMap"]);
        Assert.Empty(watch.Update(["chatBox", "miniMap"], S(0)));
        Assert.Equal(["someWindow_C"], watch.Update(["chatBox", "someWindow_C"], S(1)));
        Assert.Equal(["someWindow_C", "EscMenu"], watch.Update(["someWindow_C", "EscMenu"], S(4)));   // Esc opened the menu
        var givenUp = new List<string>();
        Assert.Equal(["EscMenu"], watch.Update(["someWindow_C", "EscMenu"], S(8.5), givenUp));       // let be; the menu still closed
        Assert.Equal(["someWindow_C"], givenUp);
        Assert.Empty(watch.Update(["someWindow_C"], S(30)));
        // The chat history never counts; known windows always do.
        Assert.Empty(watch.Update(["chatHistoryWindow_C"], S(31)));
        Assert.Equal(["popup_C"], watch.Update(["popup_C"], S(60)));
        Assert.Equal(["popup_C"], watch.Update(["popup_C"], S(90)));
    }
}
