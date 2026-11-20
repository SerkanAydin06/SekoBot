using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// Fishing (the BALIKÇILIK tab): the character stands at the water with the rod in hand; the bot
/// casts (Space), waits for a bite (the fish emoji over the head, ~2.5 s) and pulls (Space). When a
/// fish is caught the game asks which of four pictures is the right one: that question is the
/// player's — the bot never answers it; it tells the user (sound, Telegram) and waits until it is gone.
/// Keys only, so the game may stay in the background.
/// </summary>
internal sealed partial class BotEngine
{
    private Task? _fishTask;
    private CancellationTokenSource? _fishCts;
    private volatile string _fishStatus = "";

    public bool Fishing => _fishTask is { IsCompleted: false };
    public string FishingStatus => _fishStatus;
    /// <summary>Casts, bites pulled, fish hooked (the picture question came), bites missed.</summary>
    public (int Casts, int Pulled, int Hooked, int Missed, int Caught) FishCounts { get; private set; }
    public DateTime? FishingSince { get; private set; }
    private TimeSpan _fishedBefore;
    /// <summary>Paused (not stopped): the next start goes on with the same counters and time.</summary>
    public bool FishingPaused { get; private set; }

    /// <summary>How long this fishing has run (pauses left out).</summary>
    public TimeSpan FishingTime => _fishedBefore + (Fishing && FishingSince is DateTime since ? DateTime.Now - since : TimeSpan.Zero);

    /// <summary>A fish is hooked: the picture question is showing and waits for the player (true), or gone (false).</summary>
    public event Action<bool>? FishQuestion;
    public event Action<string>? FishingFinished;

    /// <summary>A cast that didn't start fishing within this long failed.</summary>
    internal static readonly TimeSpan CastTakes = TimeSpan.FromSeconds(3);
    /// <summary>No bite within this long: the line is cast again (the game takes it back after ~20 s itself).</summary>
    internal static readonly TimeSpan BiteWithin = TimeSpan.FromSeconds(45);
    /// <summary>The pull this long after the bite bubble shows (ms; it shows ~2.5 s).</summary>
    internal static readonly (int Min, int Max) PullAfterMs = (500, 2400);
    /// <summary>The mini game's picture is clicked this long after it opens (ms): a look first, well in time.</summary>
    internal static readonly (int Min, int Max) PickAfterMs = (650, 1200);
    /// <summary>After the line is back the reel-in plays this long at most before the next bait and cast.</summary>
    internal static readonly TimeSpan ReelIn = TimeSpan.FromSeconds(3.5);
    /// <summary>A line back sooner than this without a bite had no bait on it.</summary>
    internal static readonly TimeSpan NoBaitBack = TimeSpan.FromSeconds(7);
    /// <summary>How long the picture question may wait for the player before the bot gives up on the catch.</summary>
    internal static readonly TimeSpan QuestionWaits = TimeSpan.FromMinutes(5);

    private FishingState? FishingNow()
    {
        lock (_worldGate)
            return _world?.ReadFishing();
    }

    public bool StartFishing(out string error)
    {
        error = "";
        if (BotRunning)
            error = "Önce farm botunu durdur.";
        else if (ScanningArea || ScanningMarket || Fishing)
            error = "Bir tarama, yürüyüş ya da balık tutma zaten sürüyor.";
        else if (GameMinimized)
            error = "Oyun simge durumunda; önce oyunu aç.";
        else if (World == null)
            error = "Oyunun belleği okunamıyor (" + _memoryStatus + ").";
        else if (IsLoginScreen(World.MapName))
            error = "Oyun giriş ekranında; önce karakterle oyuna gir.";
        else if (FishingNow() is not { } state)
            error = "Balık tutma bilgisi bellekten okunamadı.";
        else if (!state.RodInHand)
            error = "Karakterin elinde olta yok. Oltayı kuşan, yemi tak ve suya dönük dur.";
        if (error.Length > 0)
            return false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _fishCts = cts;
        if (!FishingPaused)
        {
            FishCounts = (0, 0, 0, 0, 0);
            LastPullMs = 0;
            _fishedBefore = TimeSpan.Zero;
            lock (_fishLog)
                _fishLog.Clear();
        }
        FishingPaused = false;
        FishingSince = DateTime.Now;
        _fishTask = Task.Run(async () =>
        {
            string message;
            try { message = await FishAsync(cts.Token); }
            catch (OperationCanceledException) { message = "🎣 Balık tutma durduruldu."; }
            catch (Exception ex) { message = "🎣 Balık tutma hatası: " + ex.Message; }
            var (casts, pulled, hooked, missed, caught) = FishCounts;
            if (casts > 0)
                message += $" ({caught}/{casts} atış yakalandı, %{100.0 * caught / casts:0} başarı, {missed} kaçtı)";
            _fishStatus = message;
            _log(message);
            FishQuestion?.Invoke(false);
            FishingFinished?.Invoke(message);
        });
        return true;
    }

    /// <param name="pause">Paused: the next start keeps the counters and the time.</param>
    public void StopFishing(bool pause = false)
    {
        if (Fishing && FishingSince is DateTime since)
            _fishedBefore += DateTime.Now - since;
        FishingSince = null;
        FishingPaused = pause;
        _fishCts?.Cancel();
    }

    private async Task<string> FishAsync(CancellationToken token)
    {
        var profile = Profile;
        using var input = new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        _fishInput = input;
        _lineBackAt = null;
        var random = new Random();
        int failedCasts = 0, shortCasts = 0;
        var spot = World?.Player; // where the character fishes: moved off it, casts go nowhere
        if (profile.FishBaitKey == Keys.None)
            _log("🎣 Yem tuşu ayarlı değil: yem takılmadan atılır. Yem kısayoldaysa BALIKÇILIK sekmesinde \"Yem tuşu\"nu ayarla.");
        _log("🎣 Balık tutma başladı: olta Space ile atılır, balık gelince (başın üstünde balık balonu) Space ile çekilir. " +
             "Balık takılınca çıkan resim sorusunu SEN cevaplarsın; bot bekler ve haber verir.");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (FishingNow() is not { } state)
            {
                _fishStatus = "🎣 Bellek okunamıyor; bekleniyor…";
                await Task.Delay(1000, token);
                continue;
            }
            if (World is { Dead: true })
                return "🎣 Karakter öldü; balık tutma durdu.";
            if (World is { } there && IsLoginScreen(there.MapName))
            {
                Notify("🎣 Oyundan atıldı (giriş ekranı: bakım ya da bağlantı koptu); balık tutma durdu.");
                return "🎣 Oyundan atıldı, giriş ekranında (bakım ya da bağlantı koptu); balık tutma durdu.";
            }
            if (state.Asking)
            {
                if (!await WaitForAnswerAsync(state, profile, token))
                    return $"🎣 Resim sorusu {QuestionWaits.TotalMinutes:0} dakikadır cevaplanmadı; balık tutma durdu.";
                continue;
            }
            if (!state.RodInHand)
                return "🎣 Karakterin elinde olta yok; balık tutma durdu.";
            if (state.LineOut)
            {
                // A line from before (the user cast it): wait for its bite like ours.
                await WatchLineAsync(input, random, token);
                continue;
            }

            // The reel-in after a pull / a miss plays on (the float still drawn): bait or cast now goes nowhere.
            if (state.FloatOut && _lineBackAt != null)
            {
                _fishStatus = "🎣 Olta toplanıyor…";
                // At most a few seconds: after a catch the float stays drawn ~45 s (seen), after a miss ~3 s.
                var reelSince = _lineBackAt ?? DateTime.Now;
                while (DateTime.Now - reelSince < ReelIn && FishingNow() is { FloatOut: true, LineOut: false, Asking: false })
                    await Task.Delay(150, token);
                if (DateTime.Now - reelSince >= ReelIn)
                    _lineBackAt = null;
                else
                    continue;
            }
            // Bait first (every cast: the user's way), then cast.
            await Task.Delay(random.Next(700, 1500), token); // a breath between catches, not machine-even
            if (profile.FishBaitKey != Keys.None)
            {
                _fishStatus = "🎣 Yem takılıyor…";
                input.Enqueue(new KeyTapAction(profile.FishBaitKey));
                await Task.Delay(random.Next(700, 1100), token);
            }
            _fishStatus = "🎣 Olta atılıyor…";
            input.Enqueue(new KeyTapAction(Keys.Space));
            var castAt = DateTime.Now;
            bool cast = false;
            while (DateTime.Now - castAt < CastTakes)
            {
                await Task.Delay(150, token);
                if (FishingNow() is { LineOut: true })
                {
                    cast = true;
                    break;
                }
            }
            if (!cast)
            {
                failedCasts++;
                if (failedCasts >= 4)
                {
                    if (spot is Vec3 was && World?.Player is Vec3 at && at.DistanceXY(was) > 120)
                    {
                        Notify("🎣 Karakter balık tuttuğu yerden kaymış; balık tutma durdu.");
                        return $"🎣 Karakter balık tuttuğu yerden {at.DistanceXY(was) / 100:0.0} m uzaklaşmış; olta atılamıyor. Suya dönük yerine koyup yeniden başlat.";
                    }
                    Notify("🎣 Olta atılamıyor; balık tutma durdu. Yem bitmiş olabilir.");
                    return "🎣 Olta 4 kez atılamadı: yem bitmiş, olta elde değil ya da karakter suya bakmıyor olabilir. Balık tutma durdu.";
                }
                await Task.Delay(1000, token);
                continue;
            }
            failedCasts = 0;
            FishCounts = FishCounts with { Casts = FishCounts.Casts + 1 };
            var lineAt = DateTime.Now;
            bool bit = await WatchLineAsync(input, random, token);
            _lineBackAt = DateTime.Now;
            // Back within a few seconds without a bite: no bait on the hook (seen: casting without bait).
            if (!bit && DateTime.Now - lineAt < NoBaitBack)
            {
                if (++shortCasts >= 3)
                {
                    Notify("🎣 Yem bitti gibi; balık tutma durdu.");
                    return profile.FishBaitKey == Keys.None
                        ? "🎣 Olta yemsiz atılıyor ve hemen geri geliyor. BALIKÇILIK sekmesinde \"Yem tuşu\"nu ayarla (yemin kısayol tuşu). Balık tutma durdu."
                        : $"🎣 Olta 3 kez yemsiz geri geldi: yem bitmiş ya da {KeyName(profile.FishBaitKey)} tuşunda yem yok. Balık tutma durdu.";
                }
            }
            else
                shortCasts = 0;
        }
    }

    /// <summary>The line is out: waits for the bite and pulls (a human's reaction time), or lets it come back.</summary>
    /// <returns>True when a fish bit (pulled or the question came).</returns>
    private async Task<bool> WatchLineAsync(InputExecutor input, Random random, CancellationToken token)
    {
        var since = DateTime.Now;
        _fishStatus = "🎣 Olta suda; balık bekleniyor…";
        while (DateTime.Now - since < BiteWithin)
        {
            await Task.Delay(30, token);
            if (FishingNow() is not { } state)
                continue;
            if (state.Asking)
                return true;
            if (!state.LineOut)
            {
                // Back without a pull: the bite was missed (or none came: no bait).
                bool quick = DateTime.Now - since < NoBaitBack;
                if (!quick)
                    FishCounts = FishCounts with { Missed = FishCounts.Missed + 1 };
                _fishStatus = quick ? "🎣 Olta hemen geri geldi (yem yok mu?); yeniden atılıyor." : "🎣 Balon kaçırıldı; yeniden atılıyor.";
                return false;
            }
            if (state.Bite)
            {
                // Not at once: pulled right away the fish gets off (3 of 3); the user pulls 1.5–2.6 s after the bubble shows.
                _fishStatus = "🎣 Balık geldi! Çekiliyor…";
                var biteAt = DateTime.Now;
                var pullAt = biteAt + TimeSpan.FromMilliseconds(random.Next(PullAfterMs.Min, PullAfterMs.Max));
                while (DateTime.Now < pullAt)
                {
                    await Task.Delay(50, token);
                    if (FishingNow() is { } now && (!now.LineOut || now.Asking))
                        return now.Asking;
                }
                input.Enqueue(new KeyTapAction(Keys.Space));
                var pulledAt = DateTime.Now;
                LastPullMs = (int)(pulledAt - biteAt).TotalMilliseconds;
                FishCounts = FishCounts with { Pulled = FishCounts.Pulled + 1 };
                // Hooked (the picture question opens) or got off (the game says 1 for ~30 ms): read often.
                while (DateTime.Now - pulledAt < TimeSpan.FromSeconds(4))
                {
                    await Task.Delay(10, token);
                    if (FishingNow() is not { } after)
                        continue;
                    if (after.Asking)
                    {
                        _pullPending = LastPullMs;
                        return true;
                    }
                    if (after.Result == 1)
                    {
                        FishCounts = FishCounts with { Missed = FishCounts.Missed + 1 };
                        _fishStatus = $"🎣 Balık kaçtı (çekiş {LastPullMs / 1000.0:0.00} sn).";
                        RecordPull(LastPullMs, "kaçtı", "");
                        return true;
                    }
                }
                return true;
            }
        }
        // Nothing for too long: pull the line back in and cast again.
        input.Enqueue(new KeyTapAction(Keys.Space));
        await Task.Delay(1500, token);
        return false;
    }

    /// <summary>A fish is hooked: the picture question is the player's. Tells them, then waits until it's gone.</summary>
    /// <returns>False when nobody answered in time.</returns>
    private async Task<bool> WaitForAnswerAsync(FishingState state, BotProfile profile, CancellationToken token)
    {
        FishCounts = FishCounts with { Hooked = FishCounts.Hooked + 1 };
        string fish = state.FishId > 0 && ItemBook.Get(state.FishId) != null ? ItemBook.NameOf(state.FishId) : "balık";
        // The mini game: the upright picture (turned 0°) is clicked, after a look at the four.
        if (profile.FishAutoPick && await PickUprightAsync(fish, token))
            return true;
        _fishStatus = $"🎣 {fish} takıldı! Oyundaki resimlerden düz olanı seç.";
        _log(_fishStatus);
        FishQuestion?.Invoke(true);
        if (profile.FishNotify)
            Notify($"🎣 {fish} takıldı; oyundaki resimlerden düz olanı seç.");
        var since = DateTime.Now;
        while (FishingNow() is not { Asking: false })
        {
            if (DateTime.Now - since > QuestionWaits)
                return false; // nobody answered: stop rather than cast on with the question open
            await Task.Delay(300, token);
        }
        FishQuestion?.Invoke(false);
        _fishStatus = "🎣 Soru cevaplandı; devam.";
        await Task.Delay(1500, token);
        return true;
    }

    private DateTime? _lineBackAt;
    private int? _pullPending;

    /// <summary>How long after the bite bubble the last pull was (ms); 0 before the first.</summary>
    public int LastPullMs { get; private set; }

    private readonly List<string> _fishLog = [];

    /// <summary>The last pulls, newest first ("17:09 Levrek yakalandı · 1,72 sn").</summary>
    public List<string> FishLog()
    {
        lock (_fishLog)
            return [.. _fishLog];
    }

    /// <summary>Every pull with its wait and outcome, to find the best wait (balik-cekis.csv).</summary>
    private void RecordPull(int? ms, string outcome, string fish)
    {
        lock (_fishLog)
        {
            string icon = outcome == "yakalandı" ? "✔" : "✖";
            _fishLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {icon} {(fish.Length > 0 ? fish + " " : "")}{outcome}" + (ms is int w ? $" · {w / 1000.0:0.00} sn" : ""));
            if (_fishLog.Count > 100)
                _fishLog.RemoveAt(_fishLog.Count - 1);
        }
        try
        {
            string file = Path.Combine(Config.ProfileStore.Directory, "balik-cekis.csv");
            bool header = !File.Exists(file);
            File.AppendAllText(file, (header ? "Zaman;Bekleme (ms);Sonuç;Eşya;Karakter\n" : "") +
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss};{ms?.ToString() ?? ""};{outcome};{fish};{CharacterName}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private InputExecutor? _fishInput;

    /// <summary>Clicks the upright picture of the mini game; false when it can't (then the user is asked).</summary>
    private async Task<bool> PickUprightAsync(string fish, CancellationToken token)
    {
        if (_fishInput is not { } input)
            return false;
        var random = new Random();
        _fishStatus = $"🎣 {fish} takıldı; düz resim seçiliyor…";
        await Task.Delay(random.Next(PickAfterMs.Min, PickAfterMs.Max), token);
        // One click, made only while the window still shows (another SekoBot may hold the mouse a while:
        // a click after the window closed landed on the ground and walked the character off the shore).
        if (FishingNow() is { Asking: true } showing)
        {
            if (showing.Upright is not { } picture)
                return false;
            var at = new Point(picture.X + picture.Width / 2 + random.Next(-picture.Width / 6, picture.Width / 6 + 1),
                picture.Y + picture.Height / 2 + random.Next(-picture.Height / 6, picture.Height / 6 + 1));
            input.Enqueue(new ClickAction(at, Slow: true, Fast: true,
                StillWanted: () => FishingNow() is { Asking: true, Upright: { } still } && still.Contains(at)));
            var clickedAt = DateTime.Now;
            while (DateTime.Now - clickedAt < TimeSpan.FromSeconds(8) && FishingNow() is { Asking: true })
                await Task.Delay(10, token);
        }
        // The game tells how it went (2 caught, 1 got away) for only ~30 ms after the window closes: read often.
        var doneAt = DateTime.Now;
        int result = 0;
        bool stillAsking = false;
        while (DateTime.Now - doneAt < TimeSpan.FromSeconds(2.5) && result is not (1 or 2))
        {
            var now = FishingNow();
            result = now?.Result ?? 0;
            stillAsking = now is { Asking: true };
            if (result is not (1 or 2))
                await Task.Delay(5, token);
        }
        if (stillAsking && result is not (1 or 2))
            return false;
        string pull = _pullPending is int ms ? $" (çekiş {ms / 1000.0:0.00} sn)" : "";
        if (result == 2)
        {
            FishCounts = FishCounts with { Caught = FishCounts.Caught + 1 };
            _fishStatus = $"🎣 {fish} yakalandı!{pull}";
            RecordPull(_pullPending, "yakalandı", fish);
        }
        else
        {
            if (result == 1)
                FishCounts = FishCounts with { Missed = FishCounts.Missed + 1 };
            _fishStatus = result == 1 ? $"🎣 {fish} kaçtı: resim tutmadı.{pull}" : $"🎣 {fish}: resim seçildi.{pull}";
            RecordPull(_pullPending, result == 1 ? "resim tutmadı" : "?", fish);
        }
        _log(_fishStatus);
        _pullPending = null;
        await Task.Delay(random.Next(800, 1400), token);
        return true;
    }

    private static string KeyName(Keys key) => key is >= Keys.D0 and <= Keys.D9 ? ((int)(key - Keys.D0)).ToString() : key.ToString();
}
