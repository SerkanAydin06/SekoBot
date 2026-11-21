namespace SekoBot.Memory;

/// <summary>One thing an NPC's shop sells: the item, how many in the pack, the price of one, where its box is drawn.</summary>
internal readonly record struct ShopSlot(int Vnum, int Count, int Price, Rectangle At);

/// <summary>
/// An NPC's shop window (m2data_C.shopWindow, shopWindow_C; seen 2026-11-21 at the "Market" 9003: potions in
/// packs of 5, 20 and 200, red 30 / 90 / 150 yang a piece, blue 120 / 360 / 700): a slot_C per thing sold in
/// its Content box (itemRef: the item; slotItem: number and count). A right click on a box buys it at once
/// (the user, 2026-11-21; a left click asks first). Only ever reads.
/// </summary>
internal sealed record NpcShopScreen(int ShopId, IReadOnlyList<ShopSlot> Slots);

internal sealed partial class GameWorldReader
{
    public NpcShopScreen? ReadNpcShop()
    {
        try
        {
            if (!_located)
                return null;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return null;
            long shop = Pointer(instance + Offset(instanceClass, "m2data_C", "shopWindow"));
            long shopClass = MemoryReads.IsPointer(shop) ? Pointer(shop + ObjClass) : 0;
            if (!MemoryReads.IsPointer(shopClass) || ClassName(shopClass) != "shopWindow_C" || !Showing(shop))
                return null;
            int shopId = Int32(shop + Offset(shopClass, "shopWindow_C", "shopId")) ?? 0;
            long content = Pointer(shop + Offset(shopClass, "shopWindow_C", "Content"));
            var slots = new List<ShopSlot>();
            if (!MemoryReads.IsPointer(content))
                return new NpcShopScreen(shopId, slots);
            long list = content + Offset(Pointer(content + ObjClass), "PanelWidget", "Slots");
            long data = Pointer(list);
            int count = Int32(list + 8) ?? 0;
            for (int i = 0; i < count && i < 200 && MemoryReads.IsPointer(data); i++)
            {
                long panelSlot = Pointer(data + 8L * i);
                if (!MemoryReads.IsPointer(panelSlot))
                    continue;
                long box = Pointer(panelSlot + Offset(Pointer(panelSlot + ObjClass), "PanelSlot", "Content"));
                long boxClass = MemoryReads.IsPointer(box) ? Pointer(box + ObjClass) : 0;
                if (!MemoryReads.IsPointer(boxClass) || ClassName(boxClass) != "slot_C")
                    continue;
                int itemAt = Offset(boxClass, "slot_C", "slotItem");
                int vnum = Int32(box + itemAt) ?? 0, pack = Int32(box + itemAt + 4) ?? 0;
                int price = Int32(box + Offset(boxClass, "slot_C", "itemRef.shopGold")) ?? 0;
                if (vnum > 0 && pack > 0 && Showing(box) && DrawnAt(box) is { Width: > 4, Height: > 4 } at)
                    slots.Add(new ShopSlot(vnum, pack, price, at));
            }
            return new NpcShopScreen(shopId, slots);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }
}
