using System.Text.Json;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// "Mob tara" (first step towards mobs from memory): while standing among mobs, finds the memory
/// positions that sit under the red mob names on screen, and writes everything to a file. That
/// file shows how the game keeps its mobs, so reading them every tick can be built on it.
/// </summary>
internal sealed partial class BotEngine
{
    private const int MobScanFrames = 6;
    private Task? _mobScanTask;

    public bool ScanningMobs => _mobScanTask is { IsCompleted: false };

    /// <summary>Raised from a worker thread when the mob scan ended (with the message for the log).</summary>
    public event Action<string>? MobScanFinished;

    public bool StartMobScan(out string error)
    {
        error = "";
        if (BotRunning)
        {
            error = "Önce botu durdur.";
            return false;
        }
        if (FindingCoordinates || ScanningMobs)
        {
            error = "Başka bir arama sürüyor; bitmesini bekle.";
            return false;
        }
        bool havePosition;
        lock (_positionGate)
            havePosition = _position != null;
        if (!havePosition)
        {
            error = "Önce Alan sekmesinden 'Koordinat bul' çalışmalı (Alan sekmesinde 'Koordinat: bellekten ✓' yazmalı).";
            return false;
        }
        var token = _cts.Token;
        _mobScanTask = Task.Run(async () =>
        {
            string message;
            try { message = await ScanMobsAsync(token); }
            catch (OperationCanceledException) { message = "Mob tarama durduruldu."; }
            catch (Exception ex) { message = "Mob tarama hatası: " + ex.Message; }
            _log(message);
            MobScanFinished?.Invoke(message);
        });
        return true;
    }

    private async Task<string> ScanMobsAsync(CancellationToken token)
    {
        using var memory = ProcessMemory.Open(_window.ProcessId, out string openError);
        if (memory == null)
            return "Mob tarama: " + openError + ".";
        _log("Mob taranıyor: etrafta kırmızı isimli moblar görünürken dur, oyuna dokunma (yaklaşık 30 sn).");
        var regions = memory.Regions();
        IReadOnlyList<MemoryRegion>? hot = null;
        var frames = new List<MobScanFrame>();
        int size = 4;
        long ownAddress = 0;
        for (int i = 0; i < MobScanFrames; i++)
        {
            token.ThrowIfCancellationRequested();
            if (State is not { } state)
            {
                await Task.Delay(500, token);
                continue;
            }
            double first, second, world;
            lock (_positionGate)
            {
                if (_position == null || !_position.TryReadRaw(out ownAddress, out first, out second))
                    return "Mob tarama: kendi koordinatın okunamadı; 'Koordinat bul'u yeniden çalıştır.";
                size = _position.Size;
                world = _position.WorldPerMap;
            }
            if (!CoordinateSearch.Plausible(first) || !CoordinateSearch.Plausible(second))
                return "Mob tarama: okunan koordinat geçersiz görünüyor; Alan sekmesinden 'Koordinat bul'u yeniden çalıştır.";
            var names = state.MobCandidates
                .Select(m => new PointF(m.X - state.Width / 2f, m.Y - state.Height / 2f)).ToList();
            double radius = 0.05 * world; // 5 % of the big map around us: more than the screen shows
            var scan = hot ?? regions;
            int valueSize = size;
            var near = await Task.Run(() => MobScan.FindNear(memory, scan, valueSize, first, second, radius), token);
            // Later frames only look where positions were found: much faster.
            hot ??= regions.Where(r => near.Any(p => p.Address >= r.Base && p.Address < r.Base + r.Size)).ToList();
            frames.Add(new MobScanFrame(names, near));
            _log($"Mob tarama {i + 1}/{MobScanFrames}: ekranda {names.Count} isim, bellekte {near.Count:N0} yakın konum.");
            await Task.Delay(1500, token);
        }
        if (frames.Sum(f => f.Names.Count) == 0)
            return "Mob tarama: ekranda hiç kırmızı mob ismi görülmedi. Mobların yanında dur ve tekrar dene.";

        var match = await Task.Run(() => MobScan.Match(frames), token);
        string file = SaveMobScan(memory, frames, match, size, ownAddress);
        if (match == null)
            return $"Mob tarama: ekrandaki isimlerle eşleşen bir konum bulunamadı. Sonuç dosyası: {file} (bunu gönder).";
        double share = 100.0 * match.Matched / Math.Max(1, match.Names);
        return $"Mob tarama: {match.Names} isimden {match.Matched} tanesi (%{share:0}) bellekteki bir konumla eşleşti. " +
               $"Sonuç dosyası: {file} (bunu gönder).";
    }

    /// <summary>Everything the scan saw, plus the bytes around a few matched and unmatched positions
    /// (to tell mobs from NPCs and players later).</summary>
    private static string SaveMobScan(ProcessMemory memory, List<MobScanFrame> frames, MobProjection? match,
        int size, long ownAddress)
    {
        const int before = 0x400, after = 0x100;
        string Dump(long address)
        {
            var bytes = new byte[before + after];
            return memory.Read(address - before, bytes, bytes.Length) ? Convert.ToHexString(bytes) : "";
        }
        var matched = match?.MobAddresses.Distinct().Take(30).ToList() ?? [];
        var others = frames.LastOrDefault()?.Positions.Select(p => p.Address)
            .Where(a => !matched.Contains(a)).Distinct().Take(30).ToList() ?? [];
        var report = new
        {
            Version = BuildInfo.Version,
            Size = size,
            Own = new { Address = ownAddress.ToString("X"), Bytes = Dump(ownAddress) },
            Projection = match == null ? null : new
            {
                Re = match.Scale.Real, Im = match.Scale.Imaginary, match.Mirrored, match.Matched, match.Names
            },
            Frames = frames.Select(f => new
            {
                Names = f.Names.Select(n => new[] { Math.Round(n.X), Math.Round(n.Y) }),
                Positions = MobScan.Distinct(f.Positions).Take(400)
                    .Select(p => new { Address = p.Address.ToString("X"), Dx = Math.Round(p.Dx, 1), Dy = Math.Round(p.Dy, 1) }),
            }),
            Mobs = matched.Select(a => new { Address = a.ToString("X"), Bytes = Dump(a) }),
            Others = others.Select(a => new { Address = a.ToString("X"), Bytes = Dump(a) }),
        };
        Directory.CreateDirectory(ProfileStore.Directory);
        string path = Path.Combine(ProfileStore.Directory, "mob-tarama.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
