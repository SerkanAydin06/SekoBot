using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Vision;

namespace SekoBot.Bot;

internal sealed record EngineStats(
    string CaptureName, double CaptureFps, double AnalysisMs, BotPhase Phase,
    bool BotRunning, bool DryRun, int Stones, int Skipped, TimeSpan BotUptime);

/// <summary>
/// Pipeline: capture → (latest frame only) → perception → brain → input.
/// Each stage runs at its own pace; a slow stage only ever sees the newest frame,
/// so nothing queues up and latency stays flat.
/// </summary>
internal sealed class BotEngine : IDisposable
{
    private readonly WindowInfo _window;
    private readonly Action<string> _log;
    private readonly Perception _perception = new();
    private readonly Brain _brain;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _frameGate = new();
    private readonly object _botGate = new();

    private ICaptureSource? _capture;
    private Task? _loop;
    private Frame? _latest;
    private GameState? _state;
    private InputExecutor? _input;
    private TimeSpan _botStarted;
    private double _captureFps;
    private long _framesReceived;
    private int _previewFps;
    private ChannelWriter<Frame>? _writer;
    private bool _disposed;

    public BotEngine(WindowInfo window, BotProfile profile, Action<string> log)
    {
        _window = window;
        Profile = profile;
        _log = log;
        _brain = new Brain(() => Profile);
        ReloadTemplates();
    }

    public WindowInfo Window => _window;
    public bool GameMinimized => WindowService.IsMinimized(_window);
    public BotProfile Profile { get; set; }
    public GameState? State => Volatile.Read(ref _state);
    public int TemplateCount => _perception.Detector.TemplateCount;

    /// <summary>Raised from a worker thread when capture stops or the bot stops itself.</summary>
    public event Action<string>? CaptureFailed;
    public event Action<string>? BotStopped;

    public void Start(int previewFps)
    {
        var channel = Channel.CreateBounded<Frame>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            dropped => dropped.Dispose());
        _previewFps = previewFps;
        _writer = channel.Writer;
        StartCapture(CaptureFactory.Create(_window, previewFps, _log));
        _loop = Task.Run(() => RunAsync(channel.Reader, _cts.Token));
    }

    private void StartCapture(ICaptureSource capture)
    {
        _capture = capture;
        capture.Failed += message => OnCaptureFailed(capture, message);
        _log("Yakalama yöntemi: " + capture.Name);
        capture.Start(_writer!);
    }

    /// <summary>If GPU capture breaks before delivering anything, silently fall back to GDI.</summary>
    private void OnCaptureFailed(ICaptureSource capture, string message)
    {
        if (_disposed || !ReferenceEquals(capture, _capture))
            return;
        if (capture is WgcCapture && Interlocked.Read(ref _framesReceived) == 0)
        {
            _log(message + " GDI yakalamaya geçiliyor.");
            capture.Dispose();
            StartCapture(new GdiCapture(_window, _previewFps));
            return;
        }
        CaptureFailed?.Invoke(message);
    }

    public void ReloadTemplates() =>
        _perception.Detector.LoadFrom(ProfileStore.TemplateDirectory, Profile.Scales);

    private async Task RunAsync(ChannelReader<Frame> frames, CancellationToken token)
    {
        var lastAnalysis = TimeSpan.FromDays(-1);
        var fpsWindow = Stopwatch.StartNew();
        int fpsFrames = 0;
        try
        {
            await foreach (var frame in frames.ReadAllAsync(token))
            {
                fpsFrames++;
                Interlocked.Increment(ref _framesReceived);
                if (fpsWindow.Elapsed.TotalSeconds >= 1)
                {
                    _captureFps = fpsFrames / fpsWindow.Elapsed.TotalSeconds;
                    fpsFrames = 0;
                    fpsWindow.Restart();
                }

                var now = _clock.Elapsed;
                var profile = Profile;
                if (now - lastAnalysis >= TimeSpan.FromSeconds(1.0 / Math.Clamp(profile.PerceptionFps, 1, 30)))
                {
                    lastAnalysis = now;
                    try
                    {
                        var state = _perception.Analyze(frame, profile, now);
                        Volatile.Write(ref _state, state);
                        TickBot(state);
                    }
                    catch (Exception ex)
                    {
                        _log("Analiz hatası: " + ex.Message);
                    }
                }

                lock (_frameGate)
                {
                    _latest?.Dispose();
                    _latest = frame;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            CaptureFailed?.Invoke("İşlem hattı hatası: " + ex.Message);
        }
    }

    private void TickBot(GameState state)
    {
        string? stopReason = null;
        lock (_botGate)
        {
            if (_input == null)
                return;
            foreach (var action in _brain.Tick(state))
            {
                if (action is StopAction stop)
                {
                    stopReason = stop.Reason;
                    break;
                }
                _input.Enqueue(action);
            }
        }
        if (stopReason != null)
        {
            StopBot(stopReason);
            BotStopped?.Invoke(stopReason);
        }
    }

    public bool StartBot(bool dryRun, out string error)
    {
        error = "";
        if (!Profile.IsReadyForBot)
        {
            error = "Önce 'Hedef HP barı' kalibrasyonunu yapın.";
            return false;
        }
        if (TemplateCount == 0)
        {
            error = "Önce en az bir Metin şablonu ekleyin.";
            return false;
        }
        lock (_botGate)
        {
            if (_input != null)
                return true;
            _input = new InputExecutor(_window, dryRun, _log);
            _botStarted = _clock.Elapsed;
            _brain.Start(_clock.Elapsed);
        }
        _log(dryRun ? "Bot kuru çalışmada başladı (girdi gönderilmez)." : "Bot başladı. Acil durdurma: F12");
        return true;
    }

    public void StopBot(string reason)
    {
        InputExecutor? input;
        lock (_botGate)
        {
            input = _input;
            _input = null;
            _brain.Stop();
        }
        if (input == null)
            return;
        input.Dispose();
        _log("Bot durdu: " + reason);
    }

    public bool BotRunning
    {
        get { lock (_botGate) return _input != null; }
    }

    public EngineStats Stats
    {
        get
        {
            lock (_botGate)
            {
                return new EngineStats(_capture?.Name ?? "—", _captureFps, State?.AnalysisMs ?? 0,
                    _brain.Phase, _input != null, _input?.DryRun ?? true,
                    _brain.StonesDestroyed, _brain.TargetsSkipped,
                    _input != null ? _clock.Elapsed - _botStarted : TimeSpan.Zero);
            }
        }
    }

    /// <summary>Copies the newest frame into <paramref name="target"/>, reusing it when the size matches.</summary>
    public bool TryCopyLatest(ref Bitmap? target, ref long lastSequence)
    {
        lock (_frameGate)
        {
            if (_latest == null || _latest.Sequence == lastSequence)
                return false;
            var f = _latest;
            if (target == null || target.Width != f.Width || target.Height != f.Height)
            {
                target?.Dispose();
                target = new Bitmap(f.Width, f.Height, PixelFormat.Format32bppRgb);
            }
            var bits = target.LockBits(new Rectangle(0, 0, f.Width, f.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (int y = 0; y < f.Height; y++)
                    Marshal.Copy(f.Data, y * f.Stride, bits.Scan0 + y * bits.Stride, f.Stride);
            }
            finally
            {
                target.UnlockBits(bits);
            }
            lastSequence = f.Sequence;
            return true;
        }
    }

    /// <summary>Runs <paramref name="work"/> on the newest frame while it is guaranteed alive.</summary>
    public T? WithLatestFrame<T>(Func<Frame, T> work)
    {
        lock (_frameGate)
            return _latest == null ? default : work(_latest);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopBot("bağlantı kapandı");
        _cts.Cancel();
        _capture?.Dispose();
        try { _loop?.Wait(1500); } catch { }
        lock (_frameGate)
        {
            _latest?.Dispose();
            _latest = null;
        }
        _perception.Dispose();
        _cts.Dispose();
    }
}
