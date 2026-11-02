using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Recording;
using SekoBot.Vision;

namespace SekoBot.Bot;

internal sealed record EngineStats(
    string CaptureName, double CaptureFps, double AnalysisMs, BotPhase Phase,
    bool BotRunning, bool DryRun, int Cycles, int Potions, TimeSpan BotUptime);

/// <summary>
/// Pipeline: capture → (latest frame only) → perception → brain → input.
/// Each stage runs at its own pace; a slow stage only ever sees the newest frame,
/// so nothing queues up and latency stays flat.
/// </summary>
internal sealed class BotEngine : IDisposable
{
    private readonly WindowInfo _window;
    private readonly Action<string> _log;
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
    private SessionRecorder? _recorder;
    private readonly AutoBars _autoBars = new();
    private readonly MotionDetector _motion = new();
    private readonly object _spotGate = new();
    private Point? _targetSpot;
    private TimeSpan _targetSpotAt;
    private TimeSpan _targetConstSince = TimeSpan.FromDays(-1);
    private double _targetConstValue = -1;
    private TimeSpan _lastBarSearch = TimeSpan.FromDays(-1), _barSearchStarted = TimeSpan.FromDays(-1);
    private bool _barSearchFailedLogged;
    private bool _disposed;

    public BotEngine(WindowInfo window, BotProfile profile, Action<string> log)
    {
        _window = window;
        Profile = profile;
        _log = log;
        _brain = new Brain(() => Profile);
    }

    public WindowInfo Window => _window;
    public bool GameMinimized => WindowService.IsMinimized(_window);
    public BotProfile Profile { get; set; }
    public GameState? State => Volatile.Read(ref _state);

    /// <summary>Raised from a worker thread when capture stops or the bot stops itself.</summary>
    public event Action<string>? CaptureFailed;
    public event Action<string>? BotStopped;
    /// <summary>Raised from the pipeline thread when automatic bar detection changed the profile.</summary>
    public event Action<string>? BarsChanged;

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
                        SearchBars(frame, profile, now);
                        var state = Perception.Analyze(frame, profile, now);
                        if (profile.ReviveOnDeath && BotRunning)
                        {
                            var (dead, ready) = DeathDetector.Check(frame);
                            state = state with { Dead = dead, ReviveReady = ready };
                        }
                        if (profile.ClosePopups && BotRunning && !state.Dead)
                            state = state with { PopupOpen = PopupDetector.IsOpen(frame) };
                        if (profile.NavigateMode)
                        {
                            // Top-down camera: the name sits right on the mob.
                            int down = (int)(frame.Height * 0.02);
                            var mobs = NameFinder.Find(frame, profile.RequireLevelTag)
                                .Select(n => new Point(n.X + n.Width / 2, n.Y + n.Height / 2 + down))
                                .ToList();
                            state = state with { MobCandidates = mobs };
                        }
                        else if (profile.AutoTarget)
                        {
                            var moving = _motion.Update(frame, now);
                            var mobs = MobLocator.Combine(NameFinder.Find(frame), moving, frame.Width, frame.Height,
                                    name => MobLocator.BodyBelow(frame, name))
                                .Where(m => !_motion.RecentlyMissed(m, now)).ToList();
                            state = state with { MobCandidates = mobs, TargetSpot = TrackTarget(state, mobs, now) };
                        }
                        Volatile.Write(ref _state, state);
                        CheckTargetBar(state, profile, frame, now);
                        var (phase, actions) = TickBot(state);
                        Volatile.Read(ref _recorder)?.Record(frame, state, phase, actions, IsImportant(actions, profile));
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

    /// <returns>The brain's phase and every action it produced this tick (for recording).</returns>
    private (BotPhase Phase, IReadOnlyList<BotAction> Actions) TickBot(GameState state)
    {
        string? stopReason = null;
        IReadOnlyList<BotAction> actions;
        BotPhase phase;
        lock (_botGate)
        {
            if (_input == null)
                return (BotPhase.Stopped, Array.Empty<BotAction>());
            actions = _brain.Tick(state);
            phase = _brain.Phase;
            foreach (var action in actions)
            {
                if (action is StopAction stop)
                {
                    stopReason = stop.Reason;
                    break;
                }
                if (action is NoteAction note)
                {
                    _log(note.Text);
                    continue;
                }
                _input.Enqueue(action);
            }
        }
        if (stopReason != null)
        {
            StopBot(stopReason);
            BotStopped?.Invoke(stopReason);
        }
        return (phase, actions);
    }

    private static bool IsImportant(IReadOnlyList<BotAction> actions, BotProfile profile) =>
        actions.Any(a => a is StopAction ||
                         a is KeyTapAction tap && (tap.Key == profile.Keys.HpPotion || tap.Key == profile.Keys.MpPotion));

    /// <summary>
    /// A real target bar changes (mob gets hit) or disappears (mob dies, nothing selected).
    /// One that shows the same value for 45 s while the bot runs is some other red thing at
    /// the top of the screen: forget it, never pick that spot again, and look for the real one.
    /// </summary>
    private void CheckTargetBar(GameState state, BotProfile profile, Frame frame, TimeSpan now)
    {
        if (profile.TargetBar is not { Auto: true } bar || !BotRunning || !state.HasTarget)
        {
            _targetConstSince = TimeSpan.FromDays(-1);
            return;
        }
        double value = state.TargetHp!.Value;
        if (_targetConstSince < TimeSpan.Zero || Math.Abs(value - _targetConstValue) > 0.01)
        {
            _targetConstSince = now;
            _targetConstValue = value;
            return;
        }
        if (now - _targetConstSince < TimeSpan.FromSeconds(45))
            return;
        _targetConstSince = TimeSpan.FromDays(-1);
        _autoBars.Reject(bar.Area.ToPixels(frame.Width, frame.Height), frame.Width, frame.Height);
        profile.TargetBar = null;
        _log("Hedef barı sanılan yer hiç değişmiyor; gerçek hedef barı değil. Yeniden aranıyor.");
        BarsChanged?.Invoke("");
    }

    /// <summary>Follows the selected mob on screen: the moving thing nearest to where it was.</summary>
    private Point? TrackTarget(GameState state, IReadOnlyList<Point> mobs, TimeSpan now)
    {
        lock (_spotGate)
        {
            if (_targetSpot is not Point spot)
                return null;
            if (!state.HasTarget)
            {
                // Give the target bar a moment to show after the click, then forget the spot.
                if (now - _targetSpotAt > TimeSpan.FromSeconds(2))
                    _targetSpot = null;
                return _targetSpot;
            }
            static double Dist(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
            var nearest = mobs.Where(m => Dist(m, spot) < 120).OrderBy(m => Dist(m, spot)).ToList();
            if (nearest.Count > 0)
            {
                _targetSpot = nearest[0];
                _targetSpotAt = now;
            }
            return _targetSpot;
        }
    }

    // ── Automatic bar detection ──────────────────────────────────

    private void SearchBars(Frame frame, BotProfile profile, TimeSpan now)
    {
        if (!profile.AutoDetectBars)
            return;
        bool missing = profile.HpBar == null || profile.TargetBar == null;
        // Look often while a bar is unknown (the target bar only shows after a click),
        // then now and then (to widen a bar that was not full).
        if (now - _lastBarSearch < TimeSpan.FromSeconds(missing ? 1 : 3))
            return;
        _lastBarSearch = now;
        if (_barSearchStarted < TimeSpan.Zero)
            _barSearchStarted = now;
        try
        {
            var before = (profile.HpBar, profile.MpBar, profile.TargetBar);
            var messages = _autoBars.Apply(profile, BarFinder.Find(frame), frame);
            // Also fires when a bar was only widened (no message), so the profile gets saved.
            if (before != (profile.HpBar, profile.MpBar, profile.TargetBar))
                BarsChanged?.Invoke(string.Join("  ", messages));
            if (profile.HpBar == null && !_barSearchFailedLogged && now - _barSearchStarted > TimeSpan.FromSeconds(10))
            {
                _barSearchFailedLogged = true;
                string shot = Path.Combine(ProfileStore.Directory, "hp-bulunamadi.png");
                try { Directory.CreateDirectory(ProfileStore.Directory); RecordingFormat.SavePng(frame, shot); } catch { }
                _log("HP barı otomatik bulunamadı. Oyunda karakterin HP/MP barları ekranda görünür olmalı. " +
                     $"Olmazsa şu resmi gönder: {shot}  (ya da önizlemede barı fareyle seçip 'HP barı'na bas)");
            }
        }
        catch (Exception ex)
        {
            _log("Otomatik bar arama hatası: " + ex.Message);
        }
    }

    // ── Recording ────────────────────────────────────────────────

    public bool IsRecording => Volatile.Read(ref _recorder) != null;
    public SessionRecorder? Recorder => Volatile.Read(ref _recorder);

    /// <summary>Starts recording bar readings, decisions and frames into a new folder.</summary>
    public string StartRecording()
    {
        if (Volatile.Read(ref _recorder) is { } running)
            return running.Directory;
        var recorder = new SessionRecorder(SessionRecorder.NewDirectory(), Profile, _window.Title, _log);
        if (Interlocked.CompareExchange(ref _recorder, recorder, null) != null)
            recorder.Dispose();
        _log("Kayıt başladı: " + recorder.Directory);
        return recorder.Directory;
    }

    public void StopRecording()
    {
        var recorder = Interlocked.Exchange(ref _recorder, null);
        if (recorder == null)
            return;
        recorder.Dispose();
        _log($"Kayıt bitti: {recorder.Ticks:N0} tur, {recorder.FramesSaved:N0} kare, {recorder.Bytes / 1024.0 / 1024.0:0.0} MB.");
    }

    public bool StartBot(bool dryRun, out string error) => StartBot(dryRun, false, out error);

    public bool StartBot(bool dryRun, bool background, out string error)
    {
        error = "";
        if (GameMinimized)
        {
            error = "Oyun simge durumunda; önce oyunu geri aç.";
            return false;
        }
        if (Profile.HpBar == null)
            _log("Uyarı: HP barı işaretli değil; iksir ve HP güvenlik durdurması çalışmaz.");
        lock (_botGate)
        {
            if (_input != null)
                return true;
            _input = new InputExecutor(_window, dryRun, _log,
                () => Profile.TargetBar == null ? null : State?.HasTarget == true,
                () => State?.TargetHp,
                spot => _motion.MarkMiss(spot, _clock.Elapsed),
                spot => { lock (_spotGate) { _targetSpot = spot; _targetSpotAt = _clock.Elapsed; } },
                background);
            _botStarted = _clock.Elapsed;
            _brain.Start(_clock.Elapsed);
        }
        _log(dryRun ? "Bot kuru çalışmada başladı (girdi gönderilmez)."
            : background ? "Bot arka plan modunda başladı (deneysel): tuşlar oyun penceresine mesajla gidiyor. Acil durdurma: F12"
            : "Bot başladı. Acil durdurma: F12");
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
                    _brain.Cycles, _brain.Potions,
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
        StopRecording();
        _cts.Cancel();
        _capture?.Dispose();
        try { _loop?.Wait(1500); } catch { }
        lock (_frameGate)
        {
            _latest?.Dispose();
            _latest = null;
        }
        _cts.Dispose();
    }
}
