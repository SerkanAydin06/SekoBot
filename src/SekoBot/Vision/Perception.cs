using System.Diagnostics;
using SekoBot.Capture;
using SekoBot.Config;

namespace SekoBot.Vision;

/// <summary>Screen perception is only for target HP. Character HP/MP exclusively come from memory.</summary>
internal static class Perception
{
    public static GameState Analyze(Frame frame, BotProfile profile, TimeSpan now)
    {
        var watch = Stopwatch.StartNew();
        double? target = profile.TargetBar is { } targetBar ? BarReader.Read(frame, targetBar) : null;
        return new GameState(now, frame.Width, frame.Height, null, null, target, watch.Elapsed.TotalMilliseconds);
    }
}
