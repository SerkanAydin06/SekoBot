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
    /// <summary>When the map was read (then closed a moment later) and when the map key was last pressed to close it.</summary>
    private DateTime? _mapPeekReadAt, _mapCloseTapAt;
    private int _mapCloseTaps;
    private DateTime _mapPeekLastTry = DateTime.MinValue;
    private DateTime _mapSeenSince = DateTime.MinValue;
    private string _mapSeenName = "";

    /// <summary>
    /// After reading, the map is closed this much later: pressed right as it opened, the key was ignored (seen
    /// on 4 bots: the map stayed open, and Esc — which doesn't close it — opened the game menu).
    /// </summary>
    internal static readonly TimeSpan MapCloseAfter = TimeSpan.FromMilliseconds(800);
    /// <summary>Still open this long after the key: pressed again (at most <see cref="MapCloseTaps"/> times).</summary>
    internal static readonly TimeSpan MapCloseCheck = TimeSpan.FromMilliseconds(1500);
    internal const int MapCloseTaps = 3;

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
        if (now - _mapNpcsLookedAt < TimeSpan.FromSeconds(2) || _mapPeekSince != null || _mapPeekReadAt != null)
            return;
        _mapNpcsLookedAt = now;
        // The user opened the M map in a big game: the roads are read too.
        if (ReadMapNpcsNow(world) && _terrain.RoadsOf(world.MapName) == null)
        {
            MapPicture? picture;
            lock (_worldGate)
                picture = _world?.ReadMapPicture();
            if (picture != null && Math.Min(picture.Drawn.Width, picture.Drawn.Height) >= RoadPictureMin && ReadRoads(world.MapName, picture) != null)
                _log($"🛣 {world.MapName} haritasının yolları açık M haritasından okundu; uzun yürüyüşlerde yollar kullanılacak.");
        }
    }

    // ── The roads, off the M map's picture ──
    private bool _roadsDone, _roadsGrown;
    private DateTime _roadsSince = DateTime.MinValue, _roadsGrownAt;
    private Rectangle _roadsFrom;
    /// <summary>The M map's picture this big (pixels) at least to tell its roads (in a 640×480 game it is ~120: grown for it).</summary>
    internal const int RoadPictureMin = 300;

    /// <summary>One step of reading the roads off the open M map; null when done (or nothing to do).</summary>
    private IReadOnlyList<BotAction>? RoadTick(WorldSnapshot world, DateTime now)
    {
        if (_roadsDone || _terrain.RoadsOf(world.MapName) != null)
        {
            _roadsDone = true;
            return null;
        }
        if (_roadsSince == DateTime.MinValue)
            _roadsSince = now;
        if (now - _roadsSince > TimeSpan.FromSeconds(20))
            return Give("sıra gelmedi ya da harita küçük kaldı; sonra yeniden denenecek");
        // Growing the game takes the screen: one bot at a time.
        if (!TakeTurn())
            return [];
        MapPicture? picture;
        lock (_worldGate)
            picture = _world?.ReadMapPicture();
        if (picture == null)
            return [];
        if (Math.Min(picture.Drawn.Width, picture.Drawn.Height) < RoadPictureMin)
        {
            if (_roadsGrown)
                return now - _roadsGrownAt < TimeSpan.FromSeconds(6) ? [] : Give("harita küçük kaldı");
            _roadsFrom = UI.WindowLayout.Visible(_window.Handle);
            UI.WindowLayout.FillWithGame(_window.Handle, 40);
            _roadsGrown = true;
            _roadsGrownAt = now;
            return [];
        }
        // A moment for the picture to be drawn at its new size — and captured at it (the capture follows a moment later).
        if (_roadsGrown && now - _roadsGrownAt < TimeSpan.FromSeconds(2))
            return [];
        bool captured = WithLatestFrame(f => f.Width >= picture.Drawn.Right && f.Height >= picture.Drawn.Bottom);
        if (!captured && now - _roadsGrownAt < TimeSpan.FromSeconds(10))
            return [];
        if (ReadRoads(world.MapName, picture) is null)
            return Give(captured ? "harita resminde yol rengi bulunamadı" : "büyük ekran görüntüsü gelmedi");
        _log($"🛣 {world.MapName} haritasının yolları M haritasından okundu; uzun yürüyüşlerde yollar kullanılacak.");
        EndRoads();
        return null;

        IReadOnlyList<BotAction>? Give(string why)
        {
            _log($"🛣 Haritadaki yollar okunamadı ({why}).");
            EndRoads();
            return null;
        }
    }

    /// <summary>The roads off the picture in the newest frame; kept (on disk, for every SekoBot) when they look right.</summary>
    private RoadMap? ReadRoads(string map, MapPicture picture)
    {
        var roads = WithLatestFrame(frame => RoadMap.FromPicture(map, frame, picture.Drawn, picture.OffsetX, picture.OffsetY,
            picture.ScaleX, picture.ScaleY, picture.Texture));
        if (roads != null)
            _terrain.SetRoads(roads);
        return roads;
    }

    private void EndRoads()
    {
        _roadsDone = true;
        _roadsSince = DateTime.MinValue;
        if (_roadsGrown)
        {
            _roadsGrown = false;
            UI.WindowLayout.PutBack(_window.Handle, _roadsFrom);
        }
        LetGoTurn();
    }

    /// <summary>
    /// Whether the M map shows now: the main UI's map (UI_C.bigMap) is set only while it is open. The copies the
    /// game leaves in its list of added windows stay "visible" after closing (seen: taken for still open, the
    /// key pressed again, and the map opened again — open and shut a few times, then left open); not counted.
    /// </summary>
    private bool MapOpenNow()
    {
        // Read straight from memory: the window list of the last world read can lag a moment (seen: the map taken
        // for closed right after it was read, the roads never read, the map shut by the window watch instead).
        lock (_worldGate)
            return _world?.ReadMapNpcs() != null;
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
        if (_mapPeekReadAt is DateTime readAt)
        {
            bool open = MapOpenNow();
            if (!open)
            {
                _mapPeekReadAt = _mapCloseTapAt = null;
                if (!_roadsDone)
                    EndRoads();
                return null; // closed: the brain goes on
            }
            // The roads taken off the map before it is closed (once per map, for every SekoBot).
            if (_mapCloseTapAt == null && World is { } here && RoadTick(here, now) is { } reading)
                return reading;
            if (_mapCloseTapAt is DateTime tapped ? now - tapped < MapCloseCheck : now - readAt < MapCloseAfter)
                return [];
            if (_mapCloseTaps >= MapCloseTaps)
            {
                _log("🗺 M haritası harita tuşuyla kapanmadı; açık bırakıldı (elle kapatılabilir).");
                _mapPeekReadAt = _mapCloseTapAt = null;
                return null;
            }
            _mapCloseTaps++;
            _mapCloseTapAt = now;
            return [new KeyTapAction(p.Keys.Map)];
        }
        if (_mapPeekSince is DateTime since)
        {
            if (World is { } open && ReadMapNpcsNow(open))
            {
                _mapPeekSince = null;
                _mapPeekReadAt = now;
                _mapCloseTaps = 0;
                return [];
            }
            if (now - since < MapPeekWait && state.Attackers.Count == 0 && !state.Dead)
                return [];
            _mapPeekSince = null;
            // Shown but not read (or a mob came): closed all the same.
            if (MapOpenNow())
            {
                _mapPeekReadAt = now - MapCloseAfter;
                _mapCloseTaps = 0;
                return [];
            }
            // Didn't show (another key? a window in the way?): not pressed again, tried later.
            return null;
        }
        // The map's shops and roads already known (shared by every SekoBot): no need to open the M map at all (the user: "zaten belli").
        if (World is { } known && known.MapName.Length > 0 && NpcBook.Shared.On(known.MapName).Count > 0 && _terrain.RoadsOf(known.MapName) != null)
            return null;
        if (World is not { } world || world.MapName.Length == 0 || _mapNpcsRead.Contains(world.MapName) || IsLoginScreen(world.MapName)
            || world.MapName.StartsWith('_') || now - _mapSeenSince < MapPeekAfter || now - _mapPeekLastTry < MapPeekRetry
            || _mapPeeks.GetValueOrDefault(world.MapName) >= MapPeekTries)
            return null;
        if (state.Dead || state.Attackers.Count > 0 || state.Targets.Any(t => t.AttacksMe) || _selling || _tidying || _cleaner.Active
            || _brain.Traveling || MapOpenNow())
            return null;
        _mapPeeks[world.MapName] = _mapPeeks.GetValueOrDefault(world.MapName) + 1;
        _roadsDone = false;
        _mapPeekLastTry = now;
        _mapPeekSince = now;
        var actions = new List<BotAction> { new KeyUpAction(p.Keys.Attack) };
        foreach (var key in new[] { p.Keys.MoveForward, p.Keys.MoveBack, p.Keys.MoveLeft, p.Keys.MoveRight })
            actions.Add(new KeyUpAction(key));
        actions.Add(new KeyTapAction(p.Keys.Map));
        return actions;
    }
}
