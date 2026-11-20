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
    public void A_question_is_closed_before_the_bag()
    {
        var p = new BotProfile();
        Assert.Equal(p.Keys.CloseWindow, BotEngine.TidyKey(Bag(open: true, confirm: new Point(1, 1)), p));
        Assert.Equal(p.Keys.CloseWindow, BotEngine.TidyKey(Bag(open: true, shop: 9001), p));
        Assert.Equal(p.Keys.Inventory, BotEngine.TidyKey(Bag(open: true), p));
    }
}
