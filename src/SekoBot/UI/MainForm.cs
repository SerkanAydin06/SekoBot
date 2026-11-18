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
    private readonly Button _gameButton, _startBot, _stopBot, _record, _replay, _attackButton;
    private readonly ContextMenuStrip _gameMenu = new();
    private readonly CheckBox _dryRun;
    private readonly PreviewControl _preview;
    private readonly Label _connection, _status, _calibrationHint, _calibrationState, _technical;
    private readonly Label _statTime, _statMobs, _statRate, _statDeaths, _statPotions, _statExp, _statExpRate, _statLevel, _statYang;
    private readonly Label _positionState, _skillList, _farmState, _respawnState, _marketState;
    private readonly Button _findArea, _scanMarket;
    private readonly Button _scanMobs;
    private readonly Meter _hpMeter, _mpMeter, _targetMeter;
    private readonly Button[] _tabButtons;
    private readonly Panel[] _tabPages;
    private readonly Button _dashButton;
    private readonly Panel _dashboard, _dashBottom;
    private readonly ListView _runItems;
    private readonly Label _runItemsInfo;
    private int _runItemsVersion = -1;
    private readonly Card _settingsCard;
    private readonly Label _pageTitle, _pageInfo, _bagState, _events, _identity;

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
        Text = "SekoBot";
        ClientSize = new Size(1180, 920);
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
        _connection = Theme.Label(header, "○  Oyun bekleniyor", new Rectangle(540, 12, 246, 22), 9.5f, Theme.Muted,
            FontStyle.Bold, ContentAlignment.MiddleRight);
        foreach (Control c in new Control[] { header, title, version, _connection })
        {
            c.MouseDown += DragWindow;
            c.Cursor = Cursors.SizeAll;
        }
        _gameButton = Theme.Button(header, "Oyunu seç  ▾", new Rectangle(950, 7, 110, 32), Theme.Secondary, Theme.SecondaryHover);
        _gameButton.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _gameButton.Click += (_, _) => ShowGameMenu();
        // Many games on one screen: put the games side by side, every bot small under its game.
        var arrange = Theme.Button(header, "⊞  Diz", new Rectangle(794, 7, 74, 32), Theme.Secondary, Theme.SecondaryHover);
        arrange.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        arrange.Click += (_, _) => ArrangeAll();
        var small = Theme.Button(header, "▭  Mini", new Rectangle(872, 7, 72, 32), Theme.Secondary, Theme.SecondaryHover);
        small.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        small.Click += (_, _) => GoMini();
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
        _attackButton = Theme.Button(bar, "", new Rectangle(452, 10, 196, 44), Theme.Secondary, Theme.SecondaryHover);
        _attackButton.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _attackButton.Click += (_, _) => ToggleAttack();
        ShowAttack();
        _reloads.Add(ShowAttack);
        _status = Theme.Label(bar, "Oyun bekleniyor", new Rectangle(660, 10, 484, 44), 12f, Theme.Muted, FontStyle.Bold);

        // ── Side menu: the dashboard and the settings pages ──────
        var side = new Card { Bounds = new Rectangle(12, 136, 180, 772) };
        Controls.Add(side);
        _dashButton = SideButton(side, "📊   Panel", 12);
        _dashButton.Click += (_, _) => SelectTab(-1);
        Theme.Label(side, "AYARLAR", new Rectangle(16, 62, 150, 18), 8f, Theme.Accent, FontStyle.Bold);
        string[] tabNames = ["⚔   Saldırı", "🎯   Hedef", "🎒   Eşya / Envanter", "🧪   İksir / Beceri", "📍   Alan",
                             "🔔   Bildirim", "⌨   Tuşlar", "⚙   Gelişmiş", "🛒   Pazar"];
        _tabButtons = new Button[tabNames.Length];
        _tabPages = new Panel[tabNames.Length];
        Theme.Label(side, "ETKİNLİK", new Rectangle(16, 482, 150, 18), 8f, Theme.Accent, FontStyle.Bold);
        _events = Theme.Label(side, "Bot çalışınca görünür.", new Rectangle(16, 502, 152, 300), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

        // ── Always on top: live preview + session stats (on every page) ──
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
        _statExp = Tile(statsCard, "EXP", 16, 98);
        _statExpRate = Tile(statsCard, "SAATTE EXP", 126, 98);
        _statLevel = Tile(statsCard, "SEVİYE", 236, 98, small: true);
        _statYang = Tile(statsCard, "YANG", 16, 164);
        _statPotions = Tile(statsCard, "İKSİR", 126, 164, small: true);
        _statDeaths = Tile(statsCard, "ÖLÜM", 236, 164);
        Theme.Label(statsCard, "KARAKTER", new Rectangle(16, 244, 200, 18), 8f, Theme.Accent, FontStyle.Bold);
        _hpMeter = AddMeter(statsCard, "HP", Theme.Red, 268);
        _mpMeter = AddMeter(statsCard, "MP", Theme.Accent, 304);
        _targetMeter = AddMeter(statsCard, "HEDEF", Theme.Yellow, 340);

        // ── Settings pages: one big card, title + what the page is for ──
        // Below the live view (the log and this run's items show there on the dashboard).
        _settingsCard = new Card { Bounds = new Rectangle(202, 544, 966, 364), Visible = false };
        Controls.Add(_settingsCard);
        _pageTitle = Theme.Label(_settingsCard, "", new Rectangle(24, 10, 260, 28), 13f, Theme.Text, FontStyle.Bold);
        _pageInfo = Theme.Label(_settingsCard, "", new Rectangle(290, 14, 652, 22), 9f, Theme.Muted);
        for (int i = 0; i < tabNames.Length; i++)
        {
            int index = i;
            _tabButtons[i] = SideButton(side, tabNames[i], 86 + i * 42);
            _tabButtons[i].Click += (_, _) => SelectTab(index);
            _tabPages[i] = new Panel { Bounds = new Rectangle(24, 58, 918, 296), BackColor = Theme.Surface, Visible = false };
            _settingsCard.Controls.Add(_tabPages[i]);
        }
        // A thin line under the title.
        _settingsCard.Controls.Add(new Panel { Bounds = new Rectangle(24, 46, 918, 1), BackColor = Theme.Border });

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
            "yakındaki kendi karakterlerinden birini (SekoBot'ta açtığın) takip eder; birden fazla ad virgülle yazılabilir.", 102);

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
        Hint(potions, "Tehlikede kaç: can bu yüzdenin altına düşer ve en az 3 mob saldırırsa bot vurmayı bırakıp moblardan " +
            "uzaklaşır (iksir içmeye devam eder); can %60'ı geçince ya da 2 sn kimse saldırmayınca (en çok 20 sn) dövüşe döner. 0 = kapalı.", 176);
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
            WriteLog(await TelegramNotifier.SendAsync(_profile.TelegramToken, _profile.TelegramChatId, "SekoBot: deneme mesajı ✓")
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
        _dashBottom = new Panel { Bounds = new Rectangle(202, 544, 966, 364), BackColor = Theme.Background };
        Controls.Add(_dashBottom);
        var logCard = new Card { Bounds = new Rectangle(0, 0, 600, 364) };
        _dashBottom.Controls.Add(logCard);
        Theme.Label(logCard, "GÜNLÜK", new Rectangle(16, 8, 200, 20), 8f, Theme.Accent, FontStyle.Bold);
        var itemsCard = new Card { Bounds = new Rectangle(610, 0, 356, 364) };
        _dashBottom.Controls.Add(itemsCard);
        Theme.Label(itemsCard, "BU TUR EŞYALAR", new Rectangle(16, 8, 200, 20), 8f, Theme.Accent, FontStyle.Bold);
        var openItems = Theme.Button(itemsCard, "📄", new Rectangle(310, 4, 32, 26), Theme.Surface, Theme.Secondary);
        openItems.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(SessionItems.FilePath) { UseShellExecute = true }); }
            catch { WriteLog("Bu turun eşya listesi henüz yok (bot başlayınca oluşur)."); }
        };
        _runItems = new ListView
        {
            Bounds = new Rectangle(14, 32, 328, 300),
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
        itemsCard.Controls.Add(_runItems);
        _runItemsInfo = Theme.Label(itemsCard, "Bot başlayınca dolar; her turda yenilenir.", new Rectangle(16, 336, 326, 20), 8f, Theme.Muted);
        _log = new RichTextBox
        {
            Bounds = new Rectangle(14, 32, 572, 318),
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
        if (Instance.IsFirst && !_hotkeys.Register(Keys.F11, () => BeginInvoke(new Action(() => StopBot("F11 acil durdurma", byUser: true)))))
            WriteLog("F11 kısayolu başka bir uygulamada kullanılıyor; Durdur düğmesini kullan.");
        // Every SekoBot has a stop key of its own: Ctrl+Shift+1 for the 1st, Ctrl+Shift+2 for the 2nd, …
        if (!_hotkeys.Register(OwnStopKey, () => BeginInvoke(new Action(() => StopBot($"{OwnStopName} acil durdurma", byUser: true))), control: true, shift: true))
            WriteLog($"{OwnStopName} kısayolu başka bir uygulamada kullanılıyor; Durdur düğmesini kullan.");
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
        // List columns don't scale with the window: fit them to the list.
        int number = 52, nameWidth = _runItems.ClientSize.Width - 2 * number;
        _runItems.Columns[0].Width = nameWidth;
        _runItems.Columns[1].Width = number;
        _runItems.Columns[2].Width = number;
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
        bool on = _profile.Attack;
        _attackButton.Text = on ? "⚔  Saldırı: AÇIK" : "✋  Sadece eşya topla";
        _attackButton.BackColor = on ? Theme.Secondary : Theme.Accent;
        _attackButton.FlatAppearance.MouseOverBackColor = on ? Theme.SecondaryHover : ControlPaint.Light(Theme.Accent, 0.2f);
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
        var number = Theme.Number(parent, new Rectangle(x + 196, y, 90, 26), min, max,
            Math.Clamp((decimal)value(), min, max), decimals, step);
        number.ValueChanged += (_, _) => { if (_reloading) return; apply((double)number.Value); SaveProfile(); };
        _reloads.Add(() => number.Value = Math.Clamp((decimal)value(), min, max));
    }

    private void Check(Control parent, string text, int x, int y, int width, Func<bool> value, Action<bool> apply)
    {
        var box = new CheckBox
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
            Bounds = new Rectangle(x + 134, y, width, 26),
            Text = value(),
            BackColor = Theme.Inset,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f),
        };
        box.TextChanged += (_, _) => { if (_reloading) return; apply(box.Text); SaveProfile(); };
        _reloads.Add(() => box.Text = value());
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
        // The live view and the stats stay; below them the log (dashboard) or the settings page.
        _dashBottom.Visible = index < 0;
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

    private MiniBar? _mini;

    /// <summary>The bot as one small bar under its game (the full window hidden).</summary>
    private void GoMini()
    {
        if (IsDisposed)
            return;
        _mini ??= new MiniBar(MiniInfoNow, ToggleBot, () => StopBot("kullanıcı durdurdu", byUser: true), GoBig, ArrangeAll, ToggleAttack);
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

    /// <summary>The full window again.</summary>
    private void GoBig()
    {
        _mini?.Hide();
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        if (_profile.MiniMode)
        {
            _profile.MiniMode = false;
            SaveProfile();
        }
    }

    /// <summary>Every game window side by side (a band left at the bottom), every SekoBot small under its game.</summary>
    private void ArrangeAll()
    {
        int band = MiniBar.HeightAt(DeviceDpi) + 2;
        var near = _engine != null && Win32.IsWindow(_engine.Window.Handle) ? WindowLayout.Visible(_engine.Window.Handle) : Bounds;
        int count = WindowLayout.ArrangeGames(near, band);
        WriteLog(count == 0
            ? "Açık oyun penceresi bulunamadı."
            : $"{count} oyun penceresi ızgaraya dizildi (her biri kendi oranında küçüldü, sıkıştırılmadı); her SekoBot kendi oyununun altına küçüldü. Büyütmek için şeride çift tıkla.");
        WindowLayout.TellAllMini();
    }

    private MiniInfo MiniInfoNow()
    {
        Rectangle? game = null;
        if (_engine is { } engine && Win32.IsWindow(engine.Window.Handle) && !Win32.IsIconic(engine.Window.Handle))
            game = WindowLayout.Visible(engine.Window.Handle);
        var state = _engine?.State;
        var (hpPotions, mpPotions) = _engine?.PotionsLeft() ?? (null, null);
        return new MiniInfo(_engine?.CharacterName ?? "", _status.Text, _status.ForeColor, state?.Hp, state?.Mp,
            _statExpRate.Text, _statMobs.Text, _engine?.BotRunning == true, game,
            _engine is { } e && Win32.IsWindow(e.Window.Handle) ? e.Window.Handle : 0, hpPotions, mpPotions, Attack: _profile.Attack);
    }

    protected override void WndProc(ref Message m)
    {
        // Another SekoBot (or this one) arranged the windows: go small under the game.
        if (m.Msg == (int)WindowLayout.MiniMessage && m.WParam == 1)
            BeginInvoke(new Action(GoMini));
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
            engine.MarketScanFinished += _ => PostUi(() =>
            {
                UpdateControls();
                UpdateStats();
                if (BotEngine.LatestReport is { } report && DateTime.Now - File.GetLastWriteTime(report) < TimeSpan.FromMinutes(1))
                    OpenFile(report);
            });
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
        _marketWindow.Show(this);
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
        foreach (var (_, name, picked, thrown) in rows)
            _runItems.Items.Add(new ListViewItem([name, picked > 0 ? picked.ToString("N0") : "", thrown > 0 ? thrown.ToString("N0") : ""])
            {
                ForeColor = thrown > 0 && picked <= thrown ? Theme.Muted : Theme.Text,
            });
        _runItems.EndUpdate();
        _runItemsInfo.Text = rows.Count == 0 ? "Bot başlayınca dolar; her turda yenilenir."
            : $"{rows.Count} çeşit · alınan {rows.Sum(r => r.Picked):N0} · atılan {rows.Sum(r => r.Thrown):N0}";
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
        UpdateRunItems();
        string name = _engine?.CharacterName ?? "";
        UseCharacter(name);
        ShareStatus(name);
        CheckWhisperInbox();
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
                ? $"✉ {character} ({other}. SekoBot) {to} kişisine yazacak: \"{text}\". Gidince ondan haber gelir."
                : "Mesaj öbür SekoBot'a iletilemedi; birazdan tekrar dene.";
        return $"{character} şu an açık bir SekoBot'ta görünmüyor; mesaj gönderilemedi.";
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
        $"👤 {(name.Length > 0 ? name : "karakter bilinmiyor")}" + (Instance.IsFirst ? "" : $" ({Instance.Number}. SekoBot)");

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
        _gameButton.Text = connected ? "Bağlantı  ▾" : "Oyunu seç  ▾";
        _startBot.Enabled = connected;
        _findArea.Enabled = connected && !running && _engine?.ScanningMarket != true;
        _scanMarket.Enabled = connected && !running && _engine?.ScanningArea != true;
        _scanMobs.Enabled = connected;
        _startBot.Text = running ? $"❚❚  DURAKLAT   {StartKey}"
            : _engine?.ScanningMarket == true || _engine?.ScanningArea == true ? $"■  TARAMAYI DURDUR   {StartKey}"
            : $"▶  BAŞLAT   {StartKey}";
        _stopBot.Enabled = running || _engine?.ScanningMarket == true || _engine?.ScanningArea == true;
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
