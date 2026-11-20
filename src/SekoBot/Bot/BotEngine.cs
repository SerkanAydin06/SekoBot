using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;
using SekoBot.Recording;
using SekoBot.Vision;

namespace SekoBot.Bot;

internal sealed record EngineStats(
    string CaptureName, double CaptureFps, double AnalysisMs, BotPhase Phase,
    bool BotRunning, bool DryRun, int Cycles, int Potions, TimeSpan BotUptime,
    int Deaths, string Area, SessionReport? Session);

/// <summary>
/// Pipeline: capture → (latest frame only) → memory + perception → brain → input.
/// Each stage runs at its own pace; a slow stage only ever sees the newest frame,
/// so nothing queues up and latency stays flat.
/// </summary>
internal sealed partial class BotEngine : IDisposable
{
    /// <summary>The keys that stop this SekoBot (F11 only for the 1st; each has Ctrl+Shift+ its number).</summary>
    internal static string StopKeys => (Instance.IsFirst ? "F11 ya da " : "") + $"Ctrl+Shift+{Math.Min(Instance.Number, 9)}";

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
    private bool _disposed;
    private readonly SessionStats _session = new();
    private readonly NoteFilter _notes = new();
    private bool _sessionStarted;
    private Vec3? _farmSpot;
    private DateTime _sessionStartedAt;
    /// <summary>The summary of the last run (for the stop message), or null.</summary>
    private string? _lastSummary;

    /// <summary>The last farm run's summary (minutes, mobs, EXP, yang, potions, deaths), once one ended.</summary>
    public string? LastSummary => _lastSummary;
    private bool _statsOk = true, _statsWarned;
    private TimeSpan _statsFailSince = TimeSpan.FromDays(-1);

    public BotEngine(WindowInfo window, BotProfile profile, Action<string> log)
    {
        _window = window;
        Profile = profile;
        _log = log;
        _brain = new Brain(() => Profile) { JunkInBag = JunkInBag, Obstacles = _obstacles, Terrain = _terrain, NoGo = _noGo, Lore = SkillLore.Shared };
        if (ItemBook.FilePath == null)
            ItemBook.Load(Path.Combine(ProfileStore.Directory, "esya-bilgisi.json"));
        _session.PotionsRanOut += kind =>
        {
            if (World is { } w && IsLoginScreen(w.MapName))
                return; // the login screen shows an empty bag: not out of potions
            _log($"⚠ {kind} iksiri bitti! Envanterde kalmadı.");
            Notify($"⚠ {kind} iksiri bitti!");
        };
    }

    /// <summary>The game's login / character screen (map "loginMain"): kicked out — maintenance or the connection lost.</summary>
    internal static bool IsLoginScreen(string map) => map.Contains("login", StringComparison.OrdinalIgnoreCase);

    /// <summary>The bot (or fishing) was working when the game fell to the login screen: only then is the
    /// login tried again by itself (a bot left standing has no reason to log in).</summary>
    public bool KickedWhileWorking { get; set; }

    /// <summary>The world last seen in the game (not on the login screen): the session's map and level.</summary>
    private WorldSnapshot? _lastInGame;

    public WindowInfo Window => _window;
    public bool GameMinimized => WindowService.IsMinimized(_window);
    public BotProfile Profile { get; set; }
    public GameState? State => Volatile.Read(ref _state);
    public string StatsSource => _memoryStatus;
    /// <summary>False while the game's memory could not be read for a while.</summary>
    public bool StatsOk => Volatile.Read(ref _statsOk);

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

    private bool _previewShown = true;

    /// <summary>
    /// The live picture on screen or not (the small bar hides it): unseen, the game is captured only as
    /// often as the bot looks (Perception fps) instead of 30 times a second — less work with many bots.
    /// </summary>
    public void ShowingPreview(bool shown)
    {
        _previewShown = shown;
        if (_capture is { } c)
            c.MaxFps = shown ? _previewFps : Math.Clamp(Profile.PerceptionFps, 1, _previewFps);
    }

    private void StartCapture(ICaptureSource capture)
    {
        _capture = capture;
        if (!_previewShown)
            capture.MaxFps = Math.Clamp(Profile.PerceptionFps, 1, _previewFps);
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
                // 0.8: a frame coming a little early still counts (captured at just this rate when the picture is hidden).
                if (now - lastAnalysis >= TimeSpan.FromSeconds(0.8 / Math.Clamp(profile.PerceptionFps, 1, 30)))
                {
                    lastAnalysis = now;
                    try
                    {
                        var watch = Stopwatch.StartNew();
                        var world = ReadWorld(now);
                        var state = new GameState(now, frame.Width, frame.Height,
                            world?.Stats?.HpFraction, world?.Stats?.MpFraction, world?.TargetHp, 0)
                        {
                            Position = world?.Player,
                            Camera = world?.Camera,
                            MapName = world?.MapName ?? "",
                            Walking = Walking(world, now),
                            Dead = world?.Dead == true,
                            Stunned = world?.Stunned == true,
                            TargetSpot = MemoryTargetSpot(world, frame.Width, frame.Height),
                        };
                        if (world is { Dead: true })
                            state = state with { ReviveReady = world.ReviveReady };
                        if (profile.ClosePopups && BotRunning && !state.Dead)
                            // While throwing items the bag and its menu are ours (not windows to close or tell).
                            state = state with
                            {
                                PopupOpen = !_cleaner.Active && !_brain.ReadyToClean && PopupOpen(world),
                                PopupIsBag = _popupIsBag,
                                Typing = world?.OpenWindows?.Contains(GameWorldReader.MessageWindow) == true,
                            };
                        var view = MemoryView(world, profile, frame.Width, frame.Height, DumpSpot(now), fit: !_fitRelaxed);
                        var mobs = view?.Mobs ?? [];
                        if (!profile.NavigateMode)
                            mobs = mobs.Where(m => !RecentlyMissed(m, now)).ToList();
                        state = state with
                        {
                            MobCandidates = mobs,
                            PriorityMobs = view?.Priority ?? [],
                            Drops = view?.Drops ?? [],
                            Junk = view?.Junk ?? [],
                            Attackers = view?.Attackers ?? [],
                            Targets = view?.Targets ?? [],
                            AttackPower = world?.AttackPower ?? 0,
                            Hotspot = Hotspot(world, profile, now),
                            Follow = profile.Attack ? null : FollowWho(world?.Players, world?.Player, CharacterName, profile.FollowName),
                            FarmSpot = _farmSpot,
                            Skills = SkillKeys(world, profile),
                            SkillsOffSlots = SkillsOffSlots(world, profile),
                            MpPoints = world?.Stats?.Mp,
                            // What the key can still drink: 0 once the game emptied it (then the bot gets away safely).
                            HpPotionsLeft = world is null ? null : Potion(world, profile.Keys.HpPotion)?.Drinkable ?? 0,
                            AnalysisMs = watch.Elapsed.TotalMilliseconds,
                        };
                        Volatile.Write(ref _state, state);
                        // No memory (loading screen): the bot waits.
                        if (TrackMemory(world != null, now))
                        {
                            if (world != null && BotRunning)
                            {
                                _session.Update(world, now, PotionVnum(world, profile.Keys.HpPotion), PotionVnum(world, profile.Keys.MpPotion));
                                CheckSafety(world, profile);
                                CheckPotionKeys(world, profile);
                                WatchTick(world, profile, now);
                                if (profile.Attack)
                                    FitTick(world, profile, now);
                                SocialTick(world, profile, now);
                                NotifyEvents(world);
                            }
                            if (world != null)
                            {
                                TrackPicked(world, now);
                                NoticeTownNpcs(world, now);
                            }
                            WatchMarket(world, now);
                            WatchTerrain(world, now);
                            ReadBagForCleaning();
                            ReadDmForWhisper();
                            var (phase, actions) = TickBot(state);
                            Volatile.Read(ref _recorder)?.Record(frame, state, phase, actions, IsImportant(actions, profile));
                        }
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
            // Kicked to the login screen (maintenance, the connection lost): the bag reads empty there — not
            // "out of potions" (seen 2026-11-20 10:03, every bot); stopped with the real reason.
            var seen = World;
            if (seen != null && !IsLoginScreen(seen.MapName))
                _lastInGame = seen;
            if (seen != null && IsLoginScreen(seen.MapName))
            {
                KickedWhileWorking = true;
                actions = [new StopAction("oyundan atıldı, giriş ekranında (bakım ya da bağlantı koptu)")];
            }
            // Off to a market NPC to sell: that trip drives the character, the brain waits.
            else
                actions = _selling ? [] : WhisperTick(state, Profile) ?? CleanTick(state, Profile) ?? _brain.Tick(state);
            phase = _brain.Phase;
            // Throwing items away: the game stays in front for all the clicks, not back and forth for each.
            _input.HoldFront = _cleaner.Active;
            // Nothing (left) to do with the screen: the turn goes to the next bot.
            if (!_brain.WantsClean && !_tidying && FrontTurn.Shared.Mine)
                LetGoTurn();
            if (_brain.ProfileChanged)
            {
                // Learned (how many mobs this character takes on this map): kept with the character.
                _brain.ProfileChanged = false;
                try { ProfileStore.Save(Profile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            foreach (var action in actions)
            {
                if (action is StopAction stop)
                {
                    stopReason = stop.Reason;
                    break;
                }
                if (action is NoteAction note)
                {
                    // The same routine note again within a few minutes isn't written (the log stayed unreadable).
                    if (_notes.ShouldWrite(note.Text, state.Time))
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
            Notify("⏹ Bot durdu: " + stopReason + (_lastSummary is { } s1 ? "\n" + s1 : ""));
        }
        return (phase, actions);
    }

    private static bool IsImportant(IReadOnlyList<BotAction> actions, BotProfile profile) =>
        actions.Any(a => a is StopAction ||
                         a is KeyTapAction tap && (tap.Key == profile.Keys.HpPotion || tap.Key == profile.Keys.MpPotion));

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
        // What the shops taught about bonuses: which weapons / armour are worth picking up for the bonus list.
        try { BonusKnowledge = BonusBook.Shared; }
        catch (IOException) { }
        error = "";
        if (GameMinimized)
        {
            error = "Oyun simge durumunda; önce oyunu geri aç.";
            return false;
        }
        // Memory is only read on the perception thread; here just look at what it last saw.
        if (World == null)
        {
            error = "Oyunun belleği okunamıyor (" + _memoryStatus + "). SekoBot'u yönetici olarak aç ve karakterle oyuna gir.";
            return false;
        }
        if (IsLoginScreen(World.MapName))
        {
            error = "Oyun giriş ekranında; önce karakterle oyuna gir.";
            return false;
        }
        if (ScanningArea || ScanningMarket || Fishing)
        {
            error = ScanningArea ? "Alan taranıyor; bitmesini bekle." : Fishing ? "Balık tutuluyor; önce onu durdur." : "Pazar taranıyor; önce onu durdur.";
            return false;
        }
        if (World.InSafeZone && Profile.StopInSafeZone)
        {
            error = "Karakter güvenli bölgede (şehirde). Önce avlanma alanına git.";
            return false;
        }
        lock (_botGate)
        {
            if (_input != null)
                return true;
            _input = new InputExecutor(_window, dryRun, _log,
                () => State is { TargetHp: not null } s ? s.HasTarget : null,
                () => State?.TargetHp,
                MarkMiss,
                _ => { }, // the selected mob's place on screen comes from memory
                background);
            _botStarted = _clock.Elapsed;
            _brain.Start(_clock.Elapsed);
            _cleaner.Abort(null, Profile, "");
            _whisper.Abort("");
            _pendingWhisper = null;
            _cleanTries = 0;
            _notes.Reset();
            _sessionItems.Reset();
            lock (_social)
                _social.Reset();
            _session.Reset(_clock.Elapsed);
            _players.Reset();
            ResetFit();
            _sessionStartedAt = DateTime.Now;
            _farmSpot = UsableFarmSpot(World, Profile, out string? why);
            if (why != null)
                _log(why);
            _botHome = _farmSpot ?? World?.Player;
            // Windows already showing when the bot starts (chat, minimap, …) belong to the screen.
            _popups.Start(World?.OpenWindows ?? []);
            _windowsSeen = "";
            _sessionStarted = true;
        }
        _log(dryRun ? "Bot kuru çalışmada başladı (girdi gönderilmez)."
            : background ? $"Bot başladı (oyun arka planda kalabilir). Acil durdurma: {StopKeys}"
            : $"Bot başladı. Acil durdurma: {StopKeys}");
        return true;
    }

    public void StopBot(string reason)
    {
        _sellCts?.Cancel(); // a sale trip stops with the bot
        ItemBook.SaveIfChanged();
        _sessionItems.Save(force: true);
        if (_obstacles.Changed)
            _obstacles.Save();
        if (_noGo.Changed)
            _noGo.Save();
        _brain.Lore.Save();
        lock (_journal)
            if (_journal.Changed)
                _journal.Save(JournalPath);
        InputExecutor? input;
        lock (_botGate)
        {
            input = _input;
            _input = null;
            _brain.Stop();
        }
        if (input == null)
            return;
        input.HoldFront = false;
        input.Dispose();
        _tidying = false;
        ShrinkAfterWork();
        FrontTurn.Shared.LetGo(); // another bot waiting can go on
        _log("Bot durdu: " + reason);
        WriteSessionReport(reason);
    }

    /// <summary>Runs over a minute go into oturumlar.csv; the summary goes to the log.</summary>
    private void WriteSessionReport(string reason)
    {
        _lastSummary = null;
        var duration = DateTime.Now - _sessionStartedAt;
        if (!_sessionStarted || duration < TimeSpan.FromMinutes(1))
            return;
        var world = World is { } now && !IsLoginScreen(now.MapName) ? now : _lastInGame ?? World;
        var report = _session.Report(world, _clock.Elapsed);
        _lastSummary = SessionLog.Summary(duration, report, _brain.Deaths);
        _log("Oturum: " + _lastSummary);
        if (_brain.Detours + _brain.NoWayMobs > 0)
            _log($"🗺 Harita: {_brain.Detours} kez engelin (duvar, kaya, ağaç) etrafından dolanıldı; " +
                 $"{_brain.NoWayMobs} mob ya da yerdeki eşyaya kısa yol olmadığı için gidilmedi.");
        try
        {
            SessionLog.Append(SessionLog.Line(_sessionStartedAt, DateTime.Now, world?.MapName ?? "", report, _brain.Deaths, reason));
        }
        catch (IOException ex)
        {
            _log("Oturum raporu yazılamadı: " + ex.Message);
        }
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
                    _input != null ? _clock.Elapsed - _botStarted : TimeSpan.Zero,
                    _brain.Deaths, _brain.AreaStatus,
                    _sessionStarted ? _session.Report(World, _clock.Elapsed) : null);
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

    /// <summary>Notes whether memory could be read. A short gap (loading screen, teleport) is
    /// waited out; a long one is reported and stops the bot.</summary>
    /// <returns>True when the bot may act this tick.</returns>
    private bool TrackMemory(bool ok, TimeSpan now)
    {
        if (ok)
        {
            _statsFailSince = TimeSpan.FromDays(-1);
            Volatile.Write(ref _statsOk, true);
            if (_statsWarned)
            {
                _statsWarned = false;
                _log("Oyunun belleği yeniden okunuyor ✓");
            }
            return true;
        }
        if (_statsFailSince < TimeSpan.Zero)
            _statsFailSince = now;
        if (now - _statsFailSince < TimeSpan.FromSeconds(2))
            return false;
        Volatile.Write(ref _statsOk, false);
        if (!_statsWarned)
        {
            _statsWarned = true;
            _log("Uyarı: oyunun belleği okunamıyor (" + _memoryStatus + "). Bot bekliyor.");
        }
        if (BotRunning && now - _statsFailSince >= MemoryLostStop)
        {
            const string reason = "oyunun belleği okunamıyor";
            StopBot(reason);
            BotStopped?.Invoke(reason);
            Notify("⏹ Bot durdu: " + reason + (_lastSummary is { } s2 ? "\n" + s2 : ""));
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopBot("bağlantı kapandı");
        StopRecording();
        lock (_marketGate)
            if (_market is { Changed: true } market)
                SaveMarket(market);
        SaveTerrainChanges(World);
        _scanCts?.Cancel();
        _marketCts?.Cancel();
        _fishCts?.Cancel();
        _loginInput?.Dispose();
        _cts.Cancel();
        _capture?.Dispose();
        try { _loop?.Wait(1500); } catch { }
        lock (_frameGate)
        {
            _latest?.Dispose();
            _latest = null;
        }
        lock (_worldGate)
        {
            _world?.Dispose();
            _world = null;
        }
        _cts.Dispose();
    }
}
