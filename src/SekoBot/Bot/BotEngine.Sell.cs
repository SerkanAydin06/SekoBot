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
        int stuck = await WalkFarAsync(input, at, TimeSpan.FromMinutes(3), token, near: 250);
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
        await WalkFarAsync(input, home, TimeSpan.FromMinutes(3), token, near: 300);
        if (sold == 0)
            return Failed("satılacak eşya satılamadı" + (cleaner.Problem is { } why ? $" ({why})" : ""));
        lock (_botGate)
            _brain.WantsClean = false;
        _cleanTries = 0;
        string done = $"💰 {sold} eşya {name} NPC'sine satıldı; alana dönüldü, avlanmaya devam.";
        Notify(done);
        return done;
    }

    /// <summary>
    /// A long walk (to a market NPC and back): <see cref="WalkToAsync"/> stops at the first thing it gets
    /// stuck on, so here, stuck, the character steps back and aside (left, then right, further each time)
    /// and goes on. The number of times it got stuck.
    /// </summary>
    private async Task<int> WalkFarAsync(InputExecutor input, Vec3 target, TimeSpan limit, CancellationToken token, double near)
    {
        var keys = Profile.Keys;
        var started = _clock.Elapsed;
        int stuck = 0;
        while (_clock.Elapsed - started < limit)
        {
            token.ThrowIfCancellationRequested();
            await WalkToAsync(input, target, limit - (_clock.Elapsed - started), token, () => { }, near);
            if (World is not { } world || world.Player.DistanceXY(target) < near)
                break;
            stuck++;
            // Back a little, then sideways (the side changes each time, the step grows).
            var side = stuck % 2 == 1 ? keys.MoveLeft : keys.MoveRight;
            int ms = Math.Min(2000, 500 + stuck * 150);
            input.Enqueue(new KeyDownAction(keys.MoveBack));
            await Task.Delay(400, token);
            input.Enqueue(new KeyUpAction(keys.MoveBack));
            input.Enqueue(new KeyDownAction(side));
            await Task.Delay(ms, token);
            input.Enqueue(new KeyUpAction(side));
            await Task.Delay(150, token);
        }
        return stuck;
    }

    private string Failed(string why)
    {
        _sellFailedAt = _clock.Elapsed;
        return $"💰 Satılamadı: {why}. Eşyalar eskisi gibi atılacak; 10 dakika satış denenmeyecek.";
    }
}
