using System.Globalization;
using System.Text;
using System.Text.Json;
using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Recording;

/// <summary>A recording folder loaded from disk.</summary>
internal sealed class SessionRecording
{
    private SessionRecording(string directory, RecordingInfo info, BotProfile profile, List<RecordedTick> ticks, int badLines)
    {
        Directory = directory;
        Info = info;
        Profile = profile;
        Ticks = ticks;
        BadLines = badLines;
    }

    public string Directory { get; }
    public RecordingInfo Info { get; }
    /// <summary>Profile that was active while recording.</summary>
    public BotProfile Profile { get; }
    public IReadOnlyList<RecordedTick> Ticks { get; }
    /// <summary>Unreadable lines (e.g. the last one after a crash) that were skipped.</summary>
    public int BadLines { get; }

    public static SessionRecording Load(string directory)
    {
        string ticksPath = Path.Combine(directory, RecordingFormat.TicksFile);
        if (!File.Exists(ticksPath))
            throw new FileNotFoundException("Bu klasör bir SekoBot kaydı değil (ticks.jsonl yok).", ticksPath);

        var info = TryRead<RecordingInfo>(Path.Combine(directory, RecordingFormat.InfoFile)) ?? new RecordingInfo();
        if (info.Version > RecordingFormat.Version)
            throw new InvalidDataException($"Kayıt daha yeni bir SekoBot sürümüyle yapılmış (biçim {info.Version}).");
        var profile = TryRead<BotProfile>(Path.Combine(directory, RecordingFormat.ProfileFile)) ?? new BotProfile();

        var ticks = new List<RecordedTick>();
        int bad = 0;
        using (var stream = new FileStream(ticksPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0)
                    continue;
                try
                {
                    if (JsonSerializer.Deserialize<RecordedTick>(line, RecordingFormat.Json) is { } tick)
                        ticks.Add(tick);
                }
                catch (JsonException)
                {
                    bad++;
                }
            }
        }
        return new SessionRecording(directory, info, profile, ticks, bad);
    }

    private static T? TryRead<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), RecordingFormat.Json) : null; }
        catch (JsonException) { return null; }
    }

    /// <summary>Loads the tick's frame, or null when it has none or the file is missing.</summary>
    public Frame? LoadFrame(RecordedTick tick, long sequence = 0)
    {
        if (tick.Frame == null)
            return null;
        string path = Path.Combine(Directory, tick.Frame);
        return File.Exists(path) ? RecordingFormat.LoadPng(path, sequence) : null;
    }
}

/// <summary>One replayed tick: what the current code reads and decides for a recorded moment.</summary>
internal sealed record ReplayStep(RecordedTick Recorded, GameState State, bool FromFrame, IReadOnlyList<BotAction> Actions);

/// <summary>
/// Feeds a recording through the current perception + brain, offline and without input.
/// Ticks with a stored frame are re-analysed with <c>profile</c> (so a new calibration can be
/// tested on old footage); ticks without one reuse the recorded bar values.
/// </summary>
internal static class Replayer
{
    public static IEnumerable<ReplayStep> Run(SessionRecording recording, BotProfile profile, bool reanalyzeFrames = true)
    {
        if (recording.Ticks.Count == 0)
            yield break;
        var brain = new Brain(() => profile);
        brain.Start(recording.Ticks[0].Time);
        long sequence = 0;
        foreach (var tick in recording.Ticks)
        {
            GameState state;
            bool fromFrame = false;
            using (var frame = reanalyzeFrames ? recording.LoadFrame(tick, ++sequence) : null)
            {
                if (frame != null)
                {
                    state = new GameState(tick.Time, frame.Width, frame.Height, tick.Hp, tick.Mp, tick.Target, 0);
                    fromFrame = true;
                }
                else
                {
                    state = new GameState(tick.Time, tick.W, tick.H, tick.Hp, tick.Mp, tick.Target, 0);
                }
            }
            yield return new ReplayStep(tick, state, fromFrame, brain.Tick(state));
        }
    }

    /// <summary>Human-readable comparison of the recording against a replay (Turkish, for the UI).</summary>
    public static string Report(SessionRecording recording, BotProfile profile)
    {
        var inv = CultureInfo.InvariantCulture;
        var recorded = new Dictionary<string, int>();
        var replayed = new Dictionary<string, int>();
        var drift = new List<string>();
        int frames = 0, driftCount = 0;
        double? minHp = null;
        string? recordedStop = null, replayStop = null;
        TimeSpan start = recording.Ticks.Count > 0 ? recording.Ticks[0].Time : TimeSpan.Zero;

        foreach (var step in Run(recording, profile))
        {
            var t = step.Recorded;
            foreach (var a in t.Actions ?? Enumerable.Empty<string>())
            {
                Count(recorded, a);
                if (recordedStop == null && a.StartsWith("Dur:", StringComparison.Ordinal))
                    recordedStop = $"{Clock(t.Time - start)} {a}";
            }
            foreach (var a in step.Actions)
            {
                Count(replayed, a.ToString()!);
                if (replayStop == null && a is StopAction stop)
                    replayStop = $"{Clock(t.Time - start)} Dur: {stop.Reason}";
            }
            if (step.State.Hp is double hp)
                minHp = Math.Min(minHp ?? 1, hp);
            if (!step.FromFrame)
                continue;
            frames++;
            double worst = Math.Max(Diff(t.Hp, step.State.Hp), Math.Max(Diff(t.Mp, step.State.Mp), Diff(t.Target, step.State.TargetHp)));
            if (worst > 0.10)
            {
                driftCount++;
                if (drift.Count < 10)
                    drift.Add($"  {t.Frame}: kayıt HP/MP/hedef {Pct(t.Hp)}/{Pct(t.Mp)}/{Pct(t.Target)} → şimdi {Pct(step.State.Hp)}/{Pct(step.State.Mp)}/{Pct(step.State.TargetHp)}");
            }
        }

        var duration = recording.Ticks.Count > 0 ? recording.Ticks[^1].Time - start : TimeSpan.Zero;
        var sb = new StringBuilder();
        sb.AppendLine($"SekoBot tekrar oynatma raporu · {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Kayıt: {recording.Directory}");
        sb.AppendLine($"Kaydeden sürüm: {recording.Info.AppVersion}  ·  Oynatan sürüm: {BuildInfo.Version}");
        sb.AppendLine($"Süre {Clock(duration)}  ·  {recording.Ticks.Count:N0} tur kaydı  ·  {frames:N0} kare yeniden analiz edildi" +
                      (recording.BadLines > 0 ? $"  ·  {recording.BadLines} okunamayan satır atlandı" : ""));
        sb.AppendLine($"En düşük HP: {Pct(minHp)}");
        sb.AppendLine();
        sb.AppendLine("Durma:");
        sb.AppendLine($"  kayıtta: {recordedStop ?? "yok"}");
        sb.AppendLine($"  şimdi:   {replayStop ?? "yok"}");
        sb.AppendLine();
        sb.AppendLine("Eylemler (kayıtta → şimdiki kodla):");
        foreach (var key in recorded.Keys.Union(replayed.Keys).Where(k => !k.StartsWith("Dur:", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            int a = recorded.GetValueOrDefault(key), b = replayed.GetValueOrDefault(key);
            sb.AppendLine($"  {key,-28} {a,6} → {b,6}{(a != b ? "   *" : "")}");
        }
        sb.AppendLine();
        sb.AppendLine(driftCount == 0
            ? "Bar okumaları kayıtla uyumlu (fark ≤ %10)."
            : $"{driftCount} karede bar okuması kayıttan %10'dan fazla farklı (kalibrasyon değişmiş olabilir):");
        foreach (var line in drift)
            sb.AppendLine(line);
        if (driftCount > drift.Count)
            sb.AppendLine($"  … ve {driftCount - drift.Count} kare daha");
        return sb.ToString();

        static void Count(Dictionary<string, int> map, string key) => map[key] = map.GetValueOrDefault(key) + 1;
        static double Diff(double? a, double? b) => a is double x && b is double y ? Math.Abs(x - y) : (a.HasValue != b.HasValue ? 1 : 0);
        string Pct(double? v) => v is double x ? "%" + (x * 100).ToString("0", inv) : "—";
        static string Clock(TimeSpan t) => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }
}
