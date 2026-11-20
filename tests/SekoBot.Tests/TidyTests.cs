using System.Drawing;
using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Before the turn goes to another bot, nothing of a bag emptying or a sale may stay on the screen.</summary>
public class TidyTests
{
    private static BagLayout Bag(bool open = false, Point? confirm = null, Point? other = null, int shop = 0) =>
        new(open, Rectangle.Empty, 0, [], [], confirm, NpcShop: shop, OtherConfirm: other);

    [Fact]
    public void A_question_the_bag_or_a_shop_left_open_is_seen()
    {
        Assert.False(BotEngine.LeftOpen(Bag()));
        Assert.True(BotEngine.LeftOpen(Bag(confirm: new Point(1, 1))));   // seen: the last item's "throw it away?"
        Assert.True(BotEngine.LeftOpen(Bag(other: new Point(1, 1))));
        Assert.True(BotEngine.LeftOpen(Bag(open: true)));
        Assert.True(BotEngine.LeftOpen(Bag(shop: 9001)));
    }

    [Fact]
    public void The_throw_question_is_answered_yes_the_rest_closed()
    {
        // Since the game's update the question doesn't close with Esc (seen: 4 tries, open for 20 s): "Evet" —
        // it was asked for an item the bot meant to throw anyway.
        var p = new BotProfile();
        Assert.Equal(new ClickAction(new Point(5, 6), Slow: true, Fast: true), BotEngine.TidyAction(Bag(open: true, confirm: new Point(5, 6)), p));
        // At a shop while selling: "sell it?" answered yes too; away from one, another question is closed.
        Assert.Equal(new ClickAction(new Point(7, 8), Slow: true, Fast: true), BotEngine.TidyAction(Bag(other: new Point(7, 8), shop: 9001), p, selling: true));
        Assert.Equal(new KeyTapAction(p.Keys.CloseWindow), BotEngine.TidyAction(Bag(other: new Point(7, 8)), p));
        Assert.Equal(new KeyTapAction(p.Keys.CloseWindow), BotEngine.TidyAction(Bag(open: true, shop: 9001), p));
        Assert.Equal(new KeyTapAction(p.Keys.Inventory), BotEngine.TidyAction(Bag(open: true), p));
    }

    [Fact]
    public void An_item_outside_a_small_game_window_is_not_clicked()
    {
        // A game arranged 636×358 cuts the bag off at the bottom (seen: clicks beside the items).
        var client = new Size(636, 358);
        Assert.True(BagCleaner.Inside(new Rectangle(500, 300, 40, 40), client));
        Assert.False(BagCleaner.Inside(new Rectangle(500, 350, 40, 40), client));   // its middle under the edge
        Assert.False(BagCleaner.Inside(new Rectangle(630, 100, 40, 40), client));   // off the right
        Assert.True(BagCleaner.Inside(new Rectangle(630, 100, 40, 40), Size.Empty)); // size unknown: tried
    }
}
