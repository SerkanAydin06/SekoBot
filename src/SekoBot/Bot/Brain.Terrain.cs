using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Ground the bot can't cross. The game's collision (trees, rocks, walls) can't be read from
/// memory, so the bot learns it: where it got stuck walking to a mob (a side step, a mob it
/// couldn't reach, a group it couldn't get to) is remembered for a while with the direction it was
/// heading, and mobs whose way leads through such a spot are not chosen. Heights are known: a mob
/// far above or below us (on a ledge, down a slope) is left alone too. Mobs attacking us always
/// count: they come to us.
/// <para>
/// Kept between sessions too (<see cref="ObstacleMap"/>): a spot run into twice is a tree or a rock
/// for good, on that map. Walking bends around it like around a sleeping mob, and the way to a mob
/// through it isn't taken.
/// </para>
/// </summary>
internal sealed partial class Brain
{
    /// <summary>A remembered spot: where we stood, where we were trying to go, when.</summary>
    private readonly List<(Vec3 At, Vec3? Heading, TimeSpan When)> _stuckSpots = [];
    /// <summary>Where we're walking to this tick (a mob, a group), for remembering the way when stuck.</summary>
    private Vec3? _walkGoal;

    internal const double StuckRadius = 150;      // a way passing this close to a spot leads into it
    internal const double StuckNearMe = 250;      // a spot this close: only its direction is blocked
    internal const double StuckAngleDeg = 40;
    internal const int StuckForgetMs = 300_000;   // 5 minutes: mobs and we move, don't block forever
    /// <summary>Seen in the game: a mob's position is ~60 below ours on flat ground (its pivot is lower).</summary>
    internal const double MobHeightOffset = -60;

    /// <summary>The obstacles of every map, kept between sessions (set by the engine; null in tests that don't need it).</summary>
    internal ObstacleMap? Obstacles { get; set; }
    private TimeSpan _walkedNotedAt = Never;
    /// <summary>Known obstacles this far around us are looked at (world units, 15 m).</summary>
    internal const double ObstacleLook = 1500;
    /// <summary>A known obstacle this close to the straight way (world units, 1.3 m) is gone around.</summary>
    internal const double ObstacleClearance = 130;

    private void ResetTerrain()
    {
        _stuckSpots.Clear();
        _walkGoal = null;
        _walkedNotedAt = Never;
        _way = null;
        _reachable.Clear();
    }

    /// <summary>Walking freely: known obstacles under our feet weren't real (once a second).</summary>
    private void NoteWalking(GameState state)
    {
        if (Obstacles == null || state.Walking != true || state.Position is not Vec3 me || state.MapName.Length == 0 ||
            state.Time - _walkedNotedAt < Ms(1000))
            return;
        _walkedNotedAt = state.Time;
        Obstacles.Walked(state.MapName, me);
    }

    /// <summary>
    /// The screen direction to walk bent around the known obstacles ahead (trees, rocks the bot ran
    /// into before on this map): one within 1.3 m of the way ahead (up to 6 m) pushes it sideways.
    /// </summary>
    internal static (double X, double Y) AroundObstacles(IReadOnlyList<Vec3> obstacles, Vec3 me, CameraView camera, double dx, double dy)
    {
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6 || obstacles.Count == 0)
            return (dx, dy);
        double ux = dx / length, uy = dy / length;
        double sideX = 0, sideY = 0;
        foreach (var o in obstacles)
        {
            var (right, up) = camera.ToScreenDirection(o.X - me.X, o.Y - me.Y);
            double ox = right, oy = -up;                     // world units, screen directions
            double ahead = ox * ux + oy * uy, across = ox * -uy + oy * ux;
            if (ahead <= 0 || ahead > AvoidAhead - 100 || Math.Abs(across) >= ObstacleClearance)
                continue;
            double away = across > 0 ? -1 : 1;
            double weight = (1 - Math.Abs(across) / ObstacleClearance) * (1 - ahead / AvoidAhead * 0.5) * 1.8;
            sideX += -uy * away * weight;
            sideY += ux * away * weight;
        }
        if (sideX == 0 && sideY == 0)
            return (dx, dy);
        return ((ux + sideX) * length, (uy + sideY) * length);
    }

    private IReadOnlyList<Vec3> KnownObstacles(GameState state) =>
        Obstacles != null && state.Position is Vec3 me && state.MapName.Length > 0
            ? Obstacles.Near(state.MapName, me, ObstacleLook) : [];

    /// <summary>The world direction the held walking keys push the character (unit), when known.</summary>
    private (double X, double Y)? HeldDirection(GameState state)
    {
        if (state.Camera is not CameraView camera || _heldMoves.Count == 0)
            return null;
        var k = _profile().Keys;
        double right = (_heldMoves.Contains(k.MoveRight) ? 1 : 0) - (_heldMoves.Contains(k.MoveLeft) ? 1 : 0);
        double up = (_heldMoves.Contains(k.MoveForward) ? 1 : 0) - (_heldMoves.Contains(k.MoveBack) ? 1 : 0);
        if (right == 0 && up == 0)
            return null;
        // ToScreenDirection turned backwards (it is a rotation).
        double yaw = camera.Yaw * Math.PI / 180, fx = Math.Cos(yaw), fy = Math.Sin(yaw);
        double wx = -right * fy + up * fx, wy = right * fx + up * fy, l = Math.Sqrt(wx * wx + wy * wy);
        return (wx / l, wy / l);
    }

    internal int StuckSpotCount => _stuckSpots.Count;

    /// <summary>Remembers that we got stuck here, walking towards <paramref name="goal"/> (when known).</summary>
    internal void RememberStuck(Vec3 at, Vec3? goal, TimeSpan now)
    {
        Vec3? heading = goal is Vec3 g && g.DistanceXY(at) > 1 ? g : null;
        _stuckSpots.RemoveAll(s => now - s.When > TimeSpan.FromMilliseconds(StuckForgetMs) ||
                                   s.At.DistanceXY(at) < 100 && Same(s.Heading, heading, at));
        _stuckSpots.Add((at, heading, now));
        if (_stuckSpots.Count > 200)
            _stuckSpots.RemoveAt(0);
    }

    private static bool Same(Vec3? a, Vec3? b, Vec3 from) =>
        a is Vec3 x && b is Vec3 y && Angle(x.X - from.X, x.Y - from.Y, y.X - from.X, y.Y - from.Y) < StuckAngleDeg;

    private static double Angle(double ax, double ay, double bx, double by)
    {
        double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
        if (la < 1e-6 || lb < 1e-6)
            return 180;
        return Math.Acos(Math.Clamp((ax * bx + ay * by) / (la * lb), -1, 1)) * 180 / Math.PI;
    }

    /// <summary>True when the way from <paramref name="me"/> to <paramref name="to"/> leads into a remembered spot.</summary>
    internal static bool WayBlocked(IEnumerable<(Vec3 At, Vec3? Heading, TimeSpan When)> spots, Vec3 me, Vec3 to)
    {
        double wx = to.X - me.X, wy = to.Y - me.Y, length2 = wx * wx + wy * wy;
        if (length2 < 1)
            return false;
        foreach (var (at, heading, _) in spots)
        {
            if (at.DistanceXY(me) <= StuckNearMe)
            {
                // Standing at it: only the way we were trying to go is blocked.
                if (heading is Vec3 h && Angle(h.X - at.X, h.Y - at.Y, wx, wy) < StuckAngleDeg)
                    return true;
                continue;
            }
            double u = ((at.X - me.X) * wx + (at.Y - me.Y) * wy) / length2;
            if (u <= 0 || u >= 1)
                continue;
            double px = me.X + u * wx - at.X, py = me.Y + u * wy - at.Y;
            if (px * px + py * py <= StuckRadius * StuckRadius)
                return true;
        }
        return false;
    }

    /// <summary>A mob far above or below us for its distance (a ledge, a cliff): not walkable to.</summary>
    internal static bool OnOtherLevel(TargetInfo t, Vec3 me) =>
        t.Position is Vec3 at && Math.Abs(at.Z - me.Z - MobHeightOffset) > Math.Max(250, 0.5 * t.Distance);

    /// <summary>An archer this much higher or lower than us (world units, 1.5 m; a quarter of its
    /// distance when further) shoots from a ledge the character can't climb (the user saw it: archers
    /// walk up where the player can't follow).</summary>
    internal const double LedgeHeight = 150;

    /// <summary>A mob (an archer) on a ledge up or down from us: it can't be walked to.</summary>
    internal static bool OnLedge(TargetInfo t, Vec3 me) =>
        t.Position is Vec3 at && Math.Abs(at.Z - me.Z - MobHeightOffset) > Math.Max(LedgeHeight, 0.25 * t.Distance);

    /// <summary>Can we walk to it? Mobs in reach or without a position always pass, and mobs
    /// attacking us that fight up close (they come to us). An archer attacking us from a ledge doesn't.</summary>
    private bool Walkable(TargetInfo t, GameState state, TimeSpan now)
    {
        if (t.Distance <= t.Reach || state.Position is not Vec3 me || t.Position is not Vec3 at)
            return true;
        if (t.AttacksMe)
            return !t.Ranged || !OnLedge(t, me) && !_unreachable.ContainsKey(t.Id);
        if (OnOtherLevel(t, me))
            return false;
        _stuckSpots.RemoveAll(s => now - s.When > TimeSpan.FromMilliseconds(StuckForgetMs));
        if (_stuckSpots.Count > 0 && WayBlocked(_stuckSpots, me, at))
            return false;
        // Walls, houses and rocks of the map (from the game): only by a short way round.
        if (!ReachableOnMap(t, state, me, at, now))
            return false;
        // Known trees and rocks of this map right on the way (not the ones we stand at: walking bends around those).
        var known = KnownObstacles(state);
        return known.Count == 0 || !WayBlocked(known.Select(o => (o, (Vec3?)null, now)), me, at);
    }

    /// <summary>A mob this close to where we were held up (world units, 3 m) may be what held us:
    /// the spot isn't noted as a tree or a rock.</summary>
    internal const double MobFreeAround = 300;
    /// <summary>The mob walked to is this much higher or lower (world units, 1 m): held up by a slope, not a tree.</summary>
    internal const double SlopeHeight = 100;

    /// <summary>Notes where we are stuck right now (heading for the goal of this tick). Only
    /// <paramref name="solid"/> (keys held, the character not moving at all) can make it a tree or
    /// a rock for good, and only with no mob around it and the goal on our level.</summary>
    private void StuckHere(GameState state, bool solid = false)
    {
        if (state.Position is not Vec3 me)
            return;
        RememberStuck(me, _walkGoal, state.Time);
        if (!solid || Obstacles == null || state.MapName.Length == 0)
            return;
        // Going up or down to a mob (an archer up a slope): the slope stopped us. That mob is
        // left alone for a while; the place is no obstacle (other ways lead past it).
        if (_walkGoal is Vec3 goal && state.Targets.MinBy(t => t.Position is Vec3 at ? at.DistanceXY(goal) : double.MaxValue) is { Position: Vec3 at } mob &&
            at.DistanceXY(goal) < 50 && Math.Abs(at.Z - me.Z - MobHeightOffset) > SlopeHeight)
        {
            _unreachable[mob.Id] = state.Time + Ms(UnreachableForMs);
            return;
        }
        // For good: the spot just in front of us, the way the keys push (else the way to the goal).
        (double X, double Y)? way = HeldDirection(state);
        if (way == null && _walkGoal is Vec3 g && g.DistanceXY(me) > 1)
            way = ((g.X - me.X) / g.DistanceXY(me), (g.Y - me.Y) / g.DistanceXY(me));
        if (way is not var (wx, wy))
            return;
        var spot = new Vec3(me.X + wx * ObstacleMap.Ahead, me.Y + wy * ObstacleMap.Ahead, me.Z);
        // A mob next to us or the spot (a living one moves: it was the mob, not the ground).
        if (state.Targets.Any(t => t.Position is Vec3 m && (m.DistanceXY(spot) < MobFreeAround || m.DistanceXY(me) < MobFreeAround)))
            return;
        Obstacles.Stuck(state.MapName, spot.X, spot.Y, DateTime.Now);
    }
}
