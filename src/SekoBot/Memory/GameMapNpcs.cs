namespace SekoBot.Memory;

/// <summary>An NPC marked on the M map: its number and where it stands (world units; the height isn't on the map).</summary>
internal readonly record struct MapNpc(int Vnum, double X, double Y);

/// <summary>
/// The NPCs of the whole map, from the game's M map (UI.bigMap: a dot per NPC, placed on the map's picture).
/// The game knows only the NPCs within ~50 m around the character, but its M map marks every one of the
/// map's (seen on c3: the shops in town and the ones in the corners, 430 m away). The map window exists only
/// while it is open; a dot's place on the picture turns into the world by the map's offset and scale
/// (gameManager.MapOffset / MapScale): picture = (world + offset) × scale × picture size / 1000.
/// Only ever reads.
/// </summary>
internal sealed partial class GameWorldReader
{
    /// <summary>A point on the M map's picture (its size <paramref name="size"/>) in the world.</summary>
    internal static (double X, double Y) PictureToWorld(double px, double py, double offsetX, double offsetY, double scaleX, double scaleY, double size) =>
        (px / (scaleX * size / 1000) - offsetX, py / (scaleY * size / 1000) - offsetY);

    /// <summary>The NPCs on the open M map; null when it isn't open (or can't be read).</summary>
    public List<MapNpc>? ReadMapNpcs()
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
            if (!MemoryReads.IsPointer(ui))
                return null;
            long uiClass = Pointer(ui + ObjClass);
            int bigAt = ClassName(uiClass) == "UI_C" ? Offset(uiClass, "UI_C", "bigMap") : 0;
            if (bigAt <= 0)
                return null;
            long map = Pointer(ui + bigAt);
            if (!MemoryReads.IsPointer(map))
                return null;
            long mapClass = Pointer(map + ObjClass);
            if (ClassName(mapClass) != "bigMap_C")
                return null;
            long gm = Pointer(instance + Offset(instanceClass, "m2data_C", "gm"));
            long gmClass = Pointer(gm + ObjClass);
            int offsetAt = Offset(gmClass, "gameManager", "MapOffset"), scaleAt = Offset(gmClass, "gameManager", "MapScale");
            int sizeAt = Offset(mapClass, "bigMap_C", "MapImgSize"), areaAt = Offset(mapClass, "bigMap_C", "dotArea");
            if (offsetAt <= 0 || scaleAt <= 0 || sizeAt <= 0 || areaAt <= 0)
                return null;
            if (Float(gm + offsetAt) is not double ox || Float(gm + offsetAt + 4) is not double oy
                || Float(gm + scaleAt) is not double sx || Float(gm + scaleAt + 4) is not double sy || Float(map + sizeAt) is not double size
                || sx <= 0 || sy <= 0 || size <= 0)
                return null;
            long area = Pointer(map + areaAt);
            if (!MemoryReads.IsPointer(area))
                return null;
            long slots = area + Offset(Pointer(area + ObjClass), "PanelWidget", "Slots");
            long data = Pointer(slots);
            int count = Int32(slots + 8) ?? 0;
            var npcs = new List<MapNpc>();
            if (!MemoryReads.IsPointer(data) || count is < 0 or > 500)
                return npcs;
            for (int i = 0; i < count; i++)
            {
                long slot = Pointer(data + 8L * i);
                if (!MemoryReads.IsPointer(slot))
                    continue;
                long slotClass = Pointer(slot + ObjClass);
                long dot = Pointer(slot + Offset(slotClass, "PanelSlot", "Content"));
                long dotClass = MemoryReads.IsPointer(dot) ? Pointer(dot + ObjClass) : 0;
                if (!MemoryReads.IsPointer(dotClass) || ClassName(dotClass) != "minimapDot_C")
                    continue;
                int layoutAt = Offset(slotClass, "CanvasPanelSlot", "LayoutData"), mobAt = Offset(dotClass, "minimapDot_C", "mobid");
                if (layoutAt <= 0 || mobAt <= 0 || Int32(dot + mobAt) is not int vnum || vnum <= 0
                    || Float(slot + layoutAt) is not double px || Float(slot + layoutAt + 4) is not double py)
                    continue;
                var (x, y) = PictureToWorld(px, py, ox, oy, sx, sy, size);
                npcs.Add(new MapNpc(vnum, x, y));
            }
            return npcs;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }
}
