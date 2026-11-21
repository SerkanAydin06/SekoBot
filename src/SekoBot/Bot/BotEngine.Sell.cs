using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Bag full: sell instead of throwing away. The game shows "Sat" in an item's menu only next to a
/// market NPC, so the bot walks to one known on this map (<see cref="NpcBook"/>, learned by passing
/// it), clicks it (its shop opens), sells what it picked up itself and nobody asked for (item menu →
/// "Sat", "Evet" when asked), closes the windows and walks back to its place. Never sold: items ticked
/// "Topla" or on the "always pick up" names, items with a bonus of the bonus list, event, quest and
/// other special items, potions, yang and whatever the user put in the bag. When no NPC is known
/// here or the shop doesn't open, the items are thrown away as before.
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>
    /// A market NPC further than this (world units, 1 km: the whole map) isn't walked to. The way there is found
    /// across the map (around mountains, slopes, rocks), so any known one on the map will do; the nearest first.
    /// </summary>
    internal const double SellTripRange = 100000;

    /// <summary>HP below this with mobs on us during a trip: the trip is given up to fight.</summary>
    internal const double SellAbortHp = 0.35;

    /// <summary>The trip given up (dead, or about to be): its task stops, the brain takes over at once.</summary>
    private void AbortSellTrip(string why)
    {
        if (_sellCts is not { IsCancellationRequested: false } cts)
            return;
        _log($"💰 Satış gezisi bırakıldı ({why}); bot kendisi dövüşüyor / diriliyor, satış biraz sonra yeniden denenecek.");
        cts.Cancel();
        _selling = false;
        _sellStage = "";
        _sellFailedAt = _clock.Elapsed;
        _sellFailsInRow++;
        LetGoTurn();
    }

    /// <summary>How long walking to <paramref name="to"/> may take: 3 minutes, and a minute more for every 100 m.</summary>
    internal static TimeSpan WalkLimit(Vec3 from, Vec3 to) => TimeSpan.FromMinutes(3 + from.DistanceXY(to) / 10000);
    /// <summary>After a trip that sold nothing (shop didn't open…): no new one this long.</summary>
    internal static readonly TimeSpan SellRetryAfter = TimeSpan.FromMinutes(10);

    private volatile bool _selling;
    private CancellationTokenSource? _sellCts;
    private TimeSpan? _sellFailedAt;
    private int _sellFailsInRow;

    /// <summary>After a failed trip: 10 minutes, doubled with every failure in a row (at most an hour).</summary>
    internal static TimeSpan SellWaitAfter(int failsInRow) =>
        TimeSpan.FromMinutes(Math.Min(60, SellRetryAfter.TotalMinutes * Math.Pow(2, Math.Max(0, failsInRow - 1))));
    private bool SellWaiting => _sellFailedAt is { } failed && _clock.Elapsed - failed < SellWaitAfter(_sellFailsInRow);
    private string _noNpcToldMap = "";

    /// <summary>The item types never sold (quest items, skill books, boxes, stones…).</summary>
    private static readonly HashSet<string> KeptTypes = ["ITEM_QUEST", "ITEM_SKILLBOOK", "ITEM_GIFTBOX", "ITEM_UNIQUE", "ITEM_METIN", "ITEM_POLYMORPH", "ITEM_TREASURE_BOX", "ITEM_TREASURE_KEY"];

    /// <summary>
    /// Whether to sell this bag item at a market NPC: a weapon or armour the bot picked up only for its
    /// price (weapons / armour not all picked up, "≥" set, worth that much in a shop) without a bonus of
    /// the bonus list. Never what was asked for (ticked "Topla", the "always pick up" names), event, quest
    /// and other special items, the user's own things. What is to be thrown (ticked "At", cheaper junk) is
    /// still thrown away, not sold.
    /// </summary>
    internal static bool ShouldSell(InventoryItem item, BotProfile profile, IReadOnlySet<(int Slot, int Vnum)>? botPicked)
    {
        if (item.Slot < CharacterProgress.FirstBagSlot || item.Vnum <= 1 || profile.LootEquipment || profile.LootEquipmentMinGold <= 0)
            return false;
        if (botPicked == null || !botPicked.Contains((item.Slot, item.Vnum)) || profile.LootThrow.Contains(item.Vnum))
            return false;
        if (AlwaysLoot(item.Vnum, profile) || Array.IndexOf(Loot.AllEventItems, item.Vnum) >= 0 || GoodBonus(item, profile))
            return false;
        if (Loot.KindOf(item.Vnum) != LootKind.Equipment || ItemBook.Get(item.Vnum) is not { Gold: > 0 } info || KeptTypes.Contains(info.Type))
            return false;
        return info.Sell >= profile.LootEquipmentMinGold;
    }

    /// <summary>The market NPC to sell at, within reach: one a walked way leads to first (no slope in
    /// between, see <see cref="WalkedPaths"/>), else the nearest.</summary>
    private NpcBook.Place? NearShop(WorldSnapshot world)
    {
        var near = NpcBook.Shared.On(world.MapName, world.Player).Where(n => new Vec3(n.X, n.Y, n.Z).DistanceXY(world.Player) <= SellTripRange).ToList();
        return near.FirstOrDefault(n => _walked.Way(world.MapName, world.Player, new Vec3(n.X, n.Y, n.Z)) is { } way &&
                                        way.Count >= 2 && way[^2].DistanceXY(new Vec3(n.X, n.Y, n.Z)) <= 500)
               ?? near.FirstOrDefault();
    }

    /// <summary>Something to sell and a market NPC near to sell it at (not right after a failed trip).</summary>
    private bool CanSellNear(WorldSnapshot world, BotProfile p)
    {
        if (!p.SellToNpc || SellWaiting || NearShop(world) == null)
            return false;
        var picked = BotLooted;
        return (world.Progress?.Inventory ?? []).Any(i => ShouldSell(i, p, picked));
    }

    /// <summary>
    /// Inside <see cref="_botGate"/>, when the bag is to be emptied: off to a market NPC instead of throwing,
    /// when one is known near on this map and there is something to sell. The keys to let go, or null.
    /// </summary>
    private IReadOnlyList<BotAction>? TryStartSellTrip(BotProfile p)
    {
        if (!p.SellToNpc && !p.BuyPotions || _selling || World is not { } world || SellWaiting)
            return null;
        var picked = BotLooted;
        int toSell = p.SellToNpc ? (world.Progress?.Inventory ?? []).Count(i => ShouldSell(i, p, picked)) : 0;
        // Potions running low: to the shop selling them (selling there too).
        bool potions = PotionNeeds(world, p).Count > 0 && (world.Progress?.Yang ?? 0) >= MinPotionYang && PotionShop(world) != null;
        if (toSell == 0 && !potions)
            return null;
        // Going to sell anyway: to the shop with potions when any is short of the target (filled up there).
        bool topUp = p.BuyPotions && PotionNeeds(world, p, topUp: true).Count > 0 && (world.Progress?.Yang ?? 0) >= MinPotionYang;
        var npc = (potions || topUp) && PotionShop(world) is { } potionShop ? potionShop : NearShop(world);
        if (npc == null)
        {
            if (_noNpcToldMap != world.MapName)
            {
                _noNpcToldMap = world.MapName;
                _log($"💰 {world.MapName} haritasında yakında bilinen market NPC'si yok; eşyalar atılıyor. (Karakter bir market NPC'sinin yanından bir kez geçerse yeri öğrenilir.)");
            }
            return null;
        }
        _selling = true;
        _sellAsked = false;
        _sellCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var token = _sellCts.Token;
        _ = Task.Run(async () =>
        {
            string message;
            try { message = await SellTripAsync(npc, toSell, token); }
            catch (OperationCanceledException) { message = "💰 Satış durduruldu."; }
            catch (Exception ex) { message = "💰 Satış yapılamadı: " + ex.Message; }
            finally
            {
                _selling = false;
                _sellStage = "";
                LetGoTurn();
            }
            _log(message);
        });
        // Nothing held while walking there with the other keys.
        var actions = new List<BotAction> { new KeyUpAction(p.Keys.Attack) };
        foreach (var key in new[] { p.Keys.MoveForward, p.Keys.MoveBack, p.Keys.MoveLeft, p.Keys.MoveRight })
            actions.Add(new KeyUpAction(key));
        return actions;
    }

    private BagLayout? ReadBagNow()
    {
        lock (_worldGate)
            return _world?.ReadBagLayout();
    }

    private async Task<string> SellTripAsync(NpcBook.Place npc, int toSell, CancellationToken token)
    {
        var profile = Profile;
        var start = World!;
        var home = _botHome ?? start.Player;
        var at = new Vec3(npc.X, npc.Y, npc.Z);
        string name = NpcBook.Shops.GetValueOrDefault(npc.Vnum, "Market");
        _log(toSell > 0
            ? $"💰 Çantada satılacak {toSell} eşya var: {at.DistanceXY(start.Player) / 100:0} m ötedeki {name} NPC'sine gidiliyor."
            : $"🧪 İksir azaldı: {at.DistanceXY(start.Player) / 100:0} m ötedeki {name} NPC'sinden iksir almaya gidiliyor.");
        _sellStage = $"💰 Satmaya gidiliyor: {name} ({at.DistanceXY(start.Player) / 100:0} m)";
        // Walking takes no mouse: the turn is let go meanwhile, so the other bots can empty their bags
        // (seen: two bots waiting 3–4 minutes while one walked to the NPC). Taken again at the shop.
        ShrinkAfterWork(); // grown for emptying the bag just before: back in its place for the walk
        FrontTurn.Shared.LetGo();
        using var input = new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        int stuck = await WalkRouteAsync(input, at, WalkLimit(start.Player, at), token, near: 250);
        if (World is { } w && w.Player.DistanceXY(at) > 600)
            _log($"💰 {name} NPC'sine varılamadı: {w.Player.DistanceXY(at) / 100:0} m kaldı, yolda {stuck} kez takıldı.");
        // Not sold: walked back to the farm area all the same (seen: left at a wrong shop place 50 m off, below a
        // slope, the bot couldn't find its way back by itself).
        async Task<string> FailedAway(string why)
        {
            string message = Failed(why);
            if (World is { } away && away.MapName == start.MapName && away.Player.DistanceXY(home) > 600)
            {
                _log($"💰 Satış olmadı; alana dönülüyor ({away.Player.DistanceXY(home) / 100:0} m).");
                _sellStage = "💰 Satış olmadı; alana dönülüyor";
                await WalkRouteAsync(input, home, WalkLimit(away.Player, home), token, near: 300);
            }
            return message;
        }
        if (World is not { } there || there.MapName != start.MapName || there.Player.DistanceXY(at) > 600)
            return await FailedAway("NPC'ye varılamadı");

        // Click the NPC: its shop opens (seen in the game: straight away).
        async Task<bool> OpenShopAsync()
        {
        bool open = false;
        for (int tries = 0; tries < 4 && !open; tries++)
        {
            token.ThrowIfCancellationRequested();
            if (World is not { Camera: { } camera } world || State is not { } state)
            {
                await Task.Delay(300, token);
                continue;
            }
            var npcAt = (world.TownNpcs ?? []).Where(n => n.Vnum == npc.Vnum).Select(n => (Vec3?)n.Position)
                .OrderBy(p => p!.Value.DistanceXY(at)).FirstOrDefault() ?? at;
            // A little above its feet, a bit more each try.
            if (camera.ToScreen(npcAt with { Z = npcAt.Z + 80 + tries * 40 }, state.Width, state.Height) is PointF p && OnPlayfield(p, state.Width, state.Height))
                input.Enqueue(new ClickAction(Point.Round(p), Slow: true));
            for (int wait = 0; wait < 12 && !open; wait++)
            {
                await Task.Delay(250, token);
                open = ReadBagNow() is { NpcShop: > 0 };
            }
        }
        return open;
        }
        if (!await WaitForTurnAsync(token))
            return Failed("sıra gelmedi (başka bir bot uzun süre fareyi kullandı)");
        // A small game cuts the bag and the shop off: made big for the sale, a moment for its screens to settle.
        while (GrowForWork())
            await Task.Delay(200, token);
        if (!await OpenShopAsync())
        {
            ShrinkAfterWork();
            FrontTurn.Shared.LetGo();
            // Nobody of that kind standing there (a wrong place, e.g. read off the M map wrongly): forgotten for every bot.
            if (World is { } here && !(here.TownNpcs ?? []).Any(n => n.Vnum == npc.Vnum && n.Position.DistanceXY(at) < 1500))
            {
                NpcBook.Shared.Forget(here.MapName, npc.Vnum, npc.X, npc.Y);
                NpcBook.Shared.Save();
                _log($"💰 Orada {name} yok; bu yer satıcı listesinden silindi.");
            }
            return await FailedAway($"{name} dükkanı açılamadı");
        }
        int reopened = 0, soldBefore = 0;
        _log($"💰 {name} dükkanı açıldı; eşyalar tek tek satılıyor (Sat → Evet).");
        _sellStage = $"💰 {name} dükkanında eşyalar satılıyor";

        // Potions first, while the shop is surely open (seen: closed after the sale and the buying skipped).
        string bought = "";
        if (PotionShops.Contains(npc.Vnum) && ReadBagNow() is { NpcShop: > 0 })
        {
            _sellStage = $"🧪 {name} dükkanından iksir alınıyor";
            input.HoldFront = true;
            try { bought = await BuyPotionsAsync(input, profile, token); }
            finally { input.HoldFront = false; }
            if (bought.Length > 0)
                _log($"🧪 İksir alındı: {bought}.");
            _sellStage = $"💰 {name} dükkanında eşyalar satılıyor";
        }

        // Sell, item by item, with the game in front the whole time (not back and forth).
        var cleaner = new BagCleaner { Selling = true };
        cleaner.Start(_clock.Elapsed);
        input.HoldFront = true;
        try
        {
            var began = _clock.Elapsed;
            while (cleaner.Active && _clock.Elapsed - began < TimeSpan.FromMinutes(4))
            {
                token.ThrowIfCancellationRequested();
                // The shop window closed by itself (seen): the inventory question is let go, the NPC clicked again.
                if (ReadBagNow() is { NpcShop: 0 } && reopened < 3)
                {
                    reopened++;
                    _log($"💰 {name} penceresi kapandı; yeniden açılıyor ({reopened}. kez).");
                    input.HoldFront = false;
                    input.Enqueue(new KeyTapAction(profile.Keys.CloseWindow));
                    await Task.Delay(600, token);
                    if (ReadBagNow() is { Open: true })
                    {
                        input.Enqueue(new KeyTapAction(profile.Keys.Inventory));
                        await Task.Delay(500, token);
                    }
                    if (!await OpenShopAsync())
                        break;
                    input.HoldFront = true;
                    soldBefore += cleaner.Thrown;
                    cleaner.Start(_clock.Elapsed);
                    continue;
                }
                var picked = BotLooted;
                var junk = BotPicked;
                var full = (World?.Progress?.Inventory ?? []).Where(i => i.Slot >= CharacterProgress.FirstBagSlot)
                    .GroupBy(i => (i.Slot, i.Vnum)).ToDictionary(g => g.Key, g => g.First());
                var state = State;
                foreach (var action in cleaner.Tick(_clock.Elapsed, ReadBagNow(),
                             // At the shop the bag is emptied too: what would be thrown is sold instead (yang rather than the ground).
                             i => full.GetValueOrDefault((i.Slot, i.Vnum), new InventoryItem(i.Slot, i.Vnum, i.Count)) is var item
                                  && (ShouldSell(item, profile, picked) || ShouldThrow(item, profile, junk)),
                             profile, new Size(state?.Width ?? 1920, state?.Height ?? 1080)))
                    input.Enqueue(action);
                foreach (var (vnum, count) in cleaner.ThrownItems)
                    _sessionItems.Sold(vnum, count);
                cleaner.ThrownItems.Clear();
                await Task.Delay(80, token);
            }
        }
        finally { input.HoldFront = false; }
        _sessionItems.Save();
        int sold = soldBefore + cleaner.Thrown;
        // Still short (the bag was full before the sale): once more, when the shop is still open.
        if (PotionShops.Contains(npc.Vnum) && World is { } after && PotionNeeds(after, profile, topUp: true).Count > 0 && ReadBagNow() is { NpcShop: > 0 })
        {
            string more = await BuyPotionsAsync(input, profile, token);
            if (more.Length > 0)
            {
                _log($"🧪 İksir alındı: {more}.");
                bought = bought.Length > 0 ? bought + ", " + more : more;
            }
        }

        // Close the shop (the inventory was closed by the cleaner), then back to the place.
        for (int i = 0; i < 3 && ReadBagNow() is { NpcShop: > 0 }; i++)
        {
            input.Enqueue(new KeyTapAction(profile.Keys.CloseWindow));
            await Task.Delay(800, token);
        }
        // Nothing of the sale left on the screen (a question, the bag, an item's menu), then the turn to the
        // others while walking back.
        await TidyAsync(input, profile, token);
        ShrinkAfterWork();
        FrontTurn.Shared.LetGo();
        _sellStage = "💰 Satış bitti; alana dönülüyor";
        await WalkRouteAsync(input, home, WalkLimit(World?.Player ?? at, home), token, near: 300);
        if (sold == 0 && bought.Length > 0)
        {
            _sellFailsInRow = 0;
            return "🧪 İksir alındı; alana dönüldü, avlanmaya devam.";
        }
        if (sold == 0)
            return Failed("satılacak eşya satılamadı" + (cleaner.Problem is { } why ? $" ({why})" : ""));
        lock (_botGate)
            _brain.WantsClean = false;
        _cleanTries = 0;
        _sellFailsInRow = 0;
        string done = $"💰 {sold} eşya {name} NPC'sine satıldı; alana dönüldü, avlanmaya devam.";
        if (profile.NotifySells)
            Notify(done);
        return done;
    }

    /// <summary>Closes what a sale left open (see <see cref="LeftOpen"/>) until the screen stays clean a second, 8 s at most.</summary>
    private async Task TidyAsync(InputExecutor input, BotProfile profile, CancellationToken token)
    {
        var until = DateTime.Now + TidyFor;
        DateTime clearSince = DateTime.MinValue, actedAt = DateTime.MinValue;
        int closed = 0;
        while (DateTime.Now < until)
        {
            if (ReadBagNow() is not { } bag || !LeftOpen(bag))
            {
                if (clearSince == DateTime.MinValue)
                    clearSince = DateTime.Now;
                if (DateTime.Now - clearSince >= TidyClearFor)
                    break;
            }
            else
            {
                clearSince = DateTime.MinValue;
                if (DateTime.Now - actedAt >= TimeSpan.FromMilliseconds(1200))
                {
                    actedAt = DateTime.Now;
                    closed++;
                    input.Enqueue(TidyAction(bag, profile, selling: true));
                }
            }
            await Task.Delay(200, token);
        }
        if (closed > 0)
            _log("💰 Satıştan sonra ekranda açık kalan pencere kapatıldı.");
    }

    /// <summary>At the NPC: the turn to use the mouse, waited for (another bot emptying its bag takes a minute or so).</summary>
    private async Task<bool> WaitForTurnAsync(CancellationToken token)
    {
        var since = _clock.Elapsed;
        while (!TakeTurn())
        {
            if (_clock.Elapsed - since > FrontTurn.MaxHold)
                return false;
            await Task.Delay(1000, token);
        }
        return true;
    }

    private bool _toldWalkedWay;

    /// <summary>Mobs on the way are passed this far off (world units, 3 m).</summary>
    internal const double PassMobsBy = 300;
    /// <summary>A town NPC is passed this far off (world units): not walked into.</summary>
    internal const double PassNpcsBy = 120;

    /// <summary>
    /// A long walk (to a market NPC and back) on a planned route: around the map's walls, houses, rocks
    /// and trees at the character's height (slopes too high, a drop: see <see cref="MapTerrain"/>), the
    /// places the bots got stuck at before (<see cref="ObstacleMap"/>), the places they couldn't reach
    /// (cliffs, water: <see cref="NoGoMap"/>) and the living mobs near the way. The route is drawn again
    /// every second (mobs move). Stuck: the spot is noted as an obstacle (for every bot), the character
    /// steps back and the route is drawn around it. Returns how many times it got stuck.
    /// </summary>
    private async Task<int> WalkRouteAsync(InputExecutor input, Vec3 target, TimeSpan limit, CancellationToken token, double near)
    {
        var keys = Profile.Keys;
        var held = new HashSet<Keys>();
        var started = _clock.Elapsed;
        var lastMove = started;
        var stuckHere = new List<MapTerrain.Circle>();
        Vec3? last = null;
        List<Vec3>? way = null, walked = null;
        var wayAt = TimeSpan.FromDays(-1);
        int stuck = 0;
        void Let(HashSet<Keys> want)
        {
            foreach (var k in held.Where(k => !want.Contains(k)).ToList())
            {
                input.Enqueue(new KeyUpAction(k));
                held.Remove(k);
            }
            foreach (var k in want.Where(held.Add))
                input.Enqueue(new KeyDownAction(k));
        }
        try
        {
            while (_clock.Elapsed - started < limit)
            {
                token.ThrowIfCancellationRequested();
                if (World is not { Camera: { } camera } world)
                {
                    await Task.Delay(250, token);
                    continue;
                }
                var me = world.Player;
                if (me.DistanceXY(target) < near)
                    break;
                if (last is Vec3 l && me.DistanceXY(l) > 30)
                    lastMove = _clock.Elapsed;
                else if (_clock.Elapsed - lastMove > TimeSpan.FromSeconds(1.5))
                {
                    // Held up: noted where (just ahead), a step back, and a route around it.
                    stuck++;
                    var ahead = way is { Count: > 0 } ? _terrain.Of(world.MapName)?.Aim(way, me) ?? target : target;
                    double d = Math.Max(1, me.DistanceXY(ahead));
                    double sx = me.X + (ahead.X - me.X) / d * ObstacleMap.Ahead, sy = me.Y + (ahead.Y - me.Y) / d * ObstacleMap.Ahead;
                    _obstacles.Stuck(world.MapName, sx, sy, DateTime.Now);
                    stuckHere.Add(new MapTerrain.Circle(sx, sy, 60 + 30 * Math.Min(4, stuckHere.Count(c => Math.Abs(c.X - sx) < 150 && Math.Abs(c.Y - sy) < 150))));
                    Let([keys.MoveBack]);
                    await Task.Delay(450, token);
                    Let([]);
                    way = walked = null;
                    lastMove = _clock.Elapsed;
                    last = null;
                    continue;
                }
                last = me;
                // Far off the way is found less often (across the map it takes a moment: worked out aside, the walking goes on).
                if (way == null || _clock.Elapsed - wayAt > TimeSpan.FromSeconds(me.DistanceXY(target) > 6000 ? 4 : 1))
                {
                    // A way walked before (over the slopes up to the town…) first; else drawn around what is known.
                    // The map's roads known: along them (the M map's roads); else a way walked before.
                    walked = _terrain.Of(world.MapName)?.Roads != null ? null : _walked.Way(world.MapName, me, target, stuckHere);
                    var avoid = stuckHere.ToList();
                    way = walked ?? await Task.Run(() => Route(world, target, avoid), token);
                    wayAt = _clock.Elapsed;
                    if (walked != null && !_toldWalkedWay)
                    {
                        _toldWalkedWay = true;
                        _log("💰 Daha önce yürünmüş yol biliniyor; onun üzerinden gidiliyor (yokuş, tepe…).");
                    }
                }
                Vec3 aim;
                if (walked is { Count: > 0 })
                {
                    // Along the walked cells: the one after the nearest.
                    int k = Enumerable.Range(0, walked.Count).MinBy(i => walked[i].DistanceXY(me));
                    aim = walked[Math.Min(k + 1, walked.Count - 1)];
                }
                else
                    aim = way is { Count: > 0 } && _terrain.Of(world.MapName) is { } map ? map.Aim(way, me) : target;
                var (right, up) = camera.ToScreenDirection(aim.X - me.X, aim.Y - me.Y);
                double length = Math.Max(1e-6, Math.Sqrt(right * right + up * up));
                double kx = right / length, ky = -up / length;
                var want = new HashSet<Keys>();
                if (kx > 0.38) want.Add(keys.MoveRight);
                if (kx < -0.38) want.Add(keys.MoveLeft);
                if (ky > 0.38) want.Add(keys.MoveBack);
                if (ky < -0.38) want.Add(keys.MoveForward);
                Let(want);
                await Task.Delay(200, token);
            }
        }
        finally
        {
            foreach (var k in held)
                input.Enqueue(new KeyUpAction(k));
            await Task.Delay(300, CancellationToken.None);
        }
        return stuck;
    }

    /// <summary>The route to <paramref name="target"/> around everything known (see <see cref="WalkRouteAsync"/>);
    /// when there is none, the mobs, then the learned places are left out. Null: straight on.</summary>
    private List<Vec3>? Route(WorldSnapshot world, Vec3 target, IReadOnlyList<MapTerrain.Circle> stuckHere)
    {
        if (_terrain.Of(world.MapName) is not { } map)
            return null;
        var me = world.Player;
        double reach = me.DistanceXY(target) + 3000;
        var learned = _obstacles.Near(world.MapName, me, reach).Select(o => new MapTerrain.Circle(o.X, o.Y, 60))
            .Concat(_noGo.Places(world.MapName, DateTime.Now).Where(p => Math.Abs(p.X - me.X) < reach && Math.Abs(p.Y - me.Y) < reach)
                .Select(p => new MapTerrain.Circle(p.X, p.Y, NoGoMap.Radius * 0.6)))
            .Concat(stuckHere).ToList();
        var mobs = world.Mobs.Where(m => m.Hp > 0 && m.Position.DistanceXY(me) < reach)
            .Select(m => new MapTerrain.Circle(m.Position.X, m.Position.Y, PassMobsBy))
            // The town's NPCs too (the one we go to stands in the goal's circle and doesn't count).
            .Concat((world.TownNpcs ?? []).Where(n => n.Position.DistanceXY(me) < reach)
                .Select(n => new MapTerrain.Circle(n.Position.X, n.Position.Y, PassNpcsBy))).ToList();
        return map.WayTo(me, target, avoid: [.. learned, .. mobs], roads: true)
               ?? map.WayTo(me, target, avoid: learned, roads: true)
               ?? map.WayTo(me, target, avoid: stuckHere)
               ?? map.WayTo(me, target);
    }

    private string Failed(string why)
    {
        _sellFailedAt = _clock.Elapsed;
        _sellFailsInRow++;
        int wait = (int)SellWaitAfter(_sellFailsInRow).TotalMinutes;
        if (_sellFailsInRow == 3)
            Notify($"💰 {_world?.CharacterName} satıcıya 3 kez üst üste gidemedi ({why}). Çanta dolu kalabilir; yolu bir kez elle yürütmek işe yarar.");
        return $"💰 Satılamadı: {why}. Eşyalar eskisi gibi atılacak; {wait} dakika satış denenmeyecek.";
    }
}
