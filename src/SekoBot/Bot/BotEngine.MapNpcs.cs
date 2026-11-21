using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// The whole map's NPCs from the game's M map: the game keeps only the NPCs within ~50 m, but its M map
/// marks every one (see <see cref="GameWorldReader.ReadMapNpcs"/>). Whenever the M map is open (the user
/// opened it, or the bot) its shops are noted for selling (shared, npc-yerleri.json). While the bot runs,
/// it opens the M map itself for a moment once per map (when calm) and closes it again.
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>The maps whose M map was read this session.</summary>
    private readonly HashSet<string> _mapNpcsRead = [];
    private readonly Dictionary<string, int> _mapPeeks = [];
    private TimeSpan _mapNpcsLookedAt = TimeSpan.FromDays(-1);
    /// <summary>The bot's own look at the M map: when the key was pressed (null: not looking).</summary>
    private DateTime? _mapPeekSince;
    private DateTime _mapPeekLastTry = DateTime.MinValue;
    private DateTime _mapSeenSince = DateTime.MinValue;
    private string _mapSeenName = "";

    /// <summary>How long the bot waits for the M map to show.</summary>
    internal static readonly TimeSpan MapPeekWait = TimeSpan.FromSeconds(2.5);
    /// <summary>On a map this long first (just arrived, loading) before the bot opens the M map.</summary>
    internal static readonly TimeSpan MapPeekAfter = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan MapPeekRetry = TimeSpan.FromMinutes(5);
    internal const int MapPeekTries = 3;

    /// <summary>Every ~2 s: when the M map is open, its NPCs are read (also while the bot is stopped).</summary>
    private void WatchMapNpcs(WorldSnapshot world, TimeSpan now)
    {
        if (world.MapName != _mapSeenName)
        {
            _mapSeenName = world.MapName;
            _mapSeenSince = DateTime.Now;
        }
        if (now - _mapNpcsLookedAt < TimeSpan.FromSeconds(2) || _mapPeekSince != null)
            return;
        _mapNpcsLookedAt = now;
        ReadMapNpcsNow(world);
    }

    /// <summary>Reads the open M map's NPCs; false when it isn't open.</summary>
    private bool ReadMapNpcsNow(WorldSnapshot world)
    {
        List<MapNpc>? npcs;
        lock (_worldGate)
            npcs = _world?.ReadMapNpcs();
        if (npcs == null)
            return false;
        string map = world.MapName;
        if (map.Length == 0 || npcs.Count == 0)
            return true;
        var slopes = _terrain.SlopesOf(map);
        var found = new List<string>();
        foreach (var npc in npcs)
        {
            double z = slopes?.Ground(npc.X, npc.Y) is double g ? g + MapTerrain.FeetBelow : world.Player.Z;
            if (NpcBook.Shared.Saw(map, npc.Vnum, new Vec3(npc.X, npc.Y, z), DateTime.Now))
                found.Add($"{NpcBook.Shops[npc.Vnum]} ({new Vec3(npc.X, npc.Y, z).DistanceXY(world.Player) / 100:0} m)");
        }
        NpcBook.Shared.Save();
        if (_mapNpcsRead.Add(map))
            _log(found.Count > 0
                ? $"🗺 M haritasından {map} haritasının NPC'leri okundu ({npcs.Count} NPC); yeni öğrenilen satıcılar: {string.Join(", ", found)}."
                : $"🗺 M haritasından {map} haritasının NPC'leri okundu ({npcs.Count} NPC, {NpcBook.Shared.On(map).Count} satıcı biliniyor).");
        return true;
    }

    /// <summary>
    /// Inside <see cref="_botGate"/>: the bot opening the M map for a moment to learn the map's NPCs (once per
    /// map, when nothing is going on). The actions for this tick, or null when the brain should tick as usual.
    /// </summary>
    private IReadOnlyList<BotAction>? MapPeekTick(GameState state, BotProfile p)
    {
        var now = DateTime.Now;
        if (_mapPeekSince is DateTime since)
        {
            if (World is { } open && ReadMapNpcsNow(open))
            {
                _mapPeekSince = null;
                return [new KeyTapAction(p.Keys.Map)];  // closed again
            }
            if (now - since < MapPeekWait && state.Attackers.Count == 0 && !state.Dead)
                return [];
            // Didn't show (another key? a window in the way?): not pressed again, tried later.
            _mapPeekSince = null;
            return null;
        }
        if (World is not { } world || world.MapName.Length == 0 || _mapNpcsRead.Contains(world.MapName) || IsLoginScreen(world.MapName)
            || world.MapName.StartsWith('_') || now - _mapSeenSince < MapPeekAfter || now - _mapPeekLastTry < MapPeekRetry
            || _mapPeeks.GetValueOrDefault(world.MapName) >= MapPeekTries)
            return null;
        if (state.Dead || state.Attackers.Count > 0 || state.Targets.Any(t => t.AttacksMe) || _selling || _tidying || _cleaner.Active)
            return null;
        _mapPeeks[world.MapName] = _mapPeeks.GetValueOrDefault(world.MapName) + 1;
        _mapPeekLastTry = now;
        _mapPeekSince = now;
        var actions = new List<BotAction> { new KeyUpAction(p.Keys.Attack) };
        foreach (var key in new[] { p.Keys.MoveForward, p.Keys.MoveBack, p.Keys.MoveLeft, p.Keys.MoveRight })
            actions.Add(new KeyUpAction(key));
        actions.Add(new KeyTapAction(p.Keys.Map));
        return actions;
    }
}
