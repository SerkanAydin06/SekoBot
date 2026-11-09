using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// "En iyi alanı bul": the character looks around (where it stands, then a short walk ~30 m to
/// each side, about a minute), every mob seen is remembered, and <see cref="FarmSpotFinder"/>
/// picks the best place. It is saved in the profile; with "Kayıtlı alanda başla" the bot walks
/// there before it starts farming.
/// </summary>
internal sealed partial class BotEngine
{
    private Task? _scanTask;
    private CancellationTokenSource? _scanCts;

    public bool ScanningArea => _scanTask is { IsCompleted: false };

    /// <summary>Raised from a worker thread when the area scan ended (message for the log); save the profile.</summary>
    public event Action<string>? AreaScanFinished;

    public bool StartAreaScan(out string error)
    {
        error = "";
        if (BotRunning)
            error = "Önce botu durdur.";
        else if (ScanningArea)
            error = "Alan zaten taranıyor.";
        else if (GameMinimized)
            error = "Oyun simge durumunda; önce oyunu aç.";
        else if (World == null)
            error = "Oyunun belleği okunamıyor (" + _memoryStatus + ").";
        if (error.Length > 0)
            return false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _scanCts = cts;
        _scanTask = Task.Run(async () =>
        {
            string message;
            try { message = await ScanAreaAsync(cts.Token); }
            catch (OperationCanceledException) { message = "Alan taraması durduruldu."; }
            catch (Exception ex) { message = "Alan taraması hatası: " + ex.Message; }
            _log(message);
            AreaScanFinished?.Invoke(message);
        });
        return true;
    }

    public void StopAreaScan() => _scanCts?.Cancel();

    private async Task<string> ScanAreaAsync(CancellationToken token)
    {
        var seen = new Dictionary<int, WorldMob>();
        void Look()
        {
            if (World is { } w)
                foreach (var mob in w.Mobs)
                    seen[mob.Id] = mob;
        }
        var startWorld = World!;
        var start = startWorld.Player;
        Look();
        _log("En iyi farm alanı aranıyor: karakter dört yöne kısa yürüyecek (yaklaşık 1 dk). Oyuna dokunma.");
        using var input = new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        (double X, double Y)[] legs = [(3000, 0), (0, 3000), (-3000, 0), (0, -3000)];
        foreach (var (dx, dy) in legs)
        {
            if (World?.Stats is { } stats && stats.HpFraction < 0.5)
            {
                _log("Can azaldı; tarama kısa kesildi.");
                break;
            }
            await WalkToAsync(input, start with { X = start.X + dx, Y = start.Y + dy }, TimeSpan.FromSeconds(15), token, Look);
        }
        Look();
        var spot = FarmSpotFinder.Best(seen.Values, Profile, start, startWorld.MapName, World?.AttackPower ?? 0, respawn: RespawnSeconds);
        if (spot == null)
            return "En iyi alan bulunamadı: çevrede uygun mob görülmedi (Hedef sekmesindeki filtrelere de bak).";
        Profile.FarmSpot = spot;
        double metres = spot.Position.DistanceXY(World?.Player ?? start) / 100;
        return $"En iyi farm alanı bulundu ✓ ({metres:0} m uzakta): 15 m içinde {spot.Mobs} mob — {spot.Names}. " +
               "\"Kayıtlı alanda başla\" işaretliyse bot başlayınca oraya gider.";
    }

    /// <summary>Walks to a world point with W/A/S/D (directions from the camera), looking around on the way.</summary>
    private async Task WalkToAsync(InputExecutor input, Vec3 target, TimeSpan limit, CancellationToken token, Action look)
    {
        var held = new HashSet<Keys>();
        var keys = Profile.Keys;
        var started = _clock.Elapsed;
        var lastMove = _clock.Elapsed;
        Vec3? last = null;
        try
        {
            while (_clock.Elapsed - started < limit)
            {
                token.ThrowIfCancellationRequested();
                look();
                if (World is not { Camera: { } camera } world)
                {
                    await Task.Delay(250, token);
                    continue;
                }
                if (world.Player.DistanceXY(target) < 300)
                    break;
                if (last is Vec3 l && world.Player.DistanceXY(l) > 30)
                    lastMove = _clock.Elapsed;
                else if (_clock.Elapsed - lastMove > TimeSpan.FromSeconds(2))
                    break; // stuck: on to the next leg
                last = world.Player;
                var (right, up) = camera.ToScreenDirection(target.X - world.Player.X, target.Y - world.Player.Y);
                double length = Math.Max(1e-6, Math.Sqrt(right * right + up * up));
                double sx = right / length, sy = -up / length;
                var want = new HashSet<Keys>();
                if (sx > 0.38) want.Add(keys.MoveRight);
                if (sx < -0.38) want.Add(keys.MoveLeft);
                if (sy > 0.38) want.Add(keys.MoveBack);
                if (sy < -0.38) want.Add(keys.MoveForward);
                foreach (var k in held.Where(k => !want.Contains(k)).ToList())
                {
                    input.Enqueue(new KeyUpAction(k));
                    held.Remove(k);
                }
                foreach (var k in want.Where(held.Add))
                    input.Enqueue(new KeyDownAction(k));
                await Task.Delay(250, token);
            }
        }
        finally
        {
            foreach (var k in held)
                input.Enqueue(new KeyUpAction(k));
            await Task.Delay(300, CancellationToken.None);
        }
    }

    /// <summary>The saved farm spot, when it is to be used now (same map, not too far).</summary>
    private Vec3? UsableFarmSpot(WorldSnapshot? world, BotProfile profile, out string? why)
    {
        why = null;
        if (!profile.UseFarmSpot || world == null)
            return null;
        if (profile.FarmSpot is not { } spot)
        {
            why = "Kayıtlı farm alanı yok; önce \"En iyi alanı bul\"a bas. Bot durduğu yerde başlıyor.";
            return null;
        }
        if (spot.MapName != world.MapName)
        {
            why = $"Kayıtlı farm alanı başka haritada ({spot.MapName}); bot durduğu yerde başlıyor.";
            return null;
        }
        if (spot.Position.DistanceXY(world.Player) > 30000)
        {
            why = "Kayıtlı farm alanı 300 m'den uzakta; bot durduğu yerde başlıyor.";
            return null;
        }
        return spot.Position;
    }
}
