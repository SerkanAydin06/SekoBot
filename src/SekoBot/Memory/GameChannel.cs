using System.Drawing;

namespace SekoBot.Memory;

/// <summary>One channel in the game's channel window: its number, how full it is (%), where its box is drawn.</summary>
internal readonly record struct ChannelBox(int Channel, int Full, Rectangle At);

/// <summary>
/// Changing channel (seen 2026-11-21): the Esc menu (UI_C.EscMenu, WBP_EscMenu_C) has a "Kanal değiştir"
/// button (its langText SYSTEM_MOVE_CHANNEL); it opens the channel window (m2data_C.chChange, chChange_C): a
/// box per channel (chBoxLogin_C: chid, yuzde = how full) and its own "Kanal değiştir" button (evet). The
/// channel the character is on: m2data_C.Channel. Only ever reads.
/// </summary>
/// <param name="Current">The channel the character is on (0: not known).</param>
/// <param name="MenuButton">The Esc menu's "Kanal değiştir" when the menu shows.</param>
/// <param name="Boxes">The channel window's boxes when it shows (empty otherwise).</param>
/// <param name="Selected">The channel chosen in the window (0: none).</param>
/// <param name="Change">The channel window's "Kanal değiştir" button when it shows.</param>
internal sealed record ChannelScreen(int Current, Rectangle? MenuButton, IReadOnlyList<ChannelBox> Boxes, int Selected, Rectangle? Change)
{
    public bool WindowOpen => Boxes.Count > 0;
}

internal sealed partial class GameWorldReader
{
    internal const string ChannelButtonKey = "SYSTEM_MOVE_CHANNEL";

    public ChannelScreen? ReadChannels()
    {
        try
        {
            if (!_located)
                return null;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return null;
            int current = Int32(instance + Offset(instanceClass, "m2data_C", "Channel")) ?? 0;
            // The Esc menu's button.
            Rectangle? menuButton = null;
            long ui = Pointer(instance + Offset(instanceClass, "m2data_C", "uiMain"));
            if (MemoryReads.IsPointer(ui) && ClassName(Pointer(ui + ObjClass)) == "UI_C")
            {
                long menu = Pointer(ui + Offset(Pointer(ui + ObjClass), "UI_C", "EscMenu"));
                if (Showing(menu) && ClassName(Pointer(menu + ObjClass)) == "WBP_EscMenu_C")
                    menuButton = ButtonByKey(menu, ChannelButtonKey);
            }
            // The channel window.
            var boxes = new List<ChannelBox>();
            int selected = 0;
            Rectangle? change = null;
            long window = Pointer(instance + Offset(instanceClass, "m2data_C", "chChange"));
            if (MemoryReads.IsPointer(window) && Pointer(window + ObjClass) is long windowClass && ClassName(windowClass) == "chChange_C" && Showing(window))
            {
                long evet = Pointer(window + Offset(windowClass, "chChange_C", "evet"));
                if (Showing(evet) && DrawnAt(evet) is { Width: > 4, Height: > 4 } e)
                    change = e;
                long chosen = Pointer(window + Offset(windowClass, "chChange_C", "selectedCh"));
                if (MemoryReads.IsPointer(chosen))
                    selected = Int32(chosen + Offset(Pointer(chosen + ObjClass), "chBox_C", "chid")) ?? 0;
                long wrap = Pointer(window + Offset(windowClass, "chChange_C", "WrapBox"));
                if (MemoryReads.IsPointer(wrap))
                {
                    long slots = wrap + Offset(Pointer(wrap + ObjClass), "PanelWidget", "Slots");
                    long data = Pointer(slots);
                    int count = Int32(slots + 8) ?? 0;
                    for (int i = 0; i < count && i < 40 && MemoryReads.IsPointer(data); i++)
                    {
                        long slot = Pointer(data + 8L * i);
                        if (!MemoryReads.IsPointer(slot))
                            continue;
                        long box = Pointer(slot + Offset(Pointer(slot + ObjClass), "PanelSlot", "Content"));
                        if (!Showing(box))
                            continue;
                        long boxClass = Pointer(box + ObjClass);
                        string boxName = ClassName(boxClass);
                        int id = Int32(box + Offset(boxClass, boxName, "chid")) ?? 0;
                        int full = Int32(box + Offset(boxClass, boxName, "yuzde")) ?? 0;
                        if (id > 0 && DrawnAt(box) is { Width: > 4, Height: > 4 } at)
                            boxes.Add(new ChannelBox(id, full, at));
                    }
                }
            }
            return new ChannelScreen(current, menuButton, boxes, selected, change);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }

    /// <summary>A <c>c_Button_C</c> of <paramref name="widget"/> by the game's text key (langText), where it is drawn.</summary>
    /// <summary>The game menu's (Esc) button with this text key (e.g. "SYSTEM_LOGOUT") when the menu shows; else null.</summary>
    public Rectangle? ReadMenuButton(string key)
    {
        try
        {
            if (!_located)
                return null;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return null;
            long ui = Pointer(instance + Offset(instanceClass, "m2data_C", "uiMain"));
            if (!MemoryReads.IsPointer(ui) || ClassName(Pointer(ui + ObjClass)) != "UI_C")
                return null;
            long menu = Pointer(ui + Offset(Pointer(ui + ObjClass), "UI_C", "EscMenu"));
            return Showing(menu) && ClassName(Pointer(menu + ObjClass)) == "WBP_EscMenu_C" ? ButtonByKey(menu, key) : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private Rectangle? ButtonByKey(long widget, string key)
    {
        long cls = Pointer(widget + ObjClass);
        if (!_widgetFields.TryGetValue(cls, out var fields))
            _widgetFields[cls] = fields = ObjectFields(cls);
        foreach (var (_, offset) in fields)
        {
            long button = Pointer(widget + offset);
            if (!MemoryReads.IsPointer(button))
                continue;
            long buttonClass = Pointer(button + ObjClass);
            if (ClassName(buttonClass) != "c_Button_C" || ReadFString(button + Offset(buttonClass, "c_Button_C", "langText"), 64) != key)
                continue;
            return Showing(button) && DrawnAt(button) is { Width: > 4, Height: > 4 } r ? r : null;
        }
        return null;
    }
}
