using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Recording;

/// <summary>
/// Writes a recording while the engine runs. <see cref="Record"/> is called from the
/// pipeline thread and never blocks: frames are cloned and handed to a writer task
/// that encodes PNGs and appends to ticks.jsonl. If the writer falls behind, frames
/// are skipped (the tick line is still written), so the bot never slows down.
/// </summary>
internal sealed class SessionRecorder : IDisposable
{
    private const int MaxPendingFrames = 4;

    private sealed record Item(RecordedTick Tick, Frame? Image);

    private readonly Channel<Item> _queue = Channel.CreateUnbounded<Item>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Action<string> _log;
    private readonly TimeSpan _frameInterval;
    private readonly long _maxBytes;
    private readonly Task _writer;
    private TimeSpan _lastFrame = TimeSpan.FromDays(-1);
    private int _pendingFrames, _framesSaved, _ticks, _frameNumber;
    private long _bytes;
    private bool _limitWarned;

    public SessionRecorder(string directory, BotProfile profile, string window, Action<string> log)
    {
        Directory = directory;
        _log = log;
        _frameInterval = TimeSpan.FromMilliseconds(Math.Max(100, profile.RecordFrameIntervalMs));
        _maxBytes = Math.Max(10, profile.RecordMaxMb) * 1024L * 1024L;

        System.IO.Directory.CreateDirectory(Path.Combine(directory, RecordingFormat.FramesDir));
        var info = new RecordingInfo { AppVersion = BuildInfo.Version, StartedUtc = DateTime.UtcNow, Window = window };
        File.WriteAllText(Path.Combine(directory, RecordingFormat.InfoFile),
            JsonSerializer.Serialize(info, RecordingFormat.JsonIndented));
        File.WriteAllText(Path.Combine(directory, RecordingFormat.ProfileFile),
            JsonSerializer.Serialize(profile, RecordingFormat.JsonIndented));
        _writer = Task.Run(WriteAsync);
    }

    public string Directory { get; }
    public int FramesSaved => Volatile.Read(ref _framesSaved);
    public int Ticks => Volatile.Read(ref _ticks);
    public long Bytes => Interlocked.Read(ref _bytes);

    public static string NewDirectory() =>
        Path.Combine(RecordingFormat.Root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));

    /// <param name="important">Potion or stop: store this frame regardless of the interval.</param>
    public void Record(Frame frame, GameState state, BotPhase phase, IReadOnlyList<BotAction> actions, bool important)
    {
        var tick = new RecordedTick
        {
            T = Math.Round(state.Time.TotalMilliseconds, 1),
            W = state.Width,
            H = state.Height,
            Hp = Round(state.Hp),
            Mp = Round(state.Mp),
            Target = Round(state.TargetHp),
            Phase = phase.ToString(),
            Actions = actions.Count == 0 ? null : actions.Select(a => a.ToString()!).ToList()
        };

        Frame? image = null;
        bool due = important || state.Time - _lastFrame >= _frameInterval;
        if (due && Interlocked.Read(ref _bytes) < _maxBytes && Volatile.Read(ref _pendingFrames) < MaxPendingFrames)
        {
            image = frame.Clone();
            // Own counter: capture sequences restart after a GPU→GDI fallback.
            tick.Frame = $"{RecordingFormat.FramesDir}/{++_frameNumber:D7}.png";
            _lastFrame = state.Time;
            Interlocked.Increment(ref _pendingFrames);
        }
        else if (due && !_limitWarned && Interlocked.Read(ref _bytes) >= _maxBytes)
        {
            _limitWarned = true;
            _log($"Kayıt boyut sınırına ({_maxBytes / 1024 / 1024} MB) ulaştı; bundan sonra yalnızca değerler kaydedilir.");
        }

        if (!_queue.Writer.TryWrite(new Item(tick, image)))
        {
            // Recorder already closed.
            if (image != null)
            {
                image.Dispose();
                Interlocked.Decrement(ref _pendingFrames);
            }
        }
    }

    private static double? Round(double? value) => value is double v ? Math.Round(v, 4) : null;

    private async Task WriteAsync()
    {
        string ticksPath = Path.Combine(Directory, RecordingFormat.TicksFile);
        try
        {
            await using var ticks = new StreamWriter(ticksPath, append: true, new UTF8Encoding(false));
            await foreach (var item in _queue.Reader.ReadAllAsync())
            {
                if (item.Image != null)
                {
                    try
                    {
                        string path = Path.Combine(Directory, item.Tick.Frame!);
                        RecordingFormat.SavePng(item.Image, path);
                        Interlocked.Add(ref _bytes, new FileInfo(path).Length);
                        Interlocked.Increment(ref _framesSaved);
                    }
                    catch (Exception ex)
                    {
                        item.Tick.Frame = null;
                        _log("Kayıt karesi yazılamadı: " + ex.Message);
                    }
                    finally
                    {
                        item.Image.Dispose();
                        Interlocked.Decrement(ref _pendingFrames);
                    }
                }
                string line = JsonSerializer.Serialize(item.Tick, RecordingFormat.Json);
                await ticks.WriteLineAsync(line);
                Interlocked.Add(ref _bytes, line.Length + 2);
                Interlocked.Increment(ref _ticks);
                if (_queue.Reader.Count == 0)
                    await ticks.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _log("Kayıt durdu: " + ex.Message);
            // Drain so cloned frames go back to the pool.
            while (_queue.Reader.TryRead(out var rest))
                rest.Image?.Dispose();
        }
    }

    /// <summary>Flushes everything that was queued and closes the files.</summary>
    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try { _writer.Wait(TimeSpan.FromSeconds(10)); } catch { }
    }
}
