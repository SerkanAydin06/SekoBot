using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// "Koordinat bul": walks the character around, reads the arrow on the big map after each walk
/// and looks for the memory values that follow it (see <see cref="CoordinateSearch"/>). Once
/// found, the position is read from memory every tick, so the bot no longer has to open the map
/// to stay in its area.
/// </summary>
internal sealed partial class BotEngine
{
    private const int FinderWalkMs = 3500;

    private readonly object _positionGate = new();
    private PositionReader? _position;
    private bool _positionTried;
    private int _positionMismatches;
    private Task? _finderTask;
    private CancellationTokenSource? _finderCts;
    private volatile bool _finderWantsMap;
    private volatile string _positionStatus = "Koordinat bilinmiyor (haritaya bakılıyor)";

    public bool FindingCoordinates => _finderTask is { IsCompleted: false };

    /// <summary>Whether the position comes from memory, for the UI.</summary>
    public string PositionStatus => _positionStatus;

    /// <summary>Raised from a worker thread when "Koordinat bul" ended (with the message for the log).</summary>
    public event Action<string>? CoordinatesFinished;

    private PointF? ReadLivePosition()
    {
        lock (_positionGate)
        {
            if (_position == null && !_positionTried)
            {
                _positionTried = true;
                _position = PositionReader.Create(_window.ProcessId, Profile.Position, out string status);
                _positionStatus = status;
            }
            return _position?.Read();
        }
    }

    /// <summary>
    /// Whenever the map is open anyway, the arrow must agree with the memory position. If it
    /// doesn't, the found address is wrong: stop using it (the bot falls back to the map).
    /// </summary>
    private void CheckPosition(Vision.GameState state)
    {
        if (state.LivePosition is not PointF live || !state.MapOpen || state.MapPosition is not PointF arrow)
            return;
        double distance = Math.Sqrt(Math.Pow(live.X - arrow.X, 2) + Math.Pow(live.Y - arrow.Y, 2));
        if (distance <= 0.03)
        {
            _positionMismatches = 0;
            return;
        }
        if (++_positionMismatches < 5)
            return;
        _positionMismatches = 0;
        lock (_positionGate)
        {
            _position?.Dispose();
            _position = null;
        }
        _positionStatus = "Koordinat haritayla uyuşmuyor: 'Koordinat bul' ile yeniden bul";
        _log("Bellekteki koordinat haritadaki okla uyuşmuyor; artık haritaya bakılacak. 'Koordinat bul' ile yeniden bulabilirsin.");
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

    private async Task<string> FindCoordinatesAsync(CancellationToken token)
    {
        using var memory = ProcessMemory.Open(_window.ProcessId, out string openError);
        if (memory == null)
            return "Koordinat bulunamadı: " + openError + ".";
        using var input = new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        var keys = Profile.Keys;
        var search = new CoordinateSearch();
        _log("Koordinat aranıyor: karakter birkaç kez yürüyecek, büyük harita açılıp kapanacak. " +
             "Yaklaşık 1–2 dakika sürer; bu sırada oyuna dokunma. Açık, düz bir yerde dur.");

        if (await ReadArrowAsync(input, keys.Map, token) is not PointF start)
            return "Koordinat bulunamadı: büyük harita (M) açılmadı ya da üzerindeki sarı ok okunamadı.";
        _log("Bellek fotoğrafı çekiliyor…");
        string? error = await Task.Run(() => search.Begin(memory, start), token);
        if (error != null)
            return "Koordinat bulunamadı: " + error + ".";

        // First walk diagonally, so that both coordinates change; other ways if blocked.
        Keys[][] firstWalks =
        [
            [keys.MoveForward, keys.MoveRight], [keys.MoveBack, keys.MoveLeft],
            [keys.MoveForward, keys.MoveLeft], [keys.MoveBack, keys.MoveRight],
        ];
        bool moved = false;
        foreach (var walk in firstWalks)
        {
            await WalkAsync(input, walk, FinderWalkMs, token);
            if (await ReadArrowAsync(input, keys.Map, token) is PointF arrow && Distance(arrow, start) >= 0.004)
            {
                _log("Değişen değerler aranıyor…");
                error = await Task.Run(() => search.FirstMove(memory, arrow), token);
                if (error != null)
                    return "Koordinat bulunamadı: " + error + ".";
                moved = true;
                break;
            }
        }
        if (!moved)
            return "Koordinat bulunamadı: haritadaki ok yerinden oynamadı (karakter yürümedi ya da bir yere takıldı). Açık bir yerde tekrar dene.";
        _log($"Yürürken değişen {search.Count:N0} değer var; eleniyor…");
        await StandStillAsync(memory, search, token);

        Keys[][] walks =
        [
            [keys.MoveForward], [keys.MoveRight], [keys.MoveRight], [keys.MoveBack],
            [keys.MoveLeft], [keys.MoveBack], [keys.MoveForward, keys.MoveRight],
        ];
        for (int i = 0; i < walks.Length; i++)
        {
            await WalkAsync(input, walks[i], FinderWalkMs, token);
            if (await ReadArrowAsync(input, keys.Map, token) is PointF arrow)
                await Task.Run(() => search.Move(memory, arrow), token);
            if (i % 2 == 1)
                await StandStillAsync(memory, search, token);
            _log($"Koordinat aranıyor… {i + 1}/{walks.Length} (kalan aday {search.Count:N0})");
        }

        var match = search.Result();
        if (match == null)
            return "Koordinat bulunamadı: haritadaki okla birlikte değişen bir değer çıkmadı. Daha açık bir alanda tekrar dene.";

        var spec = new PositionSpec
        {
            Size = match.Size,
            Ax = match.Ax, Bx = match.Bx, Ay = match.Ay, By = match.By,
            XAddress = match.MapXAddress, YAddress = match.MapYAddress,
            ProcessId = _window.ProcessId, ProcessStartTicks = memory.StartTicks,
        };
        // A path from the character's objects survives a game restart; plain addresses don't.
        if (PointerSearch.HpNodes(memory, memory.ModuleBase) is { } nodes &&
            PointerSearch.Find(memory, nodes, match.MapXAddress, match.MapYAddress) is { } path)
        {
            spec.Node = path.Node;
            spec.Offsets = path.Offsets;
            spec.XOffset = path.XOffset;
            spec.YOffset = path.YOffset;
        }
        Profile.Position = spec;
        lock (_positionGate)
        {
            _position?.Dispose();
            _position = PositionReader.Create(_window.ProcessId, spec, out string status);
            _positionTried = true;
            _positionStatus = status;
        }
        string kind = match.Size == 8 ? "double" : "float";
        string where = spec.Node >= 0
            ? "Kalıcı yol da bulundu; oyunu kapatıp açınca da çalışır."
            : "Kalıcı yol bulunamadı; oyunu yeniden açınca 'Koordinat bul'a tekrar bas.";
        return $"Koordinat bulundu ✓ ({kind}, uyum %{match.R2X * 100:0}/%{match.R2Y * 100:0}). {where} " +
               "Bot artık alanını haritayı açmadan takip eder.";
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

    private static async Task StandStillAsync(ProcessMemory memory, CoordinateSearch search, CancellationToken token)
    {
        await Task.Delay(1500, token);
        await Task.Run(() => search.Still(memory), token);
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
