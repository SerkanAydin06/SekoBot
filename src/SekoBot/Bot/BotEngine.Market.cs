using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// "Pazarı tara": on a market map the bot opens every shop one after another and writes down what
/// each one sells. Every shop of the map and where it stands is read from memory; the bot walks to
/// the nearest one not seen yet, clicks its stall like a person (the game walks up to it and opens
/// the window), reads the items and prices from memory, closes the window with Esc and goes on.
/// Nothing is bought. Memory is only read.
/// </summary>
internal sealed partial class BotEngine
{
    private Task? _marketTask;
    private CancellationTokenSource? _marketCts;
    private volatile string _marketStatus = "";

    public bool ScanningMarket => _marketTask is { IsCompleted: false };

    /// <summary>How the market scan is going, for the window.</summary>
    public string MarketStatus => _marketStatus;

    /// <summary>Raised from a worker thread when the market scan ended (message for the log).</summary>
    public event Action<string>? MarketScanFinished;

    /// <summary>Market scans are saved here.</summary>
    public static string MarketFolder => Path.Combine(ProfileStore.Directory, "pazar");

    /// <summary>A shop this close (world units, 8 m) is clicked; the game walks the rest of the way.</summary>
    internal const double ShopClickFrom = 800;
    /// <summary>The stall is clicked a little above where it stands (on its body, not the ground).</summary>
    internal const double ShopClickHeight = 60;
    /// <summary>A shop whose window didn't open after this many clicks is left out.</summary>
    internal const int ShopTries = 2;
    /// <summary>A stall drawn this close to the character on screen (pixels) is under it: a click there hits the character.</summary>
    internal const double ShopOffCharacterPx = 45;
    /// <summary>This many clicks in a row that opened nothing: clicking doesn't work, the scan stops.</summary>
    internal const int ShopClicksWithoutOpen = 8;

    public bool StartMarketScan(out string error)
    {
        error = "";
        if (BotRunning)
            error = "Önce botu durdur.";
        else if (ScanningArea || ScanningMarket)
            error = "Bir tarama zaten sürüyor.";
        else if (GameMinimized)
            error = "Oyun simge durumunda; önce oyunu aç.";
        else if (World == null)
            error = "Oyunun belleği okunamıyor (" + _memoryStatus + ").";
        if (error.Length > 0)
            return false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _marketCts = cts;
        _marketTask = Task.Run(async () =>
        {
            string message;
            try { message = await ScanMarketAsync(cts.Token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { message = "Pazar taraması hatası: " + ex.Message; }
            _marketStatus = message;
            _log(message);
            MarketScanFinished?.Invoke(message);
        });
        return true;
    }

    public void StopMarketScan() => _marketCts?.Cancel();

    private List<MarketShop> MarketShops()
    {
        lock (_worldGate)
            return _world?.ReadMarketShops() ?? [];
    }

    private OpenShop? ShopOpenNow()
    {
        lock (_worldGate)
            return _world?.ReadOpenShop();
    }

    private async Task<string> ScanMarketAsync(CancellationToken token)
    {
        var start = World!;
        var shops = MarketShops();
        if (shops.Count == 0)
            return "Bu haritada pazar dükkanı görünmüyor; önce pazar haritasına gir.";
        var book = new MarketBook(start.MapName, DateTime.Now);
        var tries = new Dictionary<int, int>();
        var random = new Random();
        string saved = "";
        void Save() => saved = book.Save(MarketFolder);
        _log($"🛒 Pazar taraması başladı: {shops.Count} dükkan var. Bot dükkanları sırayla açıp içindekileri yazacak, hiçbir şey satın almaz. " +
             "Durdurmak için aynı düğmeye bas. Oyuna dokunma.");
        using var input = new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        bool Done(int id) => book.Has(id) || tries.GetValueOrDefault(id) >= ShopTries;
        string why = "";
        int missedInARow = 0;
        try
        {
            // A window open from before: closed first (its shop is written down too).
            await TakeOpenShop(input, book, shops, token);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (World is not { Camera: { } camera } world || State is not { } state)
                {
                    await Task.Delay(500, token);
                    continue;
                }
                if (world.MapName != start.MapName)
                {
                    why = "harita değişti";
                    break;
                }
                if (world.Dead || world.Stats is { } stats && stats.HpFraction < 0.5)
                {
                    why = "karakterin canı azaldı";
                    break;
                }
                shops = MarketShops();
                _marketStatus = $"Pazar taranıyor: {book.Shops}/{shops.Count} dükkan, {book.Items} ürün okundu.";
                // A stall right under the character can't be clicked (the click hits the character).
                var meOnScreen = camera.ToScreen(world.Player, state.Width, state.Height);
                bool UnderMe(MarketShop s) => s.Body is Vec3 b && meOnScreen is PointF m &&
                    camera.ToScreen(b with { Z = b.Z + ShopClickHeight }, state.Width, state.Height) is PointF q &&
                    Math.Sqrt((q.X - m.X) * (q.X - m.X) + (q.Y - m.Y) * (q.Y - m.Y)) < ShopOffCharacterPx;
                var next = MarketBook.Next(shops, world.Player.X, world.Player.Y, id => Done(id)) is { } first && UnderMe(first)
                    ? MarketBook.Next(shops, world.Player.X, world.Player.Y, id => Done(id) || id == first.Id) ?? first
                    : MarketBook.Next(shops, world.Player.X, world.Player.Y, Done);
                if (next == null)
                    break;
                var where = new Vec3(next.X, next.Y, world.Player.Z);
                double away = where.DistanceXY(world.Player);
                // Near and drawn: click its stall (on screen), the game walks up to it and opens it.
                if (away <= ShopClickFrom && next.Body is Vec3 body &&
                    camera.ToScreen(body with { Z = body.Z + ShopClickHeight }, state.Width, state.Height) is PointF p &&
                    OnPlayfield(p, state.Width, state.Height))
                {
                    tries[next.Id] = tries.GetValueOrDefault(next.Id) + 1;
                    // A real click (the game in front, the cursor on the stall): clicks posted to
                    // the window from the background are ignored on things in the world (seen).
                    input.Enqueue(new ClickAction(Point.Round(p), Slow: true, Fast: true));
                    // Up to 5 s: the character walks over and the server sends the shop.
                    OpenShop? opened = null;
                    for (int waited = 0; waited < 5000 && opened == null; waited += 200)
                    {
                        await Task.Delay(200, token);
                        opened = ShopOpenNow();
                    }
                    if (opened != null)
                    {
                        missedInARow = 0;
                        await TakeOpenShop(input, book, shops, token);
                    }
                    else if (++missedInARow >= ShopClicksWithoutOpen)
                    {
                        why = $"{ShopClicksWithoutOpen} tıklamada hiçbir dükkan açılmadı; oyun penceresi önde mi, karakter dükkanların yanında mı?";
                        break;
                    }
                    if (book.Shops > 0 && book.Shops % 25 == 0)
                        Save();
                    await Task.Delay(random.Next(300, 900), token);
                    continue;
                }
                // Further, or near but not to be clicked on screen: walk towards it (it is drawn and
                // clickable once near). Not getting there, or there and still not clickable: one try used.
                if (away <= ShopClickFrom * 0.6)
                {
                    tries[next.Id] = tries.GetValueOrDefault(next.Id) + 1;
                    await WalkToAsync(input, where, TimeSpan.FromSeconds(3), token, () => { }, near: 150);
                    continue;
                }
                await WalkToAsync(input, where, TimeSpan.FromSeconds(8), token, () => { }, near: ShopClickFrom * 0.6);
                if (World?.Player is Vec3 now && now.DistanceXY(where) > ShopClickFrom)
                    tries[next.Id] = tries.GetValueOrDefault(next.Id) + 1; // can't get there (a wall, the edge)
            }
        }
        catch (OperationCanceledException)
        {
            why = "durduruldu";
        }
        finally
        {
            if (book.Shops > 0)
                Save();
        }
        int left = shops.Count(s => !book.Has(s.Id));
        string result = $"🛒 Pazar taraması {(why.Length > 0 ? $"bitti ({why})" : "bitti")}: {book.Shops} dükkan, {book.Items} ürün okundu" +
                        (left > 0 ? $", {left} dükkan açılamadı" : "") + ".";
        return book.Shops > 0
            ? result + $" Liste: {saved} (Excel ile açılır), özet: {Path.ChangeExtension(saved, ".txt")}"
            : result;
    }

    /// <summary>The shop window is open: writes it down, closes it with Esc and waits until it is gone.</summary>
    private async Task TakeOpenShop(InputExecutor input, MarketBook book, List<MarketShop> shops, CancellationToken token)
    {
        if (ShopOpenNow() is not { } shop)
            return;
        // The items come a moment after the window: wait until their number stays the same.
        int last = -1;
        for (int waited = 0; waited < 2000; waited += 250)
        {
            await Task.Delay(250, token);
            if (ShopOpenNow() is not { } again)
                break;
            shop = again;
            if (again.Items.Count == last && last > 0)
                break;
            last = again.Items.Count;
        }
        bool fresh = !book.Has(shop.Id);
        book.Add(shop, shops.FirstOrDefault(s => s.Id == shop.Id));
        if (fresh)
            _log($"🛒 {shop.Owner} (Sv {shop.Level}): {shop.Items.Count} ürün" +
                 (shop.Items.Count > 0 ? $", en pahalısı {ItemBook.NameOf(shop.Items.MaxBy(i => i.Price)!.Vnum)} {MarketBook.Yang(shop.Items.Max(i => i.Price))} yang" : "") + ".");
        // Esc only while the shop window is open (with nothing open it would open the game menu).
        for (int i = 0; i < 3 && ShopOpenNow() != null; i++)
        {
            input.Enqueue(new KeyTapAction(Profile.Keys.CloseWindow));
            for (int waited = 0; waited < 1500 && ShopOpenNow() != null; waited += 150)
                await Task.Delay(150, token);
        }
    }
}
