using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// The market. One list per map (<see cref="MarketBook"/>, pazar/pazar-[map].json) is kept up to date
/// all the time, not made anew: on a market map shops that are gone are taken out within half a
/// minute, and every shop window that opens (by the bot or by the player) is written into it.
/// "Pazarı tara" opens the shops not read lately one after another (the nearest first: walk, click
/// the stall like a person, read the items from memory, Esc, on). "Dükkana götür" walks to the shop
/// of an item picked from the list, clicks its stall and leaves its window open. Nothing is ever
/// bought. Memory is only read.
/// </summary>
internal sealed partial class BotEngine
{
    private Task? _marketTask;
    private CancellationTokenSource? _marketCts;
    private volatile string _marketStatus = "";
    private readonly object _marketGate = new();
    private MarketBook? _market;

    /// <summary>A market scan or a walk to a shop is going on.</summary>
    public bool ScanningMarket => _marketTask is { IsCompleted: false };

    /// <summary>How the market scan is going, for the window.</summary>
    public string MarketStatus => _marketStatus;

    /// <summary>Raised from a worker thread when the market scan ended (message for the log).</summary>
    public event Action<string>? MarketScanFinished;

    public static string MarketYang(double price) => MarketBook.Yang(price);

    /// <summary>The newest market report (pazar-….html), when there is one.</summary>
    public static string? LatestReport => Directory.Exists(MarketFolder)
        ? new DirectoryInfo(MarketFolder).GetFiles("pazar-*.html").OrderByDescending(f => f.LastWriteTime).FirstOrDefault()?.FullName
        : null;

    /// <summary>Market lists are kept here.</summary>
    public static string MarketFolder => Path.Combine(ProfileStore.Directory, "pazar");

    /// <summary>A shop read this recently isn't opened again by a scan (with "skip fresh" on).</summary>
    internal static readonly TimeSpan MarketFreshFor = TimeSpan.FromHours(2);
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

    // ── The list of the map ──

    /// <summary>The market list of a map (loaded once, then kept in memory and saved now and then).</summary>
    private MarketBook MarketOf(string map)
    {
        lock (_marketGate)
        {
            if (_market?.Map != map)
            {
                if (_market is { Changed: true } old)
                    SaveMarket(old);
                _market = MarketBook.Load(MarketFolder, map);
                lock (_worldGate)
                    _market.Skills = _world?.ReadSkillNames() ?? [];
            }
            _market.Wanted = [.. Profile.MarketWanted.Where(w => w.Name.Trim().Length > 0)];
            return _market;
        }
    }

    private string SaveMarket(MarketBook book)
    {
        try
        {
            string report = book.Save(MarketFolder, MapReport.Outlines(_terrain.Of(book.Map)));
            // The bonuses seen go into the bonus book (which bonus on which items, which values), shared by all SekoBots.
            var bonuses = BonusBook.Shared;
            bonuses.LearnFrom(book.ToJson());
            bonuses.Save();
            File.WriteAllText(Path.Combine(MarketFolder, "efsun-rehberi.html"), bonuses.Html(), new System.Text.UTF8Encoding(false));
            return report;
        }
        catch (IOException ex)
        {
            _log("Pazar listesi yazılamadı: " + ex.Message);
            return "";
        }
    }

    /// <summary>Items of the market list of the map the character is on (cheapest first).</summary>
    public List<MarketListing> MarketFind(string query)
    {
        if (World is not { MapName.Length: > 0 } world)
            return [];
        string stem = MarketBook.Stem(MarketFolder, world.MapName);
        lock (_marketGate)
            if (_market?.Map != world.MapName && !File.Exists(stem + ".json"))
                return [];
        return MarketOf(world.MapName).Find(query);
    }

    /// <summary>The market list searched with item group / bonus filters and ordering (see <see cref="MarketQuery"/>).</summary>
    public List<MarketListing> MarketFind(MarketQuery query)
    {
        if (World is not { MapName.Length: > 0 } world)
            return [];
        string stem = MarketBook.Stem(MarketFolder, world.MapName);
        lock (_marketGate)
            if (_market?.Map != world.MapName && !File.Exists(stem + ".json"))
                return [];
        return MarketOf(world.MapName).Find(query, BonusBook.Shared);
    }

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

    private TimeSpan _marketLookedAt = TimeSpan.FromDays(-1), _marketShopsAt = TimeSpan.FromDays(-1), _marketSavedAt;
    private bool _onMarketMap;
    private string _marketMapSeen = "";
    private (int Id, int Items) _lastOpenShop;

    /// <summary>
    /// On the perception thread, about once a second: on a market map (also when nothing else runs)
    /// a shop window the player opened is written into the list, shops gone from the map are taken
    /// out (every 30 s), and the list is saved when it changed (every minute). The scan and the walk
    /// to a shop do this themselves.
    /// </summary>
    private void WatchMarket(WorldSnapshot? world, TimeSpan now)
    {
        if (world == null || ScanningMarket || now - _marketLookedAt < TimeSpan.FromSeconds(1))
            return;
        _marketLookedAt = now;
        if (world.MapName != _marketMapSeen)
        {
            _marketMapSeen = world.MapName;
            _onMarketMap = false;
            _marketShopsAt = TimeSpan.FromDays(-1);
        }
        if (now - _marketShopsAt >= TimeSpan.FromSeconds(30))
        {
            _marketShopsAt = now;
            var shops = MarketShops();
            _onMarketMap = shops.Count > 0;
            if (_onMarketMap)
            {
                var book = MarketOf(world.MapName);
                int known = book.Shops;
                int gone = book.SetAllShops(shops);
                if (gone > 0)
                    _marketStatus = $"{gone} dükkan kapanmış, listeden çıkarıldı · listede {book.Shops} dükkan, {book.Items} ürün.";
                else if (_marketStatus.Length == 0 && known > 0)
                    _marketStatus = $"Listede {book.Shops} dükkan, {book.Items} ürün (son güncelleme {book.Updated:HH:mm}).";
            }
        }
        if (!_onMarketMap)
            return;
        if (ShopOpenNow() is { Items.Count: > 0 } open && (open.Id, open.Items.Count) != _lastOpenShop)
        {
            _lastOpenShop = (open.Id, open.Items.Count);
            var book = MarketOf(world.MapName);
            bool wasKnown = book.Has(open.Id);
            book.Add(open, DateTime.Now);
            _log($"🛒 Açılan dükkan listeye {(wasKnown ? "işlendi (güncellendi)" : "eklendi")}: {open.Owner}, {open.Items.Count} ürün.");
        }
        lock (_marketGate)
            if (_market is { Changed: true } changed && now - _marketSavedAt >= TimeSpan.FromMinutes(1))
            {
                _marketSavedAt = now;
                SaveMarket(changed);
            }
    }

    // ── Pazarı tara ──

    private bool CanUseMarket(out string error)
    {
        error = "";
        if (BotRunning)
            error = "Önce botu durdur.";
        else if (ScanningArea || ScanningMarket)
            error = "Bir tarama ya da yürüyüş zaten sürüyor.";
        else if (GameMinimized)
            error = "Oyun simge durumunda; önce oyunu aç.";
        else if (World == null)
            error = "Oyunun belleği okunamıyor (" + _memoryStatus + ").";
        return error.Length == 0;
    }

    private void RunMarketTask(Func<CancellationToken, Task<string>> work, string failure)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _marketCts = cts;
        _marketTask = Task.Run(async () =>
        {
            string message;
            try { message = await work(cts.Token); }
            catch (OperationCanceledException) { message = "Durduruldu."; }
            catch (Exception ex) { message = failure + ex.Message; }
            _marketStatus = message;
            _log(message);
            MarketScanFinished?.Invoke(message);
        });
    }

    public bool StartMarketScan(out string error)
    {
        if (!CanUseMarket(out error))
            return false;
        RunMarketTask(ScanMarketAsync, "Pazar taraması hatası: ");
        return true;
    }

    public void StopMarketScan() => _marketCts?.Cancel();

    private async Task<string> ScanMarketAsync(CancellationToken token)
    {
        var start = World!;
        var shops = MarketShops();
        if (shops.Count == 0)
            return "Bu haritada pazar dükkanı görünmüyor; önce pazar haritasına gir.";
        var profile = Profile;
        var book = MarketOf(start.MapName);
        int gone = book.SetAllShops(shops);
        var scanStarted = DateTime.Now;
        // With "skip fresh": shops read in the last two hours aren't opened again (new and old ones are).
        bool Read(int id) => book.Fresh(id, profile.MarketResume ? MarketFreshFor : DateTime.Now - scanStarted, DateTime.Now);
        int toRead = shops.Count(s => !Read(s.Id));
        var tries = new Dictionary<int, int>();
        var random = new Random();
        int readNow = 0;
        _log($"🛒 Pazar taraması başladı: {shops.Count} dükkan var, {toRead} tanesi okunacak" +
             (book.Shops > 0 ? $" (listede {book.Shops} dükkan vardı{(gone > 0 ? $", {gone} kapanmış dükkan çıkarıldı" : "")}; son 2 saatte okunanlar atlanıyor)" : "") +
             ". Hiçbir şey satın alınmaz. Durdurmak için F11 ya da DURDUR." +
             (book.Wanted.Count > 0 ? $" Aranan: {string.Join(", ", book.Wanted.Select(w => w.Name.Trim() + (w.MaxPrice > 0 ? $" (≤ {MarketBook.Yang(w.MaxPrice)})" : "")))}." : ""));
        using var input = new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        bool Done(int id) => Read(id) || tries.GetValueOrDefault(id) >= ShopTries;
        string why = "";
        int missedInARow = 0;
        try
        {
            // A window open from before: written down and closed first.
            if (await TakeOpenShop(input, book, token, close: true) is not null)
                readNow++;
            await PrepareForMarket(input, profile, token);
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
                book.SetAllShops(shops); // shops closed meanwhile go out of the list at once
                _marketStatus = $"Pazar taranıyor: {readNow}/{toRead} dükkan okundu · listede {book.Shops} dükkan, {book.Items} ürün.";
                var next = NextShop(shops, world, camera, state, Done);
                if (next == null)
                    break;
                var where = new Vec3(next.X, next.Y, world.Player.Z);
                double away = where.DistanceXY(world.Player);
                // Near and drawn: click its stall (on screen), the game walks up to it and opens it.
                if (ClickPoint(next, world, camera, state) is Point p)
                {
                    tries[next.Id] = tries.GetValueOrDefault(next.Id) + 1;
                    // A real click (the game in front, the cursor on the stall): clicks posted to
                    // the window from the background are ignored on things in the world (seen).
                    input.Enqueue(new ClickAction(p, Slow: true, Fast: true, Yield: true));
                    if (await WaitForShop(token) != null)
                    {
                        missedInARow = 0;
                        if (await TakeOpenShop(input, book, token, close: true) is not null)
                            readNow++;
                    }
                    else if (++missedInARow >= ShopClicksWithoutOpen)
                    {
                        why = $"{ShopClicksWithoutOpen} tıklamada hiçbir dükkan açılmadı; oyun penceresi önde mi, karakter dükkanların yanında mı?";
                        break;
                    }
                    if (readNow > 0 && readNow % 25 == 0)
                        SaveMarket(book);
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
        string report = SaveMarket(book);
        int failed = shops.Count(s => !Read(s.Id) && tries.GetValueOrDefault(s.Id) >= ShopTries);
        int left = shops.Count(s => !Read(s.Id)) - failed;
        return $"🛒 Pazar taraması {(why.Length > 0 ? $"bitti ({why})" : "bitti")}: {readNow} dükkan okundu" +
               (failed > 0 ? $", {failed} dükkan açılamadı" : "") + (left > 0 ? $", {left} dükkana sıra gelmedi (yeniden basınca sürer)" : "") +
               $". Listede {book.Shops} dükkan, {book.Items} ürün." + (report.Length > 0 ? " Pazar sekmesinde \"📊 Raporu aç\"." : "");
    }

    /// <summary>The nearest shop to open next; a stall drawn right on the character is passed over (a click there hits the character).</summary>
    private static MarketShop? NextShop(List<MarketShop> shops, WorldSnapshot world, CameraView camera, Vision.GameState state, Func<int, bool> done)
    {
        var meOnScreen = camera.ToScreen(world.Player, state.Width, state.Height);
        bool UnderMe(MarketShop s) => s.Body is Vec3 b && meOnScreen is PointF m &&
            camera.ToScreen(b with { Z = b.Z + ShopClickHeight }, state.Width, state.Height) is PointF q &&
            Math.Sqrt((q.X - m.X) * (q.X - m.X) + (q.Y - m.Y) * (q.Y - m.Y)) < ShopOffCharacterPx;
        var first = MarketBook.Next(shops, world.Player.X, world.Player.Y, done);
        return first != null && UnderMe(first)
            ? MarketBook.Next(shops, world.Player.X, world.Player.Y, id => done(id) || id == first.Id) ?? first
            : first;
    }

    /// <summary>Where to click a shop's stall on screen: near enough, drawn, on the play field; else null.</summary>
    private static Point? ClickPoint(MarketShop shop, WorldSnapshot world, CameraView camera, Vision.GameState state) =>
        new Vec3(shop.X, shop.Y, world.Player.Z).DistanceXY(world.Player) <= ShopClickFrom && shop.Body is Vec3 body &&
        camera.ToScreen(body with { Z = body.Z + ShopClickHeight }, state.Width, state.Height) is PointF p &&
        OnPlayfield(p, state.Width, state.Height)
            ? Point.Round(p)
            : null;

    /// <summary>Up to 5 s after a click: the character walks over and the server sends the shop.</summary>
    private async Task<OpenShop?> WaitForShop(CancellationToken token)
    {
        for (int waited = 0; waited < 5000; waited += 200)
        {
            await Task.Delay(200, token);
            if (ShopOpenNow() is { } opened)
                return opened;
        }
        return null;
    }

    /// <summary>The shop window is open: writes it into the list (and tells about wanted items);
    /// with <paramref name="close"/> closes it with Esc and waits until it is gone.</summary>
    private async Task<OpenShop?> TakeOpenShop(InputExecutor input, MarketBook book, CancellationToken token, bool close)
    {
        if (ShopOpenNow() is not { } shop)
            return null;
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
        bool fresh = !book.Fresh(shop.Id, TimeSpan.FromMinutes(10), DateTime.Now);
        book.Add(shop, DateTime.Now);
        _lastOpenShop = (shop.Id, shop.Items.Count);
        if (fresh)
        {
            _log($"🛒 {shop.Owner} (Sv {shop.Level}): {shop.Items.Count} ürün" +
                 (shop.Items.Count > 0 ? $", en pahalısı {ItemBook.NameOf(shop.Items.MaxBy(i => i.Price)!.Vnum)} {MarketBook.Yang(shop.Items.Max(i => i.Price))} yang" : "") + ".");
            // A wanted item at its price (or cheaper): told at once, also on Telegram.
            foreach (var (_, item, price) in book.WantedIn(shop).GroupBy(f => f.Item).Select(g => g.MinBy(f => f.UnitPrice)))
            {
                string text = $"🎯 Pazarda bulundu: {item} — tanesi {MarketBook.Yang(price)} yang, satıcı {shop.Owner}.";
                _log(text);
                if (Profile.MarketNotify)
                    Notify(text);
            }
        }
        // Esc only while the shop window is open (with nothing open it would open the game menu).
        for (int i = 0; close && i < 3 && ShopOpenNow() != null; i++)
        {
            input.Enqueue(new KeyTapAction(Profile.Keys.CloseWindow));
            for (int waited = 0; waited < 1500 && ShopOpenNow() != null; waited += 150)
                await Task.Delay(150, token);
        }
        return shop;
    }

    /// <summary>Before the scan: windows over the game are closed (inventory, menu, a shop left open)
    /// and the camera looks straight down from far up, so many stalls show and none hides another.</summary>
    private async Task PrepareForMarket(InputExecutor input, BotProfile profile, CancellationToken token)
    {
        for (int i = 0; i < 4; i++)
        {
            var open = (World?.OpenWindows ?? []).Where(KnownWindows.Contains).ToList();
            if (open.Count == 0 && ShopOpenNow() == null)
                break;
            if (i == 0)
                _log("Açık pencereler kapatılıyor: " + string.Join(", ", open.Count > 0 ? open : ["dükkan"]) + ".");
            input.Enqueue(new KeyTapAction(profile.Keys.CloseWindow));
            await Task.Delay(700, token);
        }
        if (!profile.MarketBirdsEye)
            return;
        _log("Kamera kuş bakışına alınıyor (yukarıdan, uzaktan).");
        input.Enqueue(new KeyHoldAction(profile.Keys.CameraTop, profile.CameraSetupHoldMs));
        input.Enqueue(new KeyHoldAction(profile.Keys.ZoomOut, profile.CameraSetupHoldMs));
        await Task.Delay(2 * profile.CameraSetupHoldMs + 600, token);
    }

    // ── Dükkana götür ──

    /// <summary>Walking to a shop gives up after this long.</summary>
    internal static readonly TimeSpan GoToShopFor = TimeSpan.FromMinutes(2);

    /// <summary>"Dükkana götür": walks to the shop selling <paramref name="item"/> and opens it (left open, to buy by hand).</summary>
    public bool StartGoToShop(int shopId, string item, out string error)
    {
        if (!CanUseMarket(out error))
            return false;
        RunMarketTask(token => GoToShopAsync(shopId, item, token), "Dükkana gidilemedi: ");
        return true;
    }

    private async Task<string> GoToShopAsync(int shopId, string item, CancellationToken token)
    {
        var start = World!;
        var book = MarketOf(start.MapName);
        var shops = MarketShops();
        if (shops.Count == 0)
            return "Bu haritada pazar dükkanı görünmüyor; listedeki dükkana gitmek için o pazarda ol.";
        book.SetAllShops(shops);
        string owner = book.Find(item).FirstOrDefault(l => l.ShopId == shopId)?.Owner ?? "dükkan";
        if (shops.All(s => s.Id != shopId))
        {
            SaveMarket(book);
            return $"🚶 {owner} dükkanını kapatmış; listeden çıkarıldı. Listede başka satıcıya bak.";
        }
        _log($"🚶 {owner} dükkanına gidiliyor ({item}). Durdurmak için F11 ya da DURDUR.");
        using var input = new InputExecutor(_window, false, _log, () => null, () => null, _ => { }, _ => { }, background: true);
        await PrepareForMarket(input, Profile, token);
        var began = DateTime.Now;
        double best = double.MaxValue;
        int stuck = 0, clicks = 0;
        while (DateTime.Now - began < GoToShopFor)
        {
            token.ThrowIfCancellationRequested();
            if (World is not { Camera: { } camera } world || State is not { } state)
            {
                await Task.Delay(500, token);
                continue;
            }
            if (world.MapName != start.MapName)
                return "🚶 Harita değişti; dükkana gidilmedi.";
            var shop = MarketShops().FirstOrDefault(s => s.Id == shopId);
            if (shop == null)
            {
                book.SetAllShops(MarketShops());
                SaveMarket(book);
                return $"🚶 {owner} dükkanını kapatmış; listeden çıkarıldı.";
            }
            _marketStatus = $"{owner} dükkanına gidiliyor: {new Vec3(shop.X, shop.Y, 0).DistanceXY(world.Player) / 100:0} m.";
            if (ClickPoint(shop, world, camera, state) is Point p && clicks < 4)
            {
                clicks++;
                input.Enqueue(new ClickAction(p, Slow: true, Fast: true));
                if (await WaitForShop(token) is { } opened)
                {
                    bool right = opened.Id == shopId;
                    var read = await TakeOpenShop(input, book, token, close: !right);
                    SaveMarket(book);
                    if (right && read != null)
                    {
                        var listing = book.Find(item).Where(l => l.ShopId == shopId).MinBy(l => l.UnitPrice);
                        return listing != null
                            ? $"🛒 {read.Owner} dükkanı açık: {listing.Name} burada, tanesi {MarketBook.Yang(listing.UnitPrice)} yang ({listing.Count} adet). Almak senin elinde."
                            : $"🛒 {read.Owner} dükkanı açık, ama {item} artık satılmıyor (satılmış ya da kaldırılmış); liste güncellendi.";
                    }
                }
                continue;
            }
            // Walk towards it; no nearer for a while: give up.
            var where = new Vec3(shop.X, shop.Y, world.Player.Z);
            await WalkToAsync(input, where, TimeSpan.FromSeconds(6), token, () => { }, near: ShopClickFrom * 0.5);
            double now = World?.Player is Vec3 at ? at.DistanceXY(where) : double.MaxValue;
            if (now < best - 100)
            {
                best = now;
                stuck = 0;
            }
            else if (++stuck >= 4)
                return $"🚶 {owner} dükkanına yol bulunamadı (takıldı). Biraz yaklaştırıp yeniden dene.";
        }
        return clicks >= 4 ? $"🚶 {owner} dükkanı açılamadı (tezgâhına tıklanamadı)." : $"🚶 {owner} dükkanına {GoToShopFor.TotalMinutes:0} dakikada varılamadı.";
    }
}
