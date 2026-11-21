using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Every map's obstacles, kept on disk (haritalar\[map].json and a picture, .html): when the
/// character comes onto a map its walls, houses, rocks and trees are read from the game (whether
/// the bot runs or not) and saved; walking then goes around them. What the character walks
/// through (a floor, a bridge, a gate) is noted as passable as it goes.
/// </summary>
internal sealed partial class BotEngine
{
    private readonly TerrainBook _terrain = new();
    /// <summary>The ways really walked (over hills and slopes the map's obstacles don't show), shared.</summary>
    private readonly WalkedPaths _walked = WalkedPaths.Load();
    private TimeSpan _walkedSeenAt, _walkedSavedAt;
    private string _terrainMap = "", _terrainLevel = "";
    private readonly HashSet<string> _terrainTold = [];
    private TimeSpan _terrainTriedAt = TimeSpan.FromDays(-1), _terrainWalkedAt, _terrainSavedAt;

    public static string MapFolder => MapTerrain.Folder;

    /// <summary>The picture of the map the character is on (or the newest one), when there is one.</summary>
    public string? MapPicture
    {
        get
        {
            if (World?.MapName is { Length: > 0 } map && MapTerrain.Stem(MapFolder, map) + ".html" is var here && File.Exists(here))
                return here;
            return Directory.Exists(MapFolder)
                ? new DirectoryInfo(MapFolder).GetFiles("*.html").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
                : null;
        }
    }

    private void WatchTerrain(WorldSnapshot? world, TimeSpan now)
    {
        if (world == null || world.MapName.Length == 0)
            return;
        string map = world.MapName;
        // "_none" and the like: between maps (teleporting, loading).
        if (map.StartsWith('_') || map.Equals("none", StringComparison.OrdinalIgnoreCase) || IsLoginScreen(map))
            return;
        if (map != _terrainMap && now - _terrainTriedAt >= TimeSpan.FromSeconds(10))
        {
            _terrainTriedAt = now;
            List<MapBlock> read;
            string level = "";
            lock (_worldGate)
                read = _world?.ReadBlocks(out level) ?? [];
            // Right after a teleport the old map can still be loaded under the new name: the level last read
            // (named otherwise than the map) is not taken for the new map; it is read again in 10 s.
            if (level.Length > 0 && !level.Equals(map, StringComparison.OrdinalIgnoreCase) && level == _terrainLevel)
                read = [];
            if (read.Count > 0)
            {
                ReadSlopes(map);
                _terrainLevel = level;
                _terrainMap = map;
                int before = _terrain.Of(map)?.Blocks.Count ?? -1;
                var terrain = _terrain.Update(map, read);
                SaveTerrain(terrain, world.Player);
                if (_terrainTold.Add(map) && before != read.Count)
                    _log($"🗺 {map} haritası kaydedildi: {MapReport.Summary(terrain.Blocks)}. Bot yolunu bunların etrafından buluyor.");
            }
        }
        if (now - _walkedSeenAt >= TimeSpan.FromMilliseconds(400))
        {
            _walkedSeenAt = now;
            _walked.Saw(map, world.Player);
            _terrain.SlopesOf(map)?.Walked(world.Player.X, world.Player.Y);
        }
        if (now - _walkedSavedAt >= TimeSpan.FromSeconds(30))
        {
            _walkedSavedAt = now;
            _walked.Save();
        }
        if (now - _terrainWalkedAt >= TimeSpan.FromSeconds(1))
        {
            _terrainWalkedAt = now;
            _terrain.Of(map)?.Walked(world.Player);
        }
        if (now - _terrainSavedAt >= TimeSpan.FromMinutes(2))
        {
            _terrainSavedAt = now;
            SaveTerrainChanges(world);
        }
    }

    /// <summary>
    /// The map's ground heights, read from the game once per map: where it is too steep to walk (mountain sides,
    /// hills) is kept off by every way found. What the characters walked before is walkable whatever the heights say.
    /// </summary>
    private void ReadSlopes(string map)
    {
        if (_terrain.SlopesOf(map) != null)
            return;
        HeightGrid? grid;
        lock (_worldGate)
            grid = _world?.ReadHeights(map);
        if (grid == null)
            return;
        var slopes = new SlopeMap(grid);
        foreach (var (ax, ay, bx, by) in _walked.Links(map))
            slopes.WalkedLine(ax, ay, bx, by, 30);
        _terrain.SetSlopes(slopes);
        _log($"🗺 {map} haritasının yükseklikleri okundu: dik yamaç ve tepeler (haritanın %{slopes.SteepShare * 100:0}'i) yol bulunurken dolaşılıyor.");
    }

    private void SaveTerrain(MapTerrain terrain, Vec3? me)
    {
        try
        {
            IReadOnlyList<Vec3> learned = me is Vec3 at ? _obstacles.Near(terrain.Map, at, 1e9) : [];
            terrain.Save(_terrain.Folder, learned, me);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void SaveTerrainChanges(WorldSnapshot? world)
    {
        foreach (var t in _terrain.Changed())
            SaveTerrain(t, world?.MapName == t.Map ? world.Player : null);
    }

    /// <summary>Where to head now on the way to <paramref name="target"/> (around the map's obstacles).</summary>
    private Vec3 Aim(WorldSnapshot world, Vec3 target, ref List<Vec3>? way, ref TimeSpan wayAt)
    {
        if (_terrain.Of(world.MapName) is not { } map)
            return target;
        if (way == null || _clock.Elapsed - wayAt > TimeSpan.FromSeconds(1))
        {
            way = map.WayTo(world.Player, target);
            wayAt = _clock.Elapsed;
        }
        return way is { Count: > 0 } ? map.Aim(way, world.Player) : target;
    }
}
