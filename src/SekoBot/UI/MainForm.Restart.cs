using System.Diagnostics;
using SekoBot.Bot;

namespace SekoBot.UI;

/// <summary>
/// "restart" from Telegram (the user): the game closed and started again from its launcher, logged in with the game's own
/// saved details (the password is never touched) and the same character chosen. A farm going on waits for its sale trip
/// and a quiet spot first (as before an update), then goes on after the login; a bot standing only logs in again.
/// The closing, the launcher and the login are the ones used for the game's updates (<see cref="ResumeTick"/>).
/// </summary>
internal sealed partial class MainForm
{
    private enum RestartStage { None, Selling, Quieting, Closing }
    private RestartStage _restart = RestartStage.None;
    private DateTime _restartStageAt;
    private uint _restartPid;
    private bool _restartWork;

    /// <summary>The game is closed this long after asking it to close (it may wait on a question): then ended.</summary>
    internal static readonly TimeSpan RestartCloseWait = TimeSpan.FromSeconds(10);

    /// <summary>The Telegram command: the reply (why it can't be done, or what will happen).</summary>
    private string RestartGameCommand()
    {
        if (_engine is not { } e)
            return "Oyun bağlı değil; yeniden başlatılacak oyun yok.";
        if (!_profile.AutoLogin)
            return "Otomatik giriş kapalı; oyun yeniden başlatılmıyor (ayarlardan açılabilir).";
        _gameFolder ??= GameFolderOf(e.Window.ProcessId);
        if (_gameFolder == null || !File.Exists(Path.Combine(_gameFolder, "Mobile2Launcher.exe")))
            return "Oyunun klasörü (başlatıcısı) bulunamadı; oyun yeniden başlatılmıyor.";
        // (A note to go back to doesn't stop it: seen — restarted, left on the character select, "restart" refused.)
        if (_restart != RestartStage.None || _gameGoneAt != DateTime.MinValue || _closeAskedAt != DateTime.MinValue)
            return "Oyun zaten yeniden başlatılıyor.";
        if (_updating || _updatingAll)
            return "PARDUS güncelleniyor; bitince yeniden dene.";
        if (Resting)
            return "Oyundan çıkış molası sürüyor; mola bitince yeniden dene.";
        var world = e.World;
        bool inGame = world != null && world.MapName.Length > 0 && !BotEngine.IsLoginScreen(world.MapName) && !world.MapName.StartsWith('_');
        string character = inGame && e.CharacterName.Length > 0 ? e.CharacterName
            : e.CharSelectNow() is { Selected.Length: > 0 } select ? select.Selected
            : _resume is { } pending ? pending.Character
            : _lastCharacter.Length > 0 ? _lastCharacter : _profile.LastCharacter;
        if (character.Length == 0)
            return "Hangi karakterle girileceği bilinmiyor; oyun yeniden başlatılmıyor.";
        // Work waiting to go back to (left on the character select after a restart) is still gone back to.
        _restartWork = e.BotRunning || e.Fishing || e.ScanningMarket || _resume is { Work: true };
        _restartPid = e.Window.ProcessId;
        _restart = e.BotRunning && e.SellingNow ? RestartStage.Selling : e.BotRunning ? RestartStage.Quieting : RestartStage.Closing;
        _restartStageAt = DateTime.Now;
        if (_restart == RestartStage.Closing)
            CloseForRestart(e, character);
        string text = "🔄 Oyun yeniden başlatılacak: " + (_restartWork
            ? (e.BotRunning ? "önce satış bitip sakin bir yere çekilinecek, sonra " : "") + $"oyun kapatılıp açılacak, {character} ile girilip kaldığı yerden devam edilecek."
            : $"oyun kapatılıp açılacak, {character} ile girilecek (bot çalışmıyordu; başlatılmayacak).");
        WriteLog(text);
        return text;
    }

    /// <summary>Every 2 s: the restart asked for, step by step (the launcher and the login then go as after an update).</summary>
    private void RestartTick()
    {
        if (_restart == RestartStage.None)
            return;
        var now = DateTime.Now;
        if (_engine is not { } e || e.Window.ProcessId != _restartPid)
        {
            // The game gone (closed): the launcher, the login and the work follow in ResumeTick.
            if (_restart == RestartStage.Closing && _gameGoneAt == DateTime.MinValue)
                _gameGoneAt = now;
            _restart = RestartStage.None;
            return;
        }
        switch (_restart)
        {
            case RestartStage.Selling:
                if (e.BotRunning && e.SellingNow && now - _restartStageAt < SellBeforeUpdate)
                    return;
                _restart = RestartStage.Quieting;
                _restartStageAt = now;
                return;
            case RestartStage.Quieting:
                if (e.BotRunning && !e.QuietFor("🔄 Oyunu yeniden başlatma") && now - _restartStageAt < QuietBeforeUpdate)
                    return;
                e.QuietFor(null);
                string character = e.CharacterName.Length > 0 ? e.CharacterName : _lastCharacter;
                if (e.BotRunning)
                    StopBot("oyun yeniden başlatılıyor");
                _restart = RestartStage.Closing;
                _restartStageAt = now;
                CloseForRestart(e, character);
                return;
            case RestartStage.Closing:
                if (now - _restartStageAt > RestartCloseWait)
                {
                    KillGame(_restartPid, "yeniden başlatma için");
                    _restartStageAt = now; // tried again only after another wait
                }
                return;
        }
    }

    /// <summary>
    /// "başla" with the game on its login screen or character select: logged in with the game's own saved details, "Başla"
    /// pressed with the character shown (else the last one played), then the farm started (the user: "uzak bir PC'den
    /// yönetiyorum"). The reply; null when the game is already in play (started the usual way).
    /// </summary>
    private string? EnterGameAndWork(BotEngine e, string why)
    {
        var world = e.World;
        var select = e.CharSelectNow();
        bool onLogin = world != null && BotEngine.IsLoginScreen(world.MapName);
        if (select == null && !onLogin)
            return null;
        if (!_profile.AutoLogin)
            return "Başlatılamadı: oyun giriş ekranında ve otomatik giriş kapalı (ayarlardan açılabilir).";
        if (Resting)
            return "Oyundan çıkış molası sürüyor; mola bitince kendisi girip devam edecek.";
        string character = select is { Selected.Length: > 0 } s ? s.Selected
            : _resume is { } pending ? pending.Character
            : _lastCharacter.Length > 0 ? _lastCharacter : _profile.LastCharacter;
        if (character.Length == 0)
            return "Başlatılamadı: hangi karakterle girileceği bilinmiyor; karakteri seçip 'Başla'ya bas.";
        _resume = new ResumeWork(character, FarmMode, DateTime.Now, true, why);
        _wrongCharTold = _selectStuckTold = false;
        _pickTries = 0;
        _selectSince = DateTime.MinValue;
        e.KickedWhileWorking = true; // the login button pressed on the login screen
        string text = select != null
            ? $"▶ Karakter seçiminde {character} ile 'Başla'ya basılacak, oyuna girince farm başlayacak."
            : $"▶ Oyuna giriş yapılacak, {character} ile girilip farm başlayacak.";
        WriteLog(text);
        return text;
    }

    /// <summary>The work noted (by character) and the game asked to close.</summary>
    private void CloseForRestart(BotEngine e, string character)
    {
        _resume = new ResumeWork(character, _mode, DateTime.Now, _restartWork, "Oyun yeniden başlatıldı");
        _wrongCharTold = _selectStuckTold = false;
        _pickTries = 0;
        _closingPid = e.Window.ProcessId;
        try
        {
            using var game = Process.GetProcessById((int)e.Window.ProcessId);
            game.CloseMainWindow();
            WriteLog("🔄 Oyun yeniden başlatma için kapatılıyor.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
