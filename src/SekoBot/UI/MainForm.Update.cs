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

    /// <summary>The "update all" message reached this SekoBot.</summary>
    private async Task UpdateAndResumeAsync()
    {
        if (_updatingAll || _updating)
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
                WriteLog("⬆ " + result.Error);
                return;
            }
            if (result.Update is not PreparedUpdate update)
            {
                WriteLog($"⬆ PARDUS güncel (v{BuildInfo.Version}); bot olduğu gibi devam ediyor.");
                return;
            }
            // What this one does, noted with its character, then stopped for the update.
            string character = _engine?.CharacterName ?? "";
            bool running = _engine is { BotRunning: true } || _engine is { ScanningMarket: true } || _engine is { Fishing: true };
            if (character.Length > 0)
            {
                var note = new WorkBeforeUpdate { Mode = _mode, Running = running, At = DateTime.Now };
                if (SharedFile.Update<Dictionary<string, WorkBeforeUpdate>>(AfterUpdateFile, all => all[character] = note) == null)
                {
                    WriteLog("Güncelleme ertelendi; devam kaydı yazılamadı.");
                    try { File.Delete(update.FilePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    return;
                }
                WriteLog($"⬆ Yeni sürüm {update.Version}: {character} {(running ? "çalışıyordu" : "duruyordu")}; güncellemeden sonra kaldığı yerden devam edecek.");
            }
            if (_engine is { ScanningMarket: true } market)
                market.StopMarketScan();
            if (_engine is { Fishing: true } fishing)
                fishing.StopFishing();
            if (_engine is { BotRunning: true })
                StopBot("güncelleme için durduruldu");
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
        if (character.Length == 0 || world == null || BotEngine.IsLoginScreen(world.MapName) || world.MapName.Length == 0
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
