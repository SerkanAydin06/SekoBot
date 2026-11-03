using System.Diagnostics;
using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Input;
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
    private readonly Button[] _calibrationButtons;
    private readonly Label _connection, _status, _calibrationHint, _calibrationState, _technical;
    private readonly Label _statTime, _statMobs, _statRate, _statDeaths, _statPotions, _statArea;
    private readonly Meter _hpMeter, _mpMeter, _targetMeter;
    private readonly Button[] _tabButtons;
    private readonly Panel[] _tabPages;
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
        var version = Theme.Label(header, "v" + BuildInfo.Version, new Rectangle(160, 13, 110, 22), 8.5f, Theme.Muted);
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
        _startBot = Theme.Button(bar, "▶  BAŞLAT   F10", new Rectangle(12, 10, 220, 44), Theme.Primary, Theme.PrimaryHover);
        _startBot.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _startBot.Click += (_, _) => ToggleBot();
        _stopBot = Theme.Button(bar, "■  DURDUR   F11", new Rectangle(242, 10, 200, 44), Theme.Danger, Theme.DangerHover);
        _stopBot.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _stopBot.Click += (_, _) => StopBot("kullanıcı durdurdu", byUser: true);
        _status = Theme.Label(bar, "Oyun bekleniyor", new Rectangle(464, 10, 680, 44), 12f, Theme.Muted, FontStyle.Bold);

        // ── Live preview ─────────────────────────────────────────
        var previewCard = new Card { Bounds = new Rectangle(12, 136, 800, 400) };
        Controls.Add(previewCard);
        Theme.Label(previewCard, "CANLI GÖRÜNTÜ", new Rectangle(16, 8, 300, 20), 8f, Theme.Accent, FontStyle.Bold);
        Theme.Label(previewCard, "Sarı halka = bulunan mob", new Rectangle(420, 8, 364, 20), 8f, Theme.Muted,
            FontStyle.Regular, ContentAlignment.MiddleRight);
        _preview = new PreviewControl { Bounds = new Rectangle(14, 32, 772, 354), Profile = _profile };
        _preview.SelectionChanged += UpdateControls;
        _preview.MouseClick += (_, _) => { if (_engine != null && _gameMinimized) WindowService.Restore(_engine.Window); };
        previewCard.Controls.Add(_preview);

        // ── Session stats ────────────────────────────────────────
        var statsCard = new Card { Bounds = new Rectangle(822, 136, 346, 400) };
        Controls.Add(statsCard);
        Theme.Label(statsCard, "OTURUM", new Rectangle(16, 8, 200, 20), 8f, Theme.Accent, FontStyle.Bold);
        _statTime = Tile(statsCard, "SÜRE", 16, 36);
        _statMobs = Tile(statsCard, "ÖLDÜRÜLEN MOB", 180, 36);
        _statRate = Tile(statsCard, "SAATTE MOB", 16, 104);
        _statDeaths = Tile(statsCard, "ÖLÜM", 180, 104);
        _statPotions = Tile(statsCard, "İKSİR", 16, 172);
        _statArea = Tile(statsCard, "ALAN", 180, 172, small: true);
        Theme.Label(statsCard, "KARAKTER", new Rectangle(16, 244, 200, 18), 8f, Theme.Accent, FontStyle.Bold);
        _hpMeter = AddMeter(statsCard, "HP", Theme.Red, 268);
        _mpMeter = AddMeter(statsCard, "MP", Theme.Accent, 304);
        _targetMeter = AddMeter(statsCard, "HEDEF", Theme.Yellow, 340);

        // ── Settings tabs ────────────────────────────────────────
        var settings = new Card { Bounds = new Rectangle(12, 544, 760, 204) };
        Controls.Add(settings);
        string[] tabNames = ["Saldırı", "İksir & Buff", "Alan", "Tuşlar", "Gelişmiş"];
        _tabButtons = new Button[tabNames.Length];
        _tabPages = new Panel[tabNames.Length];
        for (int i = 0; i < tabNames.Length; i++)
        {
            int index = i;
            _tabButtons[i] = Theme.Button(settings, tabNames[i], new Rectangle(12 + i * 124, 10, 118, 30), Theme.Secondary, Theme.SecondaryHover);
            _tabButtons[i].Click += (_, _) => SelectTab(index);
            _tabPages[i] = new Panel { Bounds = new Rectangle(12, 50, 736, 146), BackColor = Theme.Surface, Visible = false };
            settings.Controls.Add(_tabPages[i]);
        }

        // Saldırı
        var attack = _tabPages[0];
        NumberRow(attack, "Saldırı mesafesi (%)", 0, 0, 1, 20, Math.Round(_profile.AttackRadius * 100, 1), 1, 0.5m,
            v => _profile.AttackRadius = v / 100);
        NumberRow(attack, "Eşya toplama (sn)", 0, 34, 0.5m, 10, _profile.LootEveryMs / 1000.0, 1, 0.5m,
            v => _profile.LootEveryMs = (int)(v * 1000));
        Check(attack, "Kalabalığa git (mobların toplu olduğu yere)", 370, 0, 360, _profile.PreferCrowds,
            v => _profile.PreferCrowds = v);
        NumberRow(attack, "Kalabalık: en az mob", 370, 34, 2, 10, _profile.CrowdMinMobs, 0, 1,
            v => _profile.CrowdMinMobs = (int)v);
        Hint(attack, "Saldırı mesafesi: mob karaktere bu kadar yaklaşınca saldırır (ekran yüksekliğinin %'si). " +
            "Küçük sayı = daha yakından saldırır.", 76);

        // İksir & Buff
        var potions = _tabPages[1];
        NumberRow(potions, "HP iksiri (% altında)", 0, 0, 5, 95, Math.Round(_profile.PotionHpBelow * 100), 0, 1,
            v => _profile.PotionHpBelow = v / 100);
        NumberRow(potions, "MP iksiri (% altında)", 0, 34, 0, 95, Math.Round(_profile.PotionMpBelow * 100), 0, 1,
            v => _profile.PotionMpBelow = v / 100);
        Check(potions, "Ölünce yeniden başla", 0, 68, 300, _profile.ReviveOnDeath, v => _profile.ReviveOnDeath = v);
        Theme.Label(potions, "Buff: kaç saniyede bir basılsın (0 = kapalı)", new Rectangle(370, 0, 360, 26), 9f, Theme.Muted);
        for (int i = 0; i < BotProfile.SkillSlots; i++)
        {
            int slot = i;
            int x = 370 + (i % 3) * 120, y = 34 + (i / 3) * 34;
            string key = i < _profile.Keys.Skills.Length ? KeyPicker.Name(_profile.Keys.Skills[i]) : "F" + (i + 1);
            Theme.Label(potions, key, new Rectangle(x, y, 34, 26), 9.5f, Theme.Text, FontStyle.Bold);
            var number = Theme.Number(potions, new Rectangle(x + 36, y, 70, 26), 0, 3600, Math.Clamp(_profile.SkillEverySec[i], 0, 3600));
            number.ValueChanged += (_, _) => { _profile.SkillEverySec[slot] = (int)number.Value; SaveProfile(); };
        }

        // Alan
        var area = _tabPages[2];
        NumberRow(area, "Alan sınırı (harita %)", 0, 0, 0, 30, Math.Round(_profile.LeashRadius * 100, 1), 1, 0.5m,
            v => _profile.LeashRadius = v / 100);
        NumberRow(area, "Haritaya bakma (sn)", 0, 34, 5, 120, _profile.LeashCheckMs / 1000.0, 0, 5,
            v => _profile.LeashCheckMs = (int)(v * 1000));
        Theme.Label(area, "Bot başladığı yeri hatırlar. Arada bir büyük haritayı (M) açıp bakar; bu sınırın dışına " +
            "çıktıysa başladığı yere geri yürür. Bir şeye takılırsa yana kayarak dolanır.\n0 = sınır yok.",
            new Rectangle(370, 0, 360, 100), 9f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

        // Tuşlar
        var keys = _tabPages[3];
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
            ("Büyük harita", () => k.Map, v => k.Map = v),
            ("Pencere kapat", () => k.CloseWindow, v => k.CloseWindow = v),
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
        var advanced = _tabPages[4];
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
        string[] names = ["Hedef HP barı", "Hedef barını sıfırla"];
        _calibrationButtons = new Button[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            var button = Theme.Button(advanced, names[i], new Rectangle(i * 150, 56, 142, 30),
                i == 1 ? Theme.Danger : Theme.Secondary, i == 1 ? Theme.DangerHover : Theme.SecondaryHover);
            button.Click += (_, _) => Calibrate(index);
            _calibrationButtons[i] = button;
        }
        _record = Theme.Button(advanced, "⏺  Kaydet", new Rectangle(0, 100, 142, 30), Theme.Secondary, Theme.SecondaryHover);
        _record.Click += (_, _) => ToggleRecording();
        _replay = Theme.Button(advanced, "▶  Kaydı oynat", new Rectangle(150, 100, 142, 30), Theme.Secondary, Theme.SecondaryHover);
        _replay.Click += async (_, _) => await ReplayAsync();
        var openFolder = Theme.Button(advanced, "📁  Ayar klasörü", new Rectangle(300, 100, 142, 30), Theme.Secondary, Theme.SecondaryHover);
        openFolder.Click += (_, _) => OpenProfileFolder();

        // ── Log ──────────────────────────────────────────────────
        var logCard = new Card { Bounds = new Rectangle(782, 544, 386, 204) };
        Controls.Add(logCard);
        Theme.Label(logCard, "GÜNLÜK", new Rectangle(16, 8, 200, 20), 8f, Theme.Accent, FontStyle.Bold);
        _log = new RichTextBox
        {
            Bounds = new Rectangle(14, 32, 358, 160),
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
        SelectTab(0);

        // ── Timers & hotkeys ─────────────────────────────────────
        _scanTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _scanTimer.Tick += (_, _) => { if (_engine == null) RefreshWindows(); };
        _previewTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _previewTimer.Tick += (_, _) => RenderPreview();
        _statsTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _statsTimer.Tick += (_, _) => UpdateStats();
        _updateTimer = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromHours(2).TotalMilliseconds };
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(startup: false);

        if (!_hotkeys.Register(Keys.F10, () => BeginInvoke(new Action(ToggleBot))))
            WriteLog("F10 kısayolu başka bir uygulamada kullanılıyor.");
        // F12 is reserved by Windows for debuggers, so the emergency stop is F11.
        if (!_hotkeys.Register(Keys.F11, () => BeginInvoke(new Action(() => StopBot("F11 acil durdurma", byUser: true)))))
            WriteLog("F11 kısayolu başka bir uygulamada kullanılıyor; Durdur düğmesini kullan.");

        Shown += async (_, _) =>
        {
            WriteLog($"SekoBot {BuildInfo.Version} başlatıldı.");
            RefreshWindows();
            _scanTimer.Start();
            _statsTimer.Start();
            _updateTimer.Start();
            await CheckForUpdatesAsync(startup: true);
        };
        FormClosing += (_, _) =>
        {
            foreach (var t in new[] { _scanTimer, _previewTimer, _statsTimer, _updateTimer }) t.Stop();
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
        var tile = new Panel { Bounds = new Rectangle(x, y, 150, 60), BackColor = Theme.Inset };
        parent.Controls.Add(tile);
        Theme.Label(tile, caption, new Rectangle(10, 6, 136, 16), 7.5f, Theme.Muted, FontStyle.Bold);
        return Theme.Label(tile, "—", new Rectangle(10, 24, 136, 30), small ? 8.5f : 15f, Theme.Text, FontStyle.Bold);
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

    private static void Hint(Control parent, string text, int y) =>
        Theme.Label(parent, text, new Rectangle(0, y, 736, 44), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

    private void SelectTab(int index)
    {
        for (int i = 0; i < _tabPages.Length; i++)
        {
            bool on = i == index;
            _tabPages[i].Visible = on;
            _tabButtons[i].BackColor = on ? Theme.Primary : Theme.Secondary;
            _tabButtons[i].FlatAppearance.MouseOverBackColor = on ? Theme.PrimaryHover : Theme.SecondaryHover;
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

            if (!_autoAttached && ready.Count == 1 && _games.Count == 1)
            {
                _autoAttached = true;
                BeginInvoke(new Action(() => Connect(ready[0])));
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
            engine.BarsChanged += message => PostUi(() =>
            {
                SaveProfile();
                if (message.Length > 0) WriteLog(message);
                _preview.Invalidate();
                UpdateControls();
            });
            engine.BotStopped += reason => PostUi(() => { _stopReason = reason; UpdateControls(); UpdateStats(); });
            _engine = engine;
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

    // ── Calibration ──────────────────────────────────────────────

    private void Calibrate(int kind)
    {
        if (kind == 1)
        {
            ResetCalibration();
            return;
        }
        if (_engine == null || _preview.Selection is not Rectangle selection)
            return;
        try
        {
            string message = _engine.WithLatestFrame(frame =>
            {
                _profile.TargetBar = BarReader.Calibrate(frame, selection);
                return "Hedef HP barı kaydedildi" + ColorText(_profile.TargetBar);
            }) ?? "Henüz görüntü yok.";
            SaveProfile();
            WriteLog(message);
            _preview.ClearSelection();
        }
        catch (Exception ex)
        {
            WriteLog("Kalibrasyon hatası: " + ex.Message);
        }
        UpdateControls();
    }

    private static string ColorText(BarSpec? bar) => bar == null ? "." : $" (renk {bar.R},{bar.G},{bar.B}).";

    /// <summary>Resets only target HP calibration. Character stats always come from memory.</summary>
    private void ResetCalibration()
    {
        var answer = MessageBox.Show(this, "Hedef HP barı silinip yeniden bulunsun mu?",
            "SekoBot", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;
        StopBot("kalibrasyon sıfırlandı");
        _profile.TargetBar = null;
        SaveProfile();
        _preview.State = null;
        _preview.ClearSelection();
        WriteLog("Hedef barı sıfırlandı; otomatik olarak yeniden aranıyor…");
        UpdateControls();
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
        _calibrationState.Text = $"HP/MP: bellek   Hedef {Mark(_profile.TargetBar)}";

        if (_engine == null)
        {
            foreach (var l in new[] { _statTime, _statMobs, _statRate, _statDeaths, _statPotions, _statArea })
                l.Text = "—";
            _hpMeter.Value = _mpMeter.Value = _targetMeter.Value = null;
            _technical.Text = "";
            SetLine(_updating ? "Güncelleniyor…" : "Oyun bekleniyor", Theme.Muted);
            return;
        }
        var s = _engine.Stats;
        var state = _engine.State;
        _technical.Text = $"{_engine.StatsSource} · {s.CaptureFps:0} FPS";
        _hpMeter.Value = state?.Hp;
        _mpMeter.Value = state?.Mp;
        _targetMeter.Value = state?.TargetHp;

        if (s.BotRunning)
        {
            _statTime.Text = s.BotUptime.ToString(@"hh\:mm\:ss");
            _statMobs.Text = s.Cycles.ToString("N0");
            _statRate.Text = s.BotUptime.TotalMinutes >= 1 ? (s.Cycles / s.BotUptime.TotalHours).ToString("N0") : "—";
            _statDeaths.Text = s.Deaths.ToString("N0");
            _statDeaths.ForeColor = s.Deaths > 0 ? Theme.Red : Theme.Text;
            _statPotions.Text = s.Potions.ToString("N0");
            _statArea.Text = s.Area;
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
                    ? $"Durdu ({reason}). Devam etmek için BAŞLAT ya da F10."
                    : "Hazır. Başlatmak için BAŞLAT ya da F10.",
                _stopReason != null ? Theme.Yellow : Theme.Text);
        }
    }

    private void SetLine(string text, Color color)
    {
        if (_status.Text != text) _status.Text = text;
        _status.ForeColor = color;
    }

    private static string Mark(BarSpec? bar) => bar == null ? "✗" : "✓";

    private void UpdateControls()
    {
        bool connected = _engine != null;
        bool running = _engine?.BotRunning == true;
        _gameButton.Text = connected ? "Bağlantı  ▾" : "Oyunu seç  ▾";
        _startBot.Enabled = connected;
        _startBot.Text = running ? "❚❚  DURAKLAT   F10" : "▶  BAŞLAT   F10";
        _stopBot.Enabled = running;
        bool recording = _engine?.IsRecording == true;
        _record.Enabled = connected;
        _record.Text = recording ? "■  Kaydı bitir" : "⏺  Kaydet";
        _record.BackColor = recording ? Theme.Danger : Theme.Secondary;
        _record.FlatAppearance.MouseOverBackColor = recording ? Theme.DangerHover : Theme.SecondaryHover;
        _replay.Enabled = !_replaying;
        _dryRun.Enabled = !running;
        bool hasSelection = connected && _preview.Selection != null;
        for (int i = 0; i < 1; i++)
            _calibrationButtons[i].Enabled = hasSelection;
        _calibrationHint.Text = !connected
            ? "HP/MP bellekten okunur. Hedef barını işaretlemek için oyuna bağlan."
            : hasSelection
                ? "Hedef HP barını seçtiysen düğmesine bas."
                : _profile.TargetBar == null
                    ? "Hedef barı otomatik aranıyor; gerekirse canlı görüntüden seç."
                    : "HP/MP bellekten; hedef barı görüntüden okunuyor.";
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
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        if (_log.TextLength > 20000)
            _log.Text = _log.Text[^14000..];
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}
