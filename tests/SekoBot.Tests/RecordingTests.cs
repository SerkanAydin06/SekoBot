using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Recording;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public sealed class RecordingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SekoBotTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>200×100 frame with a red HP bar at y 40..50, x 20..180, filled to <paramref name="fill"/>.</summary>
    private static Frame HpFrame(double fill, long sequence)
    {
        int w = 200, h = 100;
        var data = new byte[w * h * 4];
        int fillTo = 20 + (int)Math.Round(160 * fill);
        for (int y = 40; y < 50; y++)
        for (int x = 20; x < 180; x++)
        {
            int i = (y * w + x) * 4;
            bool filled = x < fillTo;
            data[i] = filled ? (byte)30 : (byte)25;
            data[i + 1] = filled ? (byte)30 : (byte)25;
            data[i + 2] = filled ? (byte)200 : (byte)40;
        }
        return Frame.FromArray(w, h, data, sequence);
    }

    private static BotProfile CalibratedProfile()
    {
        using var full = HpFrame(1, 0);
        return new BotProfile
        {
            SkillEverySec = new int[5],
            RecordFrameIntervalMs = 100
        };
    }

    /// <summary>Records <paramref name="seconds"/> of 10 Hz ticks with HP following <paramref name="hpAt"/>.</summary>
    private void RecordSession(BotProfile profile, double seconds, Func<double, double> hpAt)
    {
        var errors = new List<string>();
        var recorder = new SessionRecorder(_dir, profile, "Test", m => { lock (errors) errors.Add(m); });
        var brain = new Brain(() => profile);
        brain.Start(TimeSpan.Zero);
        long seq = 0;
        for (double ms = 0; ms < seconds * 1000; ms += 100)
        {
            using var frame = HpFrame(hpAt(ms), ++seq);
            var state = new GameState(TimeSpan.FromMilliseconds(ms), frame.Width, frame.Height, hpAt(ms), null, null, 0);
            var actions = brain.Tick(state);
            recorder.Record(frame, state, brain.Phase, actions, important: false);
            // The real engine runs at 10 Hz; give the writer time like it would have.
            if (seq % 10 == 0) Thread.Sleep(20);
        }
        recorder.Dispose();
        Assert.Empty(errors);
    }

    [Fact]
    public void Png_round_trip_keeps_pixels()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "f.png");
        using var original = HpFrame(0.5, 7);
        RecordingFormat.SavePng(original, path);
        using var loaded = RecordingFormat.LoadPng(path);
        Assert.Equal(original.Width, loaded.Width);
        Assert.Equal(original.Height, loaded.Height);
        for (int y = 0; y < original.Height; y += 7)
        for (int x = 0; x < original.Width; x += 3)
            Assert.Equal(original.Pixel(x, y), loaded.Pixel(x, y));
    }

    [Fact]
    public void Recording_is_written_and_loaded_back()
    {
        var profile = CalibratedProfile();
        RecordSession(profile, 2, _ => 0.8);

        var recording = SessionRecording.Load(_dir);
        Assert.Equal(20, recording.Ticks.Count);
        Assert.Equal(0, recording.BadLines);
        Assert.All(recording.Ticks, t => Assert.InRange(t.Hp!.Value, 0.75, 0.85));
        var withFrame = recording.Ticks.Where(t => t.Frame != null).ToList();
        Assert.NotEmpty(withFrame);
        using var frame = recording.LoadFrame(withFrame[0]);
        Assert.NotNull(frame);
        Assert.Equal(200, frame!.Width);
    }

    [Fact]
    public void Replay_reproduces_recorded_decisions()
    {
        var profile = CalibratedProfile();
        // HP falls below the potion threshold after one second.
        RecordSession(profile, 3, ms => ms < 1000 ? 0.9 : 0.4);

        var recording = SessionRecording.Load(_dir);
        var steps = Replayer.Run(recording, recording.Profile).ToList();
        Assert.Equal(recording.Ticks.Count, steps.Count);
        Assert.Contains(steps, s => s.FromFrame);

        var recorded = recording.Ticks.SelectMany(t => t.Actions ?? Enumerable.Empty<string>()).ToList();
        var replayed = steps.SelectMany(s => s.Actions).Select(a => a.ToString()!).ToList();
        Assert.Equal(recorded, replayed);
        Assert.Contains(replayed, a => a == new KeyTapAction(profile.Keys.HpPotion).ToString());
    }

    [Fact]
    public void Replay_with_new_settings_changes_decisions_and_report_shows_it()
    {
        var profile = CalibratedProfile();
        RecordSession(profile, 3, ms => ms < 1000 ? 0.9 : 0.4);
        var recording = SessionRecording.Load(_dir);

        // With a lower potion threshold the same footage should not trigger a potion.
        var stricter = CalibratedProfile();
        stricter.PotionHpBelow = 0.3;
        var replayed = Replayer.Run(recording, stricter).SelectMany(s => s.Actions).ToList();
        Assert.DoesNotContain(replayed, a => a is KeyTapAction t && t.Key == stricter.Keys.HpPotion);

        string report = Replayer.Report(recording, stricter);
        Assert.Contains("Tuş D1", report);
        Assert.Contains("*", report);
    }

    [Fact]
    public void Truncated_last_line_is_skipped()
    {
        var profile = CalibratedProfile();
        RecordSession(profile, 1, _ => 1);
        File.AppendAllText(Path.Combine(_dir, RecordingFormat.TicksFile), "{\"T\":12");
        var recording = SessionRecording.Load(_dir);
        Assert.Equal(10, recording.Ticks.Count);
        Assert.Equal(1, recording.BadLines);
    }
}
