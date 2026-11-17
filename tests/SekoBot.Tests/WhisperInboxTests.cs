using SekoBot.Bot;
using Xunit;

namespace SekoBot.Tests;

/// <summary>A Telegram answer to IMEDUSA06's whisper is written from IMEDUSA06's game, not the first SekoBot's.</summary>
public class WhisperInboxTests
{
    [Fact]
    public void An_answer_goes_to_the_SekoBot_playing_that_character()
    {
        string old = WhisperInbox.Folder;
        string dir = Path.Combine(Path.GetTempPath(), "sekobot-inbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            WhisperInbox.Folder = dir;
            File.WriteAllText(Path.Combine(dir, "durum-1.txt"), "👤 KAiZER06\n▶ Bot çalışıyor");
            File.WriteAllText(Path.Combine(dir, "durum-2.txt"), "👤 IMEDUSA06 (2. SekoBot)\n▶ Bot çalışıyor");
            File.WriteAllText(Path.Combine(dir, "durum-3.txt"), "👤 UNiCORN06 (3. SekoBot)\n⏸ Bot duruyor");
            File.SetLastWriteTimeUtc(Path.Combine(dir, "durum-3.txt"), DateTime.UtcNow.AddMinutes(-5));   // closed long ago

            Assert.Equal("IMEDUSA06", WhisperInbox.CharacterOf("SekoBot · IMEDUSA06\n💬 Fısıltı — Ali (Sv 40): selam"));
            Assert.Null(WhisperInbox.CharacterOf("💬 Fısıltı — Ali: selam"));
            Assert.Equal(2, WhisperInbox.SekoBotOf("imedusa06"));
            Assert.Equal(1, WhisperInbox.SekoBotOf("KAiZER06"));
            Assert.Null(WhisperInbox.SekoBotOf("UNiCORN06"));                        // not open any more

            Assert.True(WhisperInbox.Send(2, "Ali", "selam, birazdan"));
            Assert.True(WhisperInbox.Send(2, "Veli", "tamam"));
            var letters = WhisperInbox.Take(2);
            Assert.Equal(["Ali", "Veli"], letters.Select(l => l.To));
            Assert.Equal("selam, birazdan", letters[0].Text);
            Assert.Empty(WhisperInbox.Take(2));                                     // taken once
            Assert.Empty(WhisperInbox.Take(3));
        }
        finally
        {
            WhisperInbox.Folder = old;
            Directory.Delete(dir, true);
        }
    }
}
