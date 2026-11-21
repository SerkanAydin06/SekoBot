using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

    private ResumeWork? _resume;
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
        var world = e.World;
        bool inGame = world != null && world.MapName.Length > 0 && !BotEngine.IsLoginScreen(world.MapName) && !world.MapName.StartsWith('_');
        if (inGame && e.CharacterName.Length > 0)
            _lastCharacter = e.CharacterName;
        bool working = e.BotRunning || e.Fishing || e.ScanningMarket;

        // An update is out: the work noted, the game closed (started again from its launcher, which updates it).
        if (_profile.AutoLogin && e.GameOutdated() == true)
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
                CloseGame(e.Window.ProcessId, now);
            }
            else if (now - _closeAskedAt > TimeSpan.FromSeconds(10))
                KillGame(e.Window.ProcessId);
            return;
        }

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

    private void StartWork(BotEngine e, int mode)
    {
        string error = "";
        bool ok = mode switch
        {
            FishingMode => e.StartFishing(out error),
            MarketMode => e.StartMarketScan(out error, true),
            _ => e.StartBot(false, background: true, out error),
        };
        string text = ok ? $"🟢 Oyuna yeniden girildi; kaldığı yerden devam ediliyor ({ModeName(mode)})."
                         : $"Oyuna yeniden girildi ama iş başlatılamadı: {error}";
        WriteLog(text);
        e.Notify(text);
        LetGoGameTurn();
        _launches = 0;
        UpdateControls();
        UpdateStats();
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
            WriteLog($"🔄 Oyun başlatıcısı açıldı ({Path.GetFileName(_gameFolder)}): güncelleme varsa yüklenip oyun açılacak.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            WriteLog("Oyun başlatıcısı açılamadı: " + ex.Message);
            LetGoGameTurn();
            _gameGoneAt = DateTime.MinValue;
        }
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

    private void KillGame(uint processId)
    {
        try
        {
            using var game = Process.GetProcessById((int)processId);
            if (!game.HasExited)
            {
                game.Kill();
                WriteLog("Oyun kapanmadı; sonlandırıldı (güncelleme için).");
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
