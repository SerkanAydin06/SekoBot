using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Walking around what the map has in the way (walls, houses, rocks, trees: read from the game,
/// see <see cref="MapTerrain"/>). When the straight way to where we walk is blocked, the way
/// around is found and the bot heads for its next corner instead; a mob that can only be reached
/// by a long way round (or not at all) isn't chosen.
/// </summary>
internal sealed partial class Brain
{
    /// <summary>Every map's obstacles (set by the engine; null in tests that don't need it).</summary>
    internal TerrainBook? Terrain { get; set; }

    private (Vec3 Goal, TimeSpan At, List<Vec3>? Way)? _way;
    private readonly Dictionary<int, (bool Ok, TimeSpan Until)> _reachable = [];
    /// <summary>The way is found again this often (we and the goal move).</summary>
    internal const int WayRefreshMs = 700;
    /// <summary>How long whether a mob can be reached is kept.</summary>
    internal const int ReachableKeepMs = 3000;
    /// <summary>A mob whose way round is longer than this many times the straight way (plus 6 m) is left.</summary>
    internal const double DetourMost = 2.2;
    /// <summary>While farming the way round is looked for this far around (world units, 50 m): quick enough every tick.</summary>
    internal const double WayRoom = 5000;

    private MapTerrain? TerrainHere(GameState state) => state.MapName.Length > 0 ? Terrain?.Of(state.MapName) : null;

    /// <summary>The next corner on the way to <paramref name="goal"/> when the straight way is blocked; else null.</summary>
    private Vec3? NextCorner(GameState state, Vec3 goal)
    {
        if (TerrainHere(state) is not { } map || state.Position is not Vec3 me || me.DistanceXY(goal) < 60)
            return null;
        if (map.LineClear(me, goal))
        {
            _way = null;
            return null;
        }
        if (_way is not { } w || w.Goal.DistanceXY(goal) > 150 || state.Time - w.At > Ms(WayRefreshMs))
            _way = w = (goal, state.Time, map.WayTo(me, goal, room: WayRoom));
        return w.Way is { Count: > 0 } way ? map.Aim(way, me) : null;
    }

    /// <summary>The screen direction (dx, dy) turned towards the next corner on the way to <paramref name="goal"/>.</summary>
    private (double X, double Y) TowardsCorner(GameState state, Vec3? goal, double dx, double dy)
    {
        if (goal is not Vec3 g || state.Camera is not CameraView camera || state.Position is not Vec3 me || NextCorner(state, g) is not Vec3 corner)
            return (dx, dy);
        var (right, up) = camera.ToScreenDirection(corner.X - me.X, corner.Y - me.Y);
        double length = Math.Sqrt(right * right + up * up), wanted = Math.Sqrt(dx * dx + dy * dy);
        return length < 1e-6 ? (dx, dy) : (right / length * wanted, -up / length * wanted);
    }

    /// <summary>Whether the mob can be walked to around the map's obstacles (kept a few seconds per mob).</summary>
    private bool ReachableOnMap(TargetInfo t, GameState state, Vec3 me, Vec3 at, TimeSpan now)
    {
        if (TerrainHere(state) is not { } map)
            return true;
        if (_reachable.TryGetValue(t.Id, out var kept) && now < kept.Until)
            return kept.Ok;
        if (_reachable.Count > 500)
            _reachable.Clear();
        bool ok = map.LineClear(me, at) ||
                  map.WayTo(me, at, room: WayRoom) is { } way && MapTerrain.Length(me, way) <= DetourMost * me.DistanceXY(at) + 600;
        _reachable[t.Id] = (ok, now + Ms(ReachableKeepMs));
        return ok;
    }
}
