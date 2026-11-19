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
    public (int Casts, int Pulled, int Hooked, int Missed) FishCounts { get; private set; }
    public DateTime? FishingSince { get; private set; }

    /// <summary>A fish is hooked: the picture question is showing and waits for the player (true), or gone (false).</summary>
    public event Action<bool>? FishQuestion;
    public event Action<string>? FishingFinished;

    /// <summary>A cast that didn't start fishing within this long failed.</summary>
    internal static readonly TimeSpan CastTakes = TimeSpan.FromSeconds(3);
    /// <summary>No bite within this long: the line is cast again (the game takes it back after ~20 s itself).</summary>
    internal static readonly TimeSpan BiteWithin = TimeSpan.FromSeconds(45);
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
        else if (FishingNow() is not { } state)
            error = "Balık tutma bilgisi bellekten okunamadı.";
        else if (!state.RodInHand)
            error = "Karakterin elinde olta yok. Oltayı kuşan, yemi tak ve suya dönük dur.";
        if (error.Length > 0)
            return false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _fishCts = cts;
        FishCounts = (0, 0, 0, 0);
        FishingSince = DateTime.Now;
        _fishTask = Task.Run(async () =>
        {
            string message;
            try { message = await FishAsync(cts.Token); }
            catch (OperationCanceledException) { message = "🎣 Balık tutma durduruldu."; }
            catch (Exception ex) { message = "🎣 Balık tutma hatası: " + ex.Message; }
            var (casts, pulled, hooked, missed) = FishCounts;
            if (casts > 0)
                message += $" ({casts} atış, {hooked} balık takıldı, {missed} kaçtı)";
            _fishStatus = message;
            _log(message);
            FishQuestion?.Invoke(false);
            FishingFinished?.Invoke(message);
        });
        return true;
    }

    public void StopFishing() => _fishCts?.Cancel();

    private async Task<string> FishAsync(CancellationToken token)
    {
        var profile = Profile;
        using var input = new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        var random = new Random();
        int failedCasts = 0, shortCasts = 0;
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

            // Bait first (every cast: the user's way), then cast.
            await Task.Delay(random.Next(600, 1500), token); // a breath between catches, not machine-even
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
            await Task.Delay(100, token);
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
                _fishStatus = quick ? "🎣 Olta hemen geri geldi (yem yok mu?); yeniden atılıyor." : "🎣 Balık kaçtı; yeniden atılıyor.";
                return false;
            }
            if (state.Bite)
            {
                _fishStatus = "🎣 Balık geldi! Çekiliyor…";
                await Task.Delay(random.Next(250, 650), token);
                input.Enqueue(new KeyTapAction(Keys.Space));
                FishCounts = FishCounts with { Pulled = FishCounts.Pulled + 1 };
                // The line comes back (or the picture question opens).
                var pulledAt = DateTime.Now;
                while (DateTime.Now - pulledAt < TimeSpan.FromSeconds(4))
                {
                    await Task.Delay(150, token);
                    if (FishingNow() is { } after && (after.Asking || !after.LineOut))
                        return true;
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
        _fishStatus = $"🎣 {fish} takıldı! Oyundaki resim sorusunu cevapla (doğru resmi seç).";
        _log(_fishStatus);
        FishQuestion?.Invoke(true);
        if (profile.FishNotify)
            Notify($"🎣 {fish} takıldı; oyundaki resim sorusunu cevapla.");
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

    private static string KeyName(Keys key) => key is >= Keys.D0 and <= Keys.D9 ? ((int)(key - Keys.D0)).ToString() : key.ToString();
}
