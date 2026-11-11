using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class BagCleanerTests
{
    // As read in the game (1204×688 window): inventory on the right, page buttons, 42-px slots.
    private static readonly Size Client = new(1204, 688);
    private static readonly Rectangle Window = new(662, 20, 520, 599);
    private static readonly Point[] Pages = [new(962, 165), new(1022, 165), new(1082, 165), new(1141, 165)];
    private const int Junk = 7080, Keep = 27002;

    private static BagSlotView Item(int slot, int vnum, int page, bool shown) =>
        new(slot, vnum, 1, page, shown ? new Rectangle(926 + (slot - 11) % 6 * 42, 209 + (slot - 11) % 48 / 6 * 42, 42, 42) : Rectangle.Empty);

    private static BagLayout Bag(bool open, int page, BagSlotView[] items, Point? confirm = null) =>
        new(open, open ? Window : Rectangle.Empty, open ? page : -1, open ? Pages : [], items, confirm);

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Opens_the_bag_goes_to_the_page_drags_the_item_out_and_says_yes()
    {
        var p = new BotProfile();
        var cleaner = new BagCleaner();
        Func<BagSlotView, bool> toThrow = i => i.Vnum == Junk;
        cleaner.Start(S(0));
        var junkClosed = Item(104, Junk, 1, false);

        // Closed: the inventory key.
        Assert.Equal([new KeyTapAction(p.Keys.Inventory)], cleaner.Tick(S(0), Bag(false, 0, [Item(11, Keep, 0, false), junkClosed]), toThrow, p, Client));
        // Open on page 1, the item is on page 2: its button (second from the left).
        var page = cleaner.Tick(S(1.5), Bag(true, 0, [Item(11, Keep, 0, true), junkClosed]), toThrow, p, Client);
        Assert.Equal([new ClickAction(Pages[1], Slow: true)], page);
        // Page 2 showing: drag the item from its slot onto the ground left of the window.
        var shown = Item(104, Junk, 1, true);
        var drag = Assert.IsType<MouseDragAction>(Assert.Single(cleaner.Tick(S(3.5), Bag(true, 1, [Item(11, Keep, 0, false), shown]), toThrow, p, Client)));
        Assert.Equal(new Point(shown.Rect.X + 21, shown.Rect.Y + 21), drag.From);
        Assert.True(drag.To.X < Window.Left && drag.To.Y > 0 && drag.To.Y < Client.Height);
        // The question: "Evet".
        var yes = new Point(531, 389);
        Assert.Equal([new ClickAction(yes, Slow: true)], cleaner.Tick(S(6), Bag(true, 1, [shown], yes), toThrow, p, Client));
        // Gone: nothing else to throw, close the bag.
        Assert.Equal([new KeyTapAction(p.Keys.Inventory)], cleaner.Tick(S(8), Bag(true, 1, [Item(11, Keep, 0, false)]), toThrow, p, Client));
        Assert.Equal(1, cleaner.Thrown);
        Assert.True(cleaner.Active);
        Assert.Empty(cleaner.Tick(S(10), Bag(false, 0, [Item(11, Keep, 0, false)]), toThrow, p, Client));
        Assert.False(cleaner.Active);
        Assert.Null(cleaner.Problem);
    }

    [Fact]
    public void An_item_that_does_not_go_is_left_after_two_tries()
    {
        var p = new BotProfile();
        var cleaner = new BagCleaner();
        Func<BagSlotView, bool> toThrow = i => i.Vnum == Junk;
        var bag = Bag(true, 0, [Item(20, Junk, 0, true)]);
        cleaner.Start(S(0));
        var actions = new List<BotAction>();
        for (double t = 0; t < 30 && cleaner.Active; t += 0.5)
            actions.AddRange(cleaner.Tick(S(t), cleaner.Thrown == 0 && actions.OfType<KeyTapAction>().Any() ? Bag(false, 0, []) : bag, toThrow, p, Client));
        Assert.Equal(2, actions.OfType<MouseDragAction>().Count()); // no question came up either time
        Assert.DoesNotContain(actions, a => a is ClickAction);
        Assert.Equal(0, cleaner.Thrown);
        Assert.False(cleaner.Active);
        Assert.Contains("atılamadı", cleaner.Problem);
    }

    [Fact]
    public void A_question_it_did_not_ask_for_is_answered_no()
    {
        var p = new BotProfile();
        var cleaner = new BagCleaner();
        cleaner.Start(S(0));
        var actions = cleaner.Tick(S(0), Bag(true, 0, [Item(20, Junk, 0, true)], new Point(531, 389)), i => i.Vnum == Junk, p, Client);
        Assert.Equal([new KeyTapAction(p.Keys.CloseWindow)], actions);
    }

    [Fact]
    public void Attacked_closes_the_bag()
    {
        var p = new BotProfile();
        var cleaner = new BagCleaner();
        cleaner.Start(S(0));
        Assert.Equal([new KeyTapAction(p.Keys.Inventory)], cleaner.Abort(Bag(true, 0, []), p, "saldırı geldi"));
        Assert.False(cleaner.Active);
        Assert.Empty(new BagCleaner().Abort(Bag(false, 0, []), p, "saldırı geldi"));
    }

    [Fact]
    public void Lets_go_beside_the_window_on_the_wider_side()
    {
        Assert.Equal(new Point(331, 378), BagCleaner.DropPoint(Bag(true, 0, []), Client));
        var leftWindow = new BagLayout(true, new Rectangle(10, 20, 500, 600), 0, Pages, [], null);
        Assert.Equal(new Point(510 + 347, 378), BagCleaner.DropPoint(leftWindow, Client));
    }

    [Fact]
    public void Items_ticked_to_throw_are_never_picked_up()
    {
        ItemBook.Learn(new ItemInfo(95080, "Kalkan+0", "ITEM_ARMOR", "ARMOR_SHIELD", 100));
        var p = new BotProfile { LootEquipment = true, LootThrow = [95080] };
        Assert.False(BotEngine.WantedLoot(new GroundItem(1, new Vec3(0, 0, 0), 95080, 1), p));
        p.LootThrow = [];
        Assert.True(BotEngine.WantedLoot(new GroundItem(1, new Vec3(0, 0, 0), 95080, 1), p));
    }
}

public class CleanSpotTests
{
    private const int W = 1920, H = 1080;
    private static readonly Point Center = new(W / 2, H / 2);

    private static GameState State(double ms, Point[] mobs, Point[]? attackers = null) =>
        new(TimeSpan.FromMilliseconds(ms), W, H, 0.9, 1, 0, 1)
        {
            HpPotionsLeft = 100, MobCandidates = mobs, Attackers = attackers ?? [], Walking = true,
        };

    [Fact]
    public void Fights_what_attacks_then_gets_to_a_quiet_spot()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5], AutoSkills = false };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        brain.WantsClean = true;
        var right = new Point(Center.X + 120, Center.Y);

        // Hit by a mob: no quiet spot yet.
        for (double t = 0; t < 2000; t += 100)
            brain.Tick(State(t, [right], [right]));
        Assert.False(brain.ReadyToClean);

        // It is dead, another stands close: walk away from it (left), no attacking.
        var away = new List<BotAction>();
        for (double t = 2000; t < 3000; t += 100)
            away.AddRange(brain.Tick(State(t, [right])));
        Assert.Contains(away, a => a is NoteAction n && n.Text.Contains("sakin"));
        Assert.Contains(away, a => a is KeyDownAction d && d.Key == p.Keys.MoveLeft);
        Assert.False(brain.ReadyToClean);

        // Nothing near for 3 s: ready, standing still.
        var quiet = new List<BotAction>();
        for (double t = 3000; t < 6500; t += 100)
            quiet.AddRange(brain.Tick(State(t, [new Point(Center.X + 900, Center.Y)])));
        Assert.True(brain.ReadyToClean);
        Assert.DoesNotContain(quiet, a => a is StopAction);
        Assert.DoesNotContain(quiet, a => a is KeyDownAction d && d.Key == p.Keys.Attack);

        brain.WantsClean = false;
        Assert.False(brain.ReadyToClean);
    }
}

public class ThrowCheapEquipmentTests
{
    [Fact]
    public void Cheap_weapons_and_armour_the_bot_picked_up_are_thrown_the_users_own_stay()
    {
        ItemBook.Learn(new ItemInfo(96001, "Kılıç+0", "ITEM_WEAPON", "WEAPON_SWORD", 120));
        ItemBook.Learn(new ItemInfo(96002, "Tahta Kolye+0", "ITEM_ARMOR", "ARMOR_NECK", 300));
        ItemBook.Learn(new ItemInfo(96003, "Altın Kolye+0", "ITEM_ARMOR", "ARMOR_NECK", 900));
        ItemBook.Learn(new ItemInfo(96004, "Ayı Safrası", "ITEM_MATERIAL", "MATERIAL_LEATHER", 10));
        ItemBook.Learn(new ItemInfo(96005, "Olay Kılıcı", "ITEM_WEAPON", "WEAPON_SWORD", 0));
        // Picked up by the bot: slots 20–24 (slot 12 is a cheap sword of the user's own).
        HashSet<(int, int)> atStart = [(20, 96001), (21, 96002), (22, 96003), (23, 96004), (24, 96005)];
        InventoryItem[] bag =
        [
            new(4, 96001, 1),   // worn
            new(12, 96001, 1),  // the user's own
            new(20, 96001, 1), new(21, 96002, 1), new(22, 96003, 1), new(23, 96004, 1), new(24, 96005, 1),
        ];
        var p = new BotProfile { LootEquipment = false, LootEquipmentMinGold = 500 };
        int[] Thrown(IReadOnlySet<(int, int)>? start) => [.. bag.Where(i => BotEngine.ShouldThrow(i, p, start)).Select(i => i.Slot)];
        p.LootOther = true;                         // (the material in slot 23 wanted)
        Assert.Equal([20, 21], Thrown(atStart));
        Assert.Empty(Thrown(null));                 // not known what the bot picked: none thrown this way
        p.LootPick = [96002];                       // ticked "Topla": kept
        Assert.Equal([20], Thrown(atStart));
        p.LootEquipment = true;                     // all equipment wanted: nothing thrown
        Assert.Empty(Thrown(atStart));
        p.LootThrow = [96004];                      // "At" still counts
        Assert.Equal([23], Thrown(atStart));
        p.LootThrow = [];
        p.LootOther = false;                        // "Diğer" off: the material the bot took goes
        Assert.Equal([23], Thrown(atStart));
    }
}

public class TallSpaceTests
{
    private static CharacterProgress Bag(params InventoryItem[] items) => new(0, 0, 0, items, [], InventoryEnd: 58);

    [Fact]
    public void Room_for_armour_needs_two_free_slots_one_under_the_other()
    {
        // Page 1 (slots 11–58): everything taken except slot 20 and the slot under it, 26.
        var full = Enumerable.Range(11, 48).Where(s => s is not (20 or 26)).Select(s => new InventoryItem(s, 27002, 1)).ToArray();
        Assert.True(Bag(full).TallSpace());
        // Only 20 and 21 free (side by side): 2 free slots, but no armour fits.
        var sideBySide = Enumerable.Range(11, 48).Where(s => s is not (20 or 21)).Select(s => new InventoryItem(s, 27002, 1)).ToArray();
        Assert.Equal(2, Bag(sideBySide).FreeSlots());
        Assert.False(Bag(sideBySide).TallSpace());
        // The last row of a page has nothing under it on that page.
        var lastRow = Enumerable.Range(11, 48).Where(s => s is not (53 or 54)).Select(s => new InventoryItem(s, 27002, 1)).ToArray();
        Assert.False(Bag(lastRow).TallSpace());
    }
}

public class FullBagLootTests
{
    [Fact]
    public void A_full_bag_only_takes_yang_and_what_adds_to_a_stack()
    {
        // Page 1 full: potions (27002, 150 in a stack), a full stack of 27003, the rest single items.
        var items = Enumerable.Range(13, 46).Select(s => new InventoryItem(s, 30070, 1)).Prepend(new InventoryItem(12, 27003, 200))
            .Prepend(new InventoryItem(11, 27002, 150)).ToArray();
        var full = new CharacterProgress(0, 0, 0, items, [], InventoryEnd: 58);
        Assert.Equal(0, full.FreeSlots());
        Assert.True(full.Fits(1));           // yang
        Assert.True(full.Fits(27002));       // adds to the potion stack
        Assert.False(full.Fits(27003));      // that stack is full
        Assert.False(full.Fits(11210));      // armour
        Assert.False(full.Fits(50300));      // anything else

        // Two free slots side by side: a ring fits, armour (two slots one under the other) doesn't.
        var two = new CharacterProgress(0, 0, 0, items.Where(i => i.Slot is not (20 or 21)).ToArray(), [], InventoryEnd: 58);
        Assert.True(two.Fits(50300));
        Assert.False(two.Fits(11210));
    }

    [Fact]
    public void Items_that_cannot_go_into_the_bag_are_not_walked_to()
    {
        var p = new BotProfile { LootYang = true, LootOther = true, LootEquipment = true, PickUpDrops = true };
        var items = Enumerable.Range(11, 48).Select(s => new InventoryItem(s, 30070, 1)).ToArray();
        var me = new Vec3(10000, 10000, 1800);
        var camera = new CameraView(me with { Z = me.Z + 1000 }, -90, 0, 0, 90);
        var world = new WorldSnapshot(me, camera, [], 0, "c2", Progress: new CharacterProgress(0, 0, 0, items, [], InventoryEnd: 58),
            Drops: [new GroundItem(1, new Vec3(10100, 10000, 1800), 1, 50), new GroundItem(2, new Vec3(10000, 10100, 1800), 11210, 1)]);
        var view = BotEngine.MemoryView(world, p, 1920, 1080);
        Assert.Equal([1L], view!.Value.Drops.Select(d => d.Id));
    }
}

public class PickedItemsTests
{
    private static InventoryItem I(int slot, int vnum) => new(slot, vnum, 1);

    [Fact]
    public void What_comes_in_while_the_bot_runs_is_the_bots_moves_keep_their_owner()
    {
        var picked = new PickedItems();
        picked.Update([I(12, 500), I(13, 600)], botRunning: false);          // the user's own
        picked.Update([I(12, 500), I(13, 600), I(20, 700)], botRunning: true); // picked by the bot
        Assert.Equal([(20, 700)], picked.Picked);
        picked.Update([I(12, 500), I(13, 600), I(20, 700), I(21, 800)], botRunning: false); // the user's, bot stopped
        Assert.Equal([(20, 700)], picked.Picked);
        // The user moves the bot's item and his own: the bot's stays the bot's, his own stays his.
        picked.Update([I(12, 500), I(30, 600), I(25, 700), I(21, 800)], botRunning: true);
        Assert.Equal([(25, 700)], picked.Picked);
        // Thrown away: forgotten.
        picked.Update([I(12, 500), I(30, 600), I(21, 800)], botRunning: true);
        Assert.Empty(picked.Picked);
    }

    [Fact]
    public void A_bad_read_with_the_bag_almost_empty_does_not_make_everything_the_bots()
    {
        var picked = new PickedItems();
        InventoryItem[] bag = [I(12, 1), I(13, 2), I(14, 3), I(15, 4), I(16, 5), I(17, 6)];
        picked.Update(bag, botRunning: false);
        picked.Update([I(12, 1)], botRunning: true); // one bad read
        picked.Update(bag, botRunning: true);
        Assert.Empty(picked.Picked);
    }
}
