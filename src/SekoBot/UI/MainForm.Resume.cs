using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SekoBot.Config;
using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Memory;
using SekoBot.Native;

namespace SekoBot.UI;

/// <summary>
/// Going back to work by itself: after a maintenance or a lost connection (the game on its login screen), after
/// the game closed, or when the game wants an update. SekoBot logs in (the login button only, see LoginTick),
/// starts the same character on the character select ("Başla"), and once in the game starts what it was doing
/// (farm — walking to the saved farm spot first —, fishing, the market watch). A game closed or wanting an
/// update is started again from its own folder's launcher (Mobile2Launcher.exe: it updates the game, then
/// starts it); the SekoBots do that one at a time and each one connects to its own folder's game again.
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>What to go back to: the character and the work (farm, market, fishing).</summary>
    private sealed record ResumeWork(string Character, int Mode, DateTime Since);

    /// <summary>The work to go back to — kept on disk too (devam-N.json): a PARDUS that restarts (an update of its own)
    /// still knows it (seen 2026-11-26: restarted while its game was stuck after an update, it forgot to go back).</summary>
    private ResumeWork? _resume
    {
        get => _resumeNote;
        set
        {
            _resumeNote = value;
            try
            {
                if (value == null)
                    File.Delete(ResumeFile);
                else
                    File.WriteAllText(ResumeFile, JsonSerializer.Serialize(new SavedResume(value.Character, value.Mode, value.Since, _gameFolder)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trouble.Tell("Devam notu yazılamadı", ex); }
        }
    }
    private ResumeWork? _resumeNote;

    private sealed record SavedResume(string Character, int Mode, DateTime Since, string? Folder);
    private static string ResumeFile => Path.Combine(ProfileStore.Directory, $"devam-{Instance.Number}.json");
    /// <summary>A note older than this is not gone back to (the user will have done something else).</summary>
    internal static readonly TimeSpan ResumeNoteFor = TimeSpan.FromHours(3);

    /// <summary>At the start: the work this PARDUS was to go back to before it restarted; its game started again when
    /// it doesn't come up by itself.</summary>
    private void LoadResumeNote()
    {
        try
        {
            if (!File.Exists(ResumeFile) || JsonSerializer.Deserialize<SavedResume>(File.ReadAllText(ResumeFile)) is not { } saved)
                return;
            if (DateTime.Now - saved.Since > ResumeNoteFor || saved.Character.Length == 0)
            {
                File.Delete(ResumeFile);
                return;
            }
            _resumeNote = new ResumeWork(saved.Character, saved.Mode, saved.Since);
            _gameFolder ??= saved.Folder;
            _gameGoneAt = DateTime.Now;
            WriteLog($"↩ Yarım kalan iş hatırlandı: {saved.Character} ile {ModeName(saved.Mode)} devam edecek (oyun açık değilse başlatılacak).");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }
    private string _lastCharacter = "";
    /// <summary>This SekoBot's game copy (the folder holding Mobile2Launcher.exe).</summary>
    private string? _gameFolder;
    private DateTime _startPressedAt = DateTime.MinValue, _inGameSince = DateTime.MinValue, _gameGoneAt = DateTime.MinValue;
    private DateTime _launcherAt = DateTime.MinValue, _closeAskedAt = DateTime.MinValue;
    private bool _wrongCharTold, _outdatedTold, _launcherStarted;
    private int _launches, _startPresses;
    private uint _closingPid;
    private Mutex? _gameTurn;
    private bool _haveGameTurn;

    /// <summary>Waiting this long after the game closed before starting it again (it may be coming back by itself).</summary>
    internal static readonly TimeSpan RestartAfter = TimeSpan.FromSeconds(20);
    /// <summary>A launcher that brought no game back in this time is tried again (at most <see cref="MaxLaunches"/> times).</summary>
    internal static readonly TimeSpan LaunchWait = TimeSpan.FromMinutes(15);
    internal const int MaxLaunches = 3;
    /// <summary>The game said "old version" on its login screen but its launcher brought nothing new: tried again after
    /// this (a short maintenance or a small update may take only minutes; the login is tried meanwhile).</summary>
    internal static readonly TimeSpan NoUpdateWait = TimeSpan.FromMinutes(10);
    /// <summary>The "old version" flag only counts on the login screen, seen there this long (the game sends us there
    /// for an update or a maintenance). While playing it is never looked at: it comes up wrongly (seen 2026-11-27:
    /// three times a day, the launcher had nothing new and the game let us play — the user: "o yanıltıcı bilgiye
    /// hiç bakmasın").</summary>
    internal static readonly TimeSpan OutdatedOnLogin = TimeSpan.FromSeconds(20);
    private DateTime _onLoginSince = DateTime.MinValue;
    /// <summary>A game on its opening screen this long (no login, no character select, nothing readable) is started again
    /// (seen 2026-11-26: "İnternet bağlantısı kontrol ediliyor.. %0" for an hour after an update restart).</summary>
    internal static readonly TimeSpan StuckOpening = TimeSpan.FromMinutes(10);
    private string? _updateExe;
    private DateTime _updateExeTime, _noUpdateUntil = DateTime.MinValue, _unreadableSince = DateTime.MinValue;
    private uint _updatePid, _unreadablePid;
    /// <summary>In the game this long (loaded, the screens settled) before the work starts again.</summary>
    internal static readonly TimeSpan SettleInGame = TimeSpan.FromSeconds(8);

    /// <summary>Every 2 s (with the login): notes the character and the work, and goes back to it when needed.</summary>
    private void ResumeTick()
    {
        var now = DateTime.Now;
        if (_engine is not { } e)
        {
            RestartGameTick(now);
            return;
        }
        _gameFolder ??= GameFolderOf(e.Window.ProcessId);
        ResumeAfterUpdateTick(e);
        var world = e.World;
        // Back after closing the game for an update: its file the same (the launcher had nothing to bring) — played on.
        if (_updateExe != null && e.Window.ProcessId != _updatePid)
        {
            bool same = false;
            try { same = File.Exists(_updateExe) && File.GetLastWriteTimeUtc(_updateExe) == _updateExeTime; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            _updateExe = null;
            if (same)
            {
                _noUpdateUntil = now + NoUpdateWait;
                string text = $"ℹ Başlatıcıda yeni sürüm yoktu (oyun dosyası değişmedi). Oyun izin verdiği sürece oynanmaya devam edilecek; " +
                              $"{NoUpdateWait.TotalMinutes:0} dakika sonra güncelleme yeniden denenecek.";
                WriteLog(text);
                e.Notify(text);
            }
        }
        StuckOpeningTick(e, world, now);
        // The game's own update question at its start: "Onayla" (it downloads, then goes on to the login).
        if (_profile.AutoLogin && world == null && now - _updateYesAt > TimeSpan.FromSeconds(20) && e.UpdateQuestion() is { } yes)
        {
            _updateYesAt = now;
            e.PressAt(yes);
            WriteLog("🔄 Oyun başlamadan önce ek kaynak indirmek istiyor; 'Onayla'ya basıldı.");
        }
        bool inGame = world != null && world.MapName.Length > 0 && !BotEngine.IsLoginScreen(world.MapName) && !world.MapName.StartsWith('_');
        if (inGame && e.CharacterName.Length > 0)
            _lastCharacter = e.CharacterName;
        bool working = e.BotRunning || e.Fishing || e.ScanningMarket;

        // An update is out (the game sent us to its login screen and says its version is old): the work noted, the game
        // closed (started again from its launcher, which updates it).
        bool onLogin = world != null && BotEngine.IsLoginScreen(world.MapName);
        if (!onLogin)
            _onLoginSince = DateTime.MinValue;
        else if (_onLoginSince == DateTime.MinValue)
            _onLoginSince = now;
        bool closing = _closeAskedAt != DateTime.MinValue;
        if (_profile.AutoLogin && !Resting && now >= _noUpdateUntil && (closing || onLogin && now - _onLoginSince >= OutdatedOnLogin) && e.GameOutdated() == true)
        {
            if (!_outdatedTold)
            {
                _outdatedTold = true;
                if (working || e.KickedWhileWorking)
                    Remember(e);
                string text = "🔄 Oyun güncelleme istiyor: oyun kapatılıp başlatıcısıyla güncellenecek, sonra kaldığı yerden devam edilecek.";
                WriteLog(text);
                e.Notify(text);
            }
            if (working)
                StopBot("oyun güncelleme istiyor");
            if (_gameFolder == null)
            {
                if (_closeAskedAt == DateTime.MinValue)
                    WriteLog("Oyunun klasörü bulunamadı; güncelleme elle yapılmalı.");
                _closeAskedAt = now;
                return;
            }
            // One SekoBot at a time closes and updates its game.
            if (!TakeGameTurn())
                return;
            if (_closeAskedAt == DateTime.MinValue)
            {
                _closeAskedAt = now;
                // The game's file noted: if the launcher brings nothing new, the "old version" flag is played through.
                _updatePid = e.Window.ProcessId;
                _updateExe = ProcessPath(e.Window.ProcessId);
                try { _updateExeTime = _updateExe != null ? File.GetLastWriteTimeUtc(_updateExe) : default; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _updateExe = null; }
                CloseGame(e.Window.ProcessId, now);
            }
            else if (now - _closeAskedAt > TimeSpan.FromSeconds(10))
                KillGame(e.Window.ProcessId);
            return;
        }

        // A break out of the game: the login and the resume wait for it (see RestTick).
        if (Resting)
            return;
        if (working)
        {
            // The user (or the resume) started work: nothing to go back to.
            _resume = null;
            _inGameSince = DateTime.MinValue;
            return;
        }
        if (e.KickedWhileWorking && _resume == null)
            Remember(e);
        if (_resume is not { } resume || !_profile.AutoLogin)
            return;

        // The character select: "Başla" with the same character.
        if (e.CharSelectNow() is { } select)
        {
            _inGameSince = DateTime.MinValue;
            if (select.Selected.Length == 0 || select.Start is not { } start)
                return;
            if (!select.Selected.Equals(resume.Character, StringComparison.Ordinal))
            {
                if (!_wrongCharTold)
                {
                    _wrongCharTold = true;
                    string text = $"👤 Karakter seçiminde {select.Selected} seçili; devam edilecek karakter {resume.Character}. " +
                                  "Yanlış karakterle girilmiyor: karakteri elle seç ve Başla'ya bas, bot kaldığı yerden devam eder.";
                    WriteLog(text);
                    e.Notify(text);
                }
                return;
            }
            Win32.GetClientRect(e.Window.Handle, out var client);
            if (start.Bottom > client.Bottom - client.Top || start.Right > client.Right - client.Left)
            {
                if (now - _startPressedAt > TimeSpan.FromMinutes(1))
                {
                    _startPressedAt = now;
                    WriteLog("Karakter seçiminde 'Başla' pencerenin dışında kalıyor; oyunu büyüt (Büyüt düğmesi), bot basacak.");
                }
                return;
            }
            if (now - _startPressedAt < TimeSpan.FromSeconds(15))
                return;
            _startPressedAt = now;
            _startPresses++;
            e.PressStart(start);
            WriteLog($"👤 Karakter seçiminde {resume.Character} ile 'Başla'ya basıldı.");
            return;
        }

        // In the game with the character: the work starts again once the game has settled.
        if (!inGame)
        {
            _inGameSince = DateTime.MinValue;
            return;
        }
        if (e.CharacterName.Length > 0 && !e.CharacterName.Equals(resume.Character, StringComparison.Ordinal))
        {
            // Another character came in (the user chose it): not the work of this one.
            WriteLog($"Oyuna {e.CharacterName} ile girildi ({resume.Character} değil); bot kendiliğinden başlamıyor.");
            _resume = null;
            return;
        }
        if (_inGameSince == DateTime.MinValue)
            _inGameSince = now;
        if (now - _inGameSince < SettleInGame)
            return;
        _resume = null;
        _inGameSince = DateTime.MinValue;
        _wrongCharTold = _outdatedTold = false;
        // A game started again opens big: put in its place with the others ("Diz") before the work goes on.
        if (_gameRestarted)
        {
            _gameRestarted = false;
            ArrangeAll();
        }
        StartWork(e, resume.Mode);
    }

    /// <summary>The work this SekoBot was doing, to go back to (the character last seen in the game).</summary>
    private void Remember(BotEngine e)
    {
        string character = e.CharacterName.Length > 0 && e.World is { } w && !BotEngine.IsLoginScreen(w.MapName) ? e.CharacterName : _lastCharacter;
        if (character.Length == 0 || _resume != null)
            return;
        int mode = e.Fishing || e.FishingPaused ? FishingMode : e.ScanningMarket ? MarketMode : _mode;
        _resume = new ResumeWork(character, mode, DateTime.Now);
        _wrongCharTold = false;
        WriteLog($"↩ {character} ile kaldığı yerden devam edilecek ({ModeName(mode)}), oyuna yeniden girilince.");
    }

    private static string ModeName(int mode) => mode switch { FishingMode => "balık", MarketMode => "pazar", _ => "farm" };

    private static bool TryStartMode(BotEngine e, int mode, bool dryRun, out string error) => mode switch
    {
        FishingMode => e.StartFishing(out error),
        MarketMode => e.StartMarketScan(out error, true),
        _ => e.StartBot(dryRun, background: true, out error)
    };

    private bool StartWork(BotEngine e, int mode, string why = "Oyuna yeniden girildi")
    {
        bool ok = TryStartMode(e, mode, false, out string error);
        string text = ok ? $"🟢 {why}; kaldığı yerden devam ediliyor ({ModeName(mode)})."
                         : $"{why} ama iş başlatılamadı: {error}";
        WriteLog(text);
        e.Notify(text);
        LetGoGameTurn();
        _launches = 0;
        UpdateControls();
        UpdateStats();
        return ok;
    }

    /// <summary>The game went away (closed, crashed, closed for an update): the work noted to go back to.</summary>
    private void GameGone(BotEngine e)
    {
        if (e.BotRunning || e.Fishing || e.ScanningMarket || e.KickedWhileWorking || _closingPid == e.Window.ProcessId)
            Remember(e);
        _gameFolder ??= GameFolderOf(e.Window.ProcessId);
        _gameGoneAt = DateTime.Now;
        _closingPid = 0;
    }

    /// <summary>No game connected: this SekoBot's game is started again from its launcher when work is waiting.</summary>
    private void RestartGameTick(DateTime now)
    {
        if (_resume == null || _gameFolder == null || !_profile.AutoLogin || _gameGoneAt == DateTime.MinValue)
            return;
        // Our folder's launcher up (started by us, the user or the game itself): its "MOBILE2'Yİ BAŞLAT" pressed once
        // it is ready (seen 2026-11-21: the launcher updated the game, then waited on that button).
        if (OurLauncher() is { } launcherWindow)
        {
            if (!_launcherStarted)
            {
                _launcherStarted = true;
                _launcherAt = now;
            }
            PressLauncherStart(launcherWindow, now);
            if (now - _launcherAt < LaunchWait)
                return;
        }
        if (_launcherStarted)
        {
            if (now - _launcherAt < LaunchWait)
                return;
            _launcherStarted = false;
            WriteLog($"Başlatıcı {LaunchWait.TotalMinutes:0} dakikada oyunu açmadı.");
            LetGoGameTurn();
            if (_launches >= MaxLaunches)
            {
                WriteLog("Oyun kendiliğinden başlatılamadı; elle başlatılınca bot kaldığı yerden devam eder.");
                _gameGoneAt = DateTime.MinValue;
                return;
            }
        }
        if (now - _gameGoneAt < RestartAfter || _games.Any(g => SameFolder(g.ProcessId)))
            return;
        string launcher = Path.Combine(_gameFolder, "Mobile2Launcher.exe");
        if (!File.Exists(launcher))
        {
            WriteLog("Oyunun başlatıcısı bulunamadı (" + launcher + "); oyun elle başlatılmalı.");
            _gameGoneAt = DateTime.MinValue;
            return;
        }
        if (!TakeGameTurn())
            return;
        try
        {
            Process.Start(new ProcessStartInfo(launcher) { WorkingDirectory = _gameFolder, UseShellExecute = true });
            _launcherStarted = true;
            _launcherAt = now;
            _launches++;
            _gameRestarted = true;
            WriteLog($"🔄 Oyun başlatıcısı açıldı ({Path.GetFileName(_gameFolder)}): güncelleme varsa yüklenip oyun açılacak.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            WriteLog("Oyun başlatıcısı açılamadı: " + ex.Message);
            LetGoGameTurn();
            _gameGoneAt = DateTime.MinValue;
        }
    }

    private DateTime _launcherPressedAt = DateTime.MinValue, _launcherSizedAt = DateTime.MinValue, _updateYesAt = DateTime.MinValue;
    /// <summary>This SekoBot's game was started again (opens big): arranged with the others once back in the game.</summary>
    private bool _gameRestarted;

    /// <summary>The window of the launcher running from this SekoBot's game folder; null when none.</summary>
    private nint? OurLauncher()
    {
        if (_gameFolder == null)
            return null;
        foreach (var process in Process.GetProcessesByName("Mobile2Launcher"))
            using (process)
            {
                if (process.MainWindowHandle != 0 && GameFolderOf((uint)process.Id) is { } folder
                    && folder.Equals(_gameFolder, StringComparison.OrdinalIgnoreCase))
                    return process.MainWindowHandle;
            }
        return null;
    }

    /// <summary>Where "MOBILE2'Yİ BAŞLAT" is on the launcher (share of its window: measured on its 640 × 480 window).</summary>
    internal static readonly PointF LauncherStart = new(0.78f, 0.62f);

    /// <summary>
    /// "MOBILE2'Yİ BAŞLAT" pressed when it shows ready (its blue), at most once a minute — the game coming up from
    /// it is waited for (a second press would start a second game).
    /// </summary>
    private void PressLauncherStart(nint window, DateTime now)
    {
        if (now - _launcherAt < TimeSpan.FromSeconds(4) || now - _launcherPressedAt < TimeSpan.FromSeconds(60) || GameFromOurFolderRunning())
            return;
        if (!Win32.GetClientRect(window, out var client) || client.Right < 200 || client.Bottom < 150)
            return;
        // The launcher opens bigger than it was measured at (the user: "diz ile bu hale getirmek lazım"): made
        // 640 × 480 where it stands first, then looked at a moment later.
        if (client.Right != 640 || client.Bottom != 480)
        {
            if (_launcherSizedAt != DateTime.MinValue && now - _launcherSizedAt < TimeSpan.FromSeconds(3))
                return;
            var seen = WindowLayout.Visible(window);
            Win32.SetWindowPos(window, 0, Math.Max(0, seen.Left), Math.Max(0, seen.Top), 640 + (seen.Width - client.Right), 480 + (seen.Height - client.Bottom), 0x0014);
            _launcherSizedAt = now;
            return;
        }
        var center = new Point((int)(client.Right * LauncherStart.X), (int)(client.Bottom * LauncherStart.Y));
        var target = BagCleaner.DropClickPoint(center, new Rectangle(center.X - 4, center.Y - 4, 9, 9));
        var point = new Win32.POINT { X = target.X, Y = target.Y };
        Win32.ClientToScreen(window, ref point);
        // Brought forward, the button looked at, pressed.
        var alt = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
        alt.u.ki.wVk = 0x12;
        var altUp = alt;
        altUp.u.ki.dwFlags = Win32.KEYEVENTF_KEYUP;
        Win32.SendInput(2, [alt, altUp], System.Runtime.InteropServices.Marshal.SizeOf<Win32.INPUT>());
        Win32.SetForegroundWindow(window);
        System.Threading.Thread.Sleep(400);
        Color colour;
        using (var bmp = new Bitmap(1, 1))
        {
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(point.X, point.Y, 0, 0, new Size(1, 1));
            colour = bmp.GetPixel(0, 0);
        }
        // Ready: the button's blue (updating it is grey / dim).
        if (!(colour.B > 150 && colour.B > colour.R + 60))
            return;
        using (SekoBot.Input.MouseGuard.Hold())
        {
            Win32.GetCursorPos(out var previous);
            Win32.SetCursorPos(point.X, point.Y);
            System.Threading.Thread.Sleep(SekoBot.Input.PressTime.Vary(150));
            var down = new Win32.INPUT { type = Win32.INPUT_MOUSE };
            down.u.mi.dwFlags = Win32.MOUSEEVENTF_LEFTDOWN;
            var up = down;
            up.u.mi.dwFlags = Win32.MOUSEEVENTF_LEFTUP;
            Win32.SendInput(1, [down], System.Runtime.InteropServices.Marshal.SizeOf<Win32.INPUT>());
            System.Threading.Thread.Sleep(SekoBot.Input.PressTime.Button(fast: true));
            Win32.SendInput(1, [up], System.Runtime.InteropServices.Marshal.SizeOf<Win32.INPUT>());
            System.Threading.Thread.Sleep(SekoBot.Input.PressTime.Vary(150));
            Win32.SetCursorPos(previous.X, previous.Y);
        }
        _launcherPressedAt = now;
        _gameRestarted = true;
        WriteLog($"🔄 Başlatıcıda 'MOBILE2'Yİ BAŞLAT'a basıldı ({Path.GetFileName(_gameFolder)}); oyun açılıyor.");
    }

    /// <summary>A game process (the game or its loader) runs from this SekoBot's folder.</summary>
    private bool GameFromOurFolderRunning()
    {
        foreach (string name in new[] { "mobilemmo-Win64-Shipping", "Mobile2.bin", "Mobile2" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                    if (SameFolder((uint)process.Id))
                        return true;
        return false;
    }

    /// <summary>Connected to a game again: logging in is done for it (the login button), the turn kept until in the game.</summary>
    private void Reconnected(BotEngine e)
    {
        _launcherStarted = false;
        _gameGoneAt = DateTime.MinValue;
        _closeAskedAt = DateTime.MinValue;
        _outdatedTold = false;
        if (_resume != null)
            e.KickedWhileWorking = true;
        else
            LetGoGameTurn();
    }

    /// <summary>Whether a game process runs from this SekoBot's game folder.</summary>
    private bool SameFolder(uint processId) =>
        _gameFolder != null && GameFolderOf(processId) is { } folder && folder.Equals(_gameFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>The window to connect to, when this SekoBot waits for its own game: only the one from its folder.</summary>
    private bool MayTake(WindowInfo window) => _resume == null || _gameFolder == null || SameFolder(window.ProcessId);

    /// <summary>
    /// The game stuck on its opening screen (nothing readable, no login screen, no character select) while work waits
    /// to be gone back to: closed after <see cref="StuckOpening"/>; its launcher starts it again (at most MaxLaunches).
    /// </summary>
    private void StuckOpeningTick(BotEngine e, WorldSnapshot? world, DateTime now)
    {
        bool showing = world != null || e.LoginNow() is { Showing: true } || e.CharSelectNow() != null;
        if (showing || !_profile.AutoLogin || _resume == null || _gameFolder == null)
        {
            _unreadableSince = DateTime.MinValue;
            return;
        }
        if (_unreadablePid != e.Window.ProcessId || _unreadableSince == DateTime.MinValue)
        {
            _unreadablePid = e.Window.ProcessId;
            _unreadableSince = now;
            return;
        }
        if (now - _unreadableSince < StuckOpening || _launches >= MaxLaunches || !TakeGameTurn())
            return;
        _unreadableSince = DateTime.MinValue;
        string text = $"⚠ Oyun {StuckOpening.TotalMinutes:0} dakikadır açılış ekranında takılı; kapatılıp yeniden başlatılıyor.";
        WriteLog(text);
        e.Notify(text);
        _closingPid = e.Window.ProcessId;
        KillGame(e.Window.ProcessId, "açılış ekranında takıldı");
        _gameGoneAt = now;
    }

    private void CloseGame(uint processId, DateTime now)
    {
        _closingPid = processId;
        try
        {
            using var game = Process.GetProcessById((int)processId);
            game.CloseMainWindow();
            WriteLog("Oyun güncelleme için kapatılıyor.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _gameGoneAt = now;
    }

    private void KillGame(uint processId, string why = "güncelleme için")
    {
        try
        {
            using var game = Process.GetProcessById((int)processId);
            if (!game.HasExited)
            {
                game.Kill();
                WriteLog($"Oyun sonlandırıldı ({why}).");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    /// <summary>One SekoBot at a time closes / starts its game (a mutex shared by them all).</summary>
    private bool TakeGameTurn()
    {
        if (_haveGameTurn)
            return true;
        try
        {
            _gameTurn ??= new Mutex(false, @"Local\SekoBot.GameRestart");
            _haveGameTurn = _gameTurn.WaitOne(0);
        }
        catch (AbandonedMutexException) { _haveGameTurn = true; }
        return _haveGameTurn;
    }

    private void LetGoGameTurn()
    {
        if (!_haveGameTurn)
            return;
        _haveGameTurn = false;
        try { _gameTurn?.ReleaseMutex(); } catch (ApplicationException) { }
    }

    /// <summary>
    /// The game copy's folder of a game process: the one holding Mobile2Launcher.exe above the game's exe
    /// (C:\Oyunlar\Mobile2Global-N\Game\Mobile2Global\Binaries\Win64\…exe → C:\Oyunlar\Mobile2Global-N).
    /// </summary>
    internal static string? GameFolderOf(uint processId)
    {
        string? exe = ProcessPath(processId);
        return exe == null ? null : LauncherFolder(exe);
    }

    internal static string? LauncherFolder(string exe)
    {
        var dir = Path.GetDirectoryName(exe);
        for (int i = 0; i < 7 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            if (File.Exists(Path.Combine(dir, "Mobile2Launcher.exe")))
                return dir;
        return null;
    }

    private static string? ProcessPath(uint processId)
    {
        nint handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
        if (handle == 0)
            return null;
        try
        {
            var text = new StringBuilder(1024);
            int size = text.Capacity;
            return QueryFullProcessImageName(handle, 0, text, ref size) ? text.ToString(0, size) : null;
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
