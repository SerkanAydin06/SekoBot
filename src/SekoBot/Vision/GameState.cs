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
}
