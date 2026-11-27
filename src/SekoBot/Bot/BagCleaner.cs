using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Throws the items ticked "At" out of a full bag, the way a person does: open the inventory,
/// go to the page an item is on, click it and press "Yere at" in its menu, answer "Evet" to
/// "throw it away?", check it is gone, and at the end close the inventory. Works only from what
/// memory shows (<see cref="BagLayout"/>): where the window, the page buttons, the slots and the
/// "Evet" button are drawn. Each step waits for the input to be done and the game to show the
/// result; an item that can't be thrown (twice nothing happened) is left alone.
/// Pure logic: the engine reads the layout and sends the actions.
/// </summary>
internal sealed class BagCleaner(Func<BagLayout?>? readBagNow = null)
{
    /// <summary>Long enough for a full bag of junk (seen: 26 items took longer than 2 minutes).</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(300);
    /// <summary>No question this long after the click: it didn't take.</summary>
    public static readonly TimeSpan QuestionTimeout = TimeSpan.FromMilliseconds(4500);
    public static TimeSpan AfterClick => TimeSpan.FromMilliseconds(PressTime.Vary(1800));
    /// <summary>After "Evet" in the item menu's question: looked at again this soon (the item goes within a second, seen);
    /// still there then, waited for up to <see cref="GoneWithin"/> before it counts as not taken.</summary>
    public static TimeSpan AfterYes => TimeSpan.FromMilliseconds(PressTime.Vary(700));
    public static readonly TimeSpan GoneWithin = TimeSpan.FromMilliseconds(1800);
    public static TimeSpan AfterKey => TimeSpan.FromMilliseconds(PressTime.Vary(1200));
    public const int TriesPerItem = 2;
    /// <summary>The item's menu shows a moment after the click; not there after this: retry the item.</summary>
    public static readonly TimeSpan MenuTimeout = TimeSpan.FromMilliseconds(2500);
    /// <summary>The menu shows some 60 ms after the click (recorded 2026-11-20); looked for this soon, waited for up to <see cref="MenuTimeout"/>.</summary>
    public static TimeSpan AfterItemClick => TimeSpan.FromMilliseconds(PressTime.Vary(250));
    /// <summary>The menu's button read at one place this long before it is clicked (see <see cref="Step.Menu"/>).</summary>
    public static readonly TimeSpan MenuSettle = TimeSpan.FromMilliseconds(150);
    private Point? _menuSeenAt;
    private TimeSpan _menuSeenWhen;

    private enum Step { Open, Pick, Menu, AwaitQuestion, Confirmed, Close }
    /// <summary>Selling: a right click on the item brings the sell question at once (Enter says yes) — the menu's
    /// "Sat" only for an item that showed no question this long after its right click.</summary>
    public static readonly TimeSpan RightClickTimeout = TimeSpan.FromMilliseconds(1200);
    /// <summary>Looked at again this soon after the right click / the Enter.</summary>
    public static TimeSpan AfterRightClick => TimeSpan.FromMilliseconds(PressTime.Vary(120));
    /// <summary>After Enter the question goes within this (else "Evet" is clicked).</summary>
    public static readonly TimeSpan EnterTakes = TimeSpan.FromMilliseconds(600);
    private bool _rightClicked;
    /// <summary>Selling: items sold after a right click, and those that needed the menu's "Sat" (no question came).</summary>
    public int ByRightClick { get; private set; }
    public int ByMenu => _menuOnly.Count;
    /// <summary>Items whose right click brought no question: sold through their menu.</summary>
    private readonly HashSet<int> _menuOnly = [];
    private TimeSpan _clickedAt;

    private Step _step;
    private TimeSpan _started, _waitUntil, _requestedAt, _yesAt;
    private int _slot, _vnum, _opens, _closes, _noLayout, _yesClicks, _escapes, _leftoverYes;
    private readonly Dictionary<int, int> _failed = [];
    private readonly Dictionary<int, int> _pageClicks = [];

    public bool Active { get; private set; }

    /// <summary>Selling at a market NPC instead of throwing away: the menu's "Sat", never dragging out.</summary>
    public bool Selling { get; init; }
    private int _openWaits;
    /// <summary>Items thrown away this time.</summary>
    public int Thrown { get; private set; }

    /// <summary>What was thrown (item, count) since the engine last took the list.</summary>
    public List<(int Vnum, int Count)> ThrownItems { get; } = [];
    private int _count;
    /// <summary>Why it ended without throwing everything (null: all went well).</summary>
    public string? Problem { get; private set; }

    private TaskCompletionSource<bool>? _confirmation;
    private void ConfirmationSent(TimeSpan now)
    {
        _yesClicks++;
        _yesAt = now;
        _step = Step.Confirmed;
        _waitUntil = now + (Selling && _rightClicked ? AfterRightClick : AfterYes);
    }

    public void Start(TimeSpan now)
    {
        _confirmation = null;
        Active = true;
        _step = Step.Open;
        _started = now;
        _waitUntil = now;
        _opens = _closes = _noLayout = _yesClicks = _escapes = _leftoverYes = Thrown = _openWaits = 0;
        Problem = null;
        _failed.Clear();
        _pageClicks.Clear();
        _menuOnly.Clear();
        ByRightClick = 0;
        _rightClicked = false;
    }

    /// <summary>Whether an item's middle is inside the game's window (unknown window size: yes).</summary>
    internal static bool Inside(Rectangle r, Size client) =>
        client.Width <= 0 || client.Height <= 0 ||
        r.X + r.Width / 2 is var x && r.Y + r.Height / 2 is var y && x >= 0 && y >= 0 && x < client.Width && y < client.Height;

    internal static Point ClickPoint(Rectangle bounds) =>
        DropClickPoint(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), bounds);

    internal static Point DropClickPoint(Point center, Rectangle bounds)
    {
        if (!bounds.Contains(center))
            return center;

        var area = Rectangle.Intersect(bounds, new Rectangle(center.X - 5, center.Y - 5, 11, 11));
        int count = area.Width * area.Height;
        if (count == 1)
            return center;
        int middle = (center.Y - area.Top) * area.Width + center.X - area.Left;
        int index = Random.Shared.Next(count - 1);
        if (index >= middle)
            index++;
        return new Point(area.Left + index % area.Width, area.Top + index / area.Width);
    }

    private Func<bool>? When(Func<BagLayout, bool> check) =>
        readBagNow == null ? null : () => readBagNow() is { } live && check(live);

    internal static ClickAction DropConfirmation(Point center, Rectangle bounds, Func<BagLayout?>? readBagNow)
    {
        var target = DropClickPoint(center, bounds);
        bool StillOpen()
        {
            var current = readBagNow!();
            return current?.Confirm is Point && current.ConfirmBounds.Contains(target);
        }
        return new ClickAction(target, Slow: true, Fast: true, StillWanted: readBagNow == null ? null : StillOpen);
    }

    internal static ClickAction SaleConfirmation(Point yes, BagLayout bag, Func<BagLayout?>? readBagNow)
    {
        var at = DropClickPoint(yes, bag.OtherConfirmBounds);
        return new ClickAction(at, Slow: true, Fast: true, StillWanted: readBagNow == null ? null :
            () => readBagNow() is { NpcShop: > 0, Confirm: null, OtherConfirm: not null } live
                && live.NpcShop == bag.NpcShop && live.OtherConfirm == yes && live.OtherConfirmBounds.Contains(at));
    }

    /// <summary>Stops at once (attacked, died); closes the inventory if it is open.</summary>
    public IReadOnlyList<BotAction> Abort(BagLayout? bag, BotProfile p, string why)
    {
        _confirmation = null;
        Active = false;
        Problem = why;
        var actions = new List<BotAction>();
        // The throw / sell question left showing (seen: it stayed open while the bot fought on): the throw question
        // answered "Evet" (Esc doesn't close it since the game's update; the item was to go anyway), another one closed.
        if (bag?.Confirm is Point yes)
            actions.Add(DropConfirmation(yes, bag.ConfirmBounds, readBagNow));
        else if (bag?.OtherConfirm != null)
            actions.Add(new KeyTapAction(p.Keys.CloseWindow));
        if (bag?.Open == true)
            actions.Add(new KeyTapAction(p.Keys.Inventory));
        return actions;
    }

    public IReadOnlyList<BotAction> Tick(TimeSpan now, BagLayout? bag, Func<BagSlotView, bool> throwIt, BotProfile p, Size client)
    {
        var actions = new List<BotAction>();
        if (!Active || now < _waitUntil)
            return actions;
        if (_confirmation != null)
        {
            if (!_confirmation.Task.IsCompleted) return actions;
            bool sent = _confirmation.Task.Result;
            _confirmation = null;
            if (sent)
            {
                ConfirmationSent(now);
                return actions;
            }
        }
        if (bag == null)
        {
            if (++_noLayout > 30)
                Finish("envanter bellekten okunamadı");
            return actions;
        }
        _noLayout = 0;
        if (!Selling && _step != Step.Close && now - _started > GiveUpAfter)
            Close("süre doldu");

        // The "throw it away?" question is showing (selling: "sell it?", another window).
        if ((bag.Confirm ?? (Selling ? bag.OtherConfirm : null)) is Point yes)
        {
            // Enter pressed a moment ago: the question is given time to go before "Evet" is clicked as well.
            if (Selling && _rightClicked && _step == Step.Confirmed && _yesClicks == 1 && now - _yesAt < EnterTakes)
                return actions;
            if (_step == Step.AwaitQuestion || _step == Step.Confirmed && _yesClicks < 2)
            {
                // After a right click the question is said yes to with Enter (quicker than the mouse); still showing
                // after that, "Evet" is clicked. Only with the question up: Enter alone would open the chat.
                int expectedSlot = _slot, expectedVnum = _vnum;
                if (Selling && _rightClicked && _yesClicks == 0)
                    actions.Add(new KeyTapAction(Keys.Enter) { StillWanted = When(live => live.NpcShop == bag.NpcShop && live.NpcShop > 0
                        && (bag.Confirm.HasValue ? live.Confirm.HasValue : live.Confirm == null && live.OtherConfirm.HasValue)
                        && live.Items.Any(i => i.Slot == expectedSlot && i.Vnum == expectedVnum)) });
                else
                    actions.Add(bag.Confirm.HasValue ? DropConfirmation(yes, bag.ConfirmBounds, readBagNow)
                        : SaleConfirmation(yes, bag, readBagNow));
                if (readBagNow == null)
                    ConfirmationSent(now);
                else
                {
                    var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _confirmation = pending;
                    bool Current() => ReferenceEquals(Volatile.Read(ref _confirmation), pending);
                    actions[^1] = actions[^1] switch
                    {
                        ClickAction click => click with { Completion = pending, StillWanted = () => Current() && click.StillWanted?.Invoke() == true },
                        KeyTapAction key => key with { Completion = pending, StillWanted = () => Current() && key.StillWanted?.Invoke() == true },
                        _ => throw new InvalidOperationException()
                    };
                }
            }
            else if (bag.Confirm is Point leftover && _step is Step.Open or Step.Pick && _leftoverYes < 2)
            {
                // Our own "throw it away?" left from before (cut short by a mob): Esc doesn't close it since the
                // game's update (seen: "onay penceresi kapanmıyor") — answered "Evet", the item was to go anyway.
                actions.Add(DropConfirmation(leftover, bag.ConfirmBounds, readBagNow));
                _leftoverYes++;
                _waitUntil = now + AfterClick;
            }
            else if (_escapes < 3)
            {
                // A question that isn't ours (or "Evet" didn't take twice): say no rather than guess.
                actions.Add(new KeyTapAction(p.Keys.CloseWindow));
                _escapes++;
                _waitUntil = now + AfterKey;
                if (_step == Step.Confirmed)
                {
                    Failed(_slot);
                    _step = Step.Pick;
                }
            }
            else
                Finish("onay penceresi kapanmıyor");
            return actions;
        }

        switch (_step)
        {
            case Step.Open:
                if (bag.Open && bag.Page >= 0)
                {
                    _step = Step.Pick;
                    goto case Step.Pick;
                }
                // Open already (a market's shop opens it by itself) with its page not read yet: waited on, not
                // pressed — the key would shut it (seen: the bag closed at the shop).
                if (bag.Open && _openWaits < 4)
                {
                    _openWaits++;
                    _waitUntil = now + AfterKey;
                    break;
                }
                if (_opens >= 3)
                {
                    Finish("envanter açılamadı");
                    break;
                }
                actions.Add(new KeyTapAction(p.Keys.Inventory));
                _opens++;
                _waitUntil = now + AfterKey;
                break;

            case Step.Menu:
                // The item's menu is up: "Yere at" (the question comes next) — or "Sat".
                if ((Selling ? bag.SellButton : bag.DropButton) is Point drop && bag.MenuSlot == _slot)
                {
                    // Looked at once more, as read now, right before "Yere at" / "Sat": a bonus asked for
                    // read meanwhile (or the item gone, another one there) and it stays.
                    if (bag.Items.FirstOrDefault(i => i.Slot == _slot && i.Vnum == _vnum) is not { Vnum: > 0 } still || !throwIt(still))
                    {
                        _failed[_slot] = TriesPerItem;
                        // At a shop Esc shuts the shop too (seen): the menu is left, the next item's click replaces it.
                        if (!Selling)
                            actions.Add(new KeyTapAction(p.Keys.CloseWindow));
                        _step = Step.Pick;
                        _waitUntil = now + AfterKey;
                        break;
                    }
                    // The icons sit elsewhere for each item (and "Sat" comes in next to a market NPC): right after
                    // the menu shows, its place may still be the last menu's — seen: "Karşılaştır" clicked instead of
                    // "At". Clicked only once read at the same place twice, a moment apart.
                    if (_menuSeenAt != drop || now - _menuSeenWhen < MenuSettle)
                    {
                        if (_menuSeenAt != drop)
                        {
                            _menuSeenAt = drop;
                            _menuSeenWhen = now;
                        }
                        break;
                    }
                    var at = DropClickPoint(drop, Selling ? bag.SellBounds : bag.DropBounds);
                    int slot = _slot, vnum = _vnum;
                    actions.Add(new ClickAction(at, Slow: true, Fast: true, StillWanted: When(live =>
                        live.Open && live.Confirm == null && live.OtherConfirm == null && live.MenuSlot == slot
                        && live.NpcShop == bag.NpcShop && live.Items.Any(i => i.Slot == slot && i.Vnum == vnum)
                        && (Selling ? live.SellBounds : live.DropBounds).Contains(at))));
                    _requestedAt = now;
                    _step = Step.AwaitQuestion;
                    _waitUntil = now + AfterItemClick;
                    break;
                }
                if (now - _clickedAt >= MenuTimeout)
                {
                    if (Selling)
                    {
                        // No "Sat" in its menu (the shop closed, or the item can't be sold): left — without Esc, which
                        // would shut the shop as well (seen: "Market penceresi kapandı", the potions then not bought).
                        _failed[_slot] = TriesPerItem;
                        _step = Step.Pick;
                        _waitUntil = now + AfterKey;
                        break;
                    }
                    Failed(_slot);
                    _step = Step.Pick;
                    goto case Step.Pick;
                }
                break;

            case Step.AwaitQuestion:
                // Sold without a question: the item is gone.
                if (Selling && !bag.Items.Any(i => i.Slot == _slot && i.Vnum == _vnum))
                {
                    Thrown++;
                    if (_rightClicked)
                        ByRightClick++;
                    ThrownItems.Add((_vnum, Math.Max(1, _count)));
                    _step = Step.Pick;
                    goto case Step.Pick;
                }
                // Right clicked, no question: this one is sold through its menu ("Sat").
                if (Selling && _rightClicked && now - _requestedAt >= RightClickTimeout)
                {
                    _menuOnly.Add(_slot);
                    _step = Step.Pick;
                    goto case Step.Pick;
                }
                // No question came up: the click didn't take.
                if (now - _requestedAt >= QuestionTimeout)
                {
                    Failed(_slot);
                    _step = Step.Pick;
                    goto case Step.Pick;
                }
                break;

            case Step.Confirmed:
                // The question is gone: the item should be too (a moment more given when it is still there).
                if (bag.Items.Any(i => i.Slot == _slot && i.Vnum == _vnum) && now - _yesAt < GoneWithin)
                    break;
                if (bag.Items.Any(i => i.Slot == _slot && i.Vnum == _vnum))
                    Failed(_slot);
                else
                {
                    Thrown++;
                    if (_rightClicked)
                        ByRightClick++;
                    ThrownItems.Add((_vnum, Math.Max(1, _count)));
                }
                _step = Step.Pick;
                goto case Step.Pick;

            case Step.Pick:
                if (!bag.Open)
                {
                    _step = Step.Open;
                    break;
                }
                // Selling: the bag and the shop both open, checked before every item (read now, no waiting); the shop
                // gone, nothing is clicked (the engine opens it again).
                if (Selling && bag.NpcShop == 0)
                {
                    _waitUntil = now + AfterRightClick;
                    break;
                }
                var left = bag.Items.Where(i => throwIt(i) && _failed.GetValueOrDefault(i.Slot) < TriesPerItem).ToList();
                if (left.Count == 0)
                {
                    Close(Thrown == 0 ? "atılacak eşya atılamadı" : null);
                    goto case Step.Close;
                }
                // Only what is inside the game's window (a small game cuts the bag off: seen, clicks beside the items).
                if (left.Where(i => i.Page == bag.Page && !i.Rect.IsEmpty && Inside(i.Rect, client)).OrderBy(i => i.Slot)
                        .Select(i => (BagSlotView?)i).FirstOrDefault() is { } here)
                {
                    var from = new Point(here.Rect.X + here.Rect.Width / 2, here.Rect.Y + here.Rect.Height / 2);
                    var visible = Rectangle.Intersect(here.Rect, new Rectangle(Point.Empty, client));
                    from = DropClickPoint(from, visible);
                    var slotWanted = When(live => live.Open && live.Page == here.Page && live.NpcShop == bag.NpcShop
                        && live.Confirm == null && live.OtherConfirm == null && (live.MenuSlot != here.Slot || (live.DropButton == null && live.SellButton == null))
                        && live.Items.Any(i => i.Slot == here.Slot && i.Vnum == here.Vnum && i.Rect.Contains(from)));
                    _count = here.Count;
                    _rightClicked = false;
                    if (Selling && !_menuOnly.Contains(here.Slot))
                    {
                        // Right click: the sell question comes straight up.
                        actions.Add(new ClickAction(from, Slow: true, Fast: true, Right: true, StillWanted: slotWanted));
                        _slot = here.Slot;
                        _vnum = here.Vnum;
                        _yesClicks = 0;
                        _rightClicked = true;
                        _requestedAt = now;
                        _step = Step.AwaitQuestion;
                        _waitUntil = now + AfterRightClick;
                        break;
                    }
                    actions.Add(new ClickAction(from, Slow: true, Fast: true, StillWanted: slotWanted));
                    _slot = here.Slot;
                    _vnum = here.Vnum;
                    _yesClicks = 0;
                    _clickedAt = now;
                    _menuSeenAt = null;
                    _step = Step.Menu;
                    _waitUntil = now + AfterItemClick;
                    break;
                }
                // The rest is on other pages: click the page's button (left to right = pages 1–4).
                int page = left.Min(i => i.Page);
                if (page >= 0 && page < bag.PageButtons.Count && _pageClicks.GetValueOrDefault(page) < 3)
                {
                    var target = bag.PageButtonBounds is { } bounds && page < bounds.Count
                        ? ClickPoint(bounds[page]) : DropClickPoint(bag.PageButtons[page],
                            Rectangle.Intersect(bag.Window, new Rectangle(bag.PageButtons[page].X - 2, bag.PageButtons[page].Y - 2, 5, 5)));
                    actions.Add(new ClickAction(target, Slow: true, StillWanted: When(live => live.Open && live.Page == bag.Page && live.Confirm == null && live.OtherConfirm == null
                        && live.PageButtonBounds is { } buttons && page < buttons.Count && buttons[page].Contains(target))));
                    _pageClicks[page] = _pageClicks.GetValueOrDefault(page) + 1;
                    _waitUntil = now + AfterClick;
                }
                else
                    foreach (var item in left.Where(i => i.Page == page))
                        _failed[item.Slot] = TriesPerItem; // that page can't be shown
                break;

            case Step.Close:
                if (!bag.Open)
                {
                    Finish(Problem);
                    break;
                }
                if (_closes >= 3)
                {
                    Finish(Problem ?? "envanter kapatılamadı");
                    break;
                }
                actions.Add(new KeyTapAction(p.Keys.Inventory));
                _closes++;
                _waitUntil = now + AfterKey;
                break;
        }
        return actions;
    }

    private void Failed(int slot) => _failed[slot] = _failed.GetValueOrDefault(slot) + 1;

    private void Close(string? problem)
    {
        _step = Step.Close;
        Problem ??= problem;
    }

    private void Finish(string? problem)
    {
        Active = false;
        Problem ??= problem;
    }
}
