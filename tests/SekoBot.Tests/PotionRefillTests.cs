using System.Drawing;
using System.Windows.Forms;
using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>An empty potion key filled: the bag opened, the potion dragged onto the key, checked, the bag closed.</summary>
public class PotionRefillTests
{
    private static readonly KeyBindings Keys = new();
    private static readonly List<Rectangle?> Quick = [new Rectangle(500, 900, 40, 40), new Rectangle(545, 900, 40, 40)];

    private static BagLayout Bag(bool open, int page = 0, params BagSlotView[] items) =>
        new(open, new Rectangle(1200, 300, 300, 500), open ? page : -1, [new Point(1250, 320), new Point(1300, 320)], items, null,
            PageButtonBounds: [new Rectangle(1240, 310, 20, 20), new Rectangle(1290, 310, 20, 20)]);

    private static TimeSpan T(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void The_potion_is_dragged_onto_the_empty_key_and_the_bag_closed()
    {
        var r = new PotionRefill();
        r.Start(T(0), "HP", 1, 27002);
        var a = r.Tick(T(0), Bag(false), Quick, null, Keys);
        Assert.Equal(Keys.Inventory, Assert.IsType<KeyTapAction>(Assert.Single(a)).Key);
        var potion = new BagSlotView(40, 27002, 200, 0, new Rectangle(1300, 500, 40, 40));
        a = r.Tick(T(1), Bag(true, 0, potion), Quick, null, Keys);
        var drag = Assert.IsType<MouseDragAction>(Assert.Single(a));
        Assert.Equal(new Point(1320, 520), drag.From);
        Assert.Equal(new Point(520, 920), drag.To);
        // The key shows the potion: done, the bag closed.
        a = r.Tick(T(3), Bag(true, 0, potion), Quick, 27002, Keys);
        Assert.Equal(Keys.Inventory, Assert.IsType<KeyTapAction>(Assert.Single(a)).Key);
        a = r.Tick(T(4), Bag(false), Quick, 27002, Keys);
        Assert.Empty(a);
        Assert.False(r.Active);
        Assert.True(r.Succeeded);
    }

    [Fact]
    public void The_page_holding_the_potion_is_opened_first_and_a_drag_that_doesnt_take_is_tried_once_more()
    {
        var r = new PotionRefill();
        r.Start(T(0), "MP", 2, 27005);
        var onPage2 = new BagSlotView(60, 27005, 100, 1, new Rectangle(1300, 600, 40, 40));
        var a = r.Tick(T(0), Bag(true, 0, onPage2 with { Rect = Rectangle.Empty }), Quick, null, Keys);
        Assert.IsType<ClickAction>(Assert.Single(a)); // the page button
        a = r.Tick(T(1), Bag(true, 1, onPage2), Quick, null, Keys);
        Assert.Equal(new Point(565, 920), Assert.IsType<MouseDragAction>(Assert.Single(a)).To);
        a = r.Tick(T(3), Bag(true, 1, onPage2), Quick, null, Keys);
        Assert.IsType<MouseDragAction>(Assert.Single(a));       // once more
        Assert.Empty(r.Tick(T(5), Bag(true, 1, onPage2), Quick, null, Keys)); // given up
        a = r.Tick(T(6), Bag(true, 1, onPage2), Quick, null, Keys);
        Assert.IsType<KeyTapAction>(Assert.Single(a));          // the bag closed
        Assert.Equal("iksir hızlı slota sürüklenemedi", r.Problem);
    }

    [Fact]
    public void Potions_are_known_by_their_names_and_keys_by_their_numbers()
    {
        Assert.True(BotEngine.PotionOfKind("Kırmızı İksir(K)", "HP"));
        Assert.True(BotEngine.PotionOfKind("Mavi İksir(B)", "MP"));
        Assert.False(BotEngine.PotionOfKind("Mavi İksir(B)", "HP"));
        Assert.Equal(1, BotEngine.QuickNumber(System.Windows.Forms.Keys.D1));
        Assert.Equal(10, BotEngine.QuickNumber(System.Windows.Forms.Keys.D0));
        Assert.Equal(0, BotEngine.QuickNumber(System.Windows.Forms.Keys.F1));
    }
}
