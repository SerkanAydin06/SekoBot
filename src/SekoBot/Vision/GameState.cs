using SekoBot.Memory;

namespace SekoBot.Vision;

/// <summary>Everything the bot knows about one moment. The brain never sees pixels or memory.</summary>
/// <param name="Hp">The character's HP (0..1), from memory; null when it can't be read.</param>
/// <param name="TargetHp">The selected mob's HP (0..1; 0 = nothing selected), from memory; null when unknown.</param>
internal sealed record GameState(
    TimeSpan Time,
    int Width,
    int Height,
    double? Hp,
    double? Mp,
    double? TargetHp,
    double AnalysisMs)
{
    public bool HasTarget => TargetHp is > 0.01;

    /// <summary>The mobs on screen (client pixels, nearest to the character first), from memory.</summary>
    public IReadOnlyList<Point> MobCandidates { get; init; } = [];

    /// <summary>The character is moving (its position changed a moment ago) — null when not known.</summary>
    public bool? Walking { get; init; }

    /// <summary>The character's position in the world, from memory.</summary>
    public Vec3? Position { get; init; }

    /// <summary>The game camera (which way "screen up" points in the world).</summary>
    public CameraView? Camera { get; init; }

    /// <summary>The character is dead.</summary>
    public bool Dead { get; init; }

    /// <summary>"Burada yeniden başla" can be clicked (its text turned white).</summary>
    public bool ReviveReady { get; init; }

    /// <summary>A window covers the game (e.g. the advert after a level-up).</summary>
    public bool PopupOpen { get; init; }

    /// <summary>Where the selected mob is on screen (client pixels), if known.</summary>
    public Point? TargetSpot { get; init; }
}
