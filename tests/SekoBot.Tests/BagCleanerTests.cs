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
    public void Confirmation_waits_for_delivery_and_retries_a_skipped_input()
    {
        var item = Item(20, Junk, 0, true);
        var profile = new BotProfile();
        var live = Bag(true, 0, [item]) with { MenuKnown = true };
        var cleaner = new BagCleaner(() => live);
        cleaner.Start(S(0));
        IReadOnlyList<BotAction> Tick(double time) => cleaner.Tick(S(time), live, i => i.Vnum == Junk, profile, Client);
        Tick(0);
        live = live with { MenuSlot = item.Slot, DropButton = new Point(400, 300), DropBounds = new Rectangle(390, 290, 20, 20) };
        Tick(1);
        Tick(1.5);
        live = live with { Confirm = new Point(500, 380), ConfirmBounds = new Rectangle(490, 370, 20, 20) };
        var first = Assert.IsType<ClickAction>(Assert.Single(Tick(2)));
        Assert.Empty(Tick(10));
        first.Completion!.SetResult(false);
        var retry = Assert.IsType<ClickAction>(Assert.Single(Tick(11)));
        retry.Completion!.SetResult(true);
        Assert.Empty(Tick(12));
        Assert.Empty(Tick(12.1));
    }

    [Fact]
    public void Queued_drop_confirmation_rechecks_the_live_button_before_pressing()
    {
        var bounds = new Rectangle(500, 380, 40, 20);
        var center = new Point(520, 390);
        var open = Bag(true, 0, [], center) with { ConfirmBounds = bounds };
        BagLayout? live = open;
        var click = BagCleaner.DropConfirmation(center, bounds, () => live);
        var wanted = Assert.IsType<Func<bool>>(click.StillWanted);
        Assert.True(wanted());
        live = open with { Confirm = null, OtherConfirm = center, OtherConfirmBounds = bounds };
        Assert.False(wanted());
        live = open with { Confirm = new Point(30, 20), ConfirmBounds = new Rectangle(10, 10, 40, 20) };
        Assert.False(wanted());
        live = null;
        Assert.False(wanted());
    }

    [Fact]
    public void Offset_stays_inside_narrow_targets_and_only_uses_center_for_a_single_pixel()
    {
        var center = new Point(12, 20);
        Assert.Equal(center, BagCleaner.DropClickPoint(center, new Rectangle(center, new Size(1, 1))));
        var narrow = new Rectangle(12, 20, 1, 4);
        for (int i = 0; i < 100; i++)
        {
            var target = BagCleaner.DropClickPoint(center, narrow);
            Assert.True(narrow.Contains(target));
            Assert.NotEqual(center, target);
        }
    }

    [Fact]
    public void Opens_the_bag_goes_to_the_page_and_clicks_the_item()
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
        var pageClick = Assert.IsType<ClickAction>(Assert.Single(page));
        Assert.NotEqual(Pages[1], pageClick.Client);
        Assert.InRange(Math.Abs(pageClick.Client.X - Pages[1].X), 0, 2);
        Assert.InRange(Math.Abs(pageClick.Client.Y - Pages[1].Y), 0, 2);
        var shown = Item(104, Junk, 1, true);
        var click = Assert.IsType<ClickAction>(Assert.Single(cleaner.Tick(S(3.5), Bag(true, 1, [shown]), toThrow, p, Client)));
        Assert.False(click.Right);
        Assert.True(shown.Rect.Contains(click.Client));
    }

    [Fact]
    public void Clicks_the_item_and_presses_yere_at_in_its_menu()
    {
        // As seen in the game: one click on an item opens its menu ("Kullan", "Yere at"); "Yere at"
        // asks the same question as a drag.
        var p = new BotProfile();
        var cleaner = new BagCleaner();
        Func<BagSlotView, bool> toThrow = i => i.Vnum == Junk;
        var junk = Item(20, Junk, 0, true);
        BagLayout With(Point? menuDrop = null, int menuSlot = -1, Point? confirm = null, params BagSlotView[] items) =>
            Bag(true, 0, items, confirm) with { MenuKnown = true, DropButton = menuDrop, MenuSlot = menuSlot,
                DropBounds = menuDrop is Point at ? new Rectangle(at.X - 10, at.Y - 2, 20, 4) : default,
                ConfirmBounds = confirm is Point yesAt ? new Rectangle(yesAt.X - 10, yesAt.Y - 2, 20, 4) : default };
        cleaner.Start(S(0));
        var click = Assert.IsType<ClickAction>(Assert.Single(cleaner.Tick(S(0), With(items: junk), toThrow, p, Client)));
        Assert.InRange(click.Client.X, junk.Rect.X + 16, junk.Rect.X + 26);
        Assert.InRange(click.Client.Y, junk.Rect.Y + 16, junk.Rect.Y + 26);
        var yereAt = new Point(848, 613);
        // Read once: not clicked yet (its place may still be the last menu's); read at the same place again: clicked.
        Assert.Empty(cleaner.Tick(S(1), With(yereAt, 20, null, junk), toThrow, p, Client));
        var yereAtClick = Assert.IsType<ClickAction>(Assert.Single(cleaner.Tick(S(1.5), With(yereAt, 20, null, junk), toThrow, p, Client)));
        Assert.InRange(yereAtClick.Client.X, yereAt.X - 5, yereAt.X + 5);
        Assert.InRange(yereAtClick.Client.Y, yereAt.Y - 2, yereAt.Y + 1);
        Assert.Equal(new ClickAction(yereAtClick.Client, Slow: true, Fast: true), yereAtClick);
        var yes = new Point(531, 389);
        var yesClick = Assert.IsType<ClickAction>(Assert.Single(cleaner.Tick(S(2), With(null, -1, yes, junk), toThrow, p, Client)));
        Assert.InRange(yesClick.Client.X, yes.X - 5, yes.X + 5);
        Assert.InRange(yesClick.Client.Y, yes.Y - 2, yes.Y + 1);
        Assert.Equal(new ClickAction(yesClick.Client, Slow: true, Fast: true), yesClick);
        Assert.Equal([new KeyTapAction(p.Keys.Inventory)], cleaner.Tick(S(4), With(items: []), toThrow, p, Client));
        Assert.Equal(1, cleaner.Thrown);
    }

    [Fact]
    public void A_menu_button_that_moves_right_after_showing_is_not_clicked_at_its_old_place()
    {
        // Seen: "Karşılaştır" clicked instead of "At" — the menu's place was still the last menu's for a moment.
        var p = new BotProfile();
        var cleaner = new BagCleaner();
        var junk = Item(20, Junk, 0, true);
        BagLayout With(Point drop) => Bag(true, 0, [junk], null) with { MenuKnown = true, DropButton = drop, MenuSlot = 20 };
        cleaner.Start(S(0));
        cleaner.Tick(S(0), Bag(true, 0, [junk], null) with { MenuKnown = true }, i => i.Vnum == Junk, p, Client); // the item clicked
        var old = new Point(395, 325);
        var now = new Point(362, 325);
        Assert.Empty(cleaner.Tick(S(0.3), With(old), i => i.Vnum == Junk, p, Client));
        Assert.Empty(cleaner.Tick(S(0.5), With(now), i => i.Vnum == Junk, p, Client));   // moved: read again first
        Assert.Equal([new ClickAction(now, Slow: true, Fast: true)], cleaner.Tick(S(0.7), With(now), i => i.Vnum == Junk, p, Client));
    }

    [Fact]
    public void No_menu_after_the_click_retries_with_a_left_click()
    {
        var p = new BotProfile();
        var cleaner = new BagCleaner();
        var bag = Bag(true, 0, [Item(20, Junk, 0, true)]) with { MenuKnown = true };
        cleaner.Start(S(0));
        Assert.IsType<ClickAction>(Assert.Single(cleaner.Tick(S(0), bag, i => i.Vnum == Junk, p, Client)));
        var later = new List<BotAction>();
        for (double t = 0.5; t < 4; t += 0.5)
            later.AddRange(cleaner.Tick(S(t), bag, i => i.Vnum == Junk, p, Client));
        var retry = Assert.IsType<ClickAction>(Assert.Single(later));
        Assert.False(retry.Right);
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
        Assert.Equal(2, actions.OfType<ClickAction>().Count());
        Assert.DoesNotContain(actions, a => a is MouseDragAction);
        Assert.Equal(0, cleaner.Thrown);
        Assert.False(cleaner.Active);
        Assert.Contains("atılamadı", cleaner.Problem);
    }

    [Fact]
    public void A_throw_question_left_from_before_is_answered_yes()
    {
        // Since the game's update of 2026-11-20 Esc doesn't close it (seen: "onay penceresi kapanmıyor"); it was
        // asked for an item the bot meant to throw (cut short by a mob): "Evet".
        var p = new BotProfile();
        var cleaner = new BagCleaner();
        cleaner.Start(S(0));
        var actions = cleaner.Tick(S(0), Bag(true, 0, [Item(20, Junk, 0, true)], new Point(531, 389)), i => i.Vnum == Junk, p, Client);
        Assert.Equal([new ClickAction(new Point(531, 389), Slow: true, Fast: true)], actions);
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
    public void Items_ticked_to_throw_are_never_picked_up()
    {
        ItemBook.Learn(new ItemInfo(95080, "Kalkan+0", "ITEM_ARMOR", "ARMOR_SHIELD", 100));
        var p = new BotProfile { LootEquipment = true, LootThrow = [95080] };
        Assert.False(BotEngine.WantedLoot(new GroundItem(1, new Vec3(0, 0, 0), 95080, 1), p));
        p.LootThrow = [];
        Assert.True(BotEngine.WantedLoot(new GroundItem(1, new Vec3(0, 0, 0), 95080, 1), p));
    }

    [Fact]
    public void Cut_short_by_an_attack_the_throw_question_is_answered_and_the_bag_closed()
    {
        var p = new BotProfile();
        var cleaner = new BagCleaner();
        cleaner.Start(TimeSpan.Zero);
        var asking = new BagLayout(true, new Rectangle(10, 20, 500, 600), 0, Pages, [], new Point(300, 300));
        var actions = cleaner.Abort(asking, p, "saldırı geldi");
        Assert.Equal([new ClickAction(new Point(300, 300), Slow: true, Fast: true), new KeyTapAction(p.Keys.Inventory)], actions);
        Assert.False(cleaner.Active);
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
        var p = new BotProfile { LootEquipment = false, LootEquipmentMinGold = 100 }; // sold for 24, 60, 180
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
    public void What_was_wanted_when_picked_is_never_junk_later()
    {
        // An event's reward (Futbol Topu) picked while the event ran: kept after the event too.
        var picked = new PickedItems();
        picked.Update([I(12, 500)], botRunning: false);
        picked.Update([I(12, 500), I(20, 50096), I(21, 17000)], botRunning: true, wanted: v => v == 50096);
        Assert.Equal([(21, 17000)], picked.Picked);
    }

    [Fact]
    public void An_event_reward_is_never_thrown_also_ticked_at_or_with_event_loot_off()
    {
        var p = new BotProfile { EventLoot = false, LootThrow = [50096] };
        HashSet<(int, int)> junk = [(20, 50096)];
        Loot.AllEventItems = [50096];
        try { Assert.False(BotEngine.ShouldThrow(new InventoryItem(20, 50096, 1), p, junk)); }
        finally { Loot.AllEventItems = []; }
        Assert.True(BotEngine.ShouldThrow(new InventoryItem(20, 50096, 1), p, junk));
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

public class BonusListTests
{
    [Fact]
    public void A_weapon_with_a_wanted_bonus_is_kept_the_rest_goes()
    {
        // As read in the game: Okyanus Yelpazesi+0 with 35 / 10 ("Orklara karşı güçlü +%10").
        ItemBook.Learn(new ItemInfo(97001, "Okyanus Yelpazesi+0", "ITEM_WEAPON", "WEAPON_FAN", 15000));
        Assert.Equal("Orklara karşı güçlü", ItemBonuses.NameOf(35));
        Assert.Equal("Zehirleme şansı", ItemBonuses.NameOf(41));
        var orcs10 = new InventoryItem(20, 97001, 1, [new ItemBonus(35, 10)]);
        var poison3 = new InventoryItem(21, 97001, 1, [new ItemBonus(41, 3)]);
        var none = new InventoryItem(22, 97001, 1);
        var p = new BotProfile { LootEquipmentMinGold = 5000, LootBonuses = [new BonusWant { Type = 35, Min = 10 }] };
        // All weapons are walked to (the bonus shows only in the bag).
        Assert.True(BotEngine.WantedLoot(new GroundItem(1, default, 97001, 1), p));
        Assert.True(BotEngine.KeepPicked(orcs10, p));
        Assert.False(BotEngine.KeepPicked(poison3, p));
        Assert.False(BotEngine.KeepPicked(none, p));
        p.LootBonuses[0].Min = 15;                              // +10 is less than wanted
        Assert.False(BotEngine.KeepPicked(orcs10, p));
        p.LootBonuses[0].Min = 10;
        HashSet<(int, int)> junk = [(20, 97001), (21, 97001), (22, 97001)];
        Assert.False(BotEngine.ShouldThrow(orcs10, p, junk));
        Assert.True(BotEngine.ShouldThrow(poison3, p, junk));   // sold for 3000 < 5000, no wanted bonus
        p.LootEquipmentMinGold = 0;                             // no price limit: only the bonus counts
        Assert.True(BotEngine.ShouldThrow(none, p, junk));
        Assert.False(BotEngine.ShouldThrow(orcs10, p, junk));
    }

    [Fact]
    public void Items_coming_in_while_the_bot_runs_are_listed()
    {
        var picked = new PickedItems();
        picked.Update([new InventoryItem(12, 500, 1)], botRunning: false);
        var sword = new InventoryItem(20, 600, 1, [new ItemBonus(47, 15)]);
        picked.Update([new InventoryItem(12, 500, 1), sword], true, (Func<InventoryItem, bool>)(i => i.Bonuses != null));
        Assert.Equal([sword], picked.CameIn);
        Assert.Empty(picked.Picked); // kept by its bonus: not junk
    }
}

public class SessionItemsTests
{
    [Fact]
    public void Picked_items_are_counted_by_how_much_more_the_bag_holds_moves_do_not_count()
    {
        var picked = new PickedItems();
        picked.Update([new InventoryItem(12, 27002, 100), new InventoryItem(20, 600, 1)], botRunning: false);
        // 20 potions more on the stack, a new sword; the 600 only moved.
        picked.Update([new InventoryItem(12, 27002, 120), new InventoryItem(25, 600, 1), new InventoryItem(30, 700, 1)], botRunning: true);
        Assert.Equal([(27002, 20), (700, 1)], picked.Gained.OrderByDescending(g => g.Count));

        var run = new SessionItems();
        foreach (var (vnum, count) in picked.Gained)
            run.Picked(vnum, count);
        run.Thrown(700, 1);
        Assert.Equal([(27002, 20, 0), (700, 1, 1)], run.Rows().Select(r => (r.Vnum, r.Picked, r.Thrown)));
    }
}
