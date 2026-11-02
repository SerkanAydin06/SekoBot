namespace SekoBot.Vision;

internal readonly record struct Detection(Rectangle Box, double Score)
{
    public Point Center => new(Box.X + Box.Width / 2, Box.Y + Box.Height / 2);
}

/// <summary>Everything the bot knows about one moment. The brain never sees pixels.</summary>
internal sealed record GameState(
    TimeSpan Time,
    int Width,
    int Height,
    double? Hp,
    double? Mp,
    double? TargetHp,
    IReadOnlyList<Detection> Stones,
    double AnalysisMs)
{
    public bool HasTarget => TargetHp is > 0.01;
    public Point ScreenCenter => new(Width / 2, Height / 2);
}
