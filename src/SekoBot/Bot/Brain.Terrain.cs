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

    private void ResetTerrain()
    {
        _stuckSpots.Clear();
        _walkGoal = null;
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

    /// <summary>Can we walk to it? Mobs attacking us, in reach, or without a position always pass.</summary>
    private bool Walkable(TargetInfo t, GameState state, TimeSpan now)
    {
        if (t.AttacksMe || t.Distance <= t.Reach || state.Position is not Vec3 me || t.Position is not Vec3 at)
            return true;
        if (OnOtherLevel(t, me))
            return false;
        _stuckSpots.RemoveAll(s => now - s.When > TimeSpan.FromMilliseconds(StuckForgetMs));
        return _stuckSpots.Count == 0 || !WayBlocked(_stuckSpots, me, at);
    }

    /// <summary>Notes where we are stuck right now (heading for the goal of this tick).</summary>
    private void StuckHere(GameState state)
    {
        if (state.Position is Vec3 me)
            RememberStuck(me, _walkGoal, state.Time);
    }
}
