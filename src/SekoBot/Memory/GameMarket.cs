namespace SekoBot.Memory;

/// <summary>A player's shop on the map (the market): its number and where it stands.</summary>
/// <param name="Body">Where its stall stands in the world when it is near enough to be drawn (else null).</param>
internal sealed record MarketShop(int Id, double X, double Y, Vec3? Body);

/// <summary>One item for sale in a shop.</summary>
/// <param name="Socket">The item's first socket: on a skill book the skill it teaches, on armour a stone.</param>
internal sealed record ShopItem(int Slot, int Vnum, int Count, long Price, IReadOnlyList<ItemBonus> Bonuses, int Socket = 0)
{
    /// <summary>The price of one piece.</summary>
    public double UnitPrice => Count > 1 ? (double)Price / Count : Price;
}

/// <summary>What lies over the game picture (in client pixels): a stall under it must not be tapped.</summary>
/// <param name="ChatHistory">Where the chat history window is drawn when open (it covers the middle of the
/// screen, Esc doesn't close it). Memory shows it "visible" open or closed (seen): whether it is open
/// has to be seen in the picture.</param>
/// <param name="ChatHistoryClose">Its X button.</param>
internal sealed record ScreenCover(IReadOnlyList<Rectangle> Areas, Rectangle? ChatHistory = null, Point? ChatHistoryClose = null, Rectangle CloseBounds = default)
{
    public bool Covers(Point p) => Areas.Any(a => a.Contains(p));
}

/// <summary>The shop whose window is open: who sells, and everything in it.</summary>
internal sealed record OpenShop(int Id, string Owner, int Level, string Title, IReadOnlyList<ShopItem> Items);

/// <summary>
/// The market: gameManager.privShops holds every shop of the map (a TMap of shop number → xmob:
/// the stall actor when it is drawn, X and Y), so the whole market is known without walking it.
/// What a shop sells comes only when its window is opened (the server sends it then): the window
/// (m2data_C.PrivateShopView, gone again when closed) has the shop's number, the seller and the
/// items (gotData: the same invItem as in the bag, with the price at +0x78). Only ever reads.
/// </summary>
internal sealed partial class GameWorldReader
{
    // TMap<int, xmob> element: key (int, padded to 8), xmob (mover at 0, X at +0x10, Y at +0x14), hash links.
    private const int ShopStride = 0xD8, ShopValue = 0x8, ShopMover = 0x0, ShopX = 0x10, ShopY = 0x14;
    // invItem (0x88): ItemId 0, Count 0x3C, ID (its slot) 0x40, price 0x78.
    private const int ShopItemStride = 0x88, ShopItemCount = 0x3C, ShopItemSlot = 0x40, ShopItemPrice = 0x78, ShopItemSocket = 0x2C;

    /// <summary>Every shop of the map (empty when the map has none or it can't be read).</summary>
    public List<MarketShop> ReadMarketShops()
    {
        var shops = new List<MarketShop>();
        try
        {
            if (!_located)
                return shops;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return shops;
            long gm = Pointer(instance + Offset(instanceClass, "m2data_C", "gm"));
            if (!MemoryReads.IsPointer(gm))
                return shops;
            long map = gm + Offset(Pointer(gm + ObjClass), "gameManager", "privShops");
            long data = Pointer(map);
            int count = Int32(map + 8) ?? 0;
            if (!MemoryReads.IsPointer(data) || count <= 0 || count > 20_000)
                return shops;
            // The set's "allocated" bits: inline for up to 128 elements, else on the heap.
            int maxBits = Int32(map + 0x2C) ?? 0;
            long bitsAt = maxBits > 128 ? Pointer(map + 0x20) : map + 0x10;
            var bits = new byte[(count + 7) / 8];
            var elements = new byte[count * ShopStride];
            if (!MemoryReads.IsPointer(bitsAt) || !_memory.Read(bitsAt, bits, bits.Length) || !_memory.Read(data, elements, elements.Length))
                return shops;
            for (int i = 0; i < count; i++)
            {
                if ((bits[i / 8] & (1 << (i % 8))) == 0)
                    continue;
                int at = i * ShopStride;
                int id = BitConverter.ToInt32(elements, at);
                double x = BitConverter.ToSingle(elements, at + ShopValue + ShopX), y = BitConverter.ToSingle(elements, at + ShopValue + ShopY);
                if (id <= 0 || !double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1e7 || Math.Abs(y) > 1e7)
                    continue;
                long mover = BitConverter.ToInt64(elements, at + ShopValue + ShopMover);
                Vec3? body = MemoryReads.IsPointer(mover) && Pointer(mover + ActorRoot) is long root && TryVec3(root + ComponentWorldLocation, out var p) ? p : null;
                shops.Add(new MarketShop(id, x, y, body));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException) { }
        return shops;
    }

    /// <summary>Every skill of the game by its number (m2data_C.iskillData, the same skill struct as
    /// the character's own: the names per grade first), to tell which skill a book teaches.</summary>
    public Dictionary<int, string> ReadSkillNames()
    {
        var names = new Dictionary<int, string>();
        try
        {
            if (!_located)
                return names;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return names;
            long map = instance + Offset(instanceClass, "m2data_C", "iskillData");
            long data = Pointer(map);
            int count = Int32(map + 8) ?? 0;
            if (!MemoryReads.IsPointer(data) || count <= 0 || count > 2000)
                return names;
            int maxBits = Int32(map + 0x2C) ?? 0;
            long bitsAt = maxBits > 128 ? Pointer(map + 0x20) : map + 0x10;
            var bits = new byte[(count + 7) / 8];
            if (!MemoryReads.IsPointer(bitsAt) || !_memory.Read(bitsAt, bits, bits.Length))
                return names;
            for (int i = 0; i < count; i++)
            {
                if ((bits[i / 8] & (1 << (i % 8))) == 0)
                    continue;
                long value = data + i * SkillStride + SkillValue;
                int id = Int32(value) ?? 0;
                long namesAt = Pointer(value + 0x8);
                if (id > 0 && (Int32(value + 0x10) ?? 0) > 0 && MemoryReads.IsPointer(namesAt) && ReadFString(namesAt, 64) is { Length: > 0 } name)
                    names[id] = name;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException) { }
        return names;
    }

    /// <summary>
    /// The game's own windows over the picture now: the chat line at the bottom (a tap there may open the
    /// chat history), the message window when open, and where the chat history and its X button are
    /// drawn. Null when it can't be read.
    /// </summary>
    public ScreenCover? ReadScreenCover()
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
            var areas = new List<Rectangle>();
            long chat = Pointer(ui + Offset(uiClass, "UI_C", "chatBox"));
            if (Showing(chat) && ClassName(Pointer(chat + ObjClass)) == "chatBox_C")
            {
                long main = Pointer(chat + Offset(Pointer(chat + ObjClass), "chatBox_C", "main"));
                if (Showing(main) && DrawnAt(main) is Rectangle line)
                    areas.Add(line);
            }
            long dm = Pointer(ui + Offset(uiClass, "UI_C", "dmBox"));
            long dmClass = Pointer(dm + ObjClass);
            if (MemoryReads.IsPointer(dm) && ClassName(dmClass) == "dmBox_C" &&
                Byte(dm + Offset(dmClass, "dmBox_C", "IsOpen")) is byte dmOpen && dmOpen != 0 && DrawnAt(dm) is Rectangle dmAt)
                areas.Add(dmAt);
            Point? close = null;
            Rectangle closeBounds = default;
            Rectangle? history = null;
            long adds = ui + Offset(uiClass, "UI_C", "canvasAdds");
            long data = Pointer(adds);
            int count = Int32(adds + 8) ?? 0;
            if (MemoryReads.IsPointer(data) && count is > 0 and <= 64)
                for (int i = 0; i < count; i++)
                {
                    long widget = Pointer(data + i * 8L);
                    if (!Showing(widget))
                        continue;
                    long cls = Pointer(widget + ObjClass);
                    if (ClassName(cls) == "chatHistoryWindow_C")
                    {
                        long frame = Pointer(widget + Offset(cls, "chatHistoryWindow_C", "w_window"));
                        if (DrawnAt(frame) is Rectangle at)
                        {
                            history = at;
                            long x = Pointer(frame + Offset(Pointer(frame + ObjClass), "w_window_C", "X"));
                            if (DrawnAt(x) is Rectangle button)
                            {
                                closeBounds = button;
                                close = new Point(button.X + button.Width / 2, button.Y + button.Height / 2);
                            }
                        }
                    }
                }
            return new ScreenCover(areas, history, close, closeBounds);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The shop whose window is open now, or null.</summary>
    // Zero is a closed shop. A failed/incomplete read must not authorize another input.
    internal int? ReadOpenShopId() => OpenShopId(out _, out _);

    private int? OpenShopId(out long view, out long cls)
    {
        view = cls = 0;
        if (!_located) return null;
        long instance = Pointer(_moduleBase + _gameInstance);
        long instanceClass = Pointer(instance + ObjClass);
        if (ClassName(instanceClass) != "m2data_C") return null;
        if (!_memory.TryReadInt64(instance + Offset(instanceClass, "m2data_C", "PrivateShopView"), out view)) return null;
        if (view == 0) return 0;
        if (!MemoryReads.IsPointer(view)) return null;
        cls = Pointer(view + ObjClass);
        if (ClassName(cls) != "PrivateShopView_C") return null;
        var closed = Byte(view + Offset(cls, "PrivateShopView_C", "closed"));
        if (closed == null) return null;
        if (closed != 0) return 0;
        var id = Int32(view + Offset(cls, "PrivateShopView_C", "shopId"));
        return id > 0 ? id : null;
    }

    public OpenShop? ReadOpenShop()
    {
        try
        {
            if (OpenShopId(out long view, out long cls) is not int id || id <= 0) return null;
            long list = view + Offset(cls, "PrivateShopView_C", "gotData");
            long data = Pointer(list);
            int count = Int32(list + 8) ?? 0;
            if (count < 0 || count > 500)
                return null;
            var items = new List<ShopItem>();
            if (count > 0)
            {
                var elements = new byte[count * ShopItemStride];
                if (!MemoryReads.IsPointer(data) || !_memory.Read(data, elements, elements.Length))
                    return null;
                for (int i = 0; i < count; i++)
                {
                    int at = i * ShopItemStride;
                    int vnum = BitConverter.ToInt32(elements, at);
                    if (vnum <= 0)
                        continue;
                    items.Add(new ShopItem(BitConverter.ToInt32(elements, at + ShopItemSlot), vnum,
                        Math.Max(1, BitConverter.ToInt32(elements, at + ShopItemCount)),
                        (uint)BitConverter.ToInt32(elements, at + ShopItemPrice), Bonuses(elements, at) ?? [],
                        BitConverter.ToInt32(elements, at + ShopItemSocket)));
                }
            }
            return new OpenShop(id, ReadFString(view + Offset(cls, "PrivateShopView_C", "playername"), 64),
                Int32(view + Offset(cls, "PrivateShopView_C", "playerLevel")) ?? 0,
                ReadFString(view + Offset(cls, "PrivateShopView_C", "Text"), 200), items);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }
}
