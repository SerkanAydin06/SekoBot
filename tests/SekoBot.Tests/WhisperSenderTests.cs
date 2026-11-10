using SekoBot;
using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class WhisperSenderTests
{
    // As read in the game: the "Mesaj" button on the left, two conversations, the text box and "Gönder".
    private static readonly Point Opener = new(95, 216);
    private static readonly DmPerson Seta = new("setaaa", new Rectangle(6, 167, 129, 31));
    private static readonly DmPerson Oxy = new("OxyyGeN", new Rectangle(6, 198, 129, 31));
    private static readonly Rectangle Box = new(139, 298, 214, 16), Send = new(360, 298, 60, 16);

    private static DmLayout Closed => new(false, Opener, [], Rectangle.Empty, Rectangle.Empty, []);
    private static DmLayout Open(params DmShown[] shown) => new(true, Opener, [Seta, Oxy], Box, Send, shown);
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Opens_the_window_picks_the_player_types_sends_checks_and_closes_it_again()
    {
        var sender = new WhisperSender();
        sender.Start(S(0), "oxyygen", "selam, birazdan dönerim");
        Assert.Equal([new ClickAction(Opener, Slow: true)], sender.Tick(S(0), Closed));
        Assert.Equal([new ClickAction(new Point(70, 213), Slow: true)], sender.Tick(S(2), Open()));      // the conversation
        Assert.Equal([new ClickAction(new Point(246, 306), Slow: true)], sender.Tick(S(4), Open()));     // the text box
        Assert.Equal([new TypeTextAction("selam, birazdan dönerim")], sender.Tick(S(6), Open()));
        Assert.Equal([new ClickAction(new Point(390, 306), Slow: true)], sender.Tick(S(9), Open()));     // Gönder
        // It shows up as ours: close the window (we opened it).
        Assert.Equal([new ClickAction(Opener, Slow: true)], sender.Tick(S(11), Open(new DmShown("KAiZER06", "selam, birazdan dönerim", true))));
        Assert.Empty(sender.Tick(S(13), Closed));
        Assert.False(sender.Active);
        Assert.True(sender.Sent);
        Assert.Null(sender.Problem);
    }

    [Fact]
    public void A_player_without_a_conversation_is_not_written_to()
    {
        var sender = new WhisperSender();
        sender.Start(S(0), "Bilinmeyen", "merhaba");
        sender.Tick(S(0), Open());
        Assert.False(sender.Sent);
        Assert.Contains("konuşma yok", sender.Problem);
        Assert.DoesNotContain(sender.Tick(S(1), Open()), a => a is TypeTextAction);
    }

    [Theory]
    [InlineData("Ali merhaba nasılsın", "Ali", "merhaba nasılsın")]
    [InlineData("Ali: geliyorum", "Ali", "geliyorum")]
    public void Commands_are_read(string text, string to, string message) =>
        Assert.Equal((to, message), WhisperSender.Parse(text));

    [Fact]
    public void The_player_is_taken_from_the_whisper_notice_answered()
    {
        Assert.Equal("Ali", WhisperSender.WhisperFrom("SekoBot · Mobile2 Global\n💬 Fısıltı — Ali (Sv 40): bot musun?"));
        Assert.Equal("OxyyGeN", WhisperSender.WhisperFrom("💬 Fısıltı — OxyyGeN: yeni mesaj yazdı"));
        Assert.Null(WhisperSender.WhisperFrom("⏹ Bot durdu"));
        Assert.Null(WhisperSender.Parse("Ali"));
    }

    [Fact]
    public void A_new_message_whose_text_cannot_be_read_is_still_told_but_not_our_own_answer()
    {
        var watch = new SocialWatch();
        var p = new BotProfile();
        var now = new DateTime(2026, 11, 10, 1, 0, 0);
        SocialSnapshot Social(params WhisperThread[] threads) => new("KAiZER06", threads, [], [], [], [], 0, 0);
        watch.Update(Social(new WhisperThread("OxyyGeN", 1, [], 67)), p, 20, now);
        var notice = Assert.Single(watch.Update(Social(new WhisperThread("OxyyGeN", 2, [], 67)), p, 20, now));
        Assert.Equal("💬 Fısıltı — OxyyGeN (Sv 67): yeni mesaj yazdı (oyunda mesaj penceresinde).", notice.Text);
        watch.ExpectOwn("oxyygen", now);
        Assert.Empty(watch.Update(Social(new WhisperThread("OxyyGeN", 3, [], 67)), p, 20, now.AddSeconds(20)));   // our answer
        Assert.Single(watch.Update(Social(new WhisperThread("OxyyGeN", 4, [], 67)), p, 20, now.AddSeconds(40))); // their reply
    }
}

public class InstanceTests
{
    [Fact]
    public void The_first_sekobot_keeps_the_old_file_names() =>
        Assert.Equal("profile.json", Instance.FileName("profile", "json"));

    [Fact]
    public void A_game_marked_by_another_sekobot_is_taken_and_ours_is_not()
    {
        uint fake = 4_000_000_000u - (uint)Environment.ProcessId;
        using (new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\SekoBot-game-{fake}"))
            Assert.True(Instance.GameTaken(fake));       // another SekoBot holds it
        Assert.False(Instance.GameTaken(fake));          // let go
        Instance.TakeGame(fake);
        try { Assert.False(Instance.GameTaken(fake)); }  // ours
        finally { Instance.LeaveGame(); }
    }
}
