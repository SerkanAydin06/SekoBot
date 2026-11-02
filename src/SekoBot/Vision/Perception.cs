using System.Diagnostics;
using SekoBot.Capture;
using SekoBot.Config;

namespace SekoBot.Vision;

/// <summary>Turns a frame into a <see cref="GameState"/>.</summary>
internal sealed class Perception : IDisposable
{
    public StoneDetector Detector { get; } = new();

    public GameState Analyze(Frame frame, BotProfile profile, TimeSpan now)
    {
        var watch = Stopwatch.StartNew();
        double? hp = profile.HpBar is { } hpBar ? BarReader.Read(frame, hpBar) : null;
        double? mp = profile.MpBar is { } mpBar ? BarReader.Read(frame, mpBar) : null;
        double? target = profile.TargetBar is { } targetBar ? BarReader.Read(frame, targetBar) : null;
        var search = profile.SearchArea.ToPixels(frame.Width, frame.Height);
        var stones = Detector.Detect(frame, search, profile.MatchThreshold);
        return new GameState(now, frame.Width, frame.Height, hp, mp, target, stones,
            watch.Elapsed.TotalMilliseconds);
    }

    public void Dispose() => Detector.Dispose();
}
