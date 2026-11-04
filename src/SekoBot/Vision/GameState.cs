namespace SekoBot.Vision;

/// <summary>Everything the bot knows about one moment. The brain never sees pixels.</summary>
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

    /// <summary>Probable mobs (client pixels, nearest to the character first), from motion.</summary>
    public IReadOnlyList<Point> MobCandidates { get; init; } = [];

    /// <summary>Whether the picture is moving (we walk) — null when not measured.</summary>
    public bool? ScreenMoving { get; init; }

    /// <summary>The big map (M) is open on screen.</summary>
    public bool MapOpen { get; init; }

    /// <summary>Our position on the big map (0..1 of its width), when it is open and the arrow was found.</summary>
    public PointF? MapPosition { get; init; }

    /// <summary>Our position on the big map read from memory (no need to open the map), when known.</summary>
    public PointF? LivePosition { get; init; }

    /// <summary>The death dialog is showing.</summary>
    public bool Dead { get; init; }

    /// <summary>"Burada yeniden başla" can be clicked (its text turned white).</summary>
    public bool ReviveReady { get; init; }

    /// <summary>A window covers the game (e.g. the advert after a level-up).</summary>
    public bool PopupOpen { get; init; }

    /// <summary>Where the selected mob is believed to be on screen (client pixels), if known.</summary>
    public Point? TargetSpot { get; init; }
}
