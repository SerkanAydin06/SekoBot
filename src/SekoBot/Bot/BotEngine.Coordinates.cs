using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// The character's position: read every tick from the game's memory (world units, see
/// <see cref="GameWorldReader"/>) and turned into a big-map position with a formula learned from
/// the yellow arrow on the big map. The formula is learned by "Koordinat bul" (a few short walks)
/// or by itself whenever the big map is open, and saved per game map.
/// </summary>
internal sealed partial class BotEngine
{
    private const int FinderWalkMs = 2500;

    private readonly object _worldGate = new();
    private GameWorldReader? _world;
    private TimeSpan _worldRetryAt = TimeSpan.FromDays(-1);
    private WorldSnapshot? _snapshot;
    private readonly MapCalibration _calibration = new();
    private string _calibrationMap = "";
    private int _positionMismatches;
    private Task? _finderTask;
    private CancellationTokenSource? _finderCts;
    private volatile bool _finderWantsMap;
    private volatile string _positionStatus = "Koordinat bilinmiyor";

    public bool FindingCoordinates => _finderTask is { IsCompleted: false };

    /// <summary>Whether the position comes from memory, for the UI.</summary>
    public string PositionStatus => _positionStatus;

    /// <summary>The last look at the game world from memory (null when it can't be read).</summary>
    public WorldSnapshot? World => Volatile.Read(ref _snapshot);

    /// <summary>Raised from a worker thread when "Koordinat bul" ended or the map formula changed
    /// (with the message for the log); the profile should be saved.</summary>
    public event Action<string>? CoordinatesFinished;

    private WorldSnapshot? ReadWorld(TimeSpan now)
    {
        lock (_worldGate)
        {
            if (_world == null)
            {
                if (now < _worldRetryAt)
                    return null;
                _worldRetryAt = now + TimeSpan.FromSeconds(3);
                _world = GameWorldReader.Open(_window.ProcessId, out string error);
                if (_world == null)
                {
                    _positionStatus = "Bellek okunamıyor: " + error;
                    Volatile.Write(ref _snapshot, null);
                    return null;
                }
            }
            var snapshot = _world.Read();
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
    }

    /// <summary>Our big-map position, when the world is readable and the map formula is known.</summary>
    private PointF? LivePosition(WorldSnapshot? world)
    {
        if (world == null)
        {
            if (_world != null)
                _positionStatus = _world.Status;
            return null;
        }
        if (!HasMapFormula(world, out var spec))
        {
            _positionStatus = "Konum bellekten ✓ — harita ayarı yok: 'Koordinat bul'a bas (ya da bir kez M ile haritayı aç)";
            return null;
        }
        _positionStatus = "Koordinat: bellekten ✓";
        return spec.ToMap(world.Player);
    }

    private bool HasMapFormula(WorldSnapshot world, out PositionSpec spec)
    {
        spec = Profile.Position!;
        return spec is { Version: >= PositionSpec.CurrentVersion } && spec.MapName == world.MapName;
    }

    /// <summary>
    /// Whenever the big map is open anyway: without a formula, learn it from the arrow; with
    /// one, the arrow must agree, else the formula is dropped and learned again.
    /// </summary>
    private void LearnMap(Vision.GameState state, WorldSnapshot? world)
    {
        if (world == null || !state.MapOpen || state.MapPosition is not PointF arrow)
            return;
        if (HasMapFormula(world, out var spec))
        {
            var predicted = spec.ToMap(world.Player);
            if (Distance(predicted, arrow) <= 0.03)
            {
                _positionMismatches = 0;
                return;
            }
            if (++_positionMismatches < 5)
                return;
            _positionMismatches = 0;
            Profile.Position = null;
            _log("Bellekteki konum haritadaki okla uyuşmuyor; harita ayarı yeniden öğrenilecek.");
            CoordinatesFinished?.Invoke("");
            return;
        }
        if (_calibrationMap != world.MapName)
        {
            _calibration.Clear();
            _calibrationMap = world.MapName;
        }
        if (_calibration.Add(world.Player, arrow) && _calibration.Fit(world.MapName) is { } fit)
        {
            Profile.Position = fit;
            _calibration.Clear();
            _log($"Harita ayarı öğrenildi ✓ (harita {world.MapName}). Bot artık alanını haritayı açmadan takip eder.");
            CoordinatesFinished?.Invoke("");
        }
    }

    public bool StartCoordinateFinder(out string error)
    {
        error = "";
        if (BotRunning)
        {
            error = "Önce botu durdur.";
            return false;
        }
        if (FindingCoordinates)
        {
            error = "Koordinat zaten aranıyor.";
            return false;
        }
        if (GameMinimized)
        {
            error = "Oyun simge durumunda; önce oyunu aç.";
            return false;
        }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _finderCts = cts;
        _finderTask = Task.Run(async () =>
        {
            string message;
            try { message = await FindCoordinatesAsync(cts.Token); }
            catch (OperationCanceledException) { message = "Koordinat arama durduruldu."; }
            catch (Exception ex) { message = "Koordinat arama hatası: " + ex.Message; }
            finally { _finderWantsMap = false; }
            _log(message);
            CoordinatesFinished?.Invoke(message);
        });
        return true;
    }

    public void StopCoordinateFinder() => _finderCts?.Cancel();

    /// <summary>A few short walks; after each, the arrow on the big map and the world position
    /// from memory make one pair for <see cref="MapCalibration"/>.</summary>
    private async Task<string> FindCoordinatesAsync(CancellationToken token)
    {
        await Task.Delay(300, token);
        if (World is not { } start)
            return "Koordinat bulunamadı: oyunun belleği okunamıyor (" + _positionStatus + "). " +
                   "SekoBot'u yönetici olarak aç; oyun güncellendiyse yeni sürüm gerekebilir.";
        using var input = new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        var keys = Profile.Keys;
        var calibration = new MapCalibration();
        _log($"Konum bellekten okunuyor ✓ (X={start.Player.X:0.#}, Y={start.Player.Y:0.#}). Şimdi büyük haritayla eşleniyor: " +
             "karakter birkaç kez kısa yürüyecek, harita açılıp kapanacak (yaklaşık 30 sn). Oyuna dokunma, açık bir yerde dur.");

        Keys[][] walks =
        [
            [], [keys.MoveForward, keys.MoveRight], [keys.MoveBack, keys.MoveRight], [keys.MoveBack, keys.MoveLeft],
            [keys.MoveForward, keys.MoveLeft], [keys.MoveForward], [keys.MoveRight],
        ];
        PositionSpec? fit = null;
        for (int i = 0; i < walks.Length; i++)
        {
            if (walks[i].Length > 0)
                await WalkAsync(input, walks[i], FinderWalkMs, token);
            if (await ReadArrowAsync(input, keys.Map, token) is PointF arrow && World is { } world)
            {
                calibration.Add(world.Player, arrow);
                fit = calibration.Fit(world.MapName);
                if (fit != null && calibration.Count >= 4)
                    break;
            }
            _log($"Harita eşleniyor… {i + 1}/{walks.Length}");
        }
        if (fit == null)
            return calibration.Count < 2
                ? "Koordinat bulunamadı: büyük harita (M) açılmadı ya da üzerindeki sarı ok okunamadı."
                : "Koordinat bulunamadı: karakter yeterince yürümedi ya da haritadaki ok tutarsız okundu. Açık bir yerde tekrar dene.";
        Profile.Position = fit;
        return $"Koordinat hazır ✓ (harita {fit.MapName}, uyum %{fit.Fit * 100:0}). Bot artık alanını haritayı açmadan takip eder.";
    }

    private static double Distance(PointF a, PointF b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static async Task WalkAsync(InputExecutor input, Keys[] keys, int ms, CancellationToken token)
    {
        foreach (var key in keys)
            input.Enqueue(new KeyDownAction(key));
        try
        {
            await Task.Delay(ms, token);
        }
        finally
        {
            foreach (var key in keys)
                input.Enqueue(new KeyUpAction(key));
        }
        await Task.Delay(900, token); // come to a stop
    }

    /// <summary>Opens the big map, averages a few readings of the arrow, closes the map again.</summary>
    private async Task<PointF?> ReadArrowAsync(InputExecutor input, Keys mapKey, CancellationToken token)
    {
        _finderWantsMap = true;
        try
        {
            await Task.Delay(300, token); // let the perception see whether it is open already
            bool seenOpen = State?.MapOpen == true;
            if (!seenOpen)
                input.Enqueue(new KeyTapAction(mapKey));
            var asked = _clock.Elapsed;
            var readings = new List<PointF>();
            var lastTime = TimeSpan.Zero;
            while (_clock.Elapsed - asked < TimeSpan.FromSeconds(3) && readings.Count < 5)
            {
                await Task.Delay(120, token);
                var s = State;
                if (s is not { MapOpen: true })
                    continue;
                seenOpen = true;
                // Give the map a moment to draw; take each analysed frame once.
                if (s.MapPosition is PointF p && s.Time - asked >= TimeSpan.FromMilliseconds(500) && s.Time != lastTime)
                {
                    readings.Add(p);
                    lastTime = s.Time;
                }
            }
            if (seenOpen)
            {
                input.Enqueue(new KeyTapAction(mapKey));
                await Task.Delay(800, token);
                if (State?.MapOpen == true)
                {
                    input.Enqueue(new KeyTapAction(mapKey));
                    await Task.Delay(800, token);
                }
            }
            if (readings.Count < 2)
                return null;
            var xs = readings.Select(r => r.X).OrderBy(v => v).ToList();
            var ys = readings.Select(r => r.Y).OrderBy(v => v).ToList();
            return new PointF(xs[xs.Count / 2], ys[ys.Count / 2]);
        }
        finally
        {
            _finderWantsMap = false;
        }
    }
}
