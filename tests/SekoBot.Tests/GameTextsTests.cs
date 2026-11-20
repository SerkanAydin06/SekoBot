using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>The game's own "SYSTEM" messages: text numbers with values, put together as the game shows them.</summary>
public class GameTextsTests
{
    // As read from the game's text table (m2data.Locale) on 2026-11-20.
    private static readonly Dictionary<int, string> Texts = new()
    {
        [100515] = "Gaya Pazarı'nda satışa sunduğun %s eşyası satıldı.",
        [100516] = "Seviye Atladın!",
        [100517] = "Tebrikler! Karakterin %s. seviyeye ulaştı.",
    };

    private static string Decode(string raw) => GameTexts.Decode(raw, id => Texts.GetValueOrDefault(id));

    [Fact]
    public void The_level_up_message_is_put_into_words()
    {
        // Seen on Telegram as "💬 Fısıltı — SYSTEM: 100516||||100517||21||1".
        Assert.Equal("Seviye Atladın! Tebrikler! Karakterin 21. seviyeye ulaştı.", Decode("100516||||100517||21||1"));
    }

    [Fact]
    public void A_value_fills_its_text()
    {
        Assert.Equal("Gaya Pazarı'nda satışa sunduğun Kılıç+9 eşyası satıldı.", Decode("100515||Kılıç+9"));
    }

    [Fact]
    public void A_player_message_stays_as_it_is()
    {
        Assert.Equal("selam || nasılsın", Decode("selam || nasılsın"));
        Assert.Equal("123 yang ver", Decode("123 yang ver"));
        Assert.Equal("999999||abc", Decode("999999||abc")); // no such text: left alone
    }

    [Fact]
    public void Sold_items_are_counted_apart_from_thrown_ones()
    {
        var run = new SessionItems();
        run.Picked(700, 3);
        run.Thrown(700, 1);
        run.Sold(700, 2);
        Assert.Equal([(700, 3, 1, 2)], run.Rows().Select(r => (r.Vnum, r.Picked, r.Thrown, r.Sold)));
    }

    [Fact]
    public void The_game_writing_is_told_apart()
    {
        Assert.True(SocialWatch.IsGame("SYSTEM"));
        Assert.True(SocialWatch.IsGame(" system "));
        Assert.False(SocialWatch.IsGame("Ali"));
    }
}
