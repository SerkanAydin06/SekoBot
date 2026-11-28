using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// A potion key (1, 2) left empty — its stack used up, the potions bought land in another bag slot — filled again:
/// the inventory opened, the page with the potion shown, the potion dragged onto the key's quick slot, the key read
/// back from memory (it shows the potion: done), the inventory closed. Two drags at most; whatever goes wrong ends it
/// with <see cref="Problem"/> (the inventory closed all the same). Only works out what to press; the engine sends it.
/// </summary>
internal sealed class PotionRefill
{
    private enum Step { Open, Drag, Check, Close }

    /// <summary>Waited after a key (the bag opening, a page), after a drag (the key read back), at most for all of it.</summary>
    internal static readonly TimeSpan AfterKey = TimeSpan.FromMilliseconds(700), AfterDrag = TimeSpan.FromMilliseconds(1400),
        Longest = TimeSpan.FromSeconds(30);

    private Step _step;
    private TimeSpan _started, _waitUntil;
    private int _opens, _drags, _pageClicks, _closes;

    public bool Active { get; private set; }
    /// <summary>The key's number (1 = key "1").</summary>
    public int Key { get; private set; }
    public int Vnum { get; private set; }
    public string Kind { get; private set; } = "";
    /// <summary>The key shows the potion now.</summary>
    public bool Succeeded { get; private set; }
    /// <summary>Why it ended without the potion on the key (null: all went well).</summary>
    public string? Problem { get; private set; }

    public void Start(TimeSpan now, string kind, int key, int vnum)
    {
        Active = true;
        Kind = kind;
        Key = key;
        Vnum = vnum;
        _step = Step.Open;
        _started = _waitUntil = now;
        _opens = _drags = _pageClicks = _closes = 0;
        Succeeded = false;
        Problem = null;
    }

    /// <summary>Forgotten (the bot started again).</summary>
    public void Reset() => Active = false;

    /// <summary>Stopped (attacked, the bot stopped): the bag closed next.</summary>
    public void Abort(string why)
    {
        if (!Active || _step == Step.Close)
            return;
        Problem ??= why;
        _step = Step.Close;
        _closes = 0;
    }

    private void Finish(string? problem)
    {
        Problem ??= problem;
        _step = Step.Close;
    }

    /// <param name="bag">The inventory as drawn now (null: not read).</param>
    /// <param name="quick">Where each quick slot is drawn (null: not read).</param>
    /// <param name="onKey">The item the key shows now (null: nothing).</param>
    public IReadOnlyList<BotAction> Tick(TimeSpan now, BagLayout? bag, IReadOnlyList<Rectangle?>? quick, int? onKey, KeyBindings keys,
        Func<BagLayout?>? readBagNow = null)
    {
        if (!Active || now < _waitUntil)
            return [];
        Func<bool>? When(Func<BagLayout, bool> check) => readBagNow == null ? null : () => readBagNow() is { } live && check(live);
        if (onKey == Vnum && _step != Step.Close)
        {
            Succeeded = true;
            _step = Step.Close;
        }
        else if (_step != Step.Close && now - _started > Longest)
            Finish("30 saniyede bitmedi");
        var actions = new List<BotAction>();
        switch (_step)
        {
            case Step.Open:
                if (bag is { Open: true, Page: >= 0 })
                {
                    _step = Step.Drag;
                    goto case Step.Drag;
                }
                if (bag is { Open: true })
                {
                    _waitUntil = now + AfterKey; // its page not read yet: waited on (the key would shut it)
                    if (++_opens > 5) Finish("envanter sayfası okunamadı");
                    break;
                }
                if (_opens >= 3)
                {
                    Finish("envanter açılamadı");
                    break;
                }
                actions.Add(new KeyTapAction(keys.Inventory) { StillWanted = When(b => !b.Open) });
                _opens++;
                _waitUntil = now + AfterKey;
                break;

            case Step.Drag:
                if (bag is not { Open: true, Page: >= 0 })
                {
                    _step = Step.Open;
                    _waitUntil = now + AfterKey;
                    break;
                }
                if (quick is null || Key < 1 || Key > quick.Count || quick[Key - 1] is not { } slot)
                {
                    Finish("hızlı slotun ekrandaki yeri okunamadı");
                    break;
                }
                var stacks = bag.Items.Where(i => i.Vnum == Vnum && i.Count > 0).OrderByDescending(i => i.Count).ToList();
                if (stacks.Count == 0)
                {
                    Finish("çantada bu iksir yok");
                    break;
                }
                // The one on the page shown first; else its page opened.
                var item = stacks.FirstOrDefault(i => i.Page == bag.Page && !i.Rect.IsEmpty);
                if (item.Vnum == 0)
                {
                    int page = stacks[0].Page;
                    if (page < 0 || page >= bag.PageButtons.Count || _pageClicks >= 3)
                    {
                        Finish("iksirin bulunduğu çanta sayfası açılamadı");
                        break;
                    }
                    var target = bag.PageButtonBounds is { } bounds && page < bounds.Count
                        ? BagCleaner.ClickPoint(bounds[page]) : bag.PageButtons[page];
                    actions.Add(new ClickAction(target, Slow: true, StillWanted: When(live => live.Open && live.Page == bag.Page)));
                    _pageClicks++;
                    _waitUntil = now + AfterKey;
                    break;
                }
                if (_drags >= 2)
                {
                    Finish("iksir hızlı slota sürüklenemedi");
                    break;
                }
                var from = new Point(item.Rect.X + item.Rect.Width / 2, item.Rect.Y + item.Rect.Height / 2);
                var to = new Point(slot.X + slot.Width / 2, slot.Y + slot.Height / 2);
                int itemSlot = item.Slot;
                actions.Add(new MouseDragAction(from, to, When(live => live.Open && live.Page == bag.Page
                    && live.Items.Any(i => i.Slot == itemSlot && i.Vnum == Vnum && i.Rect.Contains(from)))));
                _drags++;
                _step = Step.Check;
                _waitUntil = now + AfterDrag;
                break;

            case Step.Check:
                // Not on the key yet (the check above): once more.
                _step = Step.Drag;
                goto case Step.Drag;

            case Step.Close:
                if (bag is { Open: true } && _closes < 3)
                {
                    actions.Add(new KeyTapAction(keys.Inventory) { StillWanted = When(b => b.Open) });
                    _closes++;
                    _waitUntil = now + AfterKey;
                    break;
                }
                Active = false;
                break;
        }
        return actions;
    }
}
