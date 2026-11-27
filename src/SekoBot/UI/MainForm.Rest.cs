using SekoBot.Bot;

namespace SekoBot.UI;

/// <summary>
/// Out of the game now and then (the user: "1 saatte bir 10 dakika çıkış yap ya da 2 saatte bir 15 dk"): after
/// <see cref="Config.BotProfile.LogoutPlayMin"/> minutes of farming nothing new is gathered, what attacks is fought and
/// a quiet spot reached ("önce etraftaki mobları bitirsin, saldıran kimse yok ise çıkış yapsın"); then the game menu's
/// "Oturumu kapat" takes the game to its login screen, waited on for <see cref="Config.BotProfile.LogoutRestMin"/>
/// minutes; then the login is pressed and the same character farms on where it was. The times are the user's, as set.
/// </summary>
internal sealed partial class MainForm
{
    private enum RestStage { None, Quieting, LoggingOut, Resting }

    private RestStage _rest = RestStage.None;
    private TimeSpan _played = TimeSpan.Zero;
    private DateTime _lastPlayTick = DateTime.MinValue, _restStageAt = DateTime.MinValue, _restUntil = DateTime.MinValue, _logoutPressedAt = DateTime.MinValue, _menuOpenedAt = DateTime.MinValue;
    private bool _quietLongTold;

    /// <summary>Logging out took longer than this: the break is left out, the farm goes on.</summary>
    internal static readonly TimeSpan LogoutTimeout = TimeSpan.FromSeconds(75);

    /// <summary>A break out of the game going on (the login and the resume wait for it).</summary>
    private bool Resting => _rest != RestStage.None;

    private void RestTick()
    {
        if (_engine is not { } e)
            return;
        var now = DateTime.Now;
        var world = e.World;
        bool onLogin = world != null && BotEngine.IsLoginScreen(world.MapName);
        switch (_rest)
        {
            case RestStage.None:
                if (!_profile.LogoutBreaks)
                {
                    _played = TimeSpan.Zero;
                    _lastPlayTick = DateTime.MinValue;
                    return;
                }
                // Farming time only (a stop, a sale trip in between: the clock waits while stopped).
                if (e.BotRunning && _lastPlayTick != DateTime.MinValue && now - _lastPlayTick < TimeSpan.FromSeconds(10))
                    _played += now - _lastPlayTick;
                _lastPlayTick = e.BotRunning ? now : DateTime.MinValue;
                if (!e.BotRunning || _played < TimeSpan.FromMinutes(Math.Max(1, _profile.LogoutPlayMin)))
                    return;
                _rest = RestStage.Quieting;
                _restStageAt = now;
                _quietLongTold = false;
                WriteLog($"☕ {_profile.LogoutPlayMin} dakika çalışıldı: {_profile.LogoutRestMin} dakika oyundan çıkılacak. Önce saldıranlar bitirilip sakin bir yere çekiliniyor.");
                return;

            case RestStage.Quieting:
                if (!e.BotRunning)
                {
                    // Stopped meanwhile (by the user, Telegram…): no break now.
                    e.QuietFor(null);
                    EndRest(null);
                    return;
                }
                if (!e.QuietFor("☕ Oyundan çıkış molası"))
                {
                    if (!_quietLongTold && now - _restStageAt > TimeSpan.FromMinutes(5))
                    {
                        _quietLongTold = true;
                        WriteLog("☕ Mola için sakin an bekleniyor: moblar hâlâ saldırıyor; bot dövüşmeye devam ediyor.");
                    }
                    return;
                }
                Remember(e);
                e.QuietFor(null);
                StopBot("oyundan çıkış molası");
                _rest = RestStage.LoggingOut;
                _restStageAt = now;
                _logoutPressedAt = DateTime.MinValue;
                WriteLog("☕ Sakin bir yerdeyiz; oyundan çıkılıyor (Oturumu kapat).");
                return;

            case RestStage.LoggingOut:
                if (onLogin)
                {
                    _rest = RestStage.Resting;
                    _restUntil = now + TimeSpan.FromMinutes(Math.Max(1, _profile.LogoutRestMin));
                    string text = $"☕ Oyundan çıkıldı; {_profile.LogoutRestMin} dakika giriş ekranında beklenecek, {_restUntil:HH:mm}'de yeniden girilecek.";
                    WriteLog(text);
                    e.Notify(text);
                    return;
                }
                if (now - _restStageAt > LogoutTimeout)
                {
                    WriteLog("☕ Oyundan çıkılamadı ('Oturumu kapat' görünmedi ya da çalışmadı); mola atlandı, farma devam.");
                    if (e.GameMenuButton(BotEngine.LogoutKey) != null)
                        e.OpenGameMenu(); // the menu shut again
                    _resume = null;
                    EndRest(e);
                    return;
                }
                // Pressed: the game may count down before it lets go (not pressed again meanwhile — Esc would stop it).
                if (now - _logoutPressedAt < TimeSpan.FromSeconds(20) || now - _restStageAt < TimeSpan.FromSeconds(1))
                    return;
                if (e.GameMenuButton(BotEngine.LogoutKey) is { } logout)
                {
                    e.PressMenuButton(logout, BotEngine.LogoutKey);
                    _logoutPressedAt = now;
                }
                else if (now - _menuOpenedAt >= TimeSpan.FromSeconds(5))
                {
                    e.OpenGameMenu();
                    _menuOpenedAt = now;
                }
                return;

            case RestStage.Resting:
                if (world != null && !onLogin && world.MapName.Length > 0 && !world.MapName.StartsWith('_') && e.CharSelectNow() == null)
                {
                    // Back in the game before the time (logged in by hand): the break is over.
                    WriteLog("☕ Oyuna elle girildi; mola bitti.");
                    _resume = null;
                    EndRest(null);
                    return;
                }
                if (now < _restUntil)
                    return;
                WriteLog("☕ Mola bitti; giriş yapılıp kaldığı yerden devam edilecek.");
                e.KickedWhileWorking = true; // the login is pressed (as after a maintenance), then the work resumed
                EndRest(null);
                return;
        }
    }

    /// <summary>The break over; with <paramref name="farmOn"/> the farm started again at once (no logout happened).</summary>
    private void EndRest(BotEngine? farmOn)
    {
        _rest = RestStage.None;
        _played = TimeSpan.Zero;
        _lastPlayTick = DateTime.MinValue;
        if (farmOn != null)
            StartWork(farmOn, FarmMode, "Mola yapılamadı");
    }
}
