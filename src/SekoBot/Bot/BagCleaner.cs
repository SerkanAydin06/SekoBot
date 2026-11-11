using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Throws the items ticked "At" out of a full bag, the way a person does: open the inventory,
/// go to the page an item is on, click it and press "Yere at" in its menu (or, when that menu
/// doesn't show, drag it out of the window onto the ground), answer "Evet" to
/// "throw it away?", check it is gone, and at the end close the inventory. Works only from what
/// memory shows (<see cref="BagLayout"/>): where the window, the page buttons, the slots and the
/// "Evet" button are drawn. Each step waits for the input to be done and the game to show the
/// result; an item that can't be thrown (twice nothing happened) is left alone.
/// Pure logic: the engine reads the layout and sends the actions.
/// </summary>
internal sealed class BagCleaner
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(120);
    /// <summary>A drag takes ~1.5 s of input; then the question needs a moment to show.</summary>
    public static readonly TimeSpan AfterDrag = TimeSpan.FromMilliseconds(2200);
    /// <summary>No question this long after the drag: it didn't take.</summary>
    public static readonly TimeSpan QuestionTimeout = TimeSpan.FromMilliseconds(4500);
    public static readonly TimeSpan AfterClick = TimeSpan.FromMilliseconds(1800);
    public static readonly TimeSpan AfterKey = TimeSpan.FromMilliseconds(1200);
    public const int TriesPerItem = 2;
    /// <summary>The item's menu shows a moment after the click; not there after this: dragged instead.</summary>
    public static readonly TimeSpan MenuTimeout = TimeSpan.FromMilliseconds(2500);
    public static readonly TimeSpan AfterItemClick = TimeSpan.FromMilliseconds(700);

    private enum Step { Open, Pick, Menu, Dragged, Confirmed, Close }
    private TimeSpan _clickedAt;
    private int _menuMisses;
    private bool _menuTried;

    private Step _step;
    private TimeSpan _started, _waitUntil, _draggedAt;
    private int _slot, _vnum, _opens, _closes, _noLayout, _yesClicks, _escapes;
    private readonly Dictionary<int, int> _failed = [];
    private readonly Dictionary<int, int> _pageClicks = [];

    public bool Active { get; private set; }
    /// <summary>Items thrown away this time.</summary>
    public int Thrown { get; private set; }
    /// <summary>Why it ended without throwing everything (null: all went well).</summary>
    public string? Problem { get; private set; }

    public void Start(TimeSpan now)
    {
        Active = true;
        _step = Step.Open;
        _started = now;
        _waitUntil = now;
        _opens = _closes = _noLayout = _yesClicks = _escapes = Thrown = _menuMisses = 0;
        Problem = null;
        _failed.Clear();
        _pageClicks.Clear();
    }

    /// <summary>Stops at once (attacked, died); closes the inventory if it is open.</summary>
    public IReadOnlyList<BotAction> Abort(BagLayout? bag, BotProfile p, string why)
    {
        Active = false;
        Problem = why;
        return bag?.Open == true ? [new KeyTapAction(p.Keys.Inventory)] : [];
    }

    public IReadOnlyList<BotAction> Tick(TimeSpan now, BagLayout? bag, Func<BagSlotView, bool> throwIt, BotProfile p, Size client)
    {
        var actions = new List<BotAction>();
        if (!Active || now < _waitUntil)
            return actions;
        if (bag == null)
        {
            if (++_noLayout > 30)
                Finish("envanter bellekten okunamadı");
            return actions;
        }
        _noLayout = 0;
        if (_step != Step.Close && now - _started > GiveUpAfter)
            Close("süre doldu");

        // The "throw it away?" question is showing.
        if (bag.Confirm is Point yes)
        {
            if (_step == Step.Dragged || _step == Step.Confirmed && _yesClicks < 2)
            {
                actions.Add(new ClickAction(yes, Slow: true));
                _yesClicks++;
                _step = Step.Confirmed;
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
                // The item's menu is up: "Yere at" (the question comes next, as after a drag).
                if (bag.DropButton is Point drop && bag.MenuSlot == _slot)
                {
                    actions.Add(new ClickAction(drop, Slow: true));
                    _draggedAt = now;
                    _step = Step.Dragged;
                    _waitUntil = now + AfterItemClick;
                    break;
                }
                if (now - _clickedAt >= MenuTimeout)
                {
                    _menuMisses++; // twice no menu: drag from now on
                    _step = Step.Pick;
                    goto case Step.Pick;
                }
                break;

            case Step.Dragged:
                // No question came up: the drag didn't take.
                if (now - _draggedAt >= QuestionTimeout)
                {
                    Failed(_slot);
                    _step = Step.Pick;
                    goto case Step.Pick;
                }
                break;

            case Step.Confirmed:
                // The question is gone: the item should be too.
                if (bag.Items.Any(i => i.Slot == _slot && i.Vnum == _vnum))
                    Failed(_slot);
                else
                    Thrown++;
                _step = Step.Pick;
                goto case Step.Pick;

            case Step.Pick:
                if (!bag.Open)
                {
                    _step = Step.Open;
                    break;
                }
                var left = bag.Items.Where(i => throwIt(i) && _failed.GetValueOrDefault(i.Slot) < TriesPerItem).ToList();
                if (left.Count == 0)
                {
                    Close(Thrown == 0 ? "atılacak eşya atılamadı" : null);
                    goto case Step.Close;
                }
                if (left.Where(i => i.Page == bag.Page && !i.Rect.IsEmpty).OrderBy(i => i.Slot)
                        .Select(i => (BagSlotView?)i).FirstOrDefault() is { } here)
                {
                    var from = new Point(here.Rect.X + here.Rect.Width / 2, here.Rect.Y + here.Rect.Height / 2);
                    // A new item: try its menu first (faster); the same item after a missing menu: drag.
                    bool sameItem = here.Slot == _slot && here.Vnum == _vnum && _menuTried;
                    if (bag.MenuKnown && _menuMisses < 2 && !sameItem)
                    {
                        actions.Add(new ClickAction(from, Slow: true));
                        _slot = here.Slot;
                        _vnum = here.Vnum;
                        _yesClicks = 0;
                        _menuTried = true;
                        _clickedAt = now;
                        _step = Step.Menu;
                        _waitUntil = now + AfterItemClick;
                        break;
                    }
                    _menuTried = false;
                    actions.Add(new MouseDragAction(from, DropPoint(bag, client)));
                    _slot = here.Slot;
                    _vnum = here.Vnum;
                    _yesClicks = 0;
                    _draggedAt = now;
                    _step = Step.Dragged;
                    _waitUntil = now + AfterDrag;
                    break;
                }
                // The rest is on other pages: click the page's button (left to right = pages 1–4).
                int page = left.Min(i => i.Page);
                if (page >= 0 && page < bag.PageButtons.Count && _pageClicks.GetValueOrDefault(page) < 3)
                {
                    actions.Add(new ClickAction(bag.PageButtons[page], Slow: true));
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

    /// <summary>
    /// Where to let go of an item: on the ground beside the inventory window (the wider side),
    /// a little below the middle of the screen, away from the window and the character.
    /// </summary>
    internal static Point DropPoint(BagLayout bag, Size client)
    {
        int y = client.Height * 55 / 100;
        var window = bag.Window;
        if (window.IsEmpty)
            return new Point(client.Width / 4, y);
        int leftRoom = window.Left, rightRoom = client.Width - window.Right;
        return leftRoom >= rightRoom
            ? new Point(leftRoom / 2, y)
            : new Point(window.Right + rightRoom / 2, y);
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
