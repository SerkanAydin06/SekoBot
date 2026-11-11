using System.Diagnostics;
using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Input;
using SekoBot.Memory;
using SekoBot.Native;
using SekoBot.Recording;
using SekoBot.Update;
using SekoBot.Vision;

namespace SekoBot.UI;

/// <summary>
/// Layout: header (connection) → control bar (Start F10 / Stop F11, one-line status) →
/// live preview + session stats → settings tabs + log.
/// </summary>
internal sealed class MainForm : Form
{
    private const int CsDropShadow = 0x00020000;

    private readonly BotProfile _profile = ProfileStore.Load();
    private readonly Button _gameButton, _startBot, _stopBot, _record, _replay;
    private readonly ContextMenuStrip _gameMenu = new();
    private readonly CheckBox _dryRun;
    private readonly PreviewControl _preview;
    private readonly Label _connection, _status, _calibrationHint, _calibrationState, _technical;
    private readonly Label _statTime, _statMobs, _statRate, _statDeaths, _statPotions, _statExp, _statExpRate, _statLevel, _statYang;
    private readonly Label _positionState, _skillList, _farmState, _respawnState;
    private readonly Button _findArea;
    private readonly Button _scanMobs;
    private readonly Meter _hpMeter, _mpMeter, _targetMeter;
    private readonly Button[] _tabButtons;
    private readonly Panel[] _tabPages;
    private readonly Button _dashButton;
    private readonly Panel _dashboard;
    private readonly Card _settingsCard;
    private readonly Label _pageTitle, _pageInfo, _bagState, _events, _identity;

    private static string StartKey => Instance.IsFirst ? "F10" : "Ctrl+F10";
    private static string StopKey => Instance.IsFirst ? "F11" : "Ctrl+F11";

    /// <summary>When a game process started (to take the oldest free game first).</summary>
    private static DateTime GameStarted(uint processId)
    {
        try { return System.Diagnostics.Process.GetProcessById((int)processId).StartTime; }
        catch { return DateTime.MaxValue; }
    }
    private static readonly string[] PageInfo =
    [
        "Mobların nasıl kesileceği: menzil ve grupları toplayıp birden kesme.",
        "Hangi moblara gidileceği: seviye, Metin taşları, isimle seçme ya da atlama.",
        "Yerden neyin toplanacağı ve çanta dolunca neyin atılacağı.",
        "Can ve mana iksirleri, ölünce yeniden başlama, becerilerin otomatik kullanımı.",
        "Avlanılan alan: sınırı, en iyi alanı bulma, ne zaman durulacağı.",
        "Telegram'a bildirim ve Telegram'dan komut (durum / dur / başla).",
        "Oyunda kullandığın tuşlar. Değiştirmek için kutuya tıkla, sonra yeni tuşa bas.",
        "Deneme, kayıt ve teknik araçlar.",
    ];
    private readonly RichTextBox _log;
    private readonly System.Windows.Forms.Timer _scanTimer, _previewTimer, _statsTimer, _updateTimer;
    private readonly GlobalHotkeys _hotkeys = new();

    private BotEngine? _engine;
    private Bitmap? _frameBitmap;
    private long _frameSequence;
    private bool _autoAttached, _updating, _gameMinimized, _replaying;
    private List<WindowInfo> _games = [], _others = [];
    /// <summary>Why the bot last stopped (shown in the status line until it starts again).</summary>
    private string? _stopReason;
    /// <summary>Verified update that arrived while the bot was running.</summary>
    private PreparedUpdate? _pendingUpdate;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CsDropShadow;
            return cp;
        }
    }

    public MainForm()
    {
        if (!string.IsNullOrWhiteSpace(_profile.GameWindowTitle))
            WindowService.GameTitles = [_profile.GameWindowTitle.Trim(), "Mobile2 Global", "Metin2"];
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "SekoBot";
        ClientSize = new Size(1180, 760);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;
        KeyPreview = true;

        // ── Header: name, version, connection ─────────────────────
        var header = new Card { Bounds = new Rectangle(12, 10, 1156, 46) };
        Controls.Add(header);
        var title = Theme.Label(header, "◆  SEKOBOT", new Rectangle(14, 8, 150, 30), 13.5f, Theme.Text, FontStyle.Bold);
        var version = _identity = Theme.Label(header, "v" + BuildInfo.Version, new Rectangle(160, 13, 380, 22), 8.5f, Theme.Muted);
        _connection = Theme.Label(header, "○  Oyun bekleniyor", new Rectangle(560, 12, 380, 22), 9.5f, Theme.Muted,
            FontStyle.Bold, ContentAlignment.MiddleRight);
        foreach (Control c in new Control[] { header, title, version, _connection })
        {
            c.MouseDown += DragWindow;
            c.Cursor = Cursors.SizeAll;
        }
        _gameButton = Theme.Button(header, "Oyunu seç  ▾", new Rectangle(950, 7, 110, 32), Theme.Secondary, Theme.SecondaryHover);
        _gameButton.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _gameButton.Click += (_, _) => ShowGameMenu();
        var minimize = Theme.Button(header, "—", new Rectangle(1068, 7, 36, 32), Theme.Surface, Theme.Secondary);
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var close = Theme.Button(header, "×", new Rectangle(1110, 7, 36, 32), Theme.Surface, Theme.Danger);
        close.Font = new Font("Segoe UI", 14f);
        close.Click += (_, _) => Close();

        // ── Control bar: Start / Stop and what the bot is doing ───
        var bar = new Card { Bounds = new Rectangle(12, 64, 1156, 64) };
        Controls.Add(bar);
        _startBot = Theme.Button(bar, $"▶  BAŞLAT   {StartKey}", new Rectangle(12, 10, 220, 44), Theme.Primary, Theme.PrimaryHover);
        _startBot.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _startBot.Click += (_, _) => ToggleBot();
        _stopBot = Theme.Button(bar, $"■  DURDUR   {StopKey}", new Rectangle(242, 10, 200, 44), Theme.Danger, Theme.DangerHover);
        _stopBot.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _stopBot.Click += (_, _) => StopBot("kullanıcı durdurdu", byUser: true);
        _status = Theme.Label(bar, "Oyun bekleniyor", new Rectangle(464, 10, 680, 44), 12f, Theme.Muted, FontStyle.Bold);

        // ── Side menu: the dashboard and the settings pages ──────
        var side = new Card { Bounds = new Rectangle(12, 136, 180, 612) };
        Controls.Add(side);
        _dashButton = SideButton(side, "📊   Panel", 12);
        _dashButton.Click += (_, _) => SelectTab(-1);
        Theme.Label(side, "AYARLAR", new Rectangle(16, 62, 150, 18), 8f, Theme.Accent, FontStyle.Bold);
        string[] tabNames = ["⚔   Saldırı", "🎯   Hedef", "🎒   Eşya / Envanter", "🧪   İksir / Beceri", "📍   Alan",
                             "🔔   Bildirim", "⌨   Tuşlar", "⚙   Gelişmiş"];
        _tabButtons = new Button[tabNames.Length];
        _tabPages = new Panel[tabNames.Length];
        Theme.Label(side, "ETKİNLİK", new Rectangle(16, 440, 150, 18), 8f, Theme.Accent, FontStyle.Bold);
        _events = Theme.Label(side, "Bot çalışınca görünür.", new Rectangle(16, 460, 152, 140), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

        // ── Dashboard: live preview + session stats ──────────────
        _dashboard = new Panel { Bounds = new Rectangle(202, 136, 966, 400), BackColor = Theme.Background };
        Controls.Add(_dashboard);
        var previewCard = new Card { Bounds = new Rectangle(0, 0, 600, 400) };
        _dashboard.Controls.Add(previewCard);
        Theme.Label(previewCard, "CANLI GÖRÜNTÜ", new Rectangle(16, 8, 300, 20), 8f, Theme.Accent, FontStyle.Bold);
        Theme.Label(previewCard, "Sarı halka = bulunan mob", new Rectangle(320, 8, 264, 20), 8f, Theme.Muted,
            FontStyle.Regular, ContentAlignment.MiddleRight);
        _preview = new PreviewControl { Bounds = new Rectangle(14, 32, 572, 354), Profile = _profile };
        _preview.SelectionChanged += UpdateControls;
        _preview.MouseClick += (_, _) => { if (_engine != null && _gameMinimized) WindowService.Restore(_engine.Window); };
        previewCard.Controls.Add(_preview);

        var statsCard = new Card { Bounds = new Rectangle(610, 0, 356, 400) };
        _dashboard.Controls.Add(statsCard);
        Theme.Label(statsCard, "OTURUM", new Rectangle(16, 8, 200, 20), 8f, Theme.Accent, FontStyle.Bold);
        // Three columns: time and kills, experience, level/money/potions/deaths.
        _statTime = Tile(statsCard, "SÜRE", 16, 32);
        _statMobs = Tile(statsCard, "ÖLDÜRÜLEN", 126, 32);
        _statRate = Tile(statsCard, "SAATTE MOB", 236, 32);
        _statExp = Tile(statsCard, "KAZANILAN EXP", 16, 98);
        _statExpRate = Tile(statsCard, "SAATTE EXP", 126, 98);
        _statLevel = Tile(statsCard, "SEVİYE", 236, 98, small: true);
        _statYang = Tile(statsCard, "YANG", 16, 164);
        _statPotions = Tile(statsCard, "İKSİR KALAN · İÇİLEN", 126, 164, small: true);
        _statDeaths = Tile(statsCard, "ÖLÜM", 236, 164);
        Theme.Label(statsCard, "KARAKTER", new Rectangle(16, 244, 200, 18), 8f, Theme.Accent, FontStyle.Bold);
        _hpMeter = AddMeter(statsCard, "HP", Theme.Red, 268);
        _mpMeter = AddMeter(statsCard, "MP", Theme.Accent, 304);
        _targetMeter = AddMeter(statsCard, "HEDEF", Theme.Yellow, 340);

        // ── Settings pages: one big card, title + what the page is for ──
        _settingsCard = new Card { Bounds = new Rectangle(202, 136, 966, 400), Visible = false };
        Controls.Add(_settingsCard);
        _pageTitle = Theme.Label(_settingsCard, "", new Rectangle(24, 14, 600, 30), 14f, Theme.Text, FontStyle.Bold);
        _pageInfo = Theme.Label(_settingsCard, "", new Rectangle(24, 44, 918, 20), 9f, Theme.Muted);
        for (int i = 0; i < tabNames.Length; i++)
        {
            int index = i;
            _tabButtons[i] = SideButton(side, tabNames[i], 86 + i * 42);
            _tabButtons[i].Click += (_, _) => SelectTab(index);
            _tabPages[i] = new Panel { Bounds = new Rectangle(24, 80, 918, 306), BackColor = Theme.Surface, Visible = false };
            _settingsCard.Controls.Add(_tabPages[i]);
        }
        // A thin line under the title.
        _settingsCard.Controls.Add(new Panel { Bounds = new Rectangle(24, 70, 918, 1), BackColor = Theme.Border });

        // Saldırı
        var attack = _tabPages[0];
        NumberRow(attack, "Saldırı mesafesi (%)", 0, 0, 1, 20, Math.Round(_profile.AttackRadius * 100, 1), 1, 0.5m,
            v => _profile.AttackRadius = v / 100);
        NumberRow(attack, "Z ile toplama (sn)", 0, 34, 0, 10, _profile.LootEveryMs / 1000.0, 1, 0.5m,
            v => _profile.LootEveryMs = (int)(v * 1000));
        Check(attack, "Grupları topla (üçlüleri toplayıp birden kes)", 370, 0, 366, _profile.PullGroups,
            v => _profile.PullGroups = v);
        NumberRow(attack, "Toplanacak en fazla mob", 370, 34, 3, 30, _profile.PullMaxMobs, 0, 1,
            v => _profile.PullMaxMobs = (int)v);
        NumberRow(attack, "Toplarken can en az (%)", 370, 68, 10, 100, Math.Round(_profile.PullMinHp * 100), 0, 5,
            v => _profile.PullMinHp = v / 100);
        NumberRow(attack, "Toplama alanı (m)", 0, 68, 3, 30, _profile.PullAreaMetres, 0, 1,
            v => _profile.PullAreaMetres = v);
        Hint(attack, "Saldırı mesafesi bellekteki gerçek menzille hesaplanır; % yalnızca yedek. Gruplar: bot en değerli üçlüden başlar, " +
            "toplama alanındaki diğer üçlüleri de toplar; sınıra ulaşınca, alan boşalınca ya da can düşünce hepsini birden keser. " +
            "Z ile toplama yalnız \"yerdeki eşyalara yürüyüp topla\" kapalıyken çalışır (açıkken bot eşyanın üstünde Z'ye basar); 0 = kapalı.", 102);

        // İksir & Buff
        // Hedef: which mobs to go for, and loot.
        var targets = _tabPages[1];
        NumberRow(targets, "En yüksek mob seviyesi", 0, 0, 0, 120, _profile.MaxMobLevel, 0, 1,
            v => _profile.MaxMobLevel = (int)v);
        Check(targets, "Önce bana saldıran mobu kes", 370, 0, 360, _profile.AttackersFirst, v => _profile.AttackersFirst = v);
        Theme.Label(targets, "Metin taşları", new Rectangle(0, 34, 190, 26), 9f, Theme.Muted);
        var metins = new ComboBox
        {
            Bounds = new Rectangle(196, 34, 150, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.Inset,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
        };
        metins.Items.AddRange(["Atla", "Normal mob gibi", "Önce onlara git"]);
        metins.SelectedIndex = (int)_profile.Metins;
        metins.SelectedIndexChanged += (_, _) => { _profile.Metins = (MetinMode)metins.SelectedIndex; SaveProfile(); };
        targets.Controls.Add(metins);
        var mobInfo = Theme.Button(targets, "📊  Moblar ve düşenler", new Rectangle(370, 34, 200, 28), Theme.Secondary, Theme.SecondaryHover);
        mobInfo.Click += (_, _) =>
        {
            using var form = new MobInfoForm(_engine);
            form.ShowDialog(this);
        };
        TextRow(targets, "Sadece bu moblar", 0, 68, _profile.OnlyMobs, v => _profile.OnlyMobs = v);
        TextRow(targets, "Bu mobları atla", 370, 68, _profile.SkipMobs, v => _profile.SkipMobs = v);
        Hint(targets, "Mob adları virgülle yazılır, adın bir kısmı yeter; boş = hepsi. Seviye 0 = sınırsız. " +
            "\"Moblar ve düşenler\": bu haritadaki moblar, verdikleri EXP ve botun gördüğü düşen eşyalar.", 110);

        // Eşya / Envanter: what is picked up, what is thrown away when the bag fills.
        var items = _tabPages[2];
        Check(items, "Yerdeki eşyalara yürüyüp topla", 0, 0, 300, _profile.PickUpDrops, v => _profile.PickUpDrops = v);
        var itemList = Theme.Button(items, "📋  Eşya listesi (Topla / Alma / At)", new Rectangle(370, 0, 286, 28), Theme.Primary, Theme.PrimaryHover);
        Theme.Label(items, "Toplanacaklar:", new Rectangle(0, 38, 120, 26), 9f, Theme.Muted);
        Check(items, "Yang", 124, 38, 70, _profile.LootYang, v => _profile.LootYang = v);
        Check(items, "İksir", 198, 38, 70, _profile.LootPotions, v => _profile.LootPotions = v);
        Check(items, "Silah / zırh", 272, 38, 110, _profile.LootEquipment, v => _profile.LootEquipment = v);
        Check(items, "Diğer (malzeme, kitap…)", 386, 38, 200, _profile.LootOther, v => _profile.LootOther = v);
        Theme.Label(items, "Silah / zırh satışta en az", new Rectangle(0, 72, 190, 26), 9f, Theme.Muted);
        var minGold = Theme.Number(items, new Rectangle(196, 72, 100, 26), 0, 10_000_000,
            Math.Clamp(_profile.LootEquipmentMinGold, 0, 10_000_000), 0, 500);
        minGold.ValueChanged += (_, _) => { _profile.LootEquipmentMinGold = (int)minGold.Value; SaveProfile(); };
        Theme.Label(items, "ÇANTA DOLARKEN", new Rectangle(0, 114, 300, 18), 8f, Theme.Accent, FontStyle.Bold);
        Check(items, "Yer azalınca 'At' işaretlileri ve botun aldığı ucuz silah/zırhı at", 0, 136, 470,
            _profile.ThrowWhenInventoryFull, v => _profile.ThrowWhenInventoryFull = v);
        Check(items, "Atılacak bir şey yoksa çanta dolunca dur", 480, 136, 330, _profile.StopWhenInventoryFull,
            v => _profile.StopWhenInventoryFull = v);
        var throwNow = Theme.Button(items, "🗑  Şimdi at", new Rectangle(0, 172, 140, 30), Theme.Secondary, Theme.SecondaryHover);
        throwNow.Click += (_, _) => WriteLog(_engine?.CleanNow() ?? "Önce oyunu bağla.");
        _bagState = Theme.Label(items, "", new Rectangle(152, 172, 760, 30), 9f, Theme.Text);
        var bonusList = Theme.Button(items, "✨  Efsun listesi", new Rectangle(310, 72, 180, 26), Theme.Secondary, Theme.SecondaryHover);
        bonusList.Click += (_, _) =>
        {
            using var form = new BonusListForm(_profile);
            form.ShowDialog(this);
            SaveProfile();
            WriteLog(_profile.LootBonuses.Count == 0
                ? "Efsun listesi boş: silah / zırh sadece fiyatına göre alınır."
                : "Efsun listesi: " + string.Join(", ", _profile.LootBonuses.Select(w => $"{Memory.ItemBonuses.NameOf(w.Type)} ≥ {w.Min}")) +
                  ". Bütün silah / zırhlar alınır, bu efsunlardan biri olmayan ucuzlar geri atılır.");
        };
        itemList.Click += (_, _) =>
        {
            using var form = new ItemListForm(_profile);
            form.ShowDialog(this);
            SaveProfile();
            WriteLog($"Eşya listesi: {_profile.LootPick.Count} eşya mutlaka toplanacak, {_profile.LootSkip.Count} eşya alınmayacak, " +
                $"{_profile.LootThrow.Count} eşya envanter dolunca atılacak.");
        };
        Hint(items, "Topla işaretli özel eşyalar (ör. okey kartı) 15 m'ye kadar, mob saldırırken bile hemen alınır. " +
            "Silah / zırh kapalıyken \"en az\" fiyatın altındakiler alınmaz; yine de çantaya girerse yer azalınca atılır " +
            "(bot başlarken çantada olanlar, giyilenler ve Topla işaretliler atılmaz). Atma: bot sakin bir yere geçer, " +
            "envanteri açıp eşyaları yere sürükler, sonra alana döner.", 216);

        var potions = _tabPages[3];
        NumberRow(potions, "HP iksiri (% altında)", 0, 0, 5, 95, Math.Round(_profile.PotionHpBelow * 100), 0, 1,
            v => _profile.PotionHpBelow = v / 100);
        NumberRow(potions, "MP iksiri (% altında)", 0, 34, 0, 95, Math.Round(_profile.PotionMpBelow * 100), 0, 1,
            v => _profile.PotionMpBelow = v / 100);
        Check(potions, "Ölünce yeniden başla", 0, 68, 300, _profile.ReviveOnDeath, v => _profile.ReviveOnDeath = v);
        NumberRow(potions, "Kaç HP iksiri kalınca kaç", 0, 102, 0, 200, _profile.RetreatAtPotions, 0, 1,
            v => _profile.RetreatAtPotions = (int)v);
        NumberRow(potions, "Tehlikede kaç: can % altı", 0, 136, 0, 90, Math.Round(_profile.FleeAtHp * 100), 0, 5,
            v => _profile.FleeAtHp = v / 100);
        Hint(potions, "Tehlikede kaç: can bu yüzdenin altına düşer ve en az 3 mob saldırırsa bot vurmayı bırakıp moblardan " +
            "uzaklaşır (iksir içmeye devam eder); can %60'ı geçince ya da 2 sn kimse saldırmayınca (en çok 20 sn) dövüşe döner. 0 = kapalı.", 176);
        Check(potions, "Becerileri otomatik kullan (bellekten)", 370, 0, 360, _profile.AutoSkills, v => _profile.AutoSkills = v);
        NumberRow(potions, "Alan becerisi: en az mob", 370, 34, 1, 15, _profile.AreaSkillMinMobs, 0, 1,
            v => _profile.AreaSkillMinMobs = (int)v);
        _skillList = Theme.Label(potions, "", new Rectangle(370, 68, 366, 72), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

        // Alan
        var area = _tabPages[4];
        NumberRow(area, "Alan sınırı (metre)", 0, 0, 0, 500, Math.Round(_profile.LeashMeters), 0, 5,
            v => _profile.LeashMeters = v);
        _findArea = Theme.Button(area, "📍  En iyi alanı bul", new Rectangle(370, 0, 180, 28), Theme.Primary, Theme.PrimaryHover);
        _findArea.Click += (_, _) => ToggleAreaScan();
        Check(area, "Kayıtlı alanda başla", 560, 1, 176, _profile.UseFarmSpot, v => _profile.UseFarmSpot = v);
        _farmState = Theme.Label(area, "", new Rectangle(370, 32, 366, 40), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);
        Check(area, "GM görünce dur", 0, 34, 180, _profile.StopNearGm, v => _profile.StopNearGm = v);
        Check(area, "Şehre girince dur", 190, 34, 170, _profile.StopInSafeZone, v => _profile.StopInSafeZone = v);
        Check(area, "Ara sıra mola ver", 0, 68, 180, _profile.Breaks, v => _profile.Breaks = v);
        NumberRow(area, "Oyuncu kalınca yeni alan (dk)", 0, 100, 0, 30, _profile.AvoidPlayerMinutes, 0, 1,
            v => _profile.AvoidPlayerMinutes = v);
        _respawnState = Theme.Label(area, "", new Rectangle(0, 140, 918, 22), 8.5f, Theme.Muted);
        _positionState = Theme.Label(area, "", new Rectangle(0, 166, 918, 40), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

        // Tuşlar
        // Bildirim: messages to the user's own Telegram bot.
        var notify = _tabPages[5];
        Check(notify, "Telegram'a bildirim gönder (GM, ölüm, seviye, iksir bitti, bot durdu)", 0, 0, 600,
            _profile.TelegramEnabled, v => _profile.TelegramEnabled = v);
        var token = TextRow(notify, "Bot anahtarı", 0, 34, _profile.TelegramToken, v => _profile.TelegramToken = v.Trim(), 420);
        var chat = TextRow(notify, "Sohbet no", 0, 68, _profile.TelegramChatId, v => _profile.TelegramChatId = v.Trim(), 160);
        var findChat = Theme.Button(notify, "Sohbeti bul", new Rectangle(310, 68, 120, 26), Theme.Secondary, Theme.SecondaryHover);
        findChat.Click += async (_, _) =>
        {
            var (id, error) = await TelegramNotifier.FindChatAsync(_profile.TelegramToken);
            if (id != null)
            {
                chat.Text = id;
                WriteLog("Telegram sohbeti bulundu ✓");
            }
            else
                WriteLog(error ?? "");
        };
        var test = Theme.Button(notify, "Deneme mesajı", new Rectangle(440, 68, 130, 26), Theme.Secondary, Theme.SecondaryHover);
        test.Click += async (_, _) =>
            WriteLog(await TelegramNotifier.SendAsync(_profile.TelegramToken, _profile.TelegramChatId, "SekoBot: deneme mesajı ✓")
                     ?? "Telegram deneme mesajı gönderildi ✓");
        Check(notify, "Komutlar (durum/dur/başla)", 580, 68, 156, _profile.TelegramCommands,
            v => _profile.TelegramCommands = v);
        Hint(notify, "Kurulum: Telegram'da @BotFather'a /newbot yaz, verdiği anahtarı buraya yapıştır. Sonra kendi botuna " +
            "\"merhaba\" yaz ve 'Sohbeti bul'a bas. Botuna \"durum\", \"dur\", \"başla\" yazınca cevap verir (yalnız bu sohbete). " +
            "Fısıltı, istek, adının geçmesi ve GM yazması uyarı sesiyle de bildirilir; hepsi günlüğe de yazılır.", 190);
        Theme.Label(notify, "NELER BİLDİRİLSİN", new Rectangle(0, 108, 300, 18), 8f, Theme.Accent, FontStyle.Bold);
        Check(notify, "Fısıltı (özel mesaj)", 0, 128, 170, _profile.NotifyWhispers, v => _profile.NotifyWhispers = v);
        Check(notify, "Takas / grup / arkadaş isteği", 176, 128, 220, _profile.NotifyRequests, v => _profile.NotifyRequests = v);
        Check(notify, "Sohbette adım ya da GM", 402, 128, 190, _profile.NotifyMentions, v => _profile.NotifyMentions = v);
        Check(notify, "Dağıtılmamış puan", 598, 128, 160, _profile.NotifyPoints, v => _profile.NotifyPoints = v);
        Check(notify, "Etkinlik başlayınca", 0, 156, 170, _profile.NotifyEvents, v => _profile.NotifyEvents = v);
        Check(notify, "Tüm etkinlik ödüllerini her zaman topla", 176, 156, 380, _profile.EventLoot, v => _profile.EventLoot = v);
        Check(notify, "Topla eşyası düşünce", 562, 156, 200, _profile.NotifyDrops, v => _profile.NotifyDrops = v);

        var keys = _tabPages[6];
        var k = _profile.Keys;
        (string, Func<Keys>, Action<Keys>)[] bindings =
        [
            ("Saldırı", () => k.Attack, v => k.Attack = v),
            ("Eşya topla", () => k.Pickup, v => k.Pickup = v),
            ("HP iksiri", () => k.HpPotion, v => k.HpPotion = v),
            ("MP iksiri", () => k.MpPotion, v => k.MpPotion = v),
            ("İleri", () => k.MoveForward, v => k.MoveForward = v),
            ("Sol", () => k.MoveLeft, v => k.MoveLeft = v),
            ("Geri", () => k.MoveBack, v => k.MoveBack = v),
            ("Sağ", () => k.MoveRight, v => k.MoveRight = v),
            ("Kamera yukarı", () => k.CameraTop, v => k.CameraTop = v),
            ("Uzaklaştır", () => k.ZoomOut, v => k.ZoomOut = v),
            ("Pencere kapat", () => k.CloseWindow, v => k.CloseWindow = v),
            ("Envanter", () => k.Inventory, v => k.Inventory = v),
        ];
        for (int i = 0; i < bindings.Length; i++)
        {
            var (caption, get, set) = bindings[i];
            int x = (i / 4) * 246, y = (i % 4) * 30;
            Theme.Label(keys, caption, new Rectangle(x, y, 120, 26), 9f, Theme.Muted);
            var picker = new KeyPicker { Bounds = new Rectangle(x + 122, y, 104, 26), Key = get() };
            picker.KeyChanged += key => { set(key); SaveProfile(); WriteLog($"{caption} tuşu: {KeyPicker.Name(key)}"); };
            keys.Controls.Add(picker);
        }
        Theme.Label(keys, "Değiştirmek için tuşa tıkla, sonra yeni tuşa bas.", new Rectangle(0, 122, 736, 22), 8.5f, Theme.Muted);

        // Gelişmiş
        var advanced = _tabPages[7];
        _dryRun = new CheckBox
        {
            Text = "Kuru çalışma (tuş göndermez)",
            Checked = false,
            Bounds = new Rectangle(0, 0, 230, 26),
            ForeColor = Theme.Text,
            BackColor = advanced.BackColor,
            Font = new Font("Segoe UI", 9f)
        };
        advanced.Controls.Add(_dryRun);
        _calibrationState = Theme.Label(advanced, "", new Rectangle(240, 0, 230, 26), 9f, Theme.Muted);
        _technical = Theme.Label(advanced, "", new Rectangle(476, 0, 260, 26), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.MiddleRight);
        _calibrationHint = Theme.Label(advanced, "", new Rectangle(0, 30, 736, 22), 8.5f, Theme.Muted);
        _record = Theme.Button(advanced, "⏺  Kaydet", new Rectangle(0, 100, 142, 30), Theme.Secondary, Theme.SecondaryHover);
        _record.Click += (_, _) => ToggleRecording();
        _replay = Theme.Button(advanced, "▶  Kaydı oynat", new Rectangle(150, 100, 142, 30), Theme.Secondary, Theme.SecondaryHover);
        _replay.Click += async (_, _) => await ReplayAsync();
        var reports = Theme.Button(advanced, "📊  Oturum raporları", new Rectangle(0, 60, 180, 30), Theme.Secondary, Theme.SecondaryHover);
        reports.Click += (_, _) =>
        {
            if (!File.Exists(SessionLog.FilePath))
            {
                WriteLog("Henüz oturum raporu yok: bot en az 1 dakika çalışıp durunca ilk satır yazılır.");
                return;
            }
            try { Process.Start(new ProcessStartInfo(SessionLog.FilePath) { UseShellExecute = true }); }
            catch (Exception ex) { WriteLog("Rapor açılamadı: " + ex.Message); }
        };
        var openFolder = Theme.Button(advanced, "📁  Ayar klasörü", new Rectangle(300, 100, 142, 30), Theme.Secondary, Theme.SecondaryHover);
        openFolder.Click += (_, _) => OpenProfileFolder();
        // The mobs the bot sees in memory, listed in the log.
        _scanMobs = Theme.Button(advanced, "🔍  Mobları göster", new Rectangle(450, 100, 180, 30), Theme.Secondary, Theme.SecondaryHover);
        _scanMobs.Click += (_, _) =>
        {
            if (_engine == null) { WriteLog("Önce oyun açık olmalı."); return; }
            WriteLog(_engine.DescribeMobs());
        };

        // ── Log ──────────────────────────────────────────────────
        var logCard = new Card { Bounds = new Rectangle(202, 544, 966, 204) };
        Controls.Add(logCard);
        Theme.Label(logCard, "GÜNLÜK", new Rectangle(16, 8, 200, 20), 8f, Theme.Accent, FontStyle.Bold);
        _log = new RichTextBox
        {
            Bounds = new Rectangle(14, 32, 938, 160),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false,
            BackColor = Theme.Inset,
            ForeColor = Theme.Muted,
            Font = new Font("Consolas", 8.5f),
            TabStop = false
        };
        logCard.Controls.Add(_log);
        SelectTab(-1);

        // ── Timers & hotkeys ─────────────────────────────────────
        _scanTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _scanTimer.Tick += (_, _) => { if (_engine == null) RefreshWindows(); };
        _previewTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _previewTimer.Tick += (_, _) => RenderPreview();
        _statsTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _statsTimer.Tick += (_, _) => UpdateStats();
        _updateTimer = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromHours(2).TotalMilliseconds };
        _updateTimer.Tick += async (_, _) => { if (Instance.IsFirst) await CheckForUpdatesAsync(startup: false); };

        // A second SekoBot (another game) uses Ctrl+F10 / Ctrl+F11; F12 is reserved by Windows for debuggers.
        bool control = !Instance.IsFirst;
        if (!_hotkeys.Register(Keys.F10, () => BeginInvoke(new Action(ToggleBot)), control))
            WriteLog($"{StartKey} kısayolu başka bir uygulamada kullanılıyor.");
        if (!_hotkeys.Register(Keys.F11, () => BeginInvoke(new Action(() => StopBot($"{StopKey} acil durdurma", byUser: true))), control))
            WriteLog($"{StopKey} kısayolu başka bir uygulamada kullanılıyor; Durdur düğmesini kullan.");
        if (!Instance.IsFirst)
            WriteLog($"Bu {Instance.Number}. SekoBot: kendi ayarlarını kullanır, kısayollar {StartKey} / {StopKey}. " +
                     "Telegram komutları ve güncelleme 1. SekoBot'ta.");

        Shown += async (_, _) =>
        {
            WriteLog($"SekoBot {BuildInfo.Version} başlatıldı.");
            RefreshWindows();
            _scanTimer.Start();
            _statsTimer.Start();
            _updateTimer.Start();
            if (Instance.IsFirst)
            {
                _ = TelegramCommandsAsync(_telegramCts.Token);
                await CheckForUpdatesAsync(startup: true);
            }
        };
        FormClosing += (_, _) =>
        {
            foreach (var t in new[] { _scanTimer, _previewTimer, _statsTimer, _updateTimer }) t.Stop();
            _telegramCts.Cancel();
            _engine?.Dispose();
            _engine = null;
            _hotkeys.Dispose();
            _preview.Image = null;
            _frameBitmap?.Dispose();
        };

        ResumeLayout(false);
        UpdateControls();
        UpdateStats();
    }

    // ── Layout helpers ───────────────────────────────────────────

    private static Label Tile(Control parent, string caption, int x, int y, bool small = false)
    {
        var tile = new Panel { Bounds = new Rectangle(x, y, 100, 60), BackColor = Theme.Inset };
        parent.Controls.Add(tile);
        Theme.Label(tile, caption, new Rectangle(8, 6, 90, 16), 7f, Theme.Muted, FontStyle.Bold);
        return Theme.Label(tile, "—", new Rectangle(8, 22, 90, 34), small ? 8.5f : 13f, Theme.Text, FontStyle.Bold);
    }

    private static Meter AddMeter(Control parent, string caption, Color fill, int y)
    {
        var meter = new Meter { Bounds = new Rectangle(16, y, 314, 28), Caption = caption, Fill = fill };
        parent.Controls.Add(meter);
        return meter;
    }

    private void NumberRow(Control parent, string caption, int x, int y, decimal min, decimal max, double value,
        int decimals, decimal step, Action<double> apply)
    {
        Theme.Label(parent, caption, new Rectangle(x, y, 190, 26), 9f, Theme.Muted);
        var number = Theme.Number(parent, new Rectangle(x + 196, y, 90, 26), min, max,
            Math.Clamp((decimal)value, min, max), decimals, step);
        number.ValueChanged += (_, _) => { apply((double)number.Value); SaveProfile(); };
    }

    private void Check(Control parent, string text, int x, int y, int width, bool value, Action<bool> apply)
    {
        var box = new CheckBox
        {
            Text = text,
            Checked = value,
            Bounds = new Rectangle(x, y, width, 26),
            ForeColor = Theme.Text,
            BackColor = parent.BackColor,
            Font = new Font("Segoe UI", 9f)
        };
        box.CheckedChanged += (_, _) => { apply(box.Checked); SaveProfile(); };
        parent.Controls.Add(box);
    }

    private TextBox TextRow(Control parent, string caption, int x, int y, string value, Action<string> apply, int width = 220)
    {
        Theme.Label(parent, caption, new Rectangle(x, y, 130, 26), 9f, Theme.Muted);
        var box = new TextBox
        {
            Bounds = new Rectangle(x + 134, y, width, 26),
            Text = value,
            BackColor = Theme.Inset,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f),
        };
        box.TextChanged += (_, _) => { apply(box.Text); SaveProfile(); };
        parent.Controls.Add(box);
        return box;
    }

    private static void Hint(Control parent, string text, int y) =>
        Theme.Label(parent, text, new Rectangle(0, y, 918, 300 - y), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

    /// <summary>A menu entry on the left: text on the left, the selected one coloured.</summary>
    private static Button SideButton(Control parent, string text, int y)
    {
        var button = Theme.Button(parent, text, new Rectangle(10, y, 160, 36), Theme.Surface, Theme.Secondary);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(8, 0, 0, 0);
        button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        return button;
    }

    /// <summary>-1: the dashboard (preview, stats); otherwise a settings page.</summary>
    private void SelectTab(int index)
    {
        _dashboard.Visible = index < 0;
        _settingsCard.Visible = index >= 0;
        if (index >= 0)
        {
            _pageTitle.Text = _tabButtons[index].Text.Split("   ", 2)[^1];
            _pageInfo.Text = PageInfo[index];
        }
        Mark(_dashButton, index < 0);
        for (int i = 0; i < _tabPages.Length; i++)
        {
            _tabPages[i].Visible = i == index;
            Mark(_tabButtons[i], i == index);
        }

        static void Mark(Button button, bool on)
        {
            button.BackColor = on ? Theme.Primary : Theme.Surface;
            button.ForeColor = on ? Theme.Text : Theme.Muted;
            button.FlatAppearance.MouseOverBackColor = on ? Theme.PrimaryHover : Theme.Secondary;
        }
    }

    // ── Windows ──────────────────────────────────────────────────

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        Win32.ReleaseCapture();
        Win32.SendMessage(Handle, 0x00A1, 2, 0);
    }

    /// <summary>Game windows first; every other window only as a fallback.</summary>
    private void ShowGameMenu()
    {
        _gameMenu.Items.Clear();
        if (_engine != null)
        {
            _gameMenu.Items.Add("Bağlantıyı kes", null, (_, _) => Disconnect("Bağlantı kapatıldı."));
        }
        else
        {
            RefreshWindows();
            foreach (var w in _games)
            {
                var window = w;
                _gameMenu.Items.Add($"★  {w.Title}   ({w.Width}×{w.Height}{(w.IsMinimized ? ", küçültülmüş" : "")})",
                    null, (_, _) => Connect(window));
            }
            if (_games.Count == 0)
                _gameMenu.Items.Add(new ToolStripMenuItem($"{_profile.GameWindowTitle} açık değil") { Enabled = false });
            if (_others.Count > 0)
            {
                var more = new ToolStripMenuItem("Diğer pencereler");
                foreach (var w in _others)
                {
                    var window = w;
                    more.DropDownItems.Add(w.Title, null, (_, _) => Connect(window));
                }
                _gameMenu.Items.Add(new ToolStripSeparator());
                _gameMenu.Items.Add(more);
            }
        }
        _gameMenu.Show(_gameButton, new Point(0, _gameButton.Height));
    }

    private void RefreshWindows()
    {
        if (_engine != null)
            return;
        try
        {
            var all = WindowService.GetOpenWindows();
            _games = all.Where(w => w.IsMetin2).ToList();
            _others = all.Where(w => !w.IsMetin2).ToList();
            var ready = _games.Where(w => !w.IsMinimized).ToList();
            string waiting = _games.Count switch
            {
                0 => $"{_profile.GameWindowTitle} bekleniyor.\nOyun açılınca kendiliğinden bağlanır.",
                _ when ready.Count == 0 => $"{_profile.GameWindowTitle} simge durumunda.\nOyunu aç, kendiliğinden bağlanır.",
                1 => $"{_profile.GameWindowTitle} bulundu, bağlanılıyor…",
                _ => $"{_games.Count} oyun penceresi açık.\nSağ üstten 'Oyunu seç' ile birini seç."
            };
            _preview.Placeholder = waiting;
            _preview.Invalidate();

            // Each SekoBot takes a game no other SekoBot is connected to (the oldest one first).
            var free = ready.Where(w => !Instance.GameTaken(w.ProcessId)).OrderBy(w => GameStarted(w.ProcessId)).ToList();
            if (!_autoAttached && free.Count > 0 && (free.Count == 1 || _games.Count > 1))
            {
                _autoAttached = true;
                BeginInvoke(new Action(() => Connect(free[0])));
            }
        }
        catch (Exception ex)
        {
            WriteLog("Pencere tarama hatası: " + ex.Message);
        }
        UpdateControls();
    }

    private void Connect(WindowInfo window)
    {
        if (_engine != null || _updating)
            return;
        if (!WindowService.IsAlive(window, out string error))
        {
            WriteLog(error);
            RefreshWindows();
            return;
        }
        try
        {
            var engine = new BotEngine(window, _profile, message => PostLog(message));
            engine.CaptureFailed += message => PostUi(() =>
            {
                if (_engine != engine)
                    return;
                // The game closed: connect again by itself when it is back. (Not when capture
                // failed on a live window, or it would reconnect and fail in a loop.)
                if (!WindowService.IsAlive(engine.Window, out _))
                    _autoAttached = false;
                Disconnect(message);
            });
            engine.AreaScanFinished += _ => PostUi(() => { SaveProfile(); UpdateControls(); UpdateStats(); });
            engine.BotStopped += reason => PostUi(() => { _stopReason = reason; UpdateControls(); UpdateStats(); });
            _engine = engine;
            Instance.TakeGame(window.ProcessId);
            engine.Start(previewFps: 30);
            _frameSequence = 0;
            _gameMinimized = false;
            _autoAttached = true;
            _previewTimer.Start();
            SetStatus($"{window.Title}'e bağlı", Theme.Green);
            WriteLog($"Bağlandı: {window.Title} (PID {window.ProcessId}, {engine.Stats.CaptureName}).");
        }
        catch (Exception ex)
        {
            _engine?.Dispose();
            _engine = null;
            SetStatus("Bağlantı hatası", Theme.Red);
            WriteLog("Bağlanılamadı: " + ex.Message);
        }
        UpdateControls();
        UpdateStats();
    }

    private void Disconnect(string reason)
    {
        if (_engine == null)
            return;
        _previewTimer.Stop();
        _engine.Dispose();
        _engine = null;
        Instance.LeaveGame();
        _preview.Image = null;
        _preview.State = null;
        _preview.Overlay = "";
        _preview.Invalidate();
        SetStatus("Bağlı değil", Theme.Muted, filled: false);
        WriteLog(reason);
        RefreshWindows();
        UpdateStats();
    }

    // ── Bot ──────────────────────────────────────────────────────

    private void ToggleBot()
    {
        if (_engine == null)
        {
            WriteLog("Önce oyun açık olmalı; açılınca kendiliğinden bağlanır.");
            return;
        }
        if (_engine.BotRunning)
        {
            StopBot("duraklatıldı", byUser: true);
            return;
        }
        // Input always goes straight to the game window, so the game may stay in the background.
        if (!_engine.StartBot(_dryRun.Checked, background: true, out string error))
        {
            WriteLog(error);
            _stopReason = "başlatılamadı: " + error;
            UpdateStats();
            return;
        }
        _stopReason = null;
        UpdateControls();
        UpdateStats();
    }

    /// <param name="byUser">F10/F11/button: a pending update may install now. Automatic stops
    /// (minimized game) keep the window as is so the user can see why it stopped.</param>
    private void StopBot(string reason, bool byUser = false)
    {
        if (_engine?.BotRunning != true)
            return;
        _engine.StopBot(reason);
        _stopReason = reason;
        UpdateControls();
        UpdateStats();
        if (byUser && _pendingUpdate is PreparedUpdate pending)
            _ = ApplyUpdateAsync(pending);
    }

    // ── Farm area ──

    private void ToggleAreaScan()
    {
        if (_engine == null)
        {
            WriteLog("Önce oyun açık olmalı.");
            return;
        }
        if (_engine.ScanningArea)
        {
            _engine.StopAreaScan();
            return;
        }
        if (!_engine.StartAreaScan(out string error))
            WriteLog(error);
        UpdateControls();
        UpdateStats();
    }

    // ── Recording / replay ───────────────────────────────────────

    private void ToggleRecording()
    {
        if (_engine == null)
            return;
        try
        {
            if (_engine.IsRecording)
                _engine.StopRecording();
            else
                _engine.StartRecording();
        }
        catch (Exception ex)
        {
            WriteLog("Kayıt başlatılamadı: " + ex.Message);
        }
        UpdateControls();
    }

    /// <summary>Runs a recording through the current calibration and bot logic, then shows the report.</summary>
    private async Task ReplayAsync()
    {
        if (_replaying)
            return;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Oynatılacak kayıt klasörünü seç (içinde ticks.jsonl olan)",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(RecordingFormat.Root) ? RecordingFormat.Root : ProfileStore.Directory
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        string folder = dialog.SelectedPath;
        // The replay runs on a worker; give it a snapshot so UI edits can't race with it.
        var profile = System.Text.Json.JsonSerializer.Deserialize<BotProfile>(
            System.Text.Json.JsonSerializer.Serialize(_profile, RecordingFormat.Json), RecordingFormat.Json)!;
        _replaying = true;
        UpdateControls();
        WriteLog("Kayıt oynatılıyor: " + folder);
        try
        {
            string report = await Task.Run(() => Replayer.Report(SessionRecording.Load(folder), profile));
            string reportPath = Path.Combine(folder, "replay-report.txt");
            await File.WriteAllTextAsync(reportPath, report);
            foreach (var line in report.Split(Environment.NewLine).Skip(3).Take(5))
                if (line.Length > 0) WriteLog(line);
            WriteLog("Rapor: " + reportPath);
            Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WriteLog("Kayıt oynatılamadı: " + ex.Message);
        }
        finally
        {
            _replaying = false;
            if (!IsDisposed) UpdateControls();
        }
    }

    private void SaveProfile()
    {
        try { ProfileStore.Save(_profile); }
        catch (Exception ex) { WriteLog("Profil kaydedilemedi: " + ex.Message); }
    }

    private void OpenProfileFolder()
    {
        try
        {
            SaveProfile();
            Process.Start(new ProcessStartInfo("explorer.exe", ProfileStore.Directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WriteLog("Klasör açılamadı: " + ex.Message);
        }
    }

    // ── Rendering ────────────────────────────────────────────────

    private void RenderPreview()
    {
        if (_engine == null)
            return;
        bool minimized = _engine.GameMinimized;
        if (minimized != _gameMinimized)
        {
            _gameMinimized = minimized;
            if (minimized)
            {
                StopBot("oyun simge durumuna alındı");
                WriteLog("Oyun simge durumunda: Windows küçültülmüş pencereyi çizmez, görüntü alınamaz.");
            }
            _preview.Placeholder = minimized
                ? "Oyun simge durumunda\n\nWindows küçültülmüş pencereyi çizmediği için görüntü yok.\nOyunu geri açmak için buraya tıkla."
                : "Oyun penceresi bekleniyor";
            _preview.Image = minimized ? null : _frameBitmap;
            _preview.State = null;
            _preview.Invalidate();
        }
        if (minimized)
            return;
        if (_engine.TryCopyLatest(ref _frameBitmap, ref _frameSequence))
        {
            _preview.Image = _frameBitmap;
            _preview.State = _engine.State;
            _preview.Overlay = _engine.IsRecording ? "⏺ KAYIT" : "";
            _preview.Invalidate();
        }
    }

    private void UpdateStats()
    {
        string name = _engine?.CharacterName ?? "";
        string identity = "v" + BuildInfo.Version + (Instance.IsFirst ? "" : $"  ·  {Instance.Number}. SekoBot") + (name.Length > 0 ? $"  ·  {name}" : "");
        if (_identity.Text != identity)
        {
            _identity.Text = identity;
            Text = "SekoBot" + (Instance.IsFirst ? "" : $" {Instance.Number}") + (name.Length > 0 ? $" — {name}" : "");
        }
        _calibrationState.Text = _engine?.World != null ? "Her şey bellekten ✓" : "Bellek bekleniyor";
        _calibrationState.ForeColor = _engine?.World != null ? Theme.Green : Theme.Muted;
        _events.Text = _engine?.EventsSummary() ?? "Bot çalışınca görünür.";
        _events.ForeColor = _events.Text.StartsWith('▶') ? Theme.Green : Theme.Muted;
        if (_bagState.Visible)
        {
            _bagState.Text = _engine?.BagSummary() ?? "Çanta: oyun bağlı değil.";
            _bagState.ForeColor = _bagState.Text.Contains("SIĞMIYOR") ? Theme.Yellow : Theme.Text;
        }

        if (_engine == null)
        {
            foreach (var l in new[] { _statTime, _statMobs, _statRate, _statDeaths, _statPotions, _statExp, _statExpRate, _statLevel, _statYang })
                l.Text = "—";
            _hpMeter.Value = _mpMeter.Value = _targetMeter.Value = null;
            _technical.Text = "";
            _positionState.Text = "";
            SetLine(_updating ? "Güncelleniyor…" : "Oyun bekleniyor", Theme.Muted);
            return;
        }
        var s = _engine.Stats;
        var state = _engine.State;
        _technical.Text = $"{_engine.StatsSource} · {s.CaptureFps:0} FPS";
        var world = _engine.World;
        _skillList.Text = state?.Skills is { Count: > 0 } skills
            ? string.Join("   ", skills.Select(sk => $"{KeyPicker.Name(sk.Key)} {sk.Name} ({sk.Kind switch
              {
                  SkillKind.Buff => "güçlendirme",
                  SkillKind.Area => "alan",
                  _ => "tek hedef",
              }})"))
            : "Hızlı slotlardaki beceriler oyuna bağlanınca bellekten okunur.";
        _positionState.Text = world != null
            ? $"📍 Konum bellekten ✓  X={world.Player.X:0}, Y={world.Player.Y:0}  (harita {world.MapName}, {world.Mobs.Count} mob, {world.OtherPlayers} oyuncu" +
              (_engine.FreeInventorySlots is int free ? $", envanterde {free} boş yer)" : ")")
            : "📍 " + _engine.MemoryStatus;
        if (world?.Drops is { Count: > 0 } ground)
        {
            var near = ground.Where(d => d.Vnum > 0 && d.Position.DistanceXY(world.Player) <= 1000)
                .GroupBy(d => d.Vnum).Select(g => g.Key == 1 ? $"{g.Sum(d => d.Count)} yang" : ItemBook.NameOf(g.Key) + (g.Count() > 1 ? $" ×{g.Count()}" : ""))
                .Take(4).ToList();
            if (near.Count > 0)
                _positionState.Text += "  ·  Yerde: " + string.Join(", ", near);
        }
        _positionState.ForeColor = world != null ? Theme.Green : Theme.Muted;
        string respawn = _engine.RespawnSummary();
        _respawnState.Text = "Doğma süreleri: " + (respawn.Length > 0 ? respawn : "ölçülüyor (bot çalışırken)") +
                             " · 0 = kapalı";
        _farmState.Text = _profile.FarmSpot is { } farm
            ? $"Kayıtlı alan ({farm.MapName}, {farm.FoundAt:dd.MM HH:mm}): {farm.Mobs} mob — {farm.Names}" +
              (world != null && world.MapName == farm.MapName ? $" · {farm.Position.DistanceXY(world.Player) / 100:0} m uzakta" : "")
            : "Kayıtlı alan yok. \"En iyi alanı bul\": karakter ~1 dk çevrede dolaşıp en verimli yeri bulur.";
        string scanText = _engine.ScanningArea ? "■  Taramayı durdur" : _profile.FarmSpot != null ? "🔄  Alanı güncelle" : "📍  En iyi alanı bul";
        if (_findArea.Text != scanText)
            _findArea.Text = scanText;
        _hpMeter.Value = state?.Hp;
        _mpMeter.Value = state?.Mp;
        _targetMeter.Value = state?.TargetHp;

        // Level and potions are shown also while the bot doesn't run.
        if (world is { Progress: { } progress })
        {
            double percent = 100.0 * progress.Exp / Math.Max(1, progress.Exp + progress.NeedExp);
            string eta = s.Session?.ToLevel is TimeSpan t ? "\n~" + Duration(t) : "";
            _statLevel.Text = $"Sv {world.Level} · %{percent:0.0}{eta}";
        }
        var (hpLeft, mpLeft) = _engine.PotionsLeft();
        int hpUsed = s.Session?.HpPotionsUsed ?? 0, mpUsed = s.Session?.MpPotionsUsed ?? 0;
        _statPotions.Text = $"HP {Count(hpLeft)} · {hpUsed}\nMP {Count(mpLeft)} · {mpUsed}";
        _statPotions.ForeColor = hpLeft is < 20 || mpLeft is < 10 ? Theme.Red : Theme.Text;

        if (s.Session is { } session)
        {
            double hours = s.BotUptime.TotalHours;
            _statMobs.Text = session.Kills.ToString("N0");
            _statRate.Text = s.BotUptime.TotalMinutes >= 1 ? (session.Kills / hours).ToString("N0") : "—";
            _statExp.Text = Short(session.Exp) + (session.LevelsGained > 0 ? $" (+{session.LevelsGained} sv)" : "");
            _statExpRate.Text = s.BotUptime.TotalMinutes >= 1 ? Short((long)(session.Exp / hours)) : "—";
            _statYang.Text = Short(session.Yang);
        }

        if (s.BotRunning)
        {
            _statTime.Text = s.BotUptime.ToString(@"hh\:mm\:ss");
            _statDeaths.Text = s.Deaths.ToString("N0");
            _statDeaths.ForeColor = s.Deaths > 0 ? Theme.Red : Theme.Text;
            string line = (s.DryRun ? "KURU ÇALIŞMA · " : "") + s.Phase.ToTurkish() + "  ·  " + s.Area;
            if (!_engine.StatsOk)
                // Without HP/MP the bot can't drink potions: make that impossible to miss.
                SetLine("⚠ HP/MP okunamıyor, iksir içilmiyor  ·  " + s.Phase.ToTurkish(), Theme.Red);
            else
                SetLine(line, s.DryRun ? Theme.Yellow : s.Phase == BotPhase.Dead ? Theme.Red : Theme.Green);
        }
        else
        {
            SetLine(_stopReason is string reason
                        ? $"Durdu ({reason}). Devam etmek için BAŞLAT ya da {StartKey}."
                        : $"Hazır. Başlatmak için BAŞLAT ya da {StartKey}.",
                    _stopReason != null ? Theme.Yellow : Theme.Text);
        }
    }

    // ── Telegram commands ──

    private readonly CancellationTokenSource _telegramCts = new();

    /// <summary>Reads what the user writes to their Telegram bot and answers (runs on the UI thread
    /// between waits): "durum" the numbers, "dur" stops, "başla" starts. Only the saved chat counts;
    /// messages written while SekoBot was closed are skipped.</summary>
    private async Task TelegramCommandsAsync(CancellationToken cancel)
    {
        long offset = -1;
        string? lastError = null;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                string token = _profile.TelegramToken, chat = _profile.TelegramChatId.Trim();
                if (!_profile.TelegramEnabled || !_profile.TelegramCommands || token.Length == 0 || chat.Length == 0)
                {
                    offset = -1;
                    await Task.Delay(5000, cancel);
                    continue;
                }
                bool skipOld = offset < 0;
                var (messages, error) = await TelegramNotifier.ReadAsync(token, Math.Max(0, offset), skipOld ? 0 : 25, cancel);
                if (error != null)
                {
                    if (error != lastError)
                        WriteLog(error);
                    lastError = error;
                    await Task.Delay(15000, cancel);
                    continue;
                }
                lastError = null;
                if (skipOld)
                    offset = 0;
                foreach (var (id, from, text, replyTo) in messages)
                {
                    offset = Math.Max(offset, id + 1);
                    if (skipOld || from != chat || text.Length == 0)
                        continue;
                    string reply = TelegramMessage(text, replyTo);
                    if (await TelegramNotifier.SendAsync(token, chat, reply) is string sendError)
                        WriteLog(sendError);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// An answer to a whisper notice ("Yanıtla" on "💬 Fısıltı — Ali …") is sent to that player in the
    /// game; "yaz Ali merhaba" too. Anything else is a command (durum / dur / başla).
    /// </summary>
    private string TelegramMessage(string text, string replyTo)
    {
        if (WhisperSender.WhisperFrom(replyTo) is string player)
            return _engine?.QueueWhisper(player, text.Trim()) ?? "Oyun bağlı değil.";
        string command = TelegramNotifier.Command(text);
        if (command is "yaz" or "cevap")
        {
            string rest = text.Trim().TrimStart('/');
            rest = rest[(rest.IndexOf(' ') is int space and > 0 ? space + 1 : rest.Length)..];
            return WhisperSender.Parse(rest) is (string to, string message)
                ? _engine?.QueueWhisper(to, message) ?? "Oyun bağlı değil."
                : "Kullanım: yaz İsim mesaj  (ya da fısıltı bildirimine \"Yanıtla\" ile cevap yaz)";
        }
        return TelegramCommand(command);
    }

    private string TelegramCommand(string command)
    {
        switch (command)
        {
            case "durum" or "status":
                return TelegramStatus();
            case "dur" or "stop":
                if (_engine?.BotRunning != true)
                    return "Bot zaten duruyor.";
                StopBot("Telegram'dan durduruldu", byUser: true);
                WriteLog("Telegram'dan durduruldu.");
                return "⏹ Bot durduruldu.\n" + TelegramStatus();
            case "basla" or "start":
                if (_engine?.BotRunning == true)
                    return "Bot zaten çalışıyor.";
                ToggleBot();
                if (_engine?.BotRunning == true)
                {
                    WriteLog("Telegram'dan başlatıldı.");
                    return "▶ Bot başladı.";
                }
                return "Başlatılamadı: " + (_stopReason ?? (_engine == null ? "oyun açık değil" : "bilinmiyor"));
            default:
                return "Komutlar: durum · dur · başla · yaz İsim mesaj\nFısıltıya cevap: bildirime \"Yanıtla\" ile yaz.";
        }
    }

    private string TelegramStatus()
    {
        if (_engine == null)
            return "SekoBot açık; oyun bağlı değil.";
        var s = _engine.Stats;
        var world = _engine.World;
        var lines = new List<string> { _engine.BotRunning ? "▶ Bot çalışıyor" : "⏸ Bot duruyor" + (_stopReason != null ? $" ({_stopReason})" : "") };
        if (world is { Progress: { } progress })
            lines.Add($"Sv {world.Level} · %{100.0 * progress.Exp / Math.Max(1, progress.Exp + progress.NeedExp):0.0}" +
                      (s.Session?.ToLevel is TimeSpan t ? $" (seviyeye ~{Duration(t)})" : ""));
        if (world?.Stats is { } stats)
            lines.Add($"Can %{stats.HpFraction * 100:0} · MP %{stats.MpFraction * 100:0}");
        if (s.Session is { } session && s.BotUptime.TotalMinutes >= 1)
        {
            double hours = s.BotUptime.TotalHours;
            lines.Add($"{Duration(s.BotUptime)}: {session.Kills:N0} mob, {Short(session.Exp)} EXP (saatte {Short((long)(session.Exp / hours))}), yang {Short(session.Yang)}");
        }
        var (hp, mp) = _engine.PotionsLeft();
        lines.Add($"İksir: HP {Count(hp)} · MP {Count(mp)}");
        if (world != null)
            lines.Add($"Harita {world.MapName}, yakında {world.OtherPlayers} oyuncu");
        return string.Join("\n", lines);
    }

    /// <summary>Big numbers short: 1.234 · 12,3 B · 1,23 Mn.</summary>
    private static string Short(long value) => Math.Abs(value) switch
    {
        < 100_000 => value.ToString("N0"),
        < 10_000_000 => (value / 1000.0).ToString("0.#") + " B",
        _ => (value / 1_000_000.0).ToString("0.##") + " Mn",
    };

    private static string Count(int? value) => value?.ToString("N0") ?? "?";

    private static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} sa {t.Minutes} dk" : $"{Math.Max(1, t.Minutes)} dk";

    private void SetLine(string text, Color color)
    {
        if (_status.Text != text) _status.Text = text;
        _status.ForeColor = color;
    }

    private void UpdateControls()
    {
        bool connected = _engine != null;
        bool running = _engine?.BotRunning == true;
        _gameButton.Text = connected ? "Bağlantı  ▾" : "Oyunu seç  ▾";
        _startBot.Enabled = connected && _engine?.ScanningArea != true;
        _findArea.Enabled = connected && !running;
        _scanMobs.Enabled = connected;
        _startBot.Text = running ? $"❚❚  DURAKLAT   {StartKey}" : $"▶  BAŞLAT   {StartKey}";
        _stopBot.Enabled = running;
        bool recording = _engine?.IsRecording == true;
        _record.Enabled = connected;
        _record.Text = recording ? "■  Kaydı bitir" : "⏺  Kaydet";
        _record.BackColor = recording ? Theme.Danger : Theme.Secondary;
        _record.FlatAppearance.MouseOverBackColor = recording ? Theme.DangerHover : Theme.SecondaryHover;
        _replay.Enabled = !_replaying;
        _dryRun.Enabled = !running;
        _calibrationHint.Text = !connected
            ? "HP/MP, konum, moblar ve hedef oyunun belleğinden okunur (SekoBot'u yönetici olarak aç)."
            : _engine!.MemoryStatus;
    }

    // ── Update ───────────────────────────────────────────────────

    private async Task CheckForUpdatesAsync(bool startup)
    {
        if (_updating || (!startup && _engine?.BotRunning == true))
            return;
        if (_pendingUpdate is PreparedUpdate ready && File.Exists(ready.FilePath))
        {
            await ApplyUpdateAsync(ready);
            return;
        }
        if (startup)
            WriteLog("Güncelleme kontrol ediliyor…");
        var result = await UpdateService.CheckAndDownloadAsync();
        if (IsDisposed)
            return;
        if (!string.IsNullOrEmpty(result.Error))
        {
            WriteLog(result.Error);
            return;
        }
        if (result.Update is not PreparedUpdate update)
        {
            if (startup) WriteLog($"SekoBot güncel (v{BuildInfo.Version}).");
            return;
        }
        await ApplyUpdateAsync(update);
    }

    private async Task ApplyUpdateAsync(PreparedUpdate update)
    {
        if (_updating || IsDisposed)
            return;
        // Never interrupt a running farm session; install when the user stops the bot.
        if (_engine?.BotRunning == true)
        {
            if (_pendingUpdate == null)
                WriteLog($"Yeni sürüm {update.Version} hazır; botu durdurduğunda (F10/F11) yüklenecek.");
            _pendingUpdate = update;
            return;
        }
        _updating = true;
        SetStatus("Güncelleniyor", Theme.Yellow);
        WriteLog($"Yeni sürüm {update.Version} doğrulandı. SekoBot yeniden başlatılıyor…");
        await Task.Delay(1200);
        if (IsDisposed)
            return;
        if (_engine?.BotRunning == true)
        {
            // The user restarted the bot during the countdown.
            _updating = false;
            _pendingUpdate = update;
            WriteLog("Bot yeniden başladı; güncelleme bot durunca yüklenecek.");
            return;
        }
        try
        {
            _engine?.Dispose();
            _engine = null;
            UpdateService.InstallAfterExit(update);
            _pendingUpdate = null;
            Close();
        }
        catch (Exception ex)
        {
            _updating = false;
            _pendingUpdate = null;
            SetStatus("Bağlı değil", Theme.Muted, filled: false);
            _autoAttached = false;
            WriteLog("Güncelleme uygulanamadı: " + ex.Message);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>The connection text in the header.</summary>
    private void SetStatus(string text, Color color, bool filled = true)
    {
        _connection.Text = (filled ? "●  " : "○  ") + text;
        _connection.ForeColor = color;
    }


    private void PostUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    private void PostLog(string message) => PostUi(() => WriteLog(message));

    private void WriteLog(string message)
    {
        if (IsDisposed)
            return;
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        _log.AppendText(line);
        LogFile.Append(line);
        if (_log.TextLength > 20000)
            _log.Text = _log.Text[^14000..];
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}
