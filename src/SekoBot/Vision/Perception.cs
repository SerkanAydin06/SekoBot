using System.Diagnostics;
using SekoBot.Capture;
using SekoBot.Config;

namespace SekoBot.Vision;

/// <summary>Turns a frame into a <see cref="GameState"/>. Only reads the calibrated bars.</summary>
internal static class Perception
{
    public static GameState Analyze(Frame frame, BotProfile profile, TimeSpan now)
    {
        var watch = Stopwatch.StartNew();
        double? hp = profile.HpBar is { } hpBar ? BarReader.Read(frame, hpBar) : null;
        double? mp = profile.MpBar is { } mpBar ? BarReader.Read(frame, mpBar) : null;
        double? target = profile.TargetBar is { } targetBar ? BarReader.Read(frame, targetBar) : null;
        return new GameState(now, frame.Width, frame.Height, hp, mp, target, watch.Elapsed.TotalMilliseconds);
    }
}
