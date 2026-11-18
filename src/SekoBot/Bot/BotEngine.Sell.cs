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
    /// <summary>A market NPC further than this (world units, 300 m) isn't walked to.</summary>
    internal const double SellTripRange = 30000;
    /// <summary>After a trip that sold nothing (shop didn't open…): no new one this long.</summary>
    internal static readonly TimeSpan SellRetryAfter = TimeSpan.FromMinutes(10);

    private volatile bool _selling;
    private CancellationTokenSource? _sellCts;
    private TimeSpan? _sellFailedAt;
    private string _noNpcToldMap = "";

    /// <summary>The item types never sold (quest items, skill books, boxes, stones…).</summary>
    private static readonly HashSet<string> KeptTypes = ["ITEM_QUEST", "ITEM_SKILLBOOK", "ITEM_GIFTBOX", "ITEM_UNIQUE", "ITEM_METIN", "ITEM_POLYMORPH", "ITEM_TREASURE_BOX", "ITEM_TREASURE_KEY"];

    /// <summary>Whether to sell this bag item at a market NPC: something the bot picked up that wasn't asked
    /// for (or ticked "At"), worth something in a shop; never what is kept (see the class).</summary>
    internal static bool ShouldSell(InventoryItem item, BotProfile profile, IReadOnlySet<(int Slot, int Vnum)>? botPicked)
    {
        if (item.Slot < CharacterProgress.FirstBagSlot || item.Vnum <= 1)
            return false;
        if (AlwaysLoot(item.Vnum, profile) || Array.IndexOf(Loot.AllEventItems, item.Vnum) >= 0 || GoodBonus(item, profile))
            return false;
        if (ItemBook.Get(item.Vnum) is not { Gold: > 0 } info || KeptTypes.Contains(info.Type))
            return false;
        if (profile.LootThrow.Contains(item.Vnum))
            return true;
        if (botPicked == null || !botPicked.Contains((item.Slot, item.Vnum)))
            return false;
        return Loot.KindOf(item.Vnum) is LootKind.Equipment or LootKind.Other;
    }

    /// <summary>The market NPC known on this map nearest to the character, within reach.</summary>
    private static NpcBook.Place? NearShop(WorldSnapshot world) =>
        NpcBook.Shared.On(world.MapName, world.Player).FirstOrDefault(n => new Vec3(n.X, n.Y, n.Z).DistanceXY(world.Player) <= SellTripRange);

    /// <summary>Something to sell and a market NPC near to sell it at (not right after a failed trip).</summary>
    private bool CanSellNear(WorldSnapshot world, BotProfile p)
    {
        if (!p.SellToNpc || _sellFailedAt is { } failed && _clock.Elapsed - failed < SellRetryAfter || NearShop(world) == null)
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
        if (!p.SellToNpc || _selling || World is not { } world || _sellFailedAt is { } failed && _clock.Elapsed - failed < SellRetryAfter)
            return null;
        var picked = BotLooted;
        int toSell = (world.Progress?.Inventory ?? []).Count(i => ShouldSell(i, p, picked));
        if (toSell == 0)
            return null;
        var npc = NearShop(world);
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
        _sellCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var token = _sellCts.Token;
        _ = Task.Run(async () =>
        {
            string message;
            try { message = await SellTripAsync(npc, toSell, token); }
            catch (OperationCanceledException) { message = "💰 Satış durduruldu."; }
            catch (Exception ex) { message = "💰 Satış yapılamadı: " + ex.Message; }
            finally { _selling = false; }
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
        _log($"💰 Çantada satılacak {toSell} eşya var: {at.DistanceXY(start.Player) / 100:0} m ötedeki {name} NPC'sine gidiliyor.");
        using var input = new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        int stuck = await WalkRouteAsync(input, at, TimeSpan.FromMinutes(3), token, near: 250);
        if (World is { } w && w.Player.DistanceXY(at) > 600)
            _log($"💰 {name} NPC'sine varılamadı: {w.Player.DistanceXY(at) / 100:0} m kaldı, yolda {stuck} kez takıldı.");
        if (World is not { } there || there.MapName != start.MapName || there.Player.DistanceXY(at) > 600)
            return Failed("NPC'ye varılamadı");

        // Click the NPC: its shop opens (seen in the game: straight away).
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
        if (!open)
            return Failed($"{name} dükkanı açılamadı");
        _log($"💰 {name} dükkanı açıldı; eşyalar tek tek satılıyor (Sat → Evet).");

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
                var picked = BotLooted;
                var full = (World?.Progress?.Inventory ?? []).Where(i => i.Slot >= CharacterProgress.FirstBagSlot)
                    .GroupBy(i => (i.Slot, i.Vnum)).ToDictionary(g => g.Key, g => g.First());
                var state = State;
                foreach (var action in cleaner.Tick(_clock.Elapsed, ReadBagNow(),
                             i => ShouldSell(full.GetValueOrDefault((i.Slot, i.Vnum), new InventoryItem(i.Slot, i.Vnum, i.Count)), profile, picked),
                             profile, new Size(state?.Width ?? 1920, state?.Height ?? 1080)))
                    input.Enqueue(action);
                foreach (var (vnum, count) in cleaner.ThrownItems)
                    _sessionItems.Thrown(vnum, count);
                cleaner.ThrownItems.Clear();
                await Task.Delay(150, token);
            }
        }
        finally { input.HoldFront = false; }
        _sessionItems.Save();
        int sold = cleaner.Thrown;

        // Close the shop (the inventory was closed by the cleaner), then back to the place.
        for (int i = 0; i < 3 && ReadBagNow() is { NpcShop: > 0 }; i++)
        {
            input.Enqueue(new KeyTapAction(profile.Keys.CloseWindow));
            await Task.Delay(800, token);
        }
        await WalkRouteAsync(input, home, TimeSpan.FromMinutes(3), token, near: 300);
        if (sold == 0)
            return Failed("satılacak eşya satılamadı" + (cleaner.Problem is { } why ? $" ({why})" : ""));
        lock (_botGate)
            _brain.WantsClean = false;
        _cleanTries = 0;
        string done = $"💰 {sold} eşya {name} NPC'sine satıldı; alana dönüldü, avlanmaya devam.";
        Notify(done);
        return done;
    }

    private bool _toldWalkedWay;

    /// <summary>Mobs on the way are passed this far off (world units, 3 m).</summary>
    internal const double PassMobsBy = 300;

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
                if (way == null || _clock.Elapsed - wayAt > TimeSpan.FromSeconds(1))
                {
                    // A way walked before (over the slopes up to the town…) first; else drawn around what is known.
                    walked = _walked.Way(world.MapName, me, target);
                    way = walked ?? Route(world, target, stuckHere);
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
            .Select(m => new MapTerrain.Circle(m.Position.X, m.Position.Y, PassMobsBy)).ToList();
        return map.WayTo(me, target, avoid: [.. learned, .. mobs])
               ?? map.WayTo(me, target, avoid: learned)
               ?? map.WayTo(me, target, avoid: stuckHere)
               ?? map.WayTo(me, target);
    }

    private string Failed(string why)
    {
        _sellFailedAt = _clock.Elapsed;
        return $"💰 Satılamadı: {why}. Eşyalar eskisi gibi atılacak; 10 dakika satış denenmeyecek.";
    }
}
