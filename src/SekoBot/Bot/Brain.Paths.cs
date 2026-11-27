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

    /// <summary>The corners of the way being walked now (null: none), for the live map.</summary>
    internal IReadOnlyList<Vec3>? WayNow => _way is { Way: { Count: > 0 } way } ? [.. way] : null;
    private readonly Dictionary<int, (bool Ok, TimeSpan Until)> _reachable = [];
    /// <summary>The way is found again this often (we and the goal move).</summary>
    internal const int WayRefreshMs = 700;
    /// <summary>How long whether a mob can be reached is kept.</summary>
    internal const int ReachableKeepMs = 3000;
    /// <summary>A mob whose way round is longer than this many times the straight way (plus 6 m) is left.</summary>
    internal const double DetourMost = 2.2;
    /// <summary>While farming the way round is looked for this far around (world units, 50 m): quick enough every tick.</summary>
    internal const double WayRoom = 5000;
    /// <summary>
    /// Further than this from where we go (walking back to the area from far off): the way is looked for across the
    /// whole map (around mountains and slopes), and found again only this often (it takes a moment).
    /// </summary>
    internal const double FarFrom = 5000;
    internal const int FarWayRefreshMs = 8000;
    /// <summary>How far round a way is looked for when none was found close by (world units, 200 m).</summary>
    internal const double FarRoom = 20000;
    /// <summary>Off the far way by this much (pushed aside, a fight): found again at once.</summary>
    internal const double OffTheWay = 1500;

    /// <summary>This run: how often a way round the map's obstacles was taken, how many mobs were left for having no short way.</summary>
    internal int Detours { get; private set; }
    internal int NoWayMobs { get; private set; }

    /// <summary>Where the character got stuck on the current long way (just ahead of it): the way is found round
    /// them at once (seen on c2: held by a ledge not in the map, it stepped aside and walked into it again for minutes —
    /// the way kept leading there).</summary>
    private readonly List<Vec3> _wayStuck = [];
    /// <summary>Held up this many times at the same spot of a road: something stands there for good (mobs move on).</summary>
    internal const int SameSpotStucks = 3;
    /// <summary>A stuck spot on the way is kept away from by this much (world units).</summary>
    internal const double WayStuckRadius = 120;

    /// <summary>Held up on a long way: the spot ahead noted (for this way and in the shared obstacle map), the way
    /// found again round it.</summary>
    private void StuckOnWay(GameState state)
    {
        if (state.Position is not Vec3 me || HeldDirection(state) is not var (wx, wy))
            return;
        StuckToward(state, me, wx, wy, ObstacleMap.Ahead);
    }

    /// <summary>Not getting anywhere on the way for <see cref="TravelNoProgressMs"/> although walking (seen on c2:
    /// sliding up and down along a ledge not in the map — the game says "walking", the blocked check never fired): the
    /// spot towards the next corner is noted, the way found again round it.</summary>
    internal const int TravelNoProgressMs = 4000;
    internal const double TravelProgress = 250;
    private Vec3? _travelCheckAt;
    private TimeSpan _travelCheckTime = Never;

    private void CheckTravelProgress(GameState state, Vec3 me, Vec3 goal)
    {
        var now = state.Time;
        if (_travelCheckAt is not Vec3 was || now - _travelCheckTime > Ms(TravelNoProgressMs * 3))
        {
            _travelCheckAt = me;
            _travelCheckTime = now;
            return;
        }
        if (me.DistanceXY(was) >= TravelProgress)
        {
            _travelCheckAt = me;
            _travelCheckTime = now;
            return;
        }
        if (now - _travelCheckTime < Ms(TravelNoProgressMs))
            return;
        var aim = NextCorner(state, goal) ?? goal;
        double dx = aim.X - me.X, dy = aim.Y - me.Y, len = Math.Sqrt(dx * dx + dy * dy);
        if (len > 1)
            StuckToward(state, me, dx / len, dy / len, 150);
        _travelCheckAt = me;
        _travelCheckTime = now;
    }

    private void StuckToward(GameState state, Vec3 me, double wx, double wy, double ahead)
    {
        var spot = new Vec3(me.X + wx * ahead, me.Y + wy * ahead, me.Z);
        if (state.Targets.Any(t => t.Position is Vec3 m && (m.DistanceXY(spot) < MobFreeAround || m.DistanceXY(me) < MobFreeAround)))
            return; // a mob in the way, not the ground
        _wayStuck.Add(spot);
        if (_wayStuck.Count > 30)
            _wayStuck.RemoveAt(0);
        // On a road: a mob (or a player) held us up, not the ground — walked round, not kept.
        // On a road: a mob (or a player) held us up, not the ground — unless it happens at the same spot again and again.
        bool again = _wayStuck.Count(s => Math.Abs(s.X - spot.X) < 150 && Math.Abs(s.Y - spot.Y) < 150) > SameSpotStucks;
        if (Obstacles != null && state.MapName.Length > 0 && (again || TerrainHere(state)?.Roads?.OnRoad(spot.X, spot.Y) != true))
            Obstacles.Stuck(state.MapName, spot.X, spot.Y, DateTime.Now);
        if (_way is { } w)
            _way = (w.Goal, Never, w.Way); // found again on the next look
    }

    private MapTerrain? TerrainHere(GameState state) => state.MapName.Length > 0 ? Terrain?.Of(state.MapName) : null;

    /// <summary>The next corner on the way to <paramref name="goal"/> when the straight way is blocked; else null.</summary>
    private Vec3? NextCorner(GameState state, Vec3 goal)
    {
        if (TerrainHere(state) is not { } map || state.Position is not Vec3 me || me.DistanceXY(goal) < 60)
            return null;
        // Far off (walking to the area): along the roads when they are known, even where the straight way is free.
        bool byRoad = map.Roads != null && me.DistanceXY(goal) > FarFrom;
        if (!byRoad && map.LineClear(me, goal)
            && !_wayStuck.Any(s => new MapTerrain.Circle(s.X, s.Y, WayStuckRadius).Crosses(me, goal, MapTerrain.Margin)))
        {
            _way = null;
            return null;
        }
        bool far = me.DistanceXY(goal) > FarFrom;
        if (_way is not { } w || w.Goal.DistanceXY(goal) > 150 || state.Time - w.At > Ms(far || w.Way == null && me.DistanceXY(goal) > 1500 ? FarWayRefreshMs : WayRefreshMs)
            || far && w.Way is { Count: > 0 } kept && OffWay(me, kept))
        {
            bool newGoal = _way is not { } old || old.Goal.DistanceXY(goal) > 150;
            if (newGoal)
                _wayStuck.Clear();
            // Round where we got stuck on this way, and the obstacles learned around here.
            var avoid = _wayStuck.Select(s => new MapTerrain.Circle(s.X, s.Y, WayStuckRadius))
                .Concat(KnownObstacles(state).Select(o => new MapTerrain.Circle(o.X, o.Y, 60))).ToList();
            double room = far ? Math.Max(WayRoom, me.DistanceXY(goal)) : WayRoom;
            var found = (avoid.Count > 0 ? map.WayTo(me, goal, room: room, avoid: avoid, roads: byRoad) : null)
                        ?? map.WayTo(me, goal, room: room, roads: byRoad);
            // No way within 50 m (a slope to go round, seen: stuck below one 50 m from the area): the whole map.
            if (found == null && !far && me.DistanceXY(goal) > 1500)
                found = map.WayTo(me, goal, room: FarRoom);
            _way = w = (goal, state.Time, found);
            if (newGoal && w.Way is { Count: > 1 })
                Detours++;
        }
        return w.Way is { Count: > 0 } way ? map.Aim(way, me) : null;
    }

    /// <summary>Whether <paramref name="me"/> is further than <see cref="OffTheWay"/> from every leg of the way (corners in turn).</summary>
    private bool OffWay(Vec3 me, List<Vec3> way)
    {
        if (_way is not { } w)
            return false;
        // The way's legs start where it was found: the first corner is reached from there.
        for (int i = 0; i < way.Count; i++)
        {
            var a = i == 0 ? way[0] : way[i - 1];
            var b = way[i];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
            double t = len <= 0 ? 0 : Math.Clamp(((me.X - a.X) * dx + (me.Y - a.Y) * dy) / len, 0, 1);
            double px = a.X + dx * t - me.X, py = a.Y + dy * t - me.Y;
            if (px * px + py * py <= OffTheWay * OffTheWay)
                return false;
        }
        return w.Way != null;
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

    /// <summary>Whether an item on the ground can be walked to around the map's obstacles (unknown: yes).</summary>
    private bool DropReachable(GameState state, GroundSpot drop)
    {
        if (drop.At is not Vec3 at || state.Position is not Vec3 me || TerrainHere(state) is not { } map || map.LineClear(me, at))
            return true;
        var way = map.WayTo(me, at, room: WayRoom);
        if (way == null)
            NoWayMobs++;
        return way != null;
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
        if (!ok && !_reachable.ContainsKey(t.Id))
            NoWayMobs++;
        _reachable[t.Id] = (ok, now + Ms(ReachableKeepMs));
        return ok;
    }
}
