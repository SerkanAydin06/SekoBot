using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Buying potions at the "Market" NPC (9003: it sells HP and MP potions in packs of 5, 20 and 200): on every
/// trip to it (selling) the potions on the HP / MP keys are filled up when below <see cref="BotProfile.BuyPotionsBelow"/>,
/// up to <see cref="BotProfile.BuyPotionsUpTo"/>; running low without anything to sell, a trip is made for them
/// alone. The same potion as on the key is bought, by right-clicking its pack (bought at once).
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>The NPCs selling potions (the general market).</summary>
    internal static readonly HashSet<int> PotionShops = [9003];

    /// <summary>A potion to buy: the key it is on, the item, how many there are, how many are wanted.</summary>
    internal readonly record struct PotionNeed(string Kind, int Vnum, int Have, int Want);

    /// <summary>The potions on the HP / MP keys below the line (to fill up to the target).</summary>
    private List<PotionNeed> PotionNeeds(WorldSnapshot world, BotProfile p)
    {
        var needs = new List<PotionNeed>();
        if (!p.BuyPotions)
            return needs;
        foreach (var (kind, key) in new[] { ("HP", p.Keys.HpPotion), ("MP", p.Keys.MpPotion) })
            if (Potion(world, key) is { Vnum: int vnum } potion && potion.InBag < p.BuyPotionsBelow)
                needs.Add(new PotionNeed(kind, vnum, potion.InBag, p.BuyPotionsUpTo));
        return needs;
    }

    /// <summary>Running low on potions with a potion shop known on this map: a trip just for them is due.</summary>
    private bool PotionTripDue(GameState state, BotProfile p)
    {
        if (!p.BuyPotions || _selling || SellWaiting || World is not { } world || state.Dead || state.Attackers.Count > 0
            || state.Targets.Any(t => t.AttacksMe) || _brain.Traveling)
            return false;
        return PotionNeeds(world, p).Count > 0 && PotionShop(world) != null && (world.Progress?.Yang ?? 0) >= MinPotionYang;
    }

    /// <summary>Not worth the walk with less yang than this.</summary>
    internal const long MinPotionYang = 3000;

    /// <summary>The nearest known potion shop on the map.</summary>
    private NpcBook.Place? PotionShop(WorldSnapshot world) =>
        NpcBook.Shared.On(world.MapName, world.Player).FirstOrDefault(n => PotionShops.Contains(n.Vnum)
            && new Vec3(n.X, n.Y, n.Z).DistanceXY(world.Player) <= SellTripRange);

    /// <summary>
    /// At the potion shop with its window open: the potions wanted bought (the biggest pack each time, right
    /// click), as long as the bag takes them and the yang lasts. Returns what was bought, in words (empty: nothing).
    /// </summary>
    private async Task<string> BuyPotionsAsync(InputExecutor input, BotProfile p, CancellationToken token)
    {
        var bought = new List<string>();
        if (World is not { } world)
            return "";
        foreach (var need in PotionNeeds(world, p))
        {
            int have = need.Have, got = 0, fails = 0;
            long spent = 0;
            for (int round = 0; round < 25 && have < need.Want && fails < 2; round++)
            {
                token.ThrowIfCancellationRequested();
                NpcShopScreen? shop;
                lock (_worldGate)
                    shop = _world?.ReadNpcShop();
                if (shop == null)
                    break;
                // The biggest pack not overshooting much (200 / 20 / 5).
                var packs = shop.Slots.Where(s => s.Vnum == need.Vnum).OrderByDescending(s => s.Count).ToList();
                if (packs.Count == 0)
                    break; // not sold here
                long yang = World?.Progress?.Yang ?? 0;
                var pack = packs.FirstOrDefault(s => have + s.Count <= need.Want + s.Count / 2 && (long)s.Price * s.Count <= yang - p.KeepYang);
                if (pack.Vnum == 0)
                    break; // the yang doesn't reach, or the rest is too small for a pack
                input.Enqueue(new ClickAction(new Point(pack.At.X + pack.At.Width / 2, pack.At.Y + pack.At.Height / 2), Slow: true, Fast: true, Right: true));
                // Bought: the bag shows more of it within a moment.
                int before = Count(need.Vnum);
                bool came = false;
                for (int wait = 0; wait < 12 && !came; wait++)
                {
                    await Task.Delay(250, token);
                    came = Count(need.Vnum) > before;
                }
                if (!came)
                {
                    fails++;
                    continue;
                }
                int now = Count(need.Vnum);
                got += now - before;
                spent += (long)pack.Price * (now - before);
                have = now;
                fails = 0;
            }
            if (got > 0)
                bought.Add($"{got} {ItemBook.NameOf(need.Vnum)} ({spent:N0} yang)");
            else
                _log($"🧪 {ItemBook.NameOf(need.Vnum)} alınamadı (dükkânda yok, yang yetmiyor ya da çanta dolu).");
        }
        return string.Join(", ", bought);

        int Count(int vnum) => (World?.Progress?.Inventory ?? []).Where(i => i.Vnum == vnum).Sum(i => i.Count);
    }
}
