using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class ChatHistoryTests
{
    private static Frame Filled(int w, int h, (byte R, byte G, byte B) color, Rectangle? panel = null, (byte R, byte G, byte B) panelColor = default)
    {
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = panel is Rectangle p && p.Contains(x, y) ? panelColor : color;
                int i = (y * w + x) * 4;
                data[i] = c.B;
                data[i + 1] = c.G;
                data[i + 2] = c.R;
                data[i + 3] = 255;
            }
        return Frame.FromArray(w, h, data);
    }

    // Colours from the game: the market's grass, and the chat history's dark blue panel.
    private static readonly Rectangle Panel = new(105, 31, 493, 333);

    [Fact]
    public void The_dark_blue_chat_history_is_seen_over_the_market()
    {
        using var open = Filled(702, 395, (29, 48, 0), Panel, (8, 20, 27));
        Assert.True(BotEngine.PanelShowing(open, Panel));
    }

    [Fact]
    public void Grass_is_not_the_chat_history()
    {
        using var closed = Filled(702, 395, (75, 99, 0));
        Assert.False(BotEngine.PanelShowing(closed, Panel));
    }

    [Fact]
    public void A_stall_under_the_chat_line_is_not_tapped()
    {
        var cover = new ScreenCover([new Rectangle(110, 333, 464, 40)]);
        Assert.True(cover.Covers(new Point(300, 350)));
        Assert.False(cover.Covers(new Point(300, 200)));
    }
}
