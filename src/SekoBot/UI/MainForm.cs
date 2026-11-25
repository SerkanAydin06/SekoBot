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
internal sealed partial class MainForm : Form
{
    private const int CsDropShadow = 0x00020000;

    private readonly BotProfile _profile = ProfileStore.Load();
    private readonly Button _gameButton, _record, _replay;
    private readonly ProButton _startBot, _stopBot, _attackButton;
    private readonly ContextMenuStrip _gameMenu = new();
    private readonly CheckBox _dryRun;
    private readonly PreviewControl _preview;
    private readonly Label _connection, _status, _calibrationHint, _calibrationState, _technical;
    private readonly Label _statTime, _statMobs, _statRate, _statDeaths, _statPotions, _statExp, _statExpRate, _statLevel, _statYang;
    private readonly Label _positionState, _skillList, _farmState, _respawnState, _marketState;
    private readonly Button _findArea, _scanMarket;
    private readonly Button _scanMobs;
    private readonly Meter _hpMeter, _mpMeter, _targetMeter;
    private readonly NavButton[] _tabButtons;
    private readonly Panel[] _tabPages;
    private readonly NavButton _dashButton;
    private readonly Panel _dashboard, _dashBottom;
    private readonly ListView _runItems;
    private readonly Label _runItemsInfo;
    private int _runItemsVersion = -1;
    private readonly Card _settingsCard;
    private readonly Label _pageTitle, _pageInfo, _bagState, _events, _identity;

    // ── Modes: what this SekoBot does (farm, market, fishing), one tab each at the top ──
    private const int FarmMode = 0, MarketMode = 1, FishingMode = 2;
    private int _mode;
    private readonly NavButton[] _modeButtons;
    private readonly Panel _side;
    // ── The new panel: the card at the top (who, what, how long, start / stop), the numbers, what just happened ──
    private readonly Card _hero, _previewCard;
    private readonly Avatar _avatar;
    private readonly Label _heroName, _timer, _timerNote;
    private readonly StatCard _kExp, _kKills, _kYang, _kPotion, _kLevel;
    private readonly Feed _feed;
    private readonly IconButton _moreButton;
    private readonly NavButton[] _logFilters;
    private Rectangle _attackInPreview;
    private readonly Panel _marketDash, _fishingDash;
    private readonly PullChart _fChart;
    private readonly Card _mCard;
    private readonly Card _logCard, _itemsCard;
    private Rectangle _dashBottomFarm, _logCardFarm, _logFarm;
    private readonly Label _mState, _fState;
    private readonly StatCard _mRound, _mRead, _mDeals, _mWanted, _mShops, _mItems, _mOldest, _mNever;
    private readonly StatCard _fTime, _fCasts, _fPulled, _fHooked, _fMissed;
    private readonly ListBox _fPulls;
    private readonly ListBox _mFinds;
    private DateTime _marketDashAt;

    private static string StartKey => Instance.IsFirst ? "F10" : "Ctrl+F10";
    private static string StopKey => Instance.IsFirst ? $"F11 / {OwnStopName}" : OwnStopName;
    /// <summary>This SekoBot's own stop key: Ctrl+Shift+ its number.</summary>
    internal static string OwnStopName => $"Ctrl+Shift+{Math.Min(Instance.Number, 9)}";
    private static Keys OwnStopKey => Keys.D0 + Math.Min(Instance.Number, 9);

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
        "Pazardaki bütün dükkanları açıp fiyatları toplama, aranan ürünleri bulma (hiçbir şey satın alınmaz).",
    ];
    private readonly RichTextBox _log;
    private readonly System.Windows.Forms.Timer _scanTimer, _previewTimer, _statsTimer, _updateTimer;
    private readonly GlobalHotkeys _hotkeys = new();

    private BotEngine? _engine;
    private Bitmap? _frameBitmap;
    private long _frameSequence;
    private bool _autoAttached, _updating, _gameMinimized, _replaying;
    private DateTime _restoreSince = DateTime.MinValue;
    private int _restoreTries;
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
        Text = "PARDUS";
        AppIcon.Use(this);
        DarkMenu.Use(_gameMenu);
        ClientSize = new Size(1180, 920);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;
        KeyPreview = true;

        // ── Title strip: the mark, the modes, the server, the game, the window's buttons ──
        // Everything below is laid out on the old 1180×920 grid (then made 0.84 smaller); B: the body's top.
        const int B = 65;
        var header = new Panel { Bounds = new Rectangle(0, 0, 1180, 64), BackColor = Theme.Rail };
        Controls.Add(header);
        header.Controls.Add(new Panel { Bounds = new Rectangle(0, 63, 1180, 1), BackColor = Theme.Border, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom });
        var mark = new Mark { Bounds = new Rectangle(18, 16, 32, 32) };
        header.Controls.Add(mark);
        var title = Theme.Label(header, "PARDUS", new Rectangle(58, 16, 104, 32), 11.5f, Theme.Text, FontStyle.Bold);
        var modes = new Card { Bounds = new Rectangle(168, 12, 380, 40), BackColor = Theme.Inset };
        header.Controls.Add(modes);
        (string Name, string Icon)[] modeNames = [("Farm", "sword"), ("Pazar", "cart"), ("Balıkçılık", "fish")];
        _modeButtons = new NavButton[modeNames.Length];
        for (int i = 0; i < modeNames.Length; i++)
        {
            int mode = i;
            _modeButtons[i] = new NavButton { Text = modeNames[i].Name, Icon = modeNames[i].Icon, Segment = true, Bounds = new Rectangle(4 + i * 124, 4, 122, 32), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            _modeButtons[i].Click += (_, _) => SetMode(mode);
            modes.Controls.Add(_modeButtons[i]);
        }
        // The game's server and channel (market prices are kept per server); the dot: connected or not.
        _connection = new Chip { Text = "○  Oyun bekleniyor", Bounds = new Rectangle(556, 12, 228, 40), Font = new Font("Segoe UI", 9f), ForeColor = Theme.Muted };
        header.Controls.Add(_connection);
        _gameButton = new ProButton { Text = "Oyunu seç", Icon = "user", Kind = ButtonKind.Ghost, Bounds = new Rectangle(792, 12, 188, 40), Font = new Font("Segoe UI", 9f) };
        _gameButton.Click += (_, _) => ShowGameMenu();
        header.Controls.Add(_gameButton);
        var small = new IconButton { Icon = "layers", Boxed = true, Bounds = new Rectangle(988, 12, 40, 40) };
        small.Click += (_, _) => GoMini();
        header.Controls.Add(small);
        // The rest in one menu: every PARDUS at once, arranging the games, updates, the settings folder.
        var more = _moreButton = new IconButton { Icon = "more", Boxed = true, Bounds = new Rectangle(1034, 12, 40, 40) };
        var moreMenu = new ContextMenuStrip { Font = new Font("Segoe UI", 9.5f) };
        DarkMenu.Use(moreMenu);
        moreMenu.Opening += (_, _) =>
        {
            moreMenu.Items.Clear();
            foreach (ToolStripItem item in WindowLayout.AllMenu().Items.Cast<ToolStripItem>().ToList())
                moreMenu.Items.Add(item);
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add("⊞   Oyunları yan yana diz", null, (_, _) => ArrangeAll());
            moreMenu.Items.Add("▭   Mini moda geç", null, (_, _) => GoMini());
            moreMenu.Items.Add(new ToolStripSeparator());
            if (Instance.IsFirst)
                moreMenu.Items.Add("↻   Güncellemeleri denetle", null, async (_, _) => await CheckForUpdatesAsync(startup: false));
            moreMenu.Items.Add("📁   Ayar klasörü", null, (_, _) => OpenProfileFolder());
        };
        moreMenu.Opened += (_, _) => more.On = true;
        moreMenu.Closed += (_, _) => more.On = false;
        more.Click += (_, _) => moreMenu.Show(more, new Point(more.Width - moreMenu.PreferredSize.Width, more.Height + 4));
        header.Controls.Add(more);
        var minimize = new IconButton { Icon = "min", Bounds = new Rectangle(1084, 12, 40, 40) };
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        header.Controls.Add(minimize);
        var close = new IconButton { Icon = "x", Danger = true, Bounds = new Rectangle(1128, 12, 40, 40) };
        close.Click += (_, _) => Close();
        header.Controls.Add(close);
        // Moved only by the mark and the name (the user: the rest of the strip showed the move cursor but didn't move).
        foreach (Control c in new Control[] { title, mark })
        {
            c.MouseDown += DragWindow;
            c.Cursor = Cursors.SizeAll;
        }
        var bar = header;

        // ── Left menu: the panel and the settings pages; the version and the keys at the bottom ──
        var side = _side = new Panel { Bounds = new Rectangle(0, B, 192, 855), BackColor = Theme.Rail };
        Controls.Add(side);
        side.Controls.Add(new Panel { Bounds = new Rectangle(191, 0, 1, 855), BackColor = Theme.Border });
        _dashButton = new NavButton { Text = "Panel", Icon = "grid", Bounds = new Rectangle(10, 14, 172, 40), Font = new Font("Segoe UI", 9.5f) };
        _dashButton.Click += (_, _) => SelectTab(-1);
        side.Controls.Add(_dashButton);
        Theme.Label(side, "AYARLAR", new Rectangle(22, 66, 150, 20), 7.5f, Theme.Faint, FontStyle.Bold);
        (string Name, string Icon)[] tabNames = [("Saldırı", "sword"), ("Hedef", "target"), ("Eşya", "bag"), ("İksir · Beceri", "flask"), ("Alan", "pin"),
                             ("Bildirim", "bell"), ("Tuşlar", "key"), ("Gelişmiş", "cog"), ("Pazar", "cart")];
        _tabButtons = new NavButton[tabNames.Length];
        _tabPages = new Panel[tabNames.Length];
        _events = new Label { Visible = false };
        var foot = new Card { Bounds = new Rectangle(10, 855 - 12 - 96, 172, 96) };
        side.Controls.Add(foot);
        _identity = Theme.Label(foot, "v" + BuildInfo.Version, new Rectangle(12, 10, 150, 22), 8.5f, Theme.Muted, FontStyle.Bold);
        Theme.Label(foot, "Başlat · " + StartKey, new Rectangle(12, 36, 150, 20), 8f, Theme.Faint);
        Theme.Label(foot, "Durdur · " + (Instance.IsFirst ? "F11 / " : "") + OwnStopName, new Rectangle(12, 58, 150, 20), 8f, Theme.Faint);

        // ── The card at the top: who, what the bot is doing, how long, start / stop ──
        var hero = _hero = new Card { Bounds = new Rectangle(202, B + 12, 966, 84) };
        Controls.Add(hero);
        _avatar = new Avatar { Bounds = new Rectangle(18, 16, 60, 56), Font = new Font("Segoe UI", 11f) };
        hero.Controls.Add(_avatar);
        _heroName = Theme.Label(hero, "Oyun bağlı değil", new Rectangle(90, 12, 380, 30), 12.5f, Theme.Muted, FontStyle.Bold);
        _status = new Pill { Text = "Oyun bekleniyor", Bounds = new Rectangle(90, 44, 380, 30), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Theme.Muted,
                             Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
        hero.Controls.Add(_status);
        _stopBot = new ProButton { Text = "Durdur\n" + (Instance.IsFirst ? "F11" : OwnStopName), Icon = "stop", Kind = ButtonKind.Danger,
                                   Bounds = new Rectangle(966 - 18 - 156, 20, 156, 46), Font = new Font("Segoe UI", 10f), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _stopBot.Click += (_, _) => StopBot("kullanıcı durdurdu", byUser: true);
        hero.Controls.Add(_stopBot);
        _startBot = new ProButton { Text = "Başlat\n" + StartKey, Icon = "play", Kind = ButtonKind.Gold,
                                    Bounds = new Rectangle(_stopBot.Left - 12 - 186, 20, 186, 46), Font = new Font("Segoe UI", 10f), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _startBot.Click += (_, _) => ToggleBot();
        hero.Controls.Add(_startBot);
        _timer = Theme.Label(hero, "--:--", new Rectangle(_startBot.Left - 18 - 150, 14, 150, 36), 16f, Theme.Faint, FontStyle.Bold, ContentAlignment.MiddleRight);
        _timer.Font = new Font("Consolas", 16f, FontStyle.Bold);
        _timer.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _timerNote = Theme.Label(hero, "bu oturum", new Rectangle(_timer.Left, 50, 150, 20), 8f, Theme.Faint, FontStyle.Regular, ContentAlignment.MiddleRight);
        _timerNote.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _status.Width = _timer.Left - 12 - _status.Left;
        _heroName.Width = _status.Width;

        // ── The panel: the session's numbers, the live view, the character and what just happened ──
        _dashboard = new Panel { Bounds = new Rectangle(202, B + 108, 966, 511), BackColor = Theme.Background };
        Controls.Add(_dashboard);
        StatCard Stat(string caption, int x, int width)
        {
            var card = new StatCard { Caption = caption, Bounds = new Rectangle(x, 0, width, 104), Font = new Font("Segoe UI", 15f, FontStyle.Bold) };
            _dashboard.Controls.Add(card);
            return card;
        }
        int[] kx = [0, 152, 304, 456, 610];
        _kExp = Stat("SAATTE EXP", kx[0], 140);
        _kKills = Stat("ÖLDÜRÜLEN", kx[1], 140);
        _kYang = Stat("YANG", kx[2], 140);
        _kPotion = Stat("İKSİR", kx[3], 142);
        _kLevel = Stat("SEVİYE", kx[4], 356);
        // The old tiles' values are still worked out (the small bar and the reports read them); the cards show them.
        _statTime = new Label(); _statMobs = new Label(); _statRate = new Label(); _statExp = new Label(); _statExpRate = new Label();
        _statLevel = new Label(); _statYang = new Label(); _statPotions = new Label(); _statDeaths = new Label();

        var previewCard = _previewCard = new Card { Bounds = new Rectangle(0, 116, 598, 395), Caption = "CANLI GÖRÜNTÜ" };
        _dashboard.Controls.Add(previewCard);
        _preview = new PreviewControl { Bounds = new Rectangle(14, 42, 570, 339), Profile = _profile };
        _preview.SelectionChanged += UpdateControls;
        _preview.MouseClick += (_, _) => { if (_engine != null && _gameMinimized) WindowService.Restore(_engine.Window); };
        previewCard.Controls.Add(_preview);
        _attackButton = new ProButton { Bounds = new Rectangle(598 - 14 - 160, 6, 160, 30), Font = new Font("Segoe UI", 8.5f), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _attackButton.Click += (_, _) =>
        {
            if (_mode == MarketMode)
                OpenMarketWindow();
            else
                ToggleAttack();
        };
        previewCard.Controls.Add(_attackButton);
        ShowAttack();
        _reloads.Add(ShowAttack);

        var charCard = new Card { Bounds = new Rectangle(610, 116, 356, 150), Caption = "KARAKTER" };
        _dashboard.Controls.Add(charCard);
        Meter Bar(string caption, Color fill, int y)
        {
            var meter = new Meter { Bounds = new Rectangle(18, y, 320, 30), Caption = caption, Fill = fill, Font = new Font("Segoe UI", 8.5f) };
            charCard.Controls.Add(meter);
            return meter;
        }
        _hpMeter = Bar("HP", Color.FromArgb(255, 79, 96), 40);
        _mpMeter = Bar("MP", Color.FromArgb(61, 139, 255), 76);
        _targetMeter = Bar("Hedef", Theme.Gold, 112);
        var feedCard = new Card { Bounds = new Rectangle(610, 278, 356, 233), Caption = "ŞİMDİ", CaptionRight = "önemli olaylar" };
        _dashboard.Controls.Add(feedCard);
        _feed = new Feed { Bounds = new Rectangle(18, 42, 322, 180), Font = new Font("Segoe UI", 8.5f) };
        feedCard.Controls.Add(_feed);

        // ── Settings pages: one big card, title + what the page is for (under the card at the top) ──
        _settingsCard = new Card { Bounds = new Rectangle(202, B + 108, 966, 735), Visible = false };
        Controls.Add(_settingsCard);
        _pageTitle = Theme.Label(_settingsCard, "", new Rectangle(24, 12, 260, 32), 14f, Theme.Text, FontStyle.Bold);
        _pageInfo = Theme.Label(_settingsCard, "", new Rectangle(290, 16, 652, 26), 9f, Theme.Muted);
        for (int i = 0; i < tabNames.Length; i++)
        {
            int index = i;
            _tabButtons[i] = new NavButton { Text = tabNames[i].Name, Icon = tabNames[i].Icon, Bounds = new Rectangle(10, 90 + i * 44, 172, 40), Font = new Font("Segoe UI", 9.5f) };
            _tabButtons[i].Click += (_, _) => SelectTab(index);
            side.Controls.Add(_tabButtons[i]);
            _tabPages[i] = new Panel { Bounds = new Rectangle(24, 62, 918, 296), BackColor = Theme.Surface, Visible = false };
            _settingsCard.Controls.Add(_tabPages[i]);
        }
        // A thin line under the title.
        _settingsCard.Controls.Add(new Panel { Bounds = new Rectangle(24, 52, 918, 1), BackColor = Theme.Border });

        // Saldırı
        var attack = _tabPages[0];
        Check(attack, "Grupları topla (üçlüleri toplayıp birden kes)", 370, 0, 366, () => _profile.PullGroups,
            v => _profile.PullGroups = v);
        NumberRow(attack, "Toplanacak en fazla mob", 370, 34, 3, 30, () => _profile.PullMaxMobs, 0, 1,
            v => _profile.PullMaxMobs = (int)v);
        NumberRow(attack, "Toplarken can en az (%)", 370, 68, 10, 100, () => Math.Round(_profile.PullMinHp * 100), 0, 5,
            v => _profile.PullMinHp = v / 100);
        Check(attack, "Saldırı açık (kapalıyken sadece yerdeki eşyaları toplar)", 0, 34, 366, () => _profile.Attack, v =>
        {
            _profile.Attack = v;
            ShowAttack();
        });
        TextRow(attack, "Takip edilecek", 0, 68, () => _profile.FollowName, v => _profile.FollowName = v.Trim());
        NumberRow(attack, "Toplama alanı (m)", 0, 0, 3, 30, () => _profile.PullAreaMetres, 0, 1,
            v => _profile.PullAreaMetres = v);
        Hint(attack, "Gruplar: bot en değerli üçlüden başlar, toplama alanındaki diğer üçlüleri de toplar (peşinde mob varken yalnız 10 m " +
            "içindekileri); sınıra ulaşınca, alan boşalınca ya da can düşünce hepsini birden keser. Can %40'ın altına düşen bir dövüşten " +
            "sonra kendiliğinden daha az mob toplar. Mob menzildeyken yerinde vurur; yürürken yoldaki mobların etrafından dolanır.\n\n" +
            "Saldırı kapalıyken bot \"Takip edilecek\" karakterin peşinden gider ve onun kestiklerinden düşenleri toplar. Boş bırakırsan " +
            "yakındaki kendi karakterlerinden birini (PARDUS'ta açtığın) takip eder; birden fazla ad virgülle yazılabilir.", 102);

        // İksir & Buff
        // Hedef: which mobs to go for, and loot.
        var targets = _tabPages[1];
        NumberRow(targets, "En yüksek mob seviyesi", 0, 0, 0, 120, () => _profile.MaxMobLevel, 0, 1,
            v => _profile.MaxMobLevel = (int)v);
        Check(targets, "Önce bana saldıran mobu kes", 370, 0, 360, () => _profile.AttackersFirst, v => _profile.AttackersFirst = v);
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
        metins.SelectedIndexChanged += (_, _) => { if (_reloading) return; _profile.Metins = (MetinMode)metins.SelectedIndex; SaveProfile(); };
        _reloads.Add(() => metins.SelectedIndex = (int)_profile.Metins);
        targets.Controls.Add(metins);
        var mobInfo = Theme.Button(targets, "📊  Moblar ve düşenler", new Rectangle(370, 34, 200, 28), Theme.Secondary, Theme.SecondaryHover);
        mobInfo.Click += (_, _) =>
        {
            using var form = new MobInfoForm(_engine);
            form.ShowDialog(this);
        };
        TextRow(targets, "Sadece bu moblar", 0, 68, () => _profile.OnlyMobs, v => _profile.OnlyMobs = v);
        TextRow(targets, "Bu mobları atla", 370, 68, () => _profile.SkipMobs, v => _profile.SkipMobs = v);
        Hint(targets, "Mob adları virgülle yazılır, adın bir kısmı yeter; boş = hepsi. Seviye 0 = sınırsız. " +
            "\"Moblar ve düşenler\": bu haritadaki moblar, verdikleri EXP ve botun gördüğü düşen eşyalar.", 110);

        // Eşya / Envanter: what is picked up, what is thrown away when the bag fills.
        var items = _tabPages[2];
        Check(items, "Yerdeki eşyalara yürüyüp topla", 0, 0, 300, () => _profile.PickUpDrops, v => _profile.PickUpDrops = v);
        var itemList = Theme.Button(items, "📋  Eşya listesi (Topla / Alma / At)", new Rectangle(370, 0, 286, 28), Theme.Primary, Theme.PrimaryHover);
        var presets = Theme.Button(items, "💾  Kayıtlı ayarlar (kaydet / yükle)", new Rectangle(666, 0, 250, 28), Theme.Secondary, Theme.SecondaryHover);
        presets.Click += (_, _) =>
        {
            using var form = new LootPresetForm(_profile, _engine?.CharacterName is { Length: > 0 } me ? me + " ayarı" : "");
            if (form.ShowDialog(this) != DialogResult.OK || form.Loaded is not { } name)
                return;
            ReloadSettings();
            SaveProfile();
            WriteLog($"💾 \"{name}\" eşya ayarları yüklendi: {_profile.LootPick.Count} eşya toplanacak, {_profile.LootSkip.Count} alınmayacak, " +
                $"{_profile.LootThrow.Count} atılacak, efsun listesinde {_profile.LootBonuses.Count} efsun" +
                (_profile.LootEquipmentMinGold > 0 ? $", silah / zırh en az {_profile.LootEquipmentMinGold:N0} yang." : "."));
        };
        Theme.Label(items, "Toplanacaklar:", new Rectangle(0, 38, 120, 26), 9f, Theme.Muted);
        Check(items, "Yang", 124, 38, 70, () => _profile.LootYang, v => _profile.LootYang = v);
        Check(items, "İksir", 198, 38, 70, () => _profile.LootPotions, v => _profile.LootPotions = v);
        Check(items, "Silah / zırh", 272, 38, 110, () => _profile.LootEquipment, v => _profile.LootEquipment = v);
        Check(items, "Diğer (malzeme, kitap…)", 386, 38, 200, () => _profile.LootOther, v => _profile.LootOther = v);
        Check(items, "Ödül ve özel eşyaları hep topla", 600, 38, 300, () => _profile.LootRewards, v => _profile.LootRewards = v);
        Check(items, "Sadece kendi sınıfımın silah / zırhı", 500, 72, 330, () => _profile.LootOwnClass, v => _profile.LootOwnClass = v);
        Theme.Label(items, "Silah / zırh satışta en az", new Rectangle(0, 72, 190, 26), 9f, Theme.Muted);
        var minGold = Theme.Number(items, new Rectangle(196, 72, 100, 26), 0, 10_000_000,
            Math.Clamp(_profile.LootEquipmentMinGold, 0, 10_000_000), 0, 500);
        minGold.ValueChanged += (_, _) => { if (_reloading) return; _profile.LootEquipmentMinGold = (int)minGold.Value; SaveProfile(); };
        _reloads.Add(() => minGold.Value = Math.Clamp(_profile.LootEquipmentMinGold, 0, 10_000_000));
        Theme.Label(items, "ÇANTA DOLARKEN", new Rectangle(0, 114, 300, 18), 8f, Theme.Accent, FontStyle.Bold);
        Check(items, "Yer azalınca 'At' işaretlileri ve botun aldığı ucuz silah/zırhı at", 0, 136, 470,
            () => _profile.ThrowWhenInventoryFull, v => _profile.ThrowWhenInventoryFull = v);
        Check(items, "Atılacak bir şey yoksa çanta dolunca dur", 480, 136, 330, () => _profile.StopWhenInventoryFull,
            v => _profile.StopWhenInventoryFull = v);
        var throwNow = Theme.Button(items, "🗑  Şimdi at", new Rectangle(0, 172, 140, 30), Theme.Secondary, Theme.SecondaryHover);
        throwNow.Click += (_, _) => WriteLog(_engine?.CleanNow() ?? "Önce oyunu bağla.");
        _bagState = Theme.Label(items, "", new Rectangle(152, 172, 760, 30), 9f, Theme.Text);
        Check(items, "Fiyatı için (≥) alınan efsunsuz silah / zırhı market NPC'sine götürüp sat (atılacaklar yine atılır; Topla / efsunlu / özel eşya satılmaz)",
            0, 208, 760, () => _profile.SellToNpc, v => _profile.SellToNpc = v);
        var bonusList = Theme.Button(items, "✨  Efsun listesi", new Rectangle(310, 72, 180, 26), Theme.Secondary, Theme.SecondaryHover);
        bonusList.Click += (_, _) =>
        {
            using var form = new BonusListForm(_profile);
            form.ShowDialog(this);
            SaveProfile();
            WriteLog(_profile.LootBonuses.Count == 0
                ? "Efsun listesi boş: silah / zırh sadece fiyatına göre alınır."
                : "Efsun listesi: " + string.Join(", ", _profile.LootBonuses.Select(w =>
                      $"{Memory.ItemBonuses.NameOf(w.Type)} ≥ {w.Min}" + (w.ItemNames().Length > 0 ? $" (sadece {w.Items})" : ""))) +
                  ". Yalnız bu efsunların gelebildiği eşyalar alınır, efsunu tutmayanlar geri atılır.");
            // A name typed that is no item the game has (a typo): said at once.
            if (Memory.ItemBook.Count > 0)
            {
                var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
                var names = Memory.ItemBook.All().Select(i => i.Name).ToList();
                foreach (var unknown in _profile.LootBonuses.SelectMany(w => w.ItemNames())
                             .Where(n => !names.Any(i => tr.IndexOf(i, n, System.Globalization.CompareOptions.IgnoreCase) >= 0)).Distinct())
                    WriteLog($"⚠ Efsun listesinde \"{unknown}\" adlı eşya oyunda bulunamadı; adı kontrol et (ör. \"Deri Çizme\").");
            }
        };
        itemList.Click += (_, _) =>
        {
            using var form = new ItemListForm(_profile);
            form.ShowDialog(this);
            SaveProfile();
            WriteLog($"Eşya listesi: {_profile.LootPick.Count} eşya mutlaka toplanacak, {_profile.LootSkip.Count} eşya alınmayacak, " +
                $"{_profile.LootThrow.Count} eşya envanter dolunca atılacak.");
        };
        Hint(items, "Fiyat = dükkânın verdiği satış fiyatı. Botun yanlışlıkla aldığı istenmeyen eşyalar molada ya da yer azalınca " +
            "atılır (senin eşyaların, Topla işaretliler, etkinlik ödülleri ve giyilenler hiç atılmaz). Atma: sakin bir yere geçer, " +
            "eşyaya tıklayıp \"Yere at\" der. Bu turda alınan / atılanlar Panel'de listelenir.", 216);

        var potions = _tabPages[3];
        NumberRow(potions, "HP iksiri (% altında)", 0, 0, 5, 95, () => Math.Round(_profile.PotionHpBelow * 100), 0, 1,
            v => _profile.PotionHpBelow = v / 100);
        NumberRow(potions, "MP iksiri (% altında)", 0, 34, 0, 95, () => Math.Round(_profile.PotionMpBelow * 100), 0, 1,
            v => _profile.PotionMpBelow = v / 100);
        Check(potions, "Ölünce yeniden başla", 0, 68, 300, () => _profile.ReviveOnDeath, v => _profile.ReviveOnDeath = v);
        NumberRow(potions, "Kaç HP iksiri kalınca kaç", 0, 102, 0, 200, () => _profile.RetreatAtPotions, 0, 1,
            v => _profile.RetreatAtPotions = (int)v);
        NumberRow(potions, "Tehlikede kaç: can % altı", 0, 136, 0, 90, () => Math.Round(_profile.FleeAtHp * 100), 0, 5,
            v => _profile.FleeAtHp = v / 100);
        Theme.Label(potions, "Tehlikede kaç: can bu yüzdenin altına düşer ve en az 3 mob saldırırsa bot vurmayı bırakıp moblardan " +
            "uzaklaşır (iksir içmeye devam eder); can %60'ı geçince ya da 2 sn kimse saldırmayınca (en çok 20 sn) dövüşe döner. 0 = kapalı.", new Rectangle(0, 176, 918, 54), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);
        Check(potions, "Market NPC'sinden iksir al (satışa gidince; azalınca ayrıca gider)", 0, 236, 520, () => _profile.BuyPotions, v => _profile.BuyPotions = v);
        NumberRow(potions, "Şu sayının altına düşünce al", 0, 270, 0, 5000, () => _profile.BuyPotionsBelow, 0, 50,
            v => _profile.BuyPotionsBelow = (int)v);
        NumberRow(potions, "Şu sayıya tamamla", 370, 270, 0, 10000, () => _profile.BuyPotionsUpTo, 0, 100,
            v => _profile.BuyPotionsUpTo = (int)v);
        Check(potions, "Becerileri otomatik kullan (bellekten)", 370, 0, 360, () => _profile.AutoSkills, v => _profile.AutoSkills = v);
        NumberRow(potions, "Alan becerisi: en az mob", 370, 34, 1, 15, () => _profile.AreaSkillMinMobs, 0, 1,
            v => _profile.AreaSkillMinMobs = (int)v);
        _skillList = Theme.Label(potions, "", new Rectangle(370, 68, 366, 72), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

        // Alan
        var area = _tabPages[4];
        NumberRow(area, "Alan sınırı (metre)", 0, 0, 0, 500, () => Math.Round(_profile.LeashMeters), 0, 5,
            v => _profile.LeashMeters = v);
        _findArea = Theme.Button(area, "📍  En iyi alanı bul", new Rectangle(370, 0, 180, 28), Theme.Primary, Theme.PrimaryHover);
        _findArea.Click += (_, _) => ToggleAreaScan();
        Check(area, "Kayıtlı alanda başla", 560, 1, 176, () => _profile.UseFarmSpot, v => _profile.UseFarmSpot = v);
        // The map's walls, houses, rocks and trees (read from the game when the character comes onto a map).
        var openMap = Theme.Button(area, "🗺  Haritayı aç", new Rectangle(746, 0, 172, 28), Theme.Secondary, Theme.SecondaryHover);
        openMap.Click += (_, _) =>
        {
            if (_engine?.MapPicture is { } picture)
                OpenFile(picture);
            else
                WriteLog("Henüz kayıtlı harita yok: oyun açıkken karakter bir haritaya girince engeller kendiliğinden okunur.");
        };
        _farmState = Theme.Label(area, "", new Rectangle(370, 32, 366, 40), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);
        Check(area, "GM görünce dur", 0, 34, 180, () => _profile.StopNearGm, v => _profile.StopNearGm = v);
        Check(area, "Şehre girince dur", 190, 34, 170, () => _profile.StopInSafeZone, v => _profile.StopInSafeZone = v);
        Check(area, "Ara sıra mola ver", 0, 68, 180, () => _profile.Breaks, v => _profile.Breaks = v);
        Check(area, "Güçlü moblara gitme", 190, 68, 176, () => _profile.AutoArea, v => _profile.AutoArea = v);
        // Off by default: it took the character to odd places (user, 2026-11-19).
        Check(area, "Oyuncu gelince başka alana geç", 0, 102, 360, () => _profile.MoveAwayFromPlayers, v => _profile.MoveAwayFromPlayers = v);
        var saveSpot = Theme.Button(area, "📌  Bu alanı kaydet", new Rectangle(370, 100, 190, 28), Theme.Primary, Theme.PrimaryHover);
        saveSpot.Click += (_, _) =>
        {
            WriteLog(_engine?.SaveMySpot() ?? "Önce oyunu bağla.");
            ReloadSettings();
            SaveProfile();
        };
        var forgetSpot = Theme.Button(area, "Kaydettiğim alanı sil", new Rectangle(570, 100, 180, 28), Theme.Secondary, Theme.SecondaryHover);
        forgetSpot.Click += (_, _) =>
        {
            if (_profile.MySpot == null)
            {
                WriteLog("Kaydettiğin bir alan yok.");
                return;
            }
            _profile.MySpot = null;
            SaveProfile();
            WriteLog("Kaydettiğin alan silindi" + (_profile.FarmSpot != null && _profile.UseFarmSpot
                ? "; bot başlarken botun bulduğu alana gider." : "; bot durduğu yerde başlar."));
        };
        _respawnState = Theme.Label(area, "", new Rectangle(0, 140, 918, 22), 8.5f, Theme.Muted);
        _positionState = Theme.Label(area, "", new Rectangle(0, 166, 918, 40), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

        // Pazar: the market list of the map (kept up to date), search it, walk to a shop, scan, wanted items.
        var market = _tabPages[8];
        _scanMarket = Theme.Button(market, "🛒  Pazarı tara", new Rectangle(0, 0, 140, 30), Theme.Secondary, Theme.SecondaryHover);
        _scanMarket.Click += (_, _) => ToggleMarketScan();
        var marketWindow = Theme.Button(market, "🖥  Pazar penceresi", new Rectangle(146, 0, 170, 30), Theme.Primary, Theme.PrimaryHover);
        marketWindow.Click += (_, _) => OpenMarketWindow();
        var openReport = Theme.Button(market, "📊  Rapor", new Rectangle(322, 0, 96, 30), Theme.Secondary, Theme.SecondaryHover);
        openReport.Click += (_, _) =>
        {
            if (BotEngine.LatestReport is { } report)
                OpenFile(report);
            else
                WriteLog("Henüz pazar listesi yok: önce pazar haritasında \"Pazarı tara\"ya bas.");
        };
        var marketFolder = Theme.Button(market, "📂  Klasör", new Rectangle(424, 0, 90, 30), Theme.Secondary, Theme.SecondaryHover);
        marketFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(BotEngine.MarketFolder);
            OpenFile(BotEngine.MarketFolder);
        };
        var bonusGuide = Theme.Button(market, "📚  Efsun rehberi", new Rectangle(520, 0, 136, 30), Theme.Secondary, Theme.SecondaryHover);
        bonusGuide.Click += (_, _) =>
        {
            try
            {
                // Which bonus comes on which items with which values, learned from every scan (built from the old ones the first time).
                var book = BonusBook.Shared;
                Directory.CreateDirectory(BotEngine.MarketFolder);
                string page = Path.Combine(BotEngine.MarketFolder, "efsun-rehberi.html");
                File.WriteAllText(page, book.Html(), new System.Text.UTF8Encoding(false));
                OpenFile(page);
                WriteLog($"📚 Efsun rehberi: pazarda görülen {book.Listings:N0} efsunlu ilandan hangi efsunun hangi eşyalarda, hangi değerlerle geldiği.");
            }
            catch (IOException ex) { WriteLog("Efsun rehberi yazılamadı: " + ex.Message); }
        };
        _marketState = Theme.Label(market, "Pazar haritasında liste kendiliğinden güncellenir: açtığın dükkanlar eklenir, kapananlar çıkarılır.",
            new Rectangle(664, 0, 254, 32), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.MiddleLeft);

        // Left: search the list, pick, walk there.
        Theme.Label(market, "PAZARDA ARA", new Rectangle(0, 40, 300, 18), 8f, Theme.Accent, FontStyle.Bold);
        var findBox = new TextBox
        {
            Bounds = new Rectangle(0, 62, 380, 26), BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f), PlaceholderText = "Eşya ya da efsun yaz (ör. kavrama, kutsama, HP)",
        };
        market.Controls.Add(findBox);
        var goShop = Theme.Button(market, "🚶  Dükkana götür", new Rectangle(388, 62, 156, 26), Theme.Primary, Theme.PrimaryHover);
        // Filters: item group, a bonus of at least a value, and the order (from the bonus book: what the shops showed).
        ComboBox Pick(Rectangle at)
        {
            var box = new ComboBox
            {
                Bounds = at, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Inset, ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f), MaxDropDownItems = 20,
            };
            market.Controls.Add(box);
            return box;
        }
        var groupPick = Pick(new Rectangle(0, 94, 104, 26));
        var bonusPick = Pick(new Rectangle(108, 94, 200, 26));
        Theme.Label(market, "en az", new Rectangle(312, 94, 36, 26), 8.5f, Theme.Muted);
        var minValue = Theme.Number(market, new Rectangle(348, 94, 58, 26), -100, 100_000, 0, 0, 1);
        var sortPick = Pick(new Rectangle(410, 94, 134, 26));
        sortPick.Items.AddRange(["Sırala: en ucuz", "Sırala: efsun değeri", "Sırala: efsun puanı"]);
        sortPick.SelectedIndex = 0;
        var groupKeys = new List<string?>();
        void FillGroups()
        {
            var book = BonusBook.Shared;
            groupKeys = [null, .. book.Groups()];
            groupPick.Items.Clear();
            groupPick.Items.AddRange([.. groupKeys.Select(g => g == null ? "Her eşya" : BonusBook.GroupName(g))]);
            groupPick.SelectedIndex = 0;
        }
        void FillBonuses()
        {
            string? group = groupPick.SelectedIndex > 0 ? groupKeys[groupPick.SelectedIndex] : null;
            string? keep = bonusPick.SelectedIndex > 0 ? bonusPick.SelectedItem as string : null;
            bonusPick.Items.Clear();
            bonusPick.Items.Add("Her efsun");
            bonusPick.Items.AddRange([.. BonusBook.Shared.Names(group)]);
            bonusPick.SelectedIndex = keep != null && bonusPick.Items.IndexOf(keep) is > 0 and var i ? i : 0;
        }
        try
        {
            FillGroups();
            FillBonuses();
        }
        catch (IOException) { }
        var found = new ListView
        {
            Bounds = new Rectangle(0, 124, 544, 170), View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9f),
            HeaderStyle = ColumnHeaderStyle.Nonclickable, OwnerDraw = true,
        };
        found.Columns.Add("Eşya", 150);
        found.Columns.Add("Efsun", 62, HorizontalAlignment.Right);
        found.Columns.Add("Tanesi", 78, HorizontalAlignment.Right);
        found.Columns.Add("Adet", 40, HorizontalAlignment.Right);
        found.Columns.Add("Satıcı", 92);
        found.Columns.Add("Uzaklık", 54, HorizontalAlignment.Right);
        found.Columns.Add("Okundu", 52);
        found.DrawColumnHeader += (_, e) =>
        {
            using var back = new SolidBrush(Theme.Secondary);
            e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, found.Font, Rectangle.Inflate(e.Bounds, -4, 0), Theme.Muted,
                TextFormatFlags.VerticalCenter | (e.Header?.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left));
        };
        found.DrawItem += (_, e) => e.DrawDefault = true;
        found.DrawSubItem += (_, e) => e.DrawDefault = true;
        market.Controls.Add(found);
        var foundRows = new List<MarketListing>();
        MarketQuery Query() => new(findBox.Text,
            groupPick.SelectedIndex > 0 ? groupKeys[groupPick.SelectedIndex] : null,
            bonusPick.SelectedIndex > 0 ? bonusPick.SelectedItem as string : null,
            (int)minValue.Value,
            (MarketSort)Math.Max(0, sortPick.SelectedIndex));
        void Search()
        {
            var query = Query();
            foundRows = _engine?.MarketFind(query) ?? [];
            var bonusBook = BonusBook.Shared;
            var me = _engine?.World?.Player;
            found.BeginUpdate();
            found.Items.Clear();
            foreach (var l in foundRows)
            {
                string away = me is { } at && (l.X != 0 || l.Y != 0) ? $"{Math.Sqrt((l.X - at.X) * (l.X - at.X) + (l.Y - at.Y) * (l.Y - at.Y)) / 100:0} m" : "—";
                int minutes = (int)(DateTime.Now - l.Seen).TotalMinutes;
                string seen = minutes < 1 ? "şimdi" : minutes < 60 ? $"{minutes} dk" : minutes < 1440 ? $"{minutes / 60} sa" : l.Seen.ToString("dd.MM");
                // The chosen bonus's value (★ = the highest seen on such items), else how good all its bonuses are.
                string bonusCell = "";
                if (query.Bonus != null && l.BonusValue is int value && BonusBook.GroupOf(l.Vnum) is string group)
                    bonusCell = (value < 0 ? "-" : "+") + Math.Abs(value) +
                                (bonusBook.Quality(query.Bonus, group, value) >= 1 ? " ★" : "");
                else if (l.Score > 0)
                    bonusCell = $"{l.Score:0.0} ✦";
                found.Items.Add(new ListViewItem([l.Name, bonusCell, BotEngine.MarketYang(l.UnitPrice), l.Count.ToString("N0"), l.Owner, away, seen])
                    { ToolTipText = bonusBook.Describe(l.Vnum, l.Bonuses) });
            }
            found.EndUpdate();
            if (!query.Empty)
                _marketState.Text = foundRows.Count == 0
                    ? (_engine == null ? "Önce oyunu bağla." : "Listede yok (bu haritanın listesi boş olabilir: önce \"Pazarı tara\").")
                    : $"{foundRows.Count} satış bulundu ({query.Sort switch { MarketSort.BonusValue => "efsunu en yüksek", MarketSort.Score => "efsun puanı en yüksek", _ => "en ucuzu" }} üstte). " +
                      "Üstüne gelince bütün efsunlar görünür. Birini seçip \"🚶 Dükkana götür\"e bas.";
        }
        var searchTimer = new System.Windows.Forms.Timer { Interval = 350 };
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); Search(); };
        findBox.TextChanged += (_, _) => { searchTimer.Stop(); searchTimer.Start(); };
        groupPick.SelectedIndexChanged += (_, _) => { FillBonuses(); Search(); };
        bonusPick.SelectedIndexChanged += (_, _) =>
        {
            // A bonus chosen: by its value from now on (unless another order was picked).
            if (bonusPick.SelectedIndex > 0 && sortPick.SelectedIndex == 0)
                sortPick.SelectedIndex = 1;
            Search();
        };
        minValue.ValueChanged += (_, _) => { searchTimer.Stop(); searchTimer.Start(); };
        sortPick.SelectedIndexChanged += (_, _) => Search();
        // New bonuses learned meanwhile (a scan, the bag): the lists show them when opened.
        groupPick.DropDown += (_, _) =>
        {
            if (BonusBook.Shared.Groups().Count + 1 != groupPick.Items.Count)
            {
                string? was = groupPick.SelectedIndex > 0 ? groupKeys[groupPick.SelectedIndex] : null;
                FillGroups();
                if (was != null && groupKeys.IndexOf(was) is > 0 and var i)
                    groupPick.SelectedIndex = i;
            }
        };
        bonusPick.DropDown += (_, _) => FillBonuses();
        found.ShowItemToolTips = true;
        void GoToSelected()
        {
            if (_engine == null)
            {
                WriteLog("Önce oyun açık olmalı.");
                return;
            }
            if (found.SelectedIndices.Count == 0 || found.SelectedIndices[0] >= foundRows.Count)
            {
                WriteLog("Önce listeden bir satış seç.");
                return;
            }
            var pick = foundRows[found.SelectedIndices[0]];
            if (!_engine.StartGoToShop(pick.ShopId, pick.Name, out string error))
                WriteLog(error);
            UpdateControls();
        }
        goShop.Click += (_, _) => GoToSelected();
        found.DoubleClick += (_, _) => GoToSelected();

        // Right: wanted items and settings.
        Theme.Label(market, "ARANAN ÜRÜNLER (taramada haber verilir)", new Rectangle(560, 40, 358, 18), 8f, Theme.Accent, FontStyle.Bold);
        var wantName = new TextBox
        {
            Bounds = new Rectangle(560, 62, 358, 26), BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f), PlaceholderText = "Eşya adı (ör. Kavrama Taşı+4)",
            AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.CustomSource,
        };
        // The game's item names (known once the game was read) as suggestions.
        wantName.Enter += (_, _) =>
        {
            if (wantName.AutoCompleteCustomSource.Count == 0 && Memory.ItemBook.Count > 0)
                wantName.AutoCompleteCustomSource.AddRange([.. Memory.ItemBook.All().Select(i => i.Name).Where(n => n.Length > 0).Distinct()]);
        };
        market.Controls.Add(wantName);
        Theme.Label(market, "tanesi en fazla", new Rectangle(560, 94, 96, 26), 9f, Theme.Muted);
        var wantMax = Theme.Number(market, new Rectangle(658, 94, 160, 26), 0, 2_000_000_000, 0, 0, 100_000);
        wantMax.ThousandsSeparator = true;
        var wantAdd = Theme.Button(market, "➕  Ekle", new Rectangle(826, 94, 92, 26), Theme.Primary, Theme.PrimaryHover);
        var wantList = new ListBox
        {
            Bounds = new Rectangle(560, 126, 358, 74), BackColor = Theme.Inset, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f), IntegralHeight = false,
        };
        market.Controls.Add(wantList);
        var wantRemove = Theme.Button(market, "Seçileni çıkar", new Rectangle(560, 204, 110, 24), Theme.Secondary, Theme.SecondaryHover);
        void ShowWanted()
        {
            wantList.Items.Clear();
            foreach (var w in _profile.MarketWanted)
                wantList.Items.Add(w.MaxPrice > 0 ? $"{w.Name}  —  tanesi ≤ {BotEngine.MarketYang(w.MaxPrice)}" : $"{w.Name}  —  her fiyat");
            if (_profile.MarketWanted.Count == 0)
                wantList.Items.Add("(boş)");
        }
        void AddWanted()
        {
            string name = wantName.Text.Trim();
            if (name.Length == 0)
                return;
            _profile.MarketWanted.RemoveAll(w => string.Equals(w.Name, name, StringComparison.CurrentCultureIgnoreCase));
            _profile.MarketWanted.Add(new MarketWant { Name = name, MaxPrice = (long)wantMax.Value });
            SaveProfile();
            wantName.Text = "";
            wantMax.Value = 0;
            ShowWanted();
        }
        wantAdd.Click += (_, _) => AddWanted();
        wantName.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddWanted(); } };
        wantRemove.Click += (_, _) =>
        {
            int i = wantList.SelectedIndex;
            if (i < 0 || i >= _profile.MarketWanted.Count)
                return;
            _profile.MarketWanted.RemoveAt(i);
            SaveProfile();
            ShowWanted();
        };
        ShowWanted();
        Check(market, "Telegram'a yaz", 680, 203, 238, () => _profile.MarketNotify, v => _profile.MarketNotify = v);
        Check(market, "Kamerayı kuş bakışına al", 560, 236, 358, () => _profile.MarketBirdsEye, v => _profile.MarketBirdsEye = v);
        Check(market, "Son 2 saatte okunan dükkanları atla (taramada)", 560, 264, 358, () => _profile.MarketResume, v => _profile.MarketResume = v);

        // Tuşlar
        // Bildirim: messages to the user's own Telegram bot.
        var notify = _tabPages[5];
        Check(notify, "Telegram'a bildirim gönder (GM, ölüm, seviye, iksir bitti, bot durdu)", 0, 0, 600,
            () => _profile.TelegramEnabled, v => _profile.TelegramEnabled = v);
        var token = TextRow(notify, "Bot anahtarı", 0, 34, () => _profile.TelegramToken, v => _profile.TelegramToken = v.Trim(), 420);
        // Hidden like a password (it gives control of the Telegram bot; seen in a screenshot it could be taken).
        token.UseSystemPasswordChar = true;
        var chat = TextRow(notify, "Sohbet no", 0, 68, () => _profile.TelegramChatId, v => _profile.TelegramChatId = v.Trim(), 160);
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
            WriteLog(await TelegramNotifier.SendAsync(_profile.TelegramToken, _profile.TelegramChatId, "PARDUS: deneme mesajı ✓")
                     ?? "Telegram deneme mesajı gönderildi ✓");
        Check(notify, "Komutlar (durum/dur/başla)", 580, 68, 156, () => _profile.TelegramCommands,
            v => _profile.TelegramCommands = v);
        Hint(notify, "Kurulum: Telegram'da @BotFather'a /newbot yaz, verdiği anahtarı buraya yapıştır. Sonra kendi botuna " +
            "\"merhaba\" yaz ve 'Sohbeti bul'a bas. Botuna \"durum\", \"dur\", \"başla\" yazınca cevap verir (yalnız bu sohbete). " +
            "Fısıltı, istek, adının geçmesi ve GM yazması uyarı sesiyle de bildirilir; hepsi günlüğe de yazılır.", 244);
        Theme.Label(notify, "NELER BİLDİRİLSİN", new Rectangle(0, 108, 300, 18), 8f, Theme.Accent, FontStyle.Bold);
        Check(notify, "Fısıltı (özel mesaj)", 0, 128, 170, () => _profile.NotifyWhispers, v => _profile.NotifyWhispers = v);
        Check(notify, "Takas / grup / arkadaş isteği", 176, 128, 220, () => _profile.NotifyRequests, v => _profile.NotifyRequests = v);
        Check(notify, "Sohbette adım ya da GM", 402, 128, 190, () => _profile.NotifyMentions, v => _profile.NotifyMentions = v);
        Check(notify, "Dağıtılmamış puan", 598, 128, 160, () => _profile.NotifyPoints, v => _profile.NotifyPoints = v);
        Check(notify, "Etkinlik başlayınca", 0, 156, 170, () => _profile.NotifyEvents, v => _profile.NotifyEvents = v);
        Check(notify, "Tüm etkinlik ödüllerini her zaman topla", 176, 156, 380, () => _profile.EventLoot, v => _profile.EventLoot = v);
        Check(notify, "Topla eşyası düşünce", 562, 156, 200, () => _profile.NotifyDrops, v => _profile.NotifyDrops = v);
        Check(notify, "Efsun listesindeki efsunlu eşya alınınca (✨ Efsun listesi'nde seçtiklerin)", 0, 184, 560,
            () => _profile.NotifyBonusItems, v => _profile.NotifyBonusItems = v);
        Check(notify, "Seviye atlayınca", 0, 212, 170, () => _profile.NotifyLevelUp, v => _profile.NotifyLevelUp = v);
        Check(notify, "Şehre gidip eşya satınca", 176, 212, 240, () => _profile.NotifySells, v => _profile.NotifySells = v);
        Check(notify, "Bot çalışırken oyundan atılırsa (bakım) sunucu açılana kadar girişi dene (şifreye dokunmaz; açılınca haber verir)", 0, 240, 700,
            () => _profile.AutoLogin, v => _profile.AutoLogin = v);

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
        advanced.AutoScroll = true;
        _dryRun = new Toggle
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
        _replay.Visible = false; // a developer tool
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
        // ── This computer: what the game sees, what Windows shows ──
        advanced.Controls.Add(new Panel { Bounds = new Rectangle(0, 146, 918, 1), BackColor = Theme.Border });
        Theme.Label(advanced, "BİLGİSAYAR", new Rectangle(0, 154, 300, 22), 7.5f, Theme.Faint, FontStyle.Bold);
        var refreshMachine = Theme.Button(advanced, "↻  Yenile", new Rectangle(918 - 300, 150, 110, 30), Theme.Secondary, Theme.SecondaryHover);
        var publicIp = Theme.Button(advanced, "🌐  Dış IP'yi göster", new Rectangle(918 - 182, 150, 182, 30), Theme.Secondary, Theme.SecondaryHover);
        _machine = new InfoList { Bounds = new Rectangle(0, 186, 892, 300), Font = new Font("Segoe UI", 9f) };
        Theme.DarkScrollBars(advanced);
        advanced.Controls.Add(_machine);
        refreshMachine.Click += (_, _) => ShowMachine();
        publicIp.Click += async (_, _) =>
        {
            publicIp.Enabled = false;
            _publicIp = "soruluyor…";
            ShowMachine();
            _publicIp = await MachineInfo.PublicIpAsync();
            publicIp.Enabled = true;
            ShowMachine();
        };
        ShowMachine();
        var openFolder = Theme.Button(advanced, "📁  Ayar klasörü", new Rectangle(150, 100, 142, 30), Theme.Secondary, Theme.SecondaryHover);
        openFolder.Click += (_, _) => OpenProfileFolder();
        // The mobs the bot sees in memory, listed in the log.
        _scanMobs = Theme.Button(advanced, "🔍  Mobları göster", new Rectangle(450, 100, 180, 30), Theme.Secondary, Theme.SecondaryHover);
        _scanMobs.Visible = false; // a developer tool (the mobs show in "Moblar ve düşenler")
        _scanMobs.Click += (_, _) =>
        {
            if (_engine == null) { WriteLog("Önce oyun açık olmalı."); return; }
            WriteLog(_engine.DescribeMobs());
        };

        // ── Dashboard bottom: the log and this run's items (the settings pages show here instead) ──
        _dashBottom = new Panel { Bounds = new Rectangle(202, B + 631, 966, 212), BackColor = Theme.Background };
        Controls.Add(_dashBottom);
        var logCard = _logCard = new Card { Bounds = new Rectangle(0, 0, 598, 212), Caption = "GÜNLÜK" };
        _dashBottom.Controls.Add(logCard);
        // Which lines: all, the sales, the warnings, the deaths.
        (string Name, string Kind)[] filters = [("Tümü", ""), ("Satış", "sell"), ("Uyarı", "warn"), ("Ölüm", "dead")];
        _logFilters = new NavButton[filters.Length];
        for (int i = 0; i < filters.Length; i++)
        {
            string kind = filters[i].Kind;
            var button = _logFilters[i] = new NavButton { Text = filters[i].Name, Segment = true, Selected = i == 0, Tag = kind,
                Bounds = new Rectangle(598 - 14 - (filters.Length - i) * 66, 7, 62, 26), Font = new Font("Segoe UI", 8.5f), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            button.Click += (_, _) => FilterLog(kind);
            logCard.Controls.Add(button);
        }
        var itemsCard = _itemsCard = new Card { Bounds = new Rectangle(610, 0, 356, 212), Caption = "BU TUR EŞYALAR" };
        _dashBottom.Controls.Add(itemsCard);
        var openItems = new IconButton { Icon = "list", Bounds = new Rectangle(356 - 14 - 30, 6, 30, 28) };
        itemsCard.Controls.Add(openItems);
        openItems.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(SessionItems.FilePath) { UseShellExecute = true }); }
            catch { WriteLog("Bu turun eşya listesi henüz yok (bot başlayınca oluşur)."); }
        };
        _runItems = new ListView
        {
            Bounds = new Rectangle(14, 40, 328, 146),
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Inset,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 8.5f),
        };
        // Dark column headers (the default ones are white).
        _runItems.OwnerDraw = true;
        _runItems.DrawColumnHeader += (_, e) =>
        {
            using var back = new SolidBrush(Theme.Surface);
            e.Graphics.FillRectangle(back, e.Bounds);
            var flags = (e.Header?.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left) |
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            var text = e.Bounds with { X = e.Bounds.X + 4, Width = e.Bounds.Width - 8 };
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, new Font("Segoe UI", 8f, FontStyle.Bold), text, Theme.Muted, flags);
        };
        _runItems.DrawItem += (_, e) => e.DrawDefault = true;
        _runItems.DrawSubItem += (_, e) => e.DrawDefault = true;
        _runItems.Columns.Add("Eşya", 200);
        _runItems.Columns.Add("Alınan", 58, HorizontalAlignment.Right);
        _runItems.Columns.Add("Atılan", 58, HorizontalAlignment.Right);
        _runItems.Columns.Add("Satılan", 58, HorizontalAlignment.Right);
        itemsCard.Controls.Add(_runItems);
        _runItemsInfo = Theme.Label(itemsCard, "Bot başlayınca dolar; her turda yenilenir.", new Rectangle(16, 188, 326, 18), 8f, Theme.Faint);
        _log = new RichTextBox
        {
            Bounds = new Rectangle(16, 42, 568, 158),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false,
            BackColor = Theme.Surface,
            ForeColor = Theme.Muted,
            Font = new Font("Consolas", 8.5f),
            TabStop = false
        };
        logCard.Controls.Add(_log);
        foreach (Control list in new Control[] { _log, _runItems })
            Theme.DarkScrollBars(list);

        // ── PAZAR mode: the market watch at a glance; the full search in the market window ──
        _marketDash = new Panel { Bounds = new Rectangle(12, B + 108, 1156, 400), BackColor = Theme.Background, Visible = false };
        Controls.Add(_marketDash);
        var watchCard = _mCard = new Card { Bounds = new Rectangle(0, 0, 760, 480), Caption = "PAZAR TAKİBİ" };
        _marketDash.Controls.Add(watchCard);
        _mState = Theme.Label(watchCard, "", new Rectangle(18, 40, 724, 34), 10.5f, Theme.Text, FontStyle.Bold);
        StatCard Number(Card card, string caption, int x, int y, int width)
        {
            var stat = new StatCard { Caption = caption, Bounds = new Rectangle(x, y, width, 92), Font = new Font("Segoe UI", 14f, FontStyle.Bold) };
            card.Controls.Add(stat);
            return stat;
        }
        const int mw = 172;
        _mRound = Number(watchCard, "TUR", 18, 82, mw);
        _mRead = Number(watchCard, "OKUNAN DÜKKAN", 18 + (mw + 12), 82, mw);
        _mDeals = Number(watchCard, "KELEPİR", 18 + 2 * (mw + 12), 82, mw);
        _mWanted = Number(watchCard, "ARANAN", 18 + 3 * (mw + 12), 82, mw);
        _mShops = Number(watchCard, "DÜKKAN", 18, 184, mw);
        _mItems = Number(watchCard, "ÜRÜN", 18 + (mw + 12), 184, mw);
        _mOldest = Number(watchCard, "EN ESKİ OKUMA", 18 + 2 * (mw + 12), 184, mw);
        _mNever = Number(watchCard, "OKUNMAMIŞ", 18 + 3 * (mw + 12), 184, mw);
        var mReport = Theme.Button(watchCard, "📊  Rapor", new Rectangle(18, 290, 140, 34), Theme.Secondary, Theme.SecondaryHover);
        mReport.Click += (_, _) => { if (BotEngine.LatestReport is { } r) OpenFile(r); else WriteLog("Henüz pazar listesi yok."); };
        var mExcel = Theme.Button(watchCard, "📄  Excel", new Rectangle(166, 290, 120, 34), Theme.Secondary, Theme.SecondaryHover);
        mExcel.Click += (_, _) => { if (BotEngine.LatestReport is { } r && File.Exists(Path.ChangeExtension(r, ".csv"))) OpenFile(Path.ChangeExtension(r, ".csv")); else WriteLog("Henüz pazar listesi yok."); };
        var mGuide = Theme.Button(watchCard, "📚  Efsun rehberi", new Rectangle(294, 290, 170, 34), Theme.Secondary, Theme.SecondaryHover);
        mGuide.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(BotEngine.MarketFolder);
                string page = Path.Combine(BotEngine.MarketFolder, "efsun-rehberi.html");
                File.WriteAllText(page, BonusBook.Shared.Html(), new System.Text.UTF8Encoding(false));
                OpenFile(page);
            }
            catch (IOException ex) { WriteLog("Efsun rehberi yazılamadı: " + ex.Message); }
        };
        var mFolder = Theme.Button(watchCard, "📂  Klasör", new Rectangle(472, 290, 120, 34), Theme.Secondary, Theme.SecondaryHover);
        mFolder.Click += (_, _) => { Directory.CreateDirectory(BotEngine.MarketFolder); OpenFile(BotEngine.MarketFolder); };
        watchCard.Controls.Add(new Panel { Bounds = new Rectangle(18, 336, 724, 1), BackColor = Theme.Border });
        Theme.Label(watchCard, "AYARLAR", new Rectangle(18, 344, 300, 20), 7.5f, Theme.Faint, FontStyle.Bold);
        NumberRow(watchCard, "Yeniden okuma (dk)", 18, 370, 5, 1440, () => _profile.MarketRefreshMinutes, 0, 5,
            v => _profile.MarketRefreshMinutes = (int)v);
        NumberRow(watchCard, "Kelepir eşiği (% ucuz)", 18, 406, 10, 90, () => _profile.MarketDealPercent, 0, 5,
            v => _profile.MarketDealPercent = (int)v);
        Check(watchCard, "Bulunanları Telegram'a yaz", 400, 370, 340, () => _profile.MarketNotify, v => _profile.MarketNotify = v);
        Check(watchCard, "Kelepirleri bildir", 400, 406, 340, () => _profile.MarketDeals, v => _profile.MarketDeals = v);
        Check(watchCard, "Kamerayı kuş bakışına al", 18, 442, 360, () => _profile.MarketBirdsEye, v => _profile.MarketBirdsEye = v);
        Theme.Label(watchCard, "Hiçbir şey satın alınmaz; almak senin elinde.", new Rectangle(400, 442, 342, 26), 8.5f, Theme.Faint);

        var findsCard = new Card { Bounds = new Rectangle(770, 0, 386, 480), Caption = "SON BULUNANLAR", CaptionRight = "aranan · kelepir", Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
        _marketDash.Controls.Add(findsCard);
        _mFinds = new ListBox
        {
            Bounds = new Rectangle(14, 42, 358, 424), BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9f), IntegralHeight = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        Rows.Style(_mFinds);
        findsCard.Controls.Add(_mFinds);
        Theme.DarkScrollBars(_mFinds);

        // ── BALIKÇILIK mode: cast, wait for the bite, pull; the picture question is the player's ──
        _fishingDash = new Panel { Bounds = new Rectangle(12, B + 108, 1156, 400), BackColor = Theme.Background, Visible = false };
        Controls.Add(_fishingDash);
        var fishCard = new Card { Bounds = new Rectangle(0, 0, 760, 500), Caption = "BALIK TUTMA" };
        _fishingDash.Controls.Add(fishCard);
        var pullsCard = new Card { Bounds = new Rectangle(770, 0, 386, 500), Caption = "SON ÇEKİŞLER", CaptionRight = "sonuç · balondan sonra", Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
        _fishingDash.Controls.Add(pullsCard);
        _fPulls = new ListBox
        {
            Bounds = new Rectangle(14, 42, 358, 444), BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9f), IntegralHeight = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        Rows.Style(_fPulls);
        pullsCard.Controls.Add(_fPulls);
        Theme.DarkScrollBars(_fPulls);
        _fState = Theme.Label(fishCard, "", new Rectangle(18, 40, 724, 34), 10.5f, Theme.Text, FontStyle.Bold);
        const int fw = 135;
        _fTime = Number(fishCard, "SÜRE", 18, 82, fw);
        _fCasts = Number(fishCard, "YAKALANAN", 18 + (fw + 12), 82, fw);
        _fHooked = Number(fishCard, "BAŞARI", 18 + 2 * (fw + 12), 82, fw);
        _fMissed = Number(fishCard, "KAÇAN", 18 + 3 * (fw + 12), 82, fw);
        _fPulled = Number(fishCard, "SON ÇEKİŞ", 18 + 4 * (fw + 12), 82, fw + 1);
        fishCard.Controls.Add(new Panel { Bounds = new Rectangle(18, 186, 724, 1), BackColor = Theme.Border });
        Theme.Label(fishCard, "AYARLAR", new Rectangle(18, 194, 300, 20), 7.5f, Theme.Faint, FontStyle.Bold);
        Theme.Label(fishCard, "Yem tuşu", new Rectangle(18, 218, 110, 28), 9f, Theme.Muted);
        var baitKey = new KeyPicker { Bounds = new Rectangle(130, 218, 104, 28), Key = _profile.FishBaitKey };
        baitKey.KeyChanged += key => { _profile.FishBaitKey = key; SaveProfile(); WriteLog($"Yem tuşu: {KeyPicker.Name(key)}"); };
        fishCard.Controls.Add(baitKey);
        Theme.Label(fishCard, "Her atıştan önce bu tuşa basılıp yem takılır (yemi kısayola koy).",
            new Rectangle(250, 218, 492, 28), 8.5f, Theme.Faint);
        Check(fishCard, "Resimlerden düz olanı bot seçsin", 18, 254, 340, () => _profile.FishAutoPick, v => _profile.FishAutoPick = v);
        Check(fishCard, "Seçemezse Telegram'a yaz", 400, 254, 340, () => _profile.FishNotify, v => _profile.FishNotify = v);
        fishCard.Controls.Add(new Panel { Bounds = new Rectangle(18, 292, 724, 1), BackColor = Theme.Border });
        Theme.Label(fishCard, "ÇEKİŞ SÜRESİNE GÖRE BAŞARI", new Rectangle(18, 300, 400, 20), 7.5f, Theme.Faint, FontStyle.Bold);
        Theme.Label(fishCard, "balondan Space'e kadar geçen süre · tüm kayıtlar", new Rectangle(400, 300, 342, 20), 8f, Theme.Faint, FontStyle.Regular, ContentAlignment.MiddleRight);
        _fChart = new PullChart { Bounds = new Rectangle(18, 324, 724, 118), Font = new Font("Segoe UI", 8.5f) };
        fishCard.Controls.Add(_fChart);
        Theme.Label(fishCard,
            "Nasıl: oltayı kuşan, yemi Yem tuşuna koy, suya dönük dur ve BALIK TUT'a bas. Bot yemi takar, atar, balon çıkınca çeker, " +
            "4 resimden düz olanı seçer. Altın sütun şimdiye kadar en çok balık getiren süre; her çekiş balik-cekis.csv dosyasına yazılır.",
            new Rectangle(18, 448, 724, 44), 8.5f, Theme.Faint, FontStyle.Regular, ContentAlignment.TopLeft);

        // The pages as tall as their card; the explanations under the settings.
        foreach (var page in _tabPages)
        {
            page.Height = _settingsCard.Height - page.Top - 18;
            StackHints(page);
        }
        SelectTab(-1);

        // ── Timers & hotkeys ─────────────────────────────────────
        _scanTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _scanTimer.Tick += (_, _) => { if (_engine == null) RefreshWindows(); else LoginTick(); ResumeTick(); };
        _previewTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _previewTimer.Tick += (_, _) => RenderPreview();
        VisibleChanged += (_, _) => _engine?.ShowingPreview(Visible);
        _statsTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _statsTimer.Tick += (_, _) => UpdateStats();
        _updateTimer = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromHours(2).TotalMilliseconds };
        _updateTimer.Tick += async (_, _) => { if (Instance.IsFirst) await CheckForUpdatesAsync(startup: false); };

        // A second SekoBot (another game) uses Ctrl+F10 / Ctrl+F11; F12 is reserved by Windows for debuggers.
        bool control = !Instance.IsFirst;
        if (!_hotkeys.Register(Keys.F10, () => BeginInvoke(new Action(ToggleBot)), control))
            WriteLog($"{StartKey} kısayolu başka bir uygulamada kullanılıyor.");
        if (Instance.IsFirst && !_hotkeys.Register(Keys.F11, () => BeginInvoke(new Action(() => StopBot("F11 acil durdurma", byUser: true)))))
            WriteLog("F11 kısayolu başka bir uygulamada kullanılıyor; Durdur düğmesini kullan.");
        // Every SekoBot has a stop key of its own: Ctrl+Shift+1 for the 1st, Ctrl+Shift+2 for the 2nd, …
        if (!_hotkeys.Register(OwnStopKey, () => BeginInvoke(new Action(() => StopBot($"{OwnStopName} acil durdurma", byUser: true))), control: true, shift: true))
            WriteLog($"{OwnStopName} kısayolu başka bir uygulamada kullanılıyor; Durdur düğmesini kullan.");
        if (!Instance.IsFirst)
            WriteLog($"Bu {Instance.Number}. PARDUS: kendi ayarlarını kullanır, kısayollar {StartKey} / {StopKey}. " +
                     "Telegram komutları ve güncelleme 1. PARDUS'ta.");

        Shown += async (_, _) =>
        {
            WriteLog($"PARDUS {BuildInfo.Version} başlatıldı.");
            // Files that could not be saved or read: told here instead of silently.
            Trouble.Listen(text =>
            {
                if (InvokeRequired)
                    BeginInvoke(() => WriteLog(text));
                else
                    WriteLog(text);
            });
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
        Shown += (_, _) =>
        {
            if (_profile.MiniMode)
                BeginInvoke(new Action(GoMini));
        };
        FormClosing += (_, _) =>
        {
            _mini?.Close();
            foreach (var t in new[] { _scanTimer, _previewTimer, _statsTimer, _updateTimer }) t.Stop();
            _telegramCts.Cancel();
            _engine?.Dispose();
            _engine = null;
            _hotkeys.Dispose();
            _preview.Image = null;
            _frameBitmap?.Dispose();
        };

        // Everything a size smaller (the window was bigger than needed).
        Compact(this, UiScale);
        // Under the header and the control bar: one body that scrolls when the screen is too small for the
        // whole window (seen: the bottom cut off on a small or high-DPI screen).
        _header = header;
        _body = new Panel { BackColor = Theme.Background, AutoScroll = true };
        int bodyTop = bar.Bottom + 1;
        _body.Bounds = new Rectangle(0, bodyTop, ClientSize.Width, ClientSize.Height - bodyTop);
        foreach (var c in Controls.Cast<Control>().Where(c => c != header && c != bar).ToList())
        {
            Controls.Remove(c);
            c.Top -= bodyTop;
            _body.Controls.Add(c);
        }
        Controls.Add(_body);
        // List columns don't scale with the window: fit them to the list.
        int number = 56, nameWidth = _runItems.ClientSize.Width - 3 * number; // "Satılan" fits
        _runItems.Columns[0].Width = nameWidth;
        _runItems.Columns[1].Width = number;
        _runItems.Columns[2].Width = number;
        _runItems.Columns[3].Width = number;
        _dashBottomFarm = _dashBottom.Bounds;
        _attackInPreview = _attackButton.Bounds;
        _logCardFarm = _logCard.Bounds;
        _logFarm = _log.Bounds;
        // The market settings page lives in the PAZAR tab now.
        _tabButtons[^1].Visible = false;
        ResumeLayout(false);
        SetMode(Math.Clamp(_profile.UiMode, FarmMode, FishingMode), save: false);
        UpdateControls();
        UpdateStats();
    }

    /// <summary>Shows one mode: FARM (the hunting bot with its pages), PAZAR (the market watch), BALIKÇILIK.</summary>
    private void SetMode(int mode, bool save = true)
    {
        _mode = mode;
        bool farm = mode == FarmMode, market = mode == MarketMode;
        SuspendLayout();
        _side.Visible = farm;
        _dashboard.Visible = farm;
        if (!farm)
            _settingsCard.Visible = false;
        else
            SelectTab(-1);
        _marketDash.Visible = market;
        _fishingDash.Visible = mode == FishingMode;
        // Away from the market (no watch, no market window): its big list out of memory.
        if (!market && _marketWindow is not { Visible: true })
            _engine?.ReleaseMarket();
        // The log: under the farm view (beside this run's items), or across the whole width under the market.
        _dashBottom.Visible = true;
        _itemsCard.Visible = farm;
        if (!farm)
        {
            // The list on the right the whole height; the log under the page's card on the left.
            var dash = market ? _marketDash : _fishingDash;
            dash.Height = _dashBottomFarm.Bottom - dash.Top;
            int left = dash.Controls[0].Width, gap = _dashBottomFarm.Left - _side.Right;
            int top = dash.Top + dash.Controls[0].Height + gap;
            _dashBottom.Bounds = new Rectangle(dash.Left, top, left, _dashBottomFarm.Bottom - top);
            _logCard.Bounds = new Rectangle(0, 0, _dashBottom.Width, _dashBottom.Height);
            _log.Bounds = new Rectangle(_logFarm.Left, _logFarm.Top, _logCard.Width - 2 * _logFarm.Left, _logCard.Height - _logFarm.Top - _logFarm.Left);
            _dashBottom.BringToFront();
        }
        else
        {
            _dashBottom.Bounds = _dashBottomFarm;
            _logCard.Bounds = _logCardFarm;
            _log.Bounds = _logFarm;
        }
        // The card at the top as wide as the page under it (beside the menu on the farm page).
        var under = farm ? _dashBottomFarm : (market ? _marketDash : _fishingDash).Bounds;
        _hero.Bounds = new Rectangle(under.Left, _hero.Top, under.Width, _hero.Height);
        // The attack button in the live view (farm); "Pazar penceresi" in the top card (market); none for fishing.
        Control host = market ? _hero : _previewCard;
        if (_attackButton.Parent != host)
        {
            _attackButton.Parent?.Controls.Remove(_attackButton);
            host.Controls.Add(_attackButton);
        }
        _attackButton.Bounds = market
            ? new Rectangle(_startBot.Left - (_stopBot.Left - _startBot.Right) - _startBot.Width, _startBot.Top, _startBot.Width, _startBot.Height)
            : _attackInPreview;
        _attackButton.Visible = mode != FishingMode;
        _timer.Visible = _timerNote.Visible = !market;
        _status.Width = (market ? _attackButton.Left : _timer.Left) - _status.Left - 12;
        _heroName.Width = _status.Width;
        _fishDashAt = DateTime.MinValue;
        for (int i = 0; i < _modeButtons.Length; i++)
            _modeButtons[i].Selected = i == mode;
        ResumeLayout(true);
        if (market)
            ShowAttack();
        else if (farm)
            ShowAttack();
        if (save && _profile.UiMode != mode)
        {
            _profile.UiMode = mode;
            SaveProfile();
        }
        _marketDashAt = DateTime.MinValue;
        UpdateControls();
        UpdateStats();
    }

    /// <summary>The PAZAR page's numbers (every 2 s at most).</summary>
    private void UpdateMarketDash()
    {
        if (DateTime.Now - _marketDashAt < TimeSpan.FromSeconds(2))
            return;
        _marketDashAt = DateTime.Now;
        var engine = _engine;
        bool scanning = engine?.ScanningMarket == true;
        bool watching = scanning && engine!.MarketWatching;
        _mState.Text = engine == null ? "Oyun bağlı değil."
            : scanning ? engine.MarketStatus
            : $"Takip kapalı. Karakter pazar haritasındayken \"🔁 SÜREKLİ TAKİP\"e bas ({StartKey}).";
        _mState.ForeColor = watching ? Theme.Green : scanning ? Theme.Yellow : Theme.Muted;
        _mCard.CaptionRight = MarketServers.Current + " sunucusu";
        var counts = engine?.MarketCounts ?? default;
        _mRound.Show(scanning && watching ? counts.Round.ToString() : "—");
        _mRead.Show(counts.Read.ToString("N0"));
        _mDeals.Show(counts.Deals.ToString("N0"));
        _mWanted.Show(counts.Wanted.ToString("N0"));
        var book = engine?.CurrentMarket();
        var (onMap, never) = book?.Coverage() ?? (0, 0);
        _mShops.Show(onMap > 0 ? onMap.ToString("N0") : book != null ? book.Shops.ToString("N0") : "—");
        _mItems.Show(book != null ? book.Items.ToString("N0") : "—");
        _mNever.Show(onMap > 0 ? never.ToString("N0") : "—");
        _mOldest.Show(book?.OldestRead() is DateTime oldest ? Ago(oldest) : "—");
        var finds = engine?.MarketFinds() ?? [];
        if (finds.Count == 0 && _mFinds.Items.Count == 0 || finds.Count > 0 && (finds.Count != _mFinds.Items.Count || !Equals(_mFinds.Items[0], finds[0])))
        {
            _mFinds.BeginUpdate();
            _mFinds.Items.Clear();
            if (finds.Count == 0)
                _mFinds.Items.Add("Henüz yok. Aranan ürün ya da kelepir çıkınca burada görünür.");
            else
                _mFinds.Items.AddRange([.. finds]);
            _mFinds.EndUpdate();
        }
    }

    private DateTime _fishDashAt = DateTime.MinValue;

    /// <summary>The BALIKÇILIK page's numbers (every second at most).</summary>
    private void UpdateFishingDash()
    {
        if (DateTime.Now - _fishDashAt < TimeSpan.FromSeconds(1))
            return;
        _fishDashAt = DateTime.Now;
        var engine = _engine;
        bool fishing = engine?.Fishing == true;
        _fState.Text = engine == null ? "Oyun bağlı değil."
            : fishing || engine.FishingStatus.Length > 0 ? engine.FishingStatus
            : $"Kapalı. Oltayı kuşan, suya dönük dur, sonra BALIK TUT'a bas ({StartKey}).";
        _fState.ForeColor = !fishing ? Theme.Muted : _fState.Text.Contains("takıldı") ? Theme.Yellow : Theme.Green;
        var (casts, _, _, missed, caught) = engine?.FishCounts ?? default;
        _fTime.Show(engine is { } fe && (fe.Fishing || fe.FishingPaused || fe.FishingTime > TimeSpan.Zero) ? fe.FishingTime.ToString(@"hh\:mm\:ss") : "—");
        _fCasts.Show($"{caught:N0}/{casts:N0}");
        _fHooked.Show(casts > 0 ? $"%{100.0 * caught / casts:0}" : "—");
        _fMissed.Show(missed.ToString("N0"));
        _fPulled.Show(engine?.LastPullMs is int ms and > 0 ? $"{ms / 1000.0:0.00} sn" : "—");
        var binsNow = engine?.PullBins() ?? FishPulls.Bins([]);
        var best = binsNow.Where(b => b.Pulls >= 5).OrderByDescending(b => b.Rate).FirstOrDefault();
        _fChart.Show([.. binsNow.Select(b => (b.Name, b.Rate, b.Caught, b.Pulls))], best == null ? -1 : binsNow.IndexOf(best));
        var pulls = engine?.FishLog() ?? [];
        if (pulls.Count == 0 && _fPulls.Items.Count == 0 || pulls.Count > 0 && (pulls.Count != _fPulls.Items.Count || !Equals(_fPulls.Items[0], pulls[0])))
        {
            _fPulls.BeginUpdate();
            _fPulls.Items.Clear();
            if (pulls.Count == 0)
                _fPulls.Items.Add("Henüz çekiş yok.");
            else
                _fPulls.Items.AddRange([.. pulls]);
            _fPulls.EndUpdate();
        }
    }

    /// <summary>A fish is hooked: the player answers the game's picture question — a sound and the game in front.</summary>
    private void FishQuestionShown(bool showing)
    {
        if (!showing || _engine is not { } engine)
            return;
        System.Media.SystemSounds.Exclamation.Play();
        WriteLog("🎣 Balık takıldı! Oyundaki resim sorusunu cevapla.");
        if (Win32.IsWindow(engine.Window.Handle))
        {
            if (Win32.IsIconic(engine.Window.Handle))
                Win32.ShowWindow(engine.Window.Handle, 9);
            Win32.SetForegroundWindow(engine.Window.Handle);
        }
    }

    /// <summary>A big button's text: the action, its key on the line under it (both centred).</summary>
    private static string Caption(string action, string key) => action + "\n" + key;

    private static string Ago(DateTime at)
    {
        int minutes = (int)(DateTime.Now - at).TotalMinutes;
        return minutes < 1 ? "şimdi" : minutes < 60 ? $"{minutes} dk önce" : minutes < 1440 ? $"{minutes / 60} sa önce" : $"{minutes / 1440} gün önce";
    }

    // ── Layout helpers ───────────────────────────────────────────

    private static Label Tile(Control parent, string caption, int x, int y, bool small = false, int width = 100)
    {
        var tile = new Panel { Bounds = new Rectangle(x, y, width, 60), BackColor = Theme.Inset };
        parent.Controls.Add(tile);
        Theme.Label(tile, caption, new Rectangle(8, 6, width - 10, 16), 7f, Theme.Muted, FontStyle.Bold);
        return Theme.Label(tile, "—", new Rectangle(8, 22, width - 10, 34), small ? 8.5f : 13f, Theme.Text, FontStyle.Bold);
    }

    private static Meter AddMeter(Control parent, string caption, Color fill, int y)
    {
        var meter = new Meter { Bounds = new Rectangle(16, y, 314, 28), Caption = caption, Fill = fill };
        parent.Controls.Add(meter);
        return meter;
    }

    /// <summary>Puts the profile's values back into the setting controls (another character's settings came in).</summary>
    private readonly List<Action> _reloads = [];
    private bool _reloading;

    /// <summary>Attack on or off ("Sadece eşya topla"): for this character, at once (also while the bot runs).</summary>
    private void ToggleAttack()
    {
        _profile.Attack = !_profile.Attack;
        SaveProfile();
        ReloadSettings();
        WriteLog(_profile.Attack
            ? "⚔ Saldırı açıldı: bot yeniden moblara saldırıyor."
            : "✋ Saldırı kapatıldı: bot hiçbir moba vurmuyor, sadece yerdeki eşyaları topluyor.");
    }

    private void ShowAttack()
    {
        if (_mode == MarketMode)
        {
            _attackButton.Text = "Pazar penceresi";
            _attackButton.Icon = "expand";
            _attackButton.Kind = ButtonKind.Ghost;
            return;
        }
        bool on = _profile.Attack;
        _attackButton.Text = on ? "Saldırı açık" : "Sadece topla";
        _attackButton.Icon = on ? "sword" : "bag";
        _attackButton.Kind = on ? ButtonKind.Ghost : ButtonKind.Amber;
    }

    private void ReloadSettings()
    {
        _reloading = true;
        try { foreach (var reload in _reloads) reload(); }
        finally { _reloading = false; }
    }

    private void NumberRow(Control parent, string caption, int x, int y, decimal min, decimal max, Func<double> value,
        int decimals, decimal step, Action<double> apply)
    {
        Theme.Label(parent, caption, new Rectangle(x, y, 190, 26), 9f, Theme.Muted);
        // A slider when the range is a few hundred steps at most; a number box for the big ones (yang).
        if ((max - min) / step <= 400)
        {
            var slider = new Slider { Bounds = new Rectangle(x + 196, y, 164, 26), Minimum = min, Maximum = max, Step = step, Decimals = decimals,
                                      Value = Math.Clamp((decimal)value(), min, max), Font = new Font("Segoe UI", 9f) };
            slider.ValueChanged += (_, _) => { if (_reloading) return; apply((double)slider.Value); SaveProfile(); };
            _reloads.Add(() => slider.Value = Math.Clamp((decimal)value(), min, max));
            parent.Controls.Add(slider);
            return;
        }
        var number = Theme.Number(parent, new Rectangle(x + 196, y, 90, 26), min, max,
            Math.Clamp((decimal)value(), min, max), decimals, step);
        number.ValueChanged += (_, _) => { if (_reloading) return; apply((double)number.Value); SaveProfile(); };
        _reloads.Add(() => number.Value = Math.Clamp((decimal)value(), min, max));
    }

    private void Check(Control parent, string text, int x, int y, int width, Func<bool> value, Action<bool> apply)
    {
        var box = new Toggle
        {
            Text = text,
            Checked = value(),
            Bounds = new Rectangle(x, y, width, 26),
            ForeColor = Theme.Text,
            BackColor = parent.BackColor,
            Font = new Font("Segoe UI", 9f)
        };
        box.CheckedChanged += (_, _) => { if (_reloading) return; apply(box.Checked); SaveProfile(); };
        _reloads.Add(() => box.Checked = value());
        parent.Controls.Add(box);
    }

    private TextBox TextRow(Control parent, string caption, int x, int y, Func<string> value, Action<string> apply, int width = 220)
    {
        Theme.Label(parent, caption, new Rectangle(x, y, 130, 26), 9f, Theme.Muted);
        var box = new TextBox
        {
            Text = value(),
            BackColor = Theme.Inset,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9f),
        };
        box.TextChanged += (_, _) => { if (_reloading) return; apply(box.Text); SaveProfile(); };
        _reloads.Add(() => box.Text = value());
        parent.Controls.Add(new Field(box) { Bounds = new Rectangle(x + 134, y - 1, width, 28) });
        return box;
    }

    private static void Hint(Control parent, string text, int y)
    {
        var label = Theme.Label(parent, text, new Rectangle(0, y, 918, 300 - y), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);
        label.Tag = "hint";
    }

    /// <summary>A page's explanations under all its settings (they sat over the last settings on some pages), each as tall as its text.</summary>
    private static void StackHints(Panel page)
    {
        var hints = page.Controls.OfType<Label>().Where(l => (string?)l.Tag == "hint").OrderBy(l => l.Top).ToList();
        if (hints.Count == 0)
            return;
        var others = page.Controls.Cast<Control>().Where(c => !hints.Contains(c)).ToList();
        int y = (others.Count > 0 ? others.Max(c => c.Bottom) : 0) + 18;
        foreach (var hint in hints)
        {
            int h = TextRenderer.MeasureText(hint.Text, hint.Font, new Size(hint.Width, 0), TextFormatFlags.WordBreak).Height + 4;
            hint.Bounds = new Rectangle(0, y, page.Width, h);
            y += h + 10;
        }
    }

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
        // The card at the top stays (start / stop); under it the panel or the settings page.
        _dashBottom.Visible = index < 0;
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

        static void Mark(NavButton button, bool on) => button.Selected = on;
        // The computer's details fresh each time the Gelişmiş page opens.
        if (index == 7)
            ShowMachine();
    }

    // ── Windows ──────────────────────────────────────────────────

    private MiniBar? _mini;

    /// <summary>The bot as one small bar under its game (the full window hidden).</summary>
    private void GoMini()
    {
        if (IsDisposed)
            return;
        _mini ??= new MiniBar(MiniInfoNow, ToggleBot, () => StopBot("kullanıcı durdurdu", byUser: true), GoBig, ArrangeAll,
            () => { if (_mode == MarketMode) OpenMarketWindow(); else if (_mode == FarmMode) ToggleAttack(); }, CloseFromMini, FillWithGame);
        _mini.ChangeChannel ??= () => WriteLog(_engine?.ChangeChannelNow() ?? "Önce oyunu bağla.");
        if (!_mini.Visible)
            _mini.Show();
        if (Visible)
            Hide();
        if (!_profile.MiniMode)
        {
            _profile.MiniMode = true;
            SaveProfile();
        }
    }

    /// <summary>The small bar's ✕: closes SekoBot (asks first while the bot or a scan is running).</summary>
    private void CloseFromMini()
    {
        bool busy = _engine is { } e && (e.BotRunning || e.ScanningMarket || e.ScanningArea);
        if (busy && !AskOnTop("Bot çalışıyor. PARDUS kapatılsın mı?"))
            return;
        Close();
    }

    /// <summary>
    /// A yes / no question shown over everything, next to the small bar: the bar never takes the focus (it
    /// mustn't take it from the game), so a question it owned opened behind the games (seen).
    /// </summary>
    private bool AskOnTop(string question)
    {
        var near = _mini is { Visible: true } bar ? bar.Bounds : Bounds;
        using var owner = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = true, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Size = new Size(1, 1),
            Location = new Point(near.Left + near.Width / 2, near.Top + near.Height / 2),
        };
        owner.Show();
        Win32.SetForegroundWindow(owner.Handle);
        owner.Activate();
        return MessageBox.Show(owner, question, "PARDUS", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    /// <summary>How long the work of this tab has run ("" when idle): farm, market watch or fishing.</summary>
    private string ElapsedNow()
    {
        if (_engine is not { } e)
            return "";
        TimeSpan? t = _mode switch
        {
            FishingMode => e.Fishing || e.FishingPaused ? e.FishingTime : null,
            MarketMode => e.ScanningMarket && e.MarketSince is DateTime since ? DateTime.Now - since : null,
            _ => e.BotRunning ? e.Stats.BotUptime : null,
        };
        return t is TimeSpan v ? (v.TotalHours >= 1 ? $"{(int)v.TotalHours}:{v.Minutes:00}:{v.Seconds:00}" : v.ToString(@"mm\:ss")) : "";
    }

    // ── EXP of the last 10 minutes (the small bar) ──
    private readonly Queue<(DateTime At, long Exp)> _expSamples = new();

    /// <summary>The session's EXP now and then; what the last 10 minutes gave ("" until a minute is known).</summary>
    private string Exp10()
    {
        if (_engine?.Stats.Session is not { } session || _engine.BotRunning != true)
        {
            _expSamples.Clear();
            return "";
        }
        var now = DateTime.Now;
        if (_expSamples.Count > 0 && session.Exp < _expSamples.Last().Exp)
            _expSamples.Clear(); // a new session
        if (_expSamples.Count == 0 || now - _expSamples.Last().At >= TimeSpan.FromSeconds(10))
            _expSamples.Enqueue((now, session.Exp));
        while (_expSamples.Count > 1 && now - _expSamples.Peek().At > TimeSpan.FromMinutes(10))
            _expSamples.Dequeue();
        var first = _expSamples.Peek();
        return now - first.At < TimeSpan.FromMinutes(1) ? "" : "+" + Short(session.Exp - first.Exp);
    }

    /// <summary>The full window again.</summary>
    private void GoBig()
    {
        _mini?.Hide();
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        FitToScreen();
        Activate();
        if (_profile.MiniMode)
        {
            _profile.MiniMode = false;
            SaveProfile();
        }
    }

    // ── Logging in again after a maintenance ──
    private bool _loginSeen, _loginEnlarged;
    private DateTime _nextLoginAt = DateTime.MinValue, _enlargedAt;
    private Rectangle _beforeEnlarged, _grownTo;
    private int _loginTries;

    /// <summary>
    /// Every 2 s: on the login screen the login button is pressed every few minutes (the game's own saved
    /// name and password; the button is cut off in a small window, so the game is made big for the press);
    /// through to the character select: a message, the windows arranged again.
    /// </summary>
    private void LoginTick()
    {
        if (_engine is not { } e || !Win32.IsWindow(e.Window.Handle))
            return;
        // Only for a game kicked out while its bot was working; a bot left standing doesn't log in.
        var s = _profile.AutoLogin && (e.KickedWhileWorking || _loginEnlarged) && !e.BotRunning && !e.Fishing && !e.ScanningMarket ? e.LoginNow() : null;
        var now = DateTime.Now;
        // Made big for a try: put back as soon as the try is answered (the button back), at the latest after 30 s —
        // whatever else happens (seen: windows left big).
        if (_loginEnlarged)
        {
            bool answered = _pressedAt > _enlargedAt && now - _pressedAt > TimeSpan.FromSeconds(2.5) && s is { Waiting: false };
            if (s is not { Showing: true } || s.LoggedIn || answered || now - _enlargedAt > TimeSpan.FromSeconds(30))
                PutGameBack(e);
        }
        if (s == null)
            return;
        if (s.LoggedIn)
        {
            e.KickedWhileWorking = false;
            if (_loginSeen)
            {
                _loginSeen = false;
                string text = $"🟢 Sunucu açıldı: giriş yapıldı ({_loginTries} deneme), karakter seçim ekranında.";
                WriteLog(text);
                e.Notify(text);
            }
            return;
        }
        if (!s.Showing)
            return;
        if (!_loginSeen)
        {
            _loginSeen = true;
            _loginTries = 0;
            // The SekoBots one after the other, not all at the same moment.
            _nextLoginAt = now + TimeSpan.FromSeconds(10 + 25 * (Instance.Number - 1));
            WriteLog($"Oyun giriş ekranında. Sunucu açılana kadar her {_profile.AutoLoginMinutes:0.#} dakikada bir giriş denenecek (oyunun kayıtlı bilgileriyle; şifreye dokunulmaz).");
        }
        if (s.Waiting || now < _nextLoginAt)
            return;
        Win32.GetClientRect(e.Window.Handle, out var client);
        bool cut = s.Button is not { } b || b.Bottom > client.Bottom - client.Top || b.Right > client.Right - client.Left || b.Width < 4;
        if (cut)
        {
            if (!_loginEnlarged)
            {
                // The button is under the window's bottom edge (the game doesn't shrink its screens below
                // 480 pixels): the game grown in its place just enough to show the screen whole, for a
                // moment — one SekoBot at a time (seen: two games big at once, the screen a mess).
                if (!TakeLoginTurn())
                    return;
                _beforeEnlarged = WindowLayout.Visible(e.Window.Handle);
                _grownTo = WindowLayout.GrowForUi(e.Window.Handle);
                if (_grownTo.IsEmpty)
                {
                    // Big enough already, still cut off: the whole work area as before.
                    WindowLayout.FillWithGame(e.Window.Handle, MiniBar.HeightAt(DeviceDpi) + 2);
                    _grownTo = WindowLayout.Visible(e.Window.Handle);
                }
                _loginEnlarged = true;
                _enlargedAt = now;
                WindowLayout.TellAllMini();
                return;
            }
            if (now - _enlargedAt < TimeSpan.FromSeconds(15))
                return; // drawing after the resize
            _nextLoginAt = now + TimeSpan.FromMinutes(Math.Max(1, _profile.AutoLoginMinutes));
            WriteLog("Giriş düğmesi görünmüyor; bir sonraki denemede yeniden bakılacak.");
            PutGameBack(e);
            return;
        }
        e.PressLogin(s.Button!.Value);
        _loginTries++;
        _pressedAt = now;
        _nextLoginAt = now + TimeSpan.FromMinutes(Math.Max(1, _profile.AutoLoginMinutes));
        if (_loginTries == 1 || _loginTries % 10 == 0)
            WriteLog($"Giriş denendi ({_loginTries}. kez); sunucu açılmadıysa {_profile.AutoLoginMinutes:0.#} dakika sonra yeniden.");
    }

    private DateTime _pressedAt = DateTime.MinValue;
    private Mutex? _loginTurn;
    private bool _haveLoginTurn;

    /// <summary>Only one SekoBot makes its game big for a login try at a time (a mutex shared by them all).</summary>
    private bool TakeLoginTurn()
    {
        try
        {
            _loginTurn ??= new Mutex(false, @"Local\SekoBot.LoginTry");
            _haveLoginTurn = _loginTurn.WaitOne(0);
        }
        catch (AbandonedMutexException) { _haveLoginTurn = true; } // a SekoBot closed while holding it
        return _haveLoginTurn;
    }

    private void LetGoLoginTurn()
    {
        if (!_haveLoginTurn)
            return;
        _haveLoginTurn = false;
        try { _loginTurn?.ReleaseMutex(); } catch (ApplicationException) { }
    }

    /// <summary>The game made big for a login try back where it was (unless the user moved it meanwhile).</summary>
    private void PutGameBack(BotEngine e)
    {
        _loginEnlarged = false;
        var seen = WindowLayout.Visible(e.Window.Handle);
        // Still where it was grown to (the user didn't put it elsewhere): back to its place.
        bool same = Math.Abs(seen.Left - _grownTo.Left) < 12 && Math.Abs(seen.Top - _grownTo.Top) < 12
                    && Math.Abs(seen.Width - _grownTo.Width) < 12 && Math.Abs(seen.Height - _grownTo.Height) < 12;
        if (same && !_beforeEnlarged.IsEmpty)
            WindowLayout.PutBack(e.Window.Handle, _beforeEnlarged);
        WindowLayout.TellAllMini();
        LetGoLoginTurn();
    }

    /// <summary>This SekoBot's game big (a band left under it for the bar); already big: all arranged again.</summary>
    private void FillWithGame()
    {
        if (_engine is not { } e || !Win32.IsWindow(e.Window.Handle))
        {
            WriteLog("Oyun bağlı değil.");
            return;
        }
        if (!WindowLayout.FillWithGame(e.Window.Handle, MiniBar.HeightAt(DeviceDpi) + 2))
            ArrangeAll();
        WindowLayout.TellAllMini();
    }

    /// <summary>Every game window side by side (a band left at the bottom), every SekoBot small under its game.</summary>
    private void ArrangeAll()
    {
        int band = MiniBar.HeightAt(DeviceDpi) + 2;
        var near = _engine != null && Win32.IsWindow(_engine.Window.Handle) ? WindowLayout.Visible(_engine.Window.Handle) : Bounds;
        int count = WindowLayout.ArrangeGames(near, band);
        WriteLog(count == 0
            ? "Açık oyun penceresi bulunamadı."
            : $"{count} oyun penceresi ızgaraya dizildi (her biri kendi oranında küçüldü, sıkıştırılmadı); her PARDUS kendi oyununun altına küçüldü. Büyütmek için şeride çift tıkla.");
        WindowLayout.TellAllMini();
    }

    private MiniInfo MiniInfoNow()
    {
        Rectangle? game = null;
        if (_engine is { } engine && Win32.IsWindow(engine.Window.Handle) && !Win32.IsIconic(engine.Window.Handle))
            game = WindowLayout.Visible(engine.Window.Handle);
        var state = _engine?.State;
        var (hpPotions, mpPotions) = _engine?.PotionsLeft() ?? (null, null);
        string? market = null;
        if (_mode == MarketMode && _engine is { } m)
        {
            var c = m.MarketCounts;
            var book = m.ScanningMarket ? m.CurrentMarket() : null;
            market = (m.ScanningMarket && m.MarketWatching ? $"{c.Round}. tur · " : "") + $"{c.Read:N0} dükkan okundu · {c.Deals} kelepir · {c.Wanted} aranan" +
                     (book != null ? $" · listede {book.Items:N0} ürün" : "");
        }
        if (_mode == FishingMode && _engine is { } f)
        {
            var (casts, _, _, missed, caught) = f.FishCounts;
            market = !f.Fishing ? "🎣 Balık tutma kapalı"
                : $"🎣 {caught}/{casts} yakalandı · %{(casts > 0 ? 100.0 * caught / casts : 0):0} başarı · {missed} kaçtı" +
                  (f.LastPullMs > 0 ? $" · son çekiş {f.LastPullMs / 1000.0:0.00} sn" : "");
        }
        return new MiniInfo(_engine?.CharacterName ?? "", _status.Text, _status.ForeColor, state?.Hp, state?.Mp,
            _statExpRate.Text, _statMobs.Text, _mode switch
            {
                MarketMode => _engine?.ScanningMarket == true,
                FishingMode => _engine?.Fishing == true,
                _ => _engine?.BotRunning == true,
            }, game,
            _engine is { } e && Win32.IsWindow(e.Window.Handle) ? e.Window.Handle : 0, hpPotions, mpPotions, Attack: _profile.Attack, Market: market,
            Exp10: Exp10(), Elapsed: ElapsedNow(),
            Items: _engine?.SessionItems.Rows().Select(r => (r.Name, r.Picked, r.Thrown, r.Sold)).ToList(),
            Level: _engine?.World?.Level ?? 0,
            Place: _engine?.World is { Server.Length: > 0 } w ? w.Server + (w.Channel > 0 ? $" K{w.Channel}" : "") : "");
    }

    protected override void WndProc(ref Message m)
    {
        // Another SekoBot (or this one) arranged the windows: go small under the game.
        if (m.Msg == (int)WindowLayout.MiniMessage && m.WParam == 1)
            BeginInvoke(new Action(GoMini));
        // One SekoBot's "all bots" button: every SekoBot starts / pauses / stops its own work.
        if (m.Msg == (int)WindowLayout.AllMessage)
        {
            int what = (int)m.WParam;
            BeginInvoke(new Action(() => AllBots(what)));
        }
        base.WndProc(ref m);
    }

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
            // Waiting for its own game (closed, being updated): only the one from its own folder.
            var free = ready.Where(w => !Instance.GameTaken(w.ProcessId) && MayTake(w)).OrderBy(w => GameStarted(w.ProcessId)).ToList();
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
                {
                    _autoAttached = false;
                    GameGone(engine);
                }
                Disconnect(message);
            });
            engine.AreaScanFinished += _ => PostUi(() => { SaveProfile(); UpdateControls(); UpdateStats(); });
            engine.MarketScanFinished += _ => PostUi(() =>
            {
                UpdateControls();
                UpdateStats();
                if (BotEngine.LatestReport is { } report && DateTime.Now - File.GetLastWriteTime(report) < TimeSpan.FromMinutes(1))
                    OpenFile(report);
            });
            engine.BotStopped += reason => PostUi(() => { _stopReason = reason; UpdateControls(); UpdateStats(); });
            engine.FishQuestion += showing => PostUi(() => FishQuestionShown(showing));
            engine.FishingFinished += _ => PostUi(() => { UpdateControls(); UpdateStats(); });
            _engine = engine;
            Instance.TakeGame(window.ProcessId);
            engine.Start(previewFps: 30);
            engine.ShowingPreview(Visible);
            _frameSequence = 0;
            _gameMinimized = false;
            _autoAttached = true;
            Reconnected(engine);
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

    /// <summary>This tab's work going (farm, market watch or fishing).</summary>
    private bool WorkingNow() => _mode switch
    {
        MarketMode => _engine?.ScanningMarket == true,
        FishingMode => _engine?.Fishing == true,
        _ => _engine?.BotRunning == true,
    };

    /// <summary>The "all bots" message: this SekoBot starts, pauses or stops its own tab's work.</summary>
    private void AllBots(int what)
    {
        if (what == WindowLayout.AllUpdate)
        {
            _ = UpdateAndResumeAsync();
            return;
        }
        if (_engine == null)
            return;
        switch (what)
        {
            case WindowLayout.AllStart when !WorkingNow():
            case WindowLayout.AllPause when WorkingNow():
                WriteLog(what == WindowLayout.AllStart ? "Tüm botlar başlatıldı." : "Tüm botlar duraklatıldı.");
                ToggleBot();
                break;
            case WindowLayout.AllStop:
                if (WorkingNow() || _engine.FishingPaused)
                {
                    WriteLog("Tüm botlar durduruldu.");
                    StopBot("tüm botlar durduruldu", byUser: true);
                }
                break;
        }
    }

    private void ToggleBot()
    {
        if (_engine == null)
        {
            WriteLog("Önce oyun açık olmalı; açılınca kendiliğinden bağlanır.");
            return;
        }
        // PAZAR: the start key / button starts and stops the continuous market watch.
        if (_mode == MarketMode)
        {
            if (_engine.ScanningMarket)
                _engine.StopMarketScan();
            else
                StartMarket(true);
            UpdateControls();
            return;
        }
        if (_mode == FishingMode)
        {
            if (_engine.Fishing)
                _engine.StopFishing(pause: true);
            else if (!_engine.StartFishing(out string fishError))
                WriteLog("🎣 " + fishError);
            UpdateControls();
            UpdateStats();
            return;
        }
        if (_engine.BotRunning || _engine.ScanningMarket || _engine.ScanningArea)
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
        if (_engine is { Fishing: true } || _engine is { FishingPaused: true })
        {
            var fisher = _engine;
            bool wasFishing = fisher.Fishing;
            fisher.StopFishing();
            if (!wasFishing)
            {
                WriteLog("🎣 Balık tutma sayaçları sıfırlanacak (duraklatma bitti).");
                UpdateControls();
                return;
            }
            WriteLog("🎣 Balık tutma durduruluyor…");
            return;
        }
        // The stop key and button also stop a market or area scan.
        if (_engine is { } scanning && (scanning.ScanningMarket || scanning.ScanningArea))
        {
            if (scanning.ScanningMarket)
                scanning.StopMarketScan();
            if (scanning.ScanningArea)
                scanning.StopAreaScan();
            WriteLog("Tarama durduruluyor…");
            return;
        }
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

    private void OpenFile(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            WriteLog("Açılamadı: " + path);
        }
    }

    private MarketForm? _marketWindow;

    /// <summary>The market window (one at a time): search the market, the continuous watch, wanted items.</summary>
    private void OpenMarketWindow()
    {
        if (_marketWindow is { IsDisposed: false })
        {
            if (_marketWindow.WindowState == FormWindowState.Minimized)
                _marketWindow.WindowState = FormWindowState.Normal;
            _marketWindow.Activate();
            return;
        }
        _marketWindow = new MarketForm(() => _engine, _profile, SaveProfile, text => PostUi(() => WriteLog(text)), StartMarket, () => _engine?.StopMarketScan());
        _marketWindow.FormClosed += (_, _) => ReloadSettings();
        if (Visible)
            _marketWindow.Show(this);
        else
            _marketWindow.Show();
    }

    /// <summary>Starts a market scan, or the continuous watch.</summary>
    private void StartMarket(bool continuous)
    {
        if (_engine == null)
        {
            WriteLog("Önce oyun açık olmalı.");
            return;
        }
        if (_engine.ScanningMarket)
            return;
        if (!_engine.StartMarketScan(out string error, continuous))
            WriteLog(error);
        UpdateControls();
        UpdateStats();
    }

    private void ToggleMarketScan()
    {
        if (_engine == null)
        {
            WriteLog("Önce oyun açık olmalı.");
            return;
        }
        if (_engine.ScanningMarket)
        {
            _engine.StopMarketScan();
            return;
        }
        if (!_engine.StartMarketScan(out string error))
            WriteLog(error);
        UpdateControls();
        UpdateStats();
    }

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
        // Minimized by mistake while the bot runs: opened again (without taking the focus) and the bot
        // carries on. Only when that keeps failing does it stop.
        if (minimized && _engine.BotRunning)
        {
            var now = DateTime.UtcNow;
            if (now - _restoreSince > TimeSpan.FromSeconds(20))
            {
                _restoreSince = now;
                _restoreTries = 0;
            }
            if (++_restoreTries <= 60)
            {
                if (_restoreTries == 1)
                    WriteLog("Oyun simge durumuna alındı; bot pencereyi geri açıp devam ediyor.");
                WindowService.RestoreQuietly(_engine.Window);
                minimized = _engine.GameMinimized;
            }
        }
        if (minimized != _gameMinimized)
        {
            _gameMinimized = minimized;
            if (minimized && _engine.BotRunning)
            {
                StopBot("oyun simge durumunda kaldı");
                WriteLog("Oyun simge durumundan geri açılamadı: Windows küçültülmüş pencereyi çizmez, görüntü alınamaz.");
            }
            _preview.Placeholder = minimized
                ? "Oyun simge durumunda\n\nWindows küçültülmüş pencereyi çizmediği için görüntü yok.\nOyunu geri açmak için buraya tıkla."
                : "Oyun penceresi bekleniyor";
            _preview.Image = minimized ? null : _frameBitmap;
            _preview.State = null;
            _preview.Invalidate();
        }
        // Hidden (the small bar): no picture to draw.
        if (minimized || !Visible)
            return;
        if (_engine.TryCopyLatest(ref _frameBitmap, ref _frameSequence))
        {
            _preview.Image = _frameBitmap;
            _preview.State = _engine.State;
            _preview.Overlay = _engine.IsRecording ? "⏺ KAYIT" : "";
            _preview.Invalidate();
        }
    }

    private Panel _body = null!;
    private Control _header = null!;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToScreen();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(FitToScreen);
    }

    /// <summary>
    /// The window as big as it is laid out, but never bigger than the screen it is on: what doesn't fit scrolls
    /// (the header and the control bar always show), and the window is kept on the screen.
    /// </summary>
    private void FitToScreen()
    {
        if (IsDisposed || _body == null || WindowState != FormWindowState.Normal)
            return;
        var work = Screen.FromControl(this).WorkingArea;
        int gap = _header.Left;
        int contentBottom = _body.Controls.Cast<Control>().Max(c => c.Bottom) - _body.AutoScrollPosition.Y;
        int contentRight = _body.Controls.Cast<Control>().Max(c => c.Right) - _body.AutoScrollPosition.X;
        var need = new Size(Math.Max(_header.Right, contentRight) + gap, _body.Top + contentBottom + gap);
        var size = FitSize(need, work.Size, SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi),
            SystemInformation.GetHorizontalScrollBarHeightForDpi(DeviceDpi));
        _body.AutoScrollMargin = new Size(gap, gap);
        if (ClientSize != size)
            ClientSize = size;
        _body.Bounds = new Rectangle(0, _body.Top, ClientSize.Width, ClientSize.Height - _body.Top);
        int x = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width));
        int y = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        if (x != Left || y != Top)
            Location = new Point(x, y);
    }

    /// <summary>
    /// The window's size on a screen of <paramref name="work"/>: <paramref name="need"/> when it fits; else cut to the
    /// screen, a scroll bar's room added the other way (so a cut height doesn't make the width scroll too).
    /// </summary>
    internal static Size FitSize(Size need, Size work, int verticalBar, int horizontalBar)
    {
        if (need.Height > work.Height)
            need.Width += verticalBar;
        if (need.Width > work.Width)
            need.Height += horizontalBar;
        return new Size(Math.Min(need.Width, work.Width), Math.Min(need.Height, work.Height));
    }

    /// <summary>The window is laid out at full size and then shrunk by this much.</summary>
    private const float UiScale = 0.84f;

    private static void Compact(Control root, float k)
    {
        root.Scale(new SizeF(k, k));
        void Fonts(Control c)
        {
            c.Font = new Font(c.Font.FontFamily, Math.Max(7f, c.Font.Size * (k + 0.03f)), c.Font.Style);
            foreach (Control child in c.Controls)
                Fonts(child);
        }
        Fonts(root);
    }

    /// <summary>This run's picked / thrown items, redrawn when they change.</summary>
    private void UpdateRunItems()
    {
        var list = _engine?.SessionItems;
        if (list == null || list.Version == _runItemsVersion)
            return;
        _runItemsVersion = list.Version;
        var rows = list.Rows();
        _runItems.BeginUpdate();
        _runItems.Items.Clear();
        foreach (var (_, name, picked, thrown, sold) in rows)
            _runItems.Items.Add(new ListViewItem([name, picked > 0 ? picked.ToString("N0") : "", thrown > 0 ? thrown.ToString("N0") : "", sold > 0 ? sold.ToString("N0") : ""])
            {
                ForeColor = thrown + sold > 0 && picked <= thrown + sold ? Theme.Muted : Theme.Text,
            });
        _runItems.EndUpdate();
        _runItemsInfo.Text = rows.Count == 0 ? "Bot başlayınca dolar; her turda yenilenir."
            : $"{rows.Count} çeşit · alınan {rows.Sum(r => r.Picked):N0} · atılan {rows.Sum(r => r.Thrown):N0} · satılan {rows.Sum(r => r.Sold):N0}";
    }

    /// <summary>
    /// The game shows a character this SekoBot hasn't played yet (or another one than before): its
    /// own settings come in (how it fights, hunts, loots, where it farms, what it learned) and show in
    /// the setting pages. A character seen for the first time starts from this SekoBot's settings.
    /// </summary>
    private void UseCharacter(string name)
    {
        if (name.Length == 0 || name == ProfileStore.Character)
            return;
        bool known = CharacterSettings.Load(name, _profile);
        if (!known)
            CharacterSettings.Fresh(_profile);
        ProfileStore.Character = name;
        SaveProfile();
        ReloadSettings();
        // The character's own page (its market / farm / fishing) — unless something runs on this one now.
        int mode = Math.Clamp(_profile.UiMode, FarmMode, FishingMode);
        if (mode != _mode && _engine is not { BotRunning: true } && _engine is not { ScanningMarket: true } && _engine is not { Fishing: true })
        {
            SetMode(mode, save: false);
            WriteLog($"👤 {name}: bu karakterin sayfası açıldı ({(mode == MarketMode ? "Pazar" : mode == FishingMode ? "Balık" : "Farm")}).");
        }
        if (known)
        {
            WriteLog($"👤 {name}: bu karakterin kendi ayarları yüklendi (toplanacak mob {_profile.PullMaxMobs}, HP iksiri %{_profile.PotionHpBelow * 100:0}" +
                     (_profile.PullLearned.Count > 0 ? ", öğrenilen: " + string.Join(", ", _profile.PullLearned.Select(l => $"{l.Key} haritasında en fazla {l.Value} mob")) : "") + ").");
        }
        else
            WriteLog($"👤 {name}: ilk kez görüldü; dövüş ayarları baştan (her haritada {Brain.PullStartMobs} mobla başlar, rahat geçen " +
                     "dövüşlerle kendi kaldırabileceği kadar artar, zorlananlarla azalır; öğrendiği karakterle saklanır). " +
                     "Toplanacak eşya seçimlerin korundu. Bundan sonra yapılan değişiklikler bu karaktere ait.");
    }

    private void UpdateStats()
    {
        ShowTop();
        ShowNumbers();
        UpdateRunItems();
        // The game server in the header (market prices are kept per server).
        if (_engine?.World is { Server.Length: > 0 } here && _connection.ForeColor == Theme.Green)
        {
            string where = here.Channel > 0 ? $"●  {here.Server} · Kanal {here.Channel}" : $"●  {here.Server}";
            if (_connection.Text != where)
                _connection.Text = where;
        }
        string name = _engine?.CharacterName ?? "";
        UseCharacter(name);
        ShareStatus(name);
        CheckWhisperInbox();
        string identity = "v" + BuildInfo.Version + (Instance.IsFirst ? "" : $"  ·  {Instance.Number}. PARDUS") + (name.Length > 0 ? $"  ·  {name}" : "");
        if (_identity.Text != identity)
        {
            _identity.Text = identity;
            Text = "PARDUS" + (Instance.IsFirst ? "" : $" {Instance.Number}") + (name.Length > 0 ? $" — {name}" : "");
        }
        _calibrationState.Text = _engine?.World != null ? "Her şey bellekten ✓" : "Bellek bekleniyor";
        _calibrationState.ForeColor = _engine?.World != null ? Theme.Green : Theme.Muted;
        string events = _engine?.EventsSummary() ?? "Bot çalışınca görünür.";
        // Nothing on now: the last run in its place (how it went and why it stopped), so the box isn't empty.
        if (!events.StartsWith('▶') && _engine?.LastSummary is { } last && _engine.BotRunning != true)
            events = (events == "Bot çalışınca görünür." ? "" : events + "\n\n") + "Son tur:\n" + last.Replace(" · ", "\n")
                     + (_stopReason is { Length: > 0 } why ? "\nDurma: " + why : "");
        if (_events.Text != events)
            _events.Text = events;
        _events.ForeColor = _events.Text.StartsWith('▶') ? Theme.Green : Theme.Muted;
        if (_bagState.Visible)
        {
            _bagState.Text = _engine?.BagSummary() ?? "Çanta: oyun bağlı değil.";
            _bagState.ForeColor = _bagState.Text.Contains("SIĞMIYOR") ? Theme.Yellow : Theme.Text;
        }

        if (_mode == MarketMode)
            UpdateMarketDash();
        if (_mode == FishingMode)
            UpdateFishingDash();
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
                  SkillKind.Heal => "can",
                  SkillKind.Area => "alan",
                  _ => sk.Traits.HasFlag(SkillTraits.Front) ? "önden" : "tek hedef",
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
        _farmState.Text = _profile.MySpot is { } mine
            ? $"📌 Kaydettiğin alan ({mine.MapName}, {mine.FoundAt:dd.MM HH:mm})" +
              (world != null && world.MapName == mine.MapName ? $" · {mine.Position.DistanceXY(world.Player) / 100:0} m uzakta" : "") +
              (_profile.UseFarmSpot ? "; bot buradan başlar." : "; 'Kayıtlı alanda başla' kapalı.")
            : _profile.FarmSpot is { } farm
            ?$"Kayıtlı alan ({farm.MapName}, {farm.FoundAt:dd.MM HH:mm}): {farm.Mobs} mob — {farm.Names}" +
              (world != null && world.MapName == farm.MapName ? $" · {farm.Position.DistanceXY(world.Player) / 100:0} m uzakta" : "")
            : "Kayıtlı alan yok. \"En iyi alanı bul\": karakter ~1 dk çevrede dolaşıp en verimli yeri bulur.";
        string scanText = _engine.ScanningArea ? "■  Taramayı durdur" : _profile.FarmSpot != null ? "🔄  Alanı güncelle" : "📍  En iyi alanı bul";
        if (_findArea.Text != scanText)
            _findArea.Text = scanText;
        string marketText = _engine.ScanningMarket ? "■  Durdur" : "🛒  Pazarı tara";
        if (_scanMarket.Text != marketText)
            _scanMarket.Text = marketText;
        if (_engine.MarketStatus is { Length: > 0 } market && _marketState.Text != market)
            _marketState.Text = market;
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

        if (_mode == MarketMode)
        {
            UpdateMarketDash();
            bool watching = _engine.ScanningMarket && _engine.MarketWatching;
            SetLine(_engine.ScanningMarket ? (watching ? "🔁 " : "🛒 ") + _engine.MarketStatus
                    : s.BotRunning ? "Farm botu çalışıyor (FARM sekmesinde)."
                    : $"Pazar takibi kapalı. Başlatmak için SÜREKLİ TAKİP ya da {StartKey}.",
                _engine.ScanningMarket ? Theme.Green : Theme.Text);
            return;
        }
        if (_mode == FishingMode)
        {
            UpdateFishingDash();
            SetLine(_engine.Fishing ? _engine.FishingStatus
                    : s.BotRunning ? "Farm botu çalışıyor (FARM sekmesinde)."
                    : $"Balık tutma kapalı. Başlatmak için BALIK TUT ya da {StartKey}.",
                _engine.Fishing ? Theme.Green : Theme.Text);
            return;
        }
        if (s.BotRunning)
        {
            _statTime.Text = s.BotUptime.ToString(@"hh\:mm\:ss");
            _statDeaths.Text = s.Deaths.ToString("N0");
            _statDeaths.ForeColor = s.Deaths > 0 ? Theme.Red : Theme.Text;
            string line = (s.DryRun ? "KURU ÇALIŞMA · " : "") + _engine.ActivityText + "  ·  " + s.Area;
            if (!_engine.StatsOk)
                // Without HP/MP the bot can't drink potions: make that impossible to miss.
                SetLine("⚠ HP/MP okunamıyor, iksir içilmiyor  ·  " + _engine.ActivityText, Theme.Red);
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
            return Whisper(WhisperInbox.CharacterOf(replyTo), player, text.Trim());
        string command = TelegramNotifier.Command(text);
        if (command is "yaz" or "cevap")
        {
            string rest = text.Trim().TrimStart('/');
            rest = rest[(rest.IndexOf(' ') is int space and > 0 ? space + 1 : rest.Length)..];
            // Answering a notice of another character ("Yanıtla" + "yaz Ali …"): written from that character.
            return WhisperSender.Parse(rest) is (string to, string message)
                ? Whisper(WhisperInbox.CharacterOf(replyTo), to, message)
                : "Kullanım: yaz İsim mesaj  (ya da fısıltı bildirimine \"Yanıtla\" ile cevap yaz)";
        }
        return TelegramCommand(command);
    }

    /// <summary>
    /// A message to a player from <paramref name="character"/>'s game: this one's when it plays it (or none
    /// is named), else handed to the SekoBot that plays it (only the first SekoBot reads Telegram).
    /// </summary>
    private string Whisper(string? character, string to, string text)
    {
        string mine = _engine?.CharacterName ?? "";
        if (character == null || character.Equals(mine, StringComparison.OrdinalIgnoreCase))
            return _engine?.QueueWhisper(to, text) ?? "Oyun bağlı değil.";
        if (WhisperInbox.SekoBotOf(character) is int other && other != Instance.Number)
            return WhisperInbox.Send(other, to, text)
                ? $"✉ {character} ({other}. PARDUS) {to} kişisine yazacak: \"{text}\". Gidince ondan haber gelir."
                : "Mesaj öbür PARDUS'a iletilemedi; birazdan tekrar dene.";
        return $"{character} şu an açık bir PARDUS'ta görünmüyor; mesaj gönderilemedi.";
    }

    /// <summary>Answers the first SekoBot handed to this one (its character was whispered to): written in this game.</summary>
    private void CheckWhisperInbox()
    {
        if (_engine is not { } engine)
            return;
        List<WhisperInbox.Letter> letters;
        try { letters = WhisperInbox.Take(Instance.Number); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        foreach (var letter in letters)
        {
            string result = engine.QueueWhisper(letter.To, letter.Text);
            WriteLog(result);
            engine.Notify(result);
        }
    }

    private string TelegramCommand(string command)
    {
        switch (command)
        {
            case "durum" or "status":
                return AllStatuses();
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

    private DateTime _statusSharedAt;

    /// <summary>This SekoBot's status for "durum" on Telegram: every SekoBot writes it (durum-N.txt) every
    /// 10 s, the first one (the one Telegram talks to) puts them all together.</summary>
    private void ShareStatus(string name)
    {
        if (DateTime.UtcNow - _statusSharedAt < TimeSpan.FromSeconds(10))
            return;
        _statusSharedAt = DateTime.UtcNow;
        try
        {
            Directory.CreateDirectory(ProfileStore.Directory);
            File.WriteAllText(StatusFile(Instance.Number), StatusTitle(name) + "\n" + TelegramStatus());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string StatusFile(int number) => Path.Combine(ProfileStore.Directory, $"durum-{number}.txt");

    private static string StatusTitle(string name) =>
        $"👤 {(name.Length > 0 ? name : "karakter bilinmiyor")}" + (Instance.IsFirst ? "" : $" ({Instance.Number}. PARDUS)");

    /// <summary>"durum": this SekoBot's status and every other SekoBot's open now (written within the last minute).</summary>
    private string AllStatuses()
    {
        var parts = new List<string> { StatusTitle(_engine?.CharacterName ?? "") + "\n" + TelegramStatus() };
        for (int n = 1; n <= 6; n++)
        {
            if (n == Instance.Number)
                continue;
            try
            {
                var file = new FileInfo(StatusFile(n));
                if (file.Exists && DateTime.UtcNow - file.LastWriteTimeUtc < TimeSpan.FromMinutes(1))
                    parts.Add(File.ReadAllText(file.FullName).Trim());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return string.Join("\n\n", parts);
    }

    private string TelegramStatus()
    {
        if (_engine == null)
            return "PARDUS açık; oyun bağlı değil.";
        var s = _engine.Stats;
        var world = _engine.World;
        var lines = new List<string> { _engine.BotRunning ? "▶ Bot çalışıyor" : "⏸ Bot duruyor" + (_stopReason != null ? $" ({_stopReason})" : "") };
        if (_engine.ScanningMarket)
        {
            var c = _engine.MarketCounts;
            lines.Add((_engine.MarketWatching ? $"🔁 Pazar takibi ({c.Round}. tur)" : "🛒 Pazar taraması") +
                      $": {c.Read:N0} dükkan okundu, {c.Deals} kelepir, {c.Wanted} aranan bulundu");
        }
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
        < 1_000_000 => (value / 1000.0).ToString("0") + " B",
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
        _gameButton.Text = connected ? (_engine!.CharacterName is { Length: > 0 } who ? who : "Bağlı") : "Oyunu seç";
        _startBot.Enabled = connected;
        _findArea.Enabled = connected && !running && _engine?.ScanningMarket != true;
        _scanMarket.Enabled = connected && !running && _engine?.ScanningArea != true;
        _scanMobs.Enabled = connected;
        var (startText, startIcon) = _mode == MarketMode
            ? (_engine?.ScanningMarket == true ? ("Takibi durdur", "stop") : ("Sürekli takip", "refresh"))
            : _mode == FishingMode ? (_engine?.Fishing == true ? ("Balığı durdur", "stop") : ("Balık tut", "fish"))
            : running ? ("Duraklat", "pause")
            : _engine?.ScanningMarket == true || _engine?.ScanningArea == true ? ("Taramayı durdur", "stop")
            : ("Başlat", "play");
        _startBot.Text = Caption(startText, StartKey);
        _startBot.Icon = startIcon;
        _startBot.Kind = startIcon == "play" || startIcon == "refresh" || startIcon == "fish" ? ButtonKind.Gold : ButtonKind.Ghost;
        if (_mode == MarketMode)
            _startBot.Enabled = connected && !running;
        if (_mode == FishingMode)
            _startBot.Enabled = connected && !running && _engine?.ScanningMarket != true;
        _stopBot.Enabled = running || _engine?.ScanningMarket == true || _engine?.ScanningArea == true || _engine?.Fishing == true;
        bool recording = _engine?.IsRecording == true;
        _record.Enabled = connected;
        _record.Text = recording ? "■  Kaydı bitir" : "⏺  Kaydet";
        _record.BackColor = recording ? Theme.Danger : Theme.Secondary;
        _record.FlatAppearance.MouseOverBackColor = recording ? Theme.DangerHover : Theme.SecondaryHover;
        _replay.Enabled = !_replaying;
        _dryRun.Enabled = !running;
        _calibrationHint.Text = !connected
            ? "HP/MP, konum, moblar ve hedef oyunun belleğinden okunur (PARDUS'u yönetici olarak aç)."
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
            if (startup) WriteLog($"PARDUS güncel (v{BuildInfo.Version}).");
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
        WriteLog($"Yeni sürüm {update.Version} doğrulandı. PARDUS yeniden başlatılıyor…");
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
        LogFile.Append(line);
        var entry = LogLine.Of(DateTime.Now, message);
        _logLines.Add(entry);
        if (_logLines.Count > 600)
        {
            _logLines.RemoveRange(0, 200);
            ShowLog();
        }
        else if (_logFilter.Length == 0 || entry.Kind == _logFilter)
        {
            AppendLog(entry);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        if (entry.Feed)
            _feed.Add(new Feed.Line(entry.At, entry.Tone, entry.Icon, entry.Short));
    }
}
