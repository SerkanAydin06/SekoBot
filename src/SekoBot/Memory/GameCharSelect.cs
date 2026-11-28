using System.Drawing;

namespace SekoBot.Memory;

/// <summary>
/// The character select (m2data_C.charSelectNew, charSelectNew_C; seen 2026-11-21 on KAiZER's account): the
/// account's characters (owningCharPanel.Chars: each one's charData.playerid and Index), the one shown
/// (charSelectNew_C.Index) and its "Başla" button (the c_Button_charSelect_C whose langText is SELECT_SELECT).
/// Only ever reads.
/// </summary>
/// <param name="Selected">The character shown (to be started with "Başla"); empty when not known.</param>
/// <param name="Start">The "Başla" button where it is drawn (client pixels); null when not shown.</param>
/// <param name="Places">Where each character's entry is drawn in the list (client pixels), to choose it; null when not known.</param>
internal sealed record CharSelectScreen(IReadOnlyList<string> Characters, string Selected, Rectangle? Start,
    IReadOnlyDictionary<string, Rectangle>? Places = null);

internal sealed partial class GameWorldReader
{
    /// <summary>The game's own text key of the "Başla" button.</summary>
    internal const string StartButtonKey = "SELECT_SELECT";

    /// <summary>The character select when it is up; null otherwise.</summary>
    public CharSelectScreen? ReadCharSelect()
    {
        try
        {
            if (!_located)
                return null;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return null;
            long screen = Pointer(instance + Offset(instanceClass, "m2data_C", "charSelectNew"));
            if (!MemoryReads.IsPointer(screen))
                return null;
            long cls = Pointer(screen + ObjClass);
            if (ClassName(cls) != "charSelectNew_C" || !Showing(screen))
                return null;
            // The characters, by their place in the list.
            var names = new SortedDictionary<int, string>();
            var places = new Dictionary<string, Rectangle>();
            long panel = Pointer(screen + Offset(cls, "charSelectNew_C", "owningCharPanel"));
            if (MemoryReads.IsPointer(panel))
            {
                long panelClass = Pointer(panel + ObjClass);
                long list = panel + Offset(panelClass, "owningCharPanel_C", "Chars");
                long data = Pointer(list);
                int count = Int32(list + 8) ?? 0;
                for (int i = 0; i < count && i < 16 && MemoryReads.IsPointer(data); i++)
                {
                    long entry = Pointer(data + 8L * i);
                    if (!MemoryReads.IsPointer(entry))
                        continue;
                    long entryClass = Pointer(entry + ObjClass);
                    int index = Int32(entry + Offset(entryClass, "selectebleOwningChar_C", "Index")) ?? i;
                    string name = ReadFString(entry + Offset(entryClass, "selectebleOwningChar_C", "charData.playerid"), 64);
                    if (name.Length > 0)
                    {
                        names[index] = name;
                        if (Showing(entry) && DrawnAt(entry) is { Width: > 4, Height: > 4 } at)
                            places[name] = at;
                    }
                }
            }
            int shown = Int32(screen + Offset(cls, "charSelectNew_C", "Index")) ?? -1;
            string selected = names.GetValueOrDefault(shown, "");
            Rectangle? start = null;
            if (!_widgetFields.TryGetValue(cls, out var fields))
                _widgetFields[cls] = fields = ObjectFields(cls);
            foreach (var (_, offset) in fields)
            {
                long button = Pointer(screen + offset);
                if (!MemoryReads.IsPointer(button))
                    continue;
                long buttonClass = Pointer(button + ObjClass);
                if (ClassName(buttonClass) != "c_Button_charSelect_C"
                    || ReadFString(button + Offset(buttonClass, "c_Button_charSelect_C", "langText"), 64) != StartButtonKey)
                    continue;
                if (Showing(button) && DrawnAt(button) is { Width: > 4, Height: > 4 } r)
                    start = r;
                break;
            }
            return new CharSelectScreen([.. names.Values], selected, start, places);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }

    /// <summary>The game says its version is old (an update is out: m2data_C.versionOld); null when not readable.</summary>
    public bool? ReadVersionOld()
    {
        try
        {
            if (!_located)
                return null;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return null;
            int at = Offset(instanceClass, "m2data_C", "versionOld");
            return at > 0 && Byte(instance + at) is byte b ? b != 0 : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }
}
