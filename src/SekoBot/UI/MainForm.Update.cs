using System.Text.Json;
using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Update;

namespace SekoBot.UI;

/// <summary>
/// Updating every SekoBot from one menu ("⬆ Tüm SekoBot'ları güncelle"): each one, a few seconds after the one before,
/// looks for the new version; with one, it notes what it was doing (by character: the page, whether it ran), stops,
/// installs and starts again — and once its character is back in the game, it goes on with that work. Kept in
/// guncelleme-devam.json (shared, by character: a SekoBot may come up on another game after the restart).
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>What a character was doing when its SekoBot updated.</summary>
    internal sealed class WorkBeforeUpdate
    {
        public int Mode { get; set; }
        public bool Running { get; set; }
        public DateTime At { get; set; }
        public string? Claim { get; set; }
        public DateTime ClaimUntil { get; set; }
    }

    internal static string AfterUpdateFile => Path.Combine(ProfileStore.Directory, "guncelleme-devam.json");

    /// <summary>A note older than this is forgotten (the update didn't come back in time, or the user went on by hand).</summary>
    internal static readonly TimeSpan AfterUpdateFor = TimeSpan.FromMinutes(20);

    private bool _updatingAll;
    private bool _checkingUpdate;

    private bool TelegramUpdateAllowed(string token, string chat) =>
        _profile.TelegramEnabled && _profile.TelegramCommands && token.Length > 0 && chat.Length > 0
        && _profile.TelegramToken == token && _profile.TelegramChatId.Trim() == chat;

    private async Task UpdateNoticeAsync(string? chat, string message)
    {
        WriteLog(message);
        string token = _profile.TelegramToken;
        if (chat != null && TelegramUpdateAllowed(token, chat)
            && await TelegramNotifier.SendAsync(token, chat, message) is string error)
            WriteLog(error);
    }

    private async Task ReportTelegramRestartAsync()
    {
        string? chat = Environment.GetEnvironmentVariable("SEKOBOT_UPDATED_CHAT");
        string? version = Environment.GetEnvironmentVariable("SEKOBOT_UPDATED_VERSION");
        Environment.SetEnvironmentVariable("SEKOBOT_UPDATED_CHAT", null);
        Environment.SetEnvironmentVariable("SEKOBOT_UPDATED_VERSION", null);
        if (chat != null && version == BuildInfo.Version && TelegramUpdateAllowed(_profile.TelegramToken, chat))
            await UpdateNoticeAsync(chat, $"PARDUS v{BuildInfo.Version} ile yeniden açıldı.");
    }

    /// <summary>Waited at most this long for a quiet spot before an update.</summary>
    internal static readonly TimeSpan QuietBeforeUpdate = TimeSpan.FromMinutes(3);

    /// <summary>The "update all" message reached this SekoBot.</summary>
    private async Task UpdateAndResumeAsync(string? telegramChat = null)
    {
        if (_updatingAll || _updating || _checkingUpdate)
            return;
        _updatingAll = true;
        try
        {
            // One after another (the same files are swapped).
            await Task.Delay(TimeSpan.FromSeconds(6 * Math.Max(0, Instance.Number - 1)));
            WriteLog("⬆ Güncelleme kontrol ediliyor (tüm PARDUS'lar)…");
            var result = await UpdateService.CheckAndDownloadAsync();
            if (IsDisposed)
                return;
            if (!string.IsNullOrEmpty(result.Error))
            {
                await UpdateNoticeAsync(telegramChat, "⬆ " + result.Error);
                return;
            }
            if (result.Update is not PreparedUpdate update)
            {
                await UpdateNoticeAsync(telegramChat, $"⬆ PARDUS güncel (v{BuildInfo.Version}); bot olduğu gibi devam ediyor.");
                return;
            }
            update = update with { TelegramChat = telegramChat };
            // What this one does, noted with its character, then stopped for the update.
            string character = _engine?.CharacterName ?? "";
            if (_engine is { ScanningArea: true })
            {
                try { File.Delete(update.FilePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                await UpdateNoticeAsync(telegramChat, "Alan taraması sürüyor. Bittiğinde güncellemeyi yeniden iste.");
                return;
            }
            int runningMode = _engine is { Fishing: true } ? FishingMode :
                _engine is { ScanningMarket: true } ? MarketMode : FarmMode;
            bool running = _engine is { BotRunning: true } || _engine is { ScanningMarket: true } || _engine is { Fishing: true };
            if (running && character.Length == 0)
            {
                await UpdateNoticeAsync(telegramChat, "Güncelleme ertelendi. Çalışan karakterin adı henüz okunamadı.");
                try { File.Delete(update.FilePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return;
            }
            if (character.Length > 0)
            {
                var note = new WorkBeforeUpdate { Mode = running ? runningMode : _mode, Running = running, At = DateTime.Now };
                if (SharedFile.Update<Dictionary<string, WorkBeforeUpdate>>(AfterUpdateFile, all => all[character] = note) == null)
                {
                    await UpdateNoticeAsync(telegramChat, "Güncelleme ertelendi; devam kaydı yazılamadı.");
                    try { File.Delete(update.FilePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    return;
                }
                WriteLog($"⬆ Yeni sürüm {update.Version}: {character} {(running ? "çalışıyordu" : "duruyordu")}; güncellemeden sonra kaldığı yerden devam edecek.");
            }
            if (_engine is { ScanningMarket: true } market)
                market.StopMarketScan();
            if (_engine is { Fishing: true } fishing)
                fishing.StopFishing();
            // Farming: what attacks us fought and a quiet spot reached first (the game stays on while PARDUS restarts; seen:
            // stopped among the mobs, found dead after the update three times). At most a few minutes.
            if (_engine is { BotRunning: true } farming && runningMode == FarmMode)
            {
                WriteLog("⬆ Güncellemeden önce etraftaki moblar bitirilip sakin bir yere çekiliniyor.");
                var until = DateTime.Now + QuietBeforeUpdate;
                while (DateTime.Now < until && farming.BotRunning && !farming.QuietFor("⬆ Güncelleme"))
                    await Task.Delay(500);
                farming.QuietFor(null);
                if (IsDisposed)
                    return;
            }
            if (_engine is { BotRunning: true })
                StopBot("güncelleme için durduruldu");
            await UpdateNoticeAsync(telegramChat, $"PARDUS v{update.Version} kuruluyor.");
            await Task.Delay(1500);
            _pendingUpdate = update;
            await ApplyUpdateAsync(update);
        }
        finally { _updatingAll = false; }
    }

    private DateTime _afterUpdateInGame = DateTime.MinValue;

    /// <summary>After an update: this character's noted work started again once it is back in the game (a moment settled).</summary>
    private void ResumeAfterUpdateTick(BotEngine e)
    {
        string character = e.CharacterName;
        var world = e.World;
        if (_updatingAll || _updating || _pendingUpdate != null || character.Length == 0 || world == null || BotEngine.IsLoginScreen(world.MapName) || world.MapName.Length == 0
            || e.BotRunning || e.ScanningMarket || e.Fishing || !File.Exists(AfterUpdateFile))
        {
            _afterUpdateInGame = DateTime.MinValue;
            return;
        }
        if (_afterUpdateInGame == DateTime.MinValue)
            _afterUpdateInGame = DateTime.Now;
        if (DateTime.Now - _afterUpdateInGame < TimeSpan.FromSeconds(6))
            return;
        _afterUpdateInGame = DateTime.Now;
        TryResume(AfterUpdateFile, character, DateTime.Now,
            work => StartWork(e, Math.Clamp(work.Mode, FarmMode, FishingMode), $"PARDUS {BuildInfo.Version} sürümüne güncellendi"));
    }

    internal static void TryResume(string path, string character, DateTime now, Func<WorkBeforeUpdate, bool> start)
    {
        string claim = Guid.NewGuid().ToString("N");
        var notes = SharedFile.Update<Dictionary<string, WorkBeforeUpdate>>(path, all =>
        {
            foreach (var key in all.Where(pair => now - pair.Value.At > AfterUpdateFor).Select(pair => pair.Key).ToList())
                all.Remove(key);
            if (!all.TryGetValue(character, out var note)) return;
            if (!note.Running) { all.Remove(character); return; }
            if (note.Claim != claim && note.ClaimUntil > now) return;
            note.Claim = claim;
            note.ClaimUntil = now.AddMinutes(1);
        });
        if (notes == null || !notes.TryGetValue(character, out var work) || work.Claim != claim)
            return;

        bool started = false;
        try { started = start(work); }
        finally
        {
            SharedFile.Update<Dictionary<string, WorkBeforeUpdate>>(path, all =>
            {
                if (!all.TryGetValue(character, out var note) || note.Claim != claim) return;
                if (started) all.Remove(character);
                else { note.Claim = null; note.ClaimUntil = default; }
            });
        }
    }
}
