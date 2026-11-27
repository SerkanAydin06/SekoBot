using SekoBot.Memory;
using SekoBot.Config;

namespace SekoBot.Bot;

/// <summary>
/// The whole map's NPCs from the game's M map: the game keeps only the NPCs within ~50 m, but its M map
/// marks every one (see <see cref="GameWorldReader.ReadMapNpcs"/>). Whenever the M map is open (the user
/// opened it outside farming), its shops are noted for selling (shared, npc-yerleri.json).
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>The maps whose M map was read this session.</summary>
    private readonly HashSet<string> _mapNpcsRead = [];
    private TimeSpan _mapNpcsLookedAt = TimeSpan.FromDays(-1);

    private void WatchMapNpcs(WorldSnapshot world, TimeSpan now)
    {
        if (BotRunning || now - _mapNpcsLookedAt < TimeSpan.FromSeconds(2))
            return;
        _mapNpcsLookedAt = now;
        // The user opened the M map in a big game: the roads are read too.
        if (ReadMapNpcsNow(world) && !RoadsGood(world.MapName))
        {
            MapPicture? picture;
            lock (_worldGate)
                picture = _world?.ReadMapPicture();
            if (picture != null && picture.Drawn.Width >= RoadMap.GoodPicture && ReadRoads(world.MapName, picture) != null)
                _log($"🛣 {world.MapName} haritasının yolları açık M haritasından okundu; uzun yürüyüşlerde yollar kullanılacak.");
        }
    }

    private bool RoadsGood(string map) => _terrain.RoadsOf(map) is { Good: true };

    /// <summary>The roads off the picture in the newest frame; kept (on disk, for every SekoBot) when they look right.</summary>
    private RoadMap? ReadRoads(string map, MapPicture picture)
    {
        var roads = WithLatestFrame(frame =>
        {
            var read = RoadMap.FromPicture(map, frame, picture.Drawn, picture.OffsetX, picture.OffsetY, picture.ScaleX, picture.ScaleY, picture.Texture);
            if (read == null)
                try { RoadMap.SavePicture(frame, picture.Drawn, MapTerrain.Stem(_terrain.Folder, map) + ".yollar-okunamadi.png"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException) { }
            return read;
        });
        if (roads == null)
            return null;
        // A new read finding much less road than the one kept (a window over the map, a bad frame): the old one stays.
        if (_terrain.RoadsOf(map) is { } before && roads.RoadCells < before.RoadCells * 0.7)
        {
            _log($"🛣 {map} yolları yeniden okundu ama eskisinden az yol çıktı ({roads.RoadCells} / {before.RoadCells}); eskisi kullanılıyor.");
            return before;
        }
        _terrain.SetRoads(roads);
        // The picture kept beside the roads, to be looked at.
        WithLatestFrame(frame =>
        {
            try { RoadMap.SavePicture(frame, picture.Drawn, MapTerrain.Stem(_terrain.Folder, map) + ".yollar.png"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException) { }
            return true;
        });
        return roads;
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
}
