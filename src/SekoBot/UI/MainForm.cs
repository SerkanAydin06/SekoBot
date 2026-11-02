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

internal sealed class MainForm : Form
{
    private const int CsDropShadow = 0x00020000;

    private readonly BotProfile _profile = ProfileStore.Load();
    private readonly ListBox _windows;
    private readonly Button _connect, _startBot, _stopBot, _record, _replay;
    private readonly CheckBox _dryRun, _background;
    private readonly NumericUpDown _potionHp, _stopHp, _attackRange, _searchSeconds;
    private readonly PreviewControl _preview;
    private readonly Button[] _calibrationButtons;
    private readonly Label _status, _scanStatus, _captureName, _calibrationHint, _calibrationState;
    private readonly Label _statPhase, _statFps, _statAnalysis, _statCycles, _statPotions, _statTime, _statBars;
    private readonly RichTextBox _log;
    private readonly System.Windows.Forms.Timer _scanTimer, _previewTimer, _statsTimer, _updateTimer;
    private readonly GlobalHotkeys _hotkeys = new();

    private BotEngine? _engine;
    private Bitmap? _frameBitmap;
    private long _frameSequence;
    private bool _autoAttached, _updating, _gameMinimized, _replaying;
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
        ClientSize = new Size(1180, 724);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;
        KeyPreview = true;

        // ── Header ────────────────────────────────────────────────
        var header = new Card { Bounds = new Rectangle(12, 10, 1156, 46) };
        Controls.Add(header);
        var title = Theme.Label(header, "◆  SEKOBOT", new Rectangle(14, 8, 170, 30), 13.5f, Theme.Text, FontStyle.Bold);
        Theme.Label(header, "v" + BuildInfo.Version, new Rectangle(176, 13, 140, 22), 8.5f, Theme.Muted);
        _status = Theme.Label(header, "● HAZIR", new Rectangle(820, 13, 230, 22), 9f, Theme.Green, FontStyle.Bold, ContentAlignment.MiddleRight);
        foreach (Control c in new Control[] { header, title })
        {
            c.MouseDown += DragWindow;
            c.Cursor = Cursors.SizeAll;
        }
        var minimize = Theme.Button(header, "—", new Rectangle(1068, 7, 36, 32), Theme.Surface, Theme.Secondary);
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var close = Theme.Button(header, "×", new Rectangle(1110, 7, 36, 32), Theme.Surface, Theme.Danger);
        close.Font = new Font("Segoe UI", 14f);
        close.Click += (_, _) => Close();

        // ── Window card ──────────────────────────────────────────
        var windowCard = new Card { Bounds = new Rectangle(12, 66, 296, 268) };
        Controls.Add(windowCard);
        Theme.Label(windowCard, "01  /  OYUN PENCERESİ", new Rectangle(14, 10, 268, 20), 8f, Theme.Accent, FontStyle.Bold);
        _scanStatus = Theme.Label(windowCard, "Pencereler aranıyor…", new Rectangle(14, 30, 268, 20), 8.5f, Theme.Muted);
        _windows = new ListBox
        {
            Bounds = new Rectangle(14, 54, 268, 116),
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 36,
            IntegralHeight = false,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Inset,
            ForeColor = Theme.Text
        };
        _windows.DrawItem += DrawWindowItem;
        _windows.SelectedIndexChanged += (_, _) => UpdateControls();
        _windows.DoubleClick += (_, _) => Connect();
        windowCard.Controls.Add(_windows);
        var refresh = Theme.Button(windowCard, "↻  Yenile", new Rectangle(14, 178, 92, 34), Theme.Secondary, Theme.SecondaryHover);
        refresh.Click += (_, _) => RefreshWindows();
        _connect = Theme.Button(windowCard, "▶  Bağlan", new Rectangle(112, 178, 170, 34), Theme.Primary, Theme.PrimaryHover);
        _connect.Click += (_, _) => { if (_engine == null) Connect(); else Disconnect("Bağlantı kapatıldı."); };
        _captureName = Theme.Label(windowCard, "Bağlı değil", new Rectangle(14, 220, 268, 38), 8f, Theme.Muted, FontStyle.Regular, ContentAlignment.TopLeft);

        // ── Bot card ─────────────────────────────────────────────
        var botCard = new Card { Bounds = new Rectangle(12, 344, 296, 368) };
        Controls.Add(botCard);
        Theme.Label(botCard, "02  /  FARM BOTU", new Rectangle(14, 10, 268, 20), 8f, Theme.Accent, FontStyle.Bold);
        _dryRun = new CheckBox
        {
            Text = "Kuru çalışma (girdi gönderme)",
            Checked = true,
            Bounds = new Rectangle(14, 34, 268, 24),
            ForeColor = Theme.Text,
            BackColor = botCard.BackColor,
            Font = new Font("Segoe UI", 9f)
        };
        botCard.Controls.Add(_dryRun);
        _startBot = Theme.Button(botCard, "▶  Başlat  (F10)", new Rectangle(14, 64, 268, 38), Theme.Primary, Theme.PrimaryHover);
        _startBot.Click += (_, _) => ToggleBot();
        _stopBot = Theme.Button(botCard, "■  Durdur  (F12)", new Rectangle(14, 108, 268, 34), Theme.Danger, Theme.DangerHover);
        _stopBot.Click += (_, _) => StopBot("kullanıcı durdurdu", byUser: true);

        Theme.Label(botCard, "HP iksiri (%)", new Rectangle(14, 156, 170, 26), 9f, Theme.Muted);
        _potionHp = Theme.Number(botCard, new Rectangle(192, 156, 90, 26), 5, 95, (decimal)(_profile.PotionHpBelow * 100));
        Theme.Label(botCard, "Botu durdur HP (%)", new Rectangle(14, 188, 170, 26), 9f, Theme.Muted);
        _stopHp = Theme.Number(botCard, new Rectangle(192, 188, 90, 26), 0, 90, (decimal)(_profile.StopHpBelow * 100));
        // Walk mode: how close (share of the screen height) a mob must be before Space is held.
        Theme.Label(botCard, "Saldırı mesafesi (%)", new Rectangle(14, 220, 170, 26), 9f, Theme.Muted);
        _attackRange = Theme.Number(botCard, new Rectangle(192, 220, 90, 26), 1, 20,
            Math.Clamp((decimal)Math.Round(_profile.AttackRadius * 100, 1), 1, 20), 1, 0.5m);
        Theme.Label(botCard, "Mob arama süresi (sn)", new Rectangle(14, 252, 176, 26), 9f, Theme.Muted);
        _searchSeconds = Theme.Number(botCard, new Rectangle(192, 252, 90, 26), 2, 30,
            Math.Clamp(_profile.SearchTimeoutMs / 1000m, 2, 30), 0, 1);
        _potionHp.ValueChanged += (_, _) => { _profile.PotionHpBelow = (double)_potionHp.Value / 100; SaveProfile(); };
        _stopHp.ValueChanged += (_, _) => { _profile.StopHpBelow = (double)_stopHp.Value / 100; SaveProfile(); };
        _attackRange.ValueChanged += (_, _) => { _profile.AttackRadius = (double)_attackRange.Value / 100; SaveProfile(); };
        _searchSeconds.ValueChanged += (_, _) => { _profile.SearchTimeoutMs = (int)(_searchSeconds.Value * 1000); SaveProfile(); };

        var openFolder = Theme.Button(botCard, "📁 Klasör", new Rectangle(14, 290, 80, 32), Theme.Secondary, Theme.SecondaryHover);
        openFolder.Click += (_, _) => OpenProfileFolder();
        _record = Theme.Button(botCard, "⏺ Kaydet", new Rectangle(98, 290, 88, 32), Theme.Secondary, Theme.SecondaryHover);
        _record.Click += (_, _) => ToggleRecording();
        _replay = Theme.Button(botCard, "▶ Kaydı oynat", new Rectangle(190, 290, 92, 32), Theme.Secondary, Theme.SecondaryHover);
        _replay.Click += async (_, _) => await ReplayAsync();
        foreach (var small in new[] { openFolder, _record, _replay })
            small.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        _background = new CheckBox
        {
            Text = "Arka planda çalış (deneysel)",
            Checked = _profile.BackgroundInput,
            Bounds = new Rectangle(14, 328, 268, 24),
            ForeColor = Theme.Text,
            BackColor = botCard.BackColor,
            Font = new Font("Segoe UI", 9f)
        };
        _background.CheckedChanged += (_, _) => { _profile.BackgroundInput = _background.Checked; SaveProfile(); };
        botCard.Controls.Add(_background);

        // ── Preview card ─────────────────────────────────────────
        var previewCard = new Card { Bounds = new Rectangle(318, 66, 850, 478) };
        Controls.Add(previewCard);
        Theme.Label(previewCard, "CANLI GÖRÜNTÜ  ·  KALİBRASYON", new Rectangle(16, 10, 400, 20), 8f, Theme.Accent, FontStyle.Bold);
        _calibrationState = Theme.Label(previewCard, "", new Rectangle(560, 10, 274, 20), 8.5f, Theme.Muted, FontStyle.Regular, ContentAlignment.MiddleRight);
        _preview = new PreviewControl { Bounds = new Rectangle(16, 34, 818, 360), Profile = _profile };
        _preview.SelectionChanged += UpdateControls;
        _preview.MouseClick += (_, _) => { if (_engine != null && _gameMinimized) WindowService.Restore(_engine.Window); };
        previewCard.Controls.Add(_preview);

        _calibrationHint = Theme.Label(previewCard, "Önizlemede fareyle bir alan seç, sonra ne olduğunu işaretle:",
            new Rectangle(16, 398, 818, 20), 8.5f, Theme.Muted);
        string[] names = ["HP barı", "MP barı", "Hedef HP barı (isteğe bağlı)", "Sıfırla / yeniden bul"];
        _calibrationButtons = new Button[names.Length];
        int bx = 16;
        for (int i = 0; i < names.Length; i++)
        {
            int width = i == 2 ? 230 : i == 3 ? 190 : 140;
            int index = i;
            var button = Theme.Button(previewCard, names[i], new Rectangle(bx, 424, width, 38),
                i == 3 ? Theme.Danger : Theme.Secondary, i == 3 ? Theme.DangerHover : Theme.SecondaryHover);
            button.Click += (_, _) => Calibrate(index);
            _calibrationButtons[i] = button;
            bx += width + 8;
        }

        // ── Stats card ───────────────────────────────────────────
        var statsCard = new Card { Bounds = new Rectangle(318, 554, 420, 158) };
        Controls.Add(statsCard);
        Theme.Label(statsCard, "İSTATİSTİK", new Rectangle(16, 10, 200, 20), 8f, Theme.Accent, FontStyle.Bold);
        _statPhase = Stat(statsCard, "DURUM", 16, 34, 196);
        _statBars = Stat(statsCard, "HP / MP / HEDEF", 216, 34, 190);
        _statCycles = Stat(statsCard, "TUR", 16, 78, 120);
        _statPotions = Stat(statsCard, "İKSİR", 140, 78, 90);
        _statTime = Stat(statsCard, "SÜRE", 234, 78, 170);
        _statFps = Stat(statsCard, "YAKALAMA FPS", 16, 116, 196, small: true);
        _statAnalysis = Stat(statsCard, "ANALİZ", 216, 116, 190, small: true);

        // ── Log card ─────────────────────────────────────────────
        var logCard = new Card { Bounds = new Rectangle(748, 554, 420, 158) };
        Controls.Add(logCard);
        Theme.Label(logCard, "SİSTEM GÜNLÜĞÜ", new Rectangle(16, 10, 200, 20), 8f, Theme.Accent, FontStyle.Bold);
        _log = new RichTextBox
        {
            Bounds = new Rectangle(16, 34, 390, 112),
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
        if (!_hotkeys.Register(Keys.F12, () => BeginInvoke(new Action(() => StopBot("F12 acil durdurma", byUser: true)))))
            WriteLog("F12 kısayolu başka bir uygulamada kullanılıyor.");

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
    }

    private static Label Stat(Control parent, string caption, int x, int y, int width, bool small = false)
    {
        Theme.Label(parent, caption, new Rectangle(x, y, width, 14), 7.5f, Theme.Muted);
        return Theme.Label(parent, "—", new Rectangle(x, y + 14, width, small ? 20 : 24),
            small ? 9f : 11.5f, Theme.Text, FontStyle.Bold);
    }

    // ── Windows ──────────────────────────────────────────────────

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        Win32.ReleaseCapture();
        Win32.SendMessage(Handle, 0x00A1, 2, 0);
    }

    private void DrawWindowItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _windows.Items[e.Index] is not WindowInfo w)
            return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var fill = new SolidBrush(selected ? Color.FromArgb(36, 68, 112) : Theme.Inset))
            e.Graphics.FillRectangle(fill, e.Bounds);
        using var bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 7.5f);
        TextRenderer.DrawText(e.Graphics, (w.IsMetin2 ? "★ " : "") + w.Title, bold,
            new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 3, e.Bounds.Width - 14, 18), Theme.Text,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string details = $"{w.Width} × {w.Height}  •  PID {w.ProcessId}" + (w.IsMinimized ? "  •  küçültülmüş" : "");
        TextRenderer.DrawText(e.Graphics, details, small,
            new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 20, e.Bounds.Width - 14, 14), Theme.Muted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void RefreshWindows()
    {
        if (_engine != null)
            return;
        try
        {
            var previous = _windows.SelectedItem as WindowInfo;
            var all = WindowService.GetOpenWindows();
            var games = all.Where(w => w.IsMetin2).ToList();
            // Only the game is of interest; show everything just as a fallback when it isn't open.
            IReadOnlyList<WindowInfo> list = games.Count > 0 ? games : all;
            _windows.BeginUpdate();
            _windows.Items.Clear();
            foreach (var w in list)
                _windows.Items.Add(w);
            int index = previous == null ? -1 : list.ToList().FindIndex(w => w.Handle == previous.Handle && w.ProcessId == previous.ProcessId);
            if (index < 0 && list.Count > 0) index = 0;
            _windows.SelectedIndex = index;
            _windows.EndUpdate();
            _scanStatus.Text = games.Count switch
            {
                0 => $"{_profile.GameWindowTitle} bekleniyor… Açılınca kendiliğinden bağlanır.",
                1 => $"{_profile.GameWindowTitle} bulundu.",
                _ => $"{games.Count} oyun penceresi var; birini seçip Bağlan'a bas."
            };
            UpdateControls();

            var ready = games.Where(w => !w.IsMinimized).ToList();
            if (!_autoAttached && ready.Count == 1)
            {
                _autoAttached = true;
                _windows.SelectedItem = ready[0];
                BeginInvoke(new Action(Connect));
            }
        }
        catch (Exception ex)
        {
            _scanStatus.Text = "Tarama hatası.";
            WriteLog("Pencere tarama hatası: " + ex.Message);
        }
    }

    private void Connect()
    {
        if (_engine != null || _updating || _windows.SelectedItem is not WindowInfo window)
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
            engine.BotStopped += reason => PostUi(() => { SetStatus("DURDU: " + reason, Theme.Yellow); UpdateControls(); });
            _engine = engine;
            engine.Start(previewFps: 30);
            _frameSequence = 0;
            _gameMinimized = false;
            _previewTimer.Start();
            _captureName.Text = $"{window.Title}\n{engine.Stats.CaptureName}";
            SetStatus("BAĞLI", Theme.Green);
            WriteLog($"Bağlandı: {window.Title} (PID {window.ProcessId}).");
        }
        catch (Exception ex)
        {
            _engine?.Dispose();
            _engine = null;
            SetStatus("BAĞLANTI HATASI", Theme.Red);
            WriteLog("Bağlanılamadı: " + ex.Message);
        }
        UpdateControls();
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
        _preview.Placeholder = "Oyun penceresi bekleniyor";
        _preview.Invalidate();
        _captureName.Text = "Bağlı değil";
        SetStatus("BAĞLI DEĞİL", Theme.Muted);
        WriteLog(reason);
        UpdateControls();
        RefreshWindows();
    }

    // ── Bot ──────────────────────────────────────────────────────

    private void ToggleBot()
    {
        if (_engine == null)
        {
            WriteLog("Önce oyun penceresine bağlanın.");
            return;
        }
        if (_engine.BotRunning)
        {
            StopBot("duraklatıldı", byUser: true);
            return;
        }
        if (!_engine.StartBot(_dryRun.Checked, _background.Checked, out string error))
        {
            WriteLog(error);
            SetStatus("BAŞLATILAMADI", Theme.Yellow);
            return;
        }
        SetStatus(_dryRun.Checked ? "BOT · KURU ÇALIŞMA" : _background.Checked ? "BOT · ARKA PLAN" : "BOT ÇALIŞIYOR",
            _dryRun.Checked ? Theme.Yellow : Theme.Green);
        UpdateControls();
    }

    /// <param name="byUser">F10/F12/button: a pending update may install now. Automatic stops
    /// (critical HP, minimized game) keep the window as is so the user can see why it stopped.</param>
    private void StopBot(string reason, bool byUser = false)
    {
        if (_engine?.BotRunning != true)
            return;
        _engine.StopBot(reason);
        SetStatus("BAĞLI", Theme.Green);
        UpdateControls();
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
        if (kind == 3)
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
                switch (kind)
                {
                    case 0:
                        _profile.HpBar = BarReader.Calibrate(frame, selection);
                        return "HP barı kaydedildi" + ColorText(_profile.HpBar);
                    case 1:
                        _profile.MpBar = BarReader.Calibrate(frame, selection);
                        return "MP barı kaydedildi" + ColorText(_profile.MpBar);
                    default:
                        _profile.TargetBar = BarReader.Calibrate(frame, selection);
                        return "Hedef HP barı kaydedildi" + ColorText(_profile.TargetBar);
                }
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

    /// <summary>Deletes every marked bar (HP/MP/target).</summary>
    private void ResetCalibration()
    {
        var answer = MessageBox.Show(this, "Tüm barlar (HP, MP, hedef) silinsin ve otomatik olarak yeniden bulunsun mu?",
            "SekoBot", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;
        StopBot("kalibrasyon sıfırlandı");
        _profile.HpBar = null;
        _profile.MpBar = null;
        _profile.TargetBar = null;
        SaveProfile();
        _preview.State = null;
        _preview.ClearSelection();
        WriteLog("Barlar sıfırlandı; otomatik olarak yeniden aranıyor…");
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
            var stats = _engine.Stats;
            _preview.Overlay = (_engine.IsRecording ? "⏺ KAYIT  " : "") + (stats.BotRunning
                ? (stats.DryRun ? "KURU · " : "") + stats.Phase.ToTurkish()
                : "");
            _preview.Invalidate();
        }
    }

    private void UpdateStats()
    {
        _calibrationState.Text = $"HP {Mark(_profile.HpBar)}   MP {Mark(_profile.MpBar)}   Hedef {Mark(_profile.TargetBar)}";

        if (_engine == null)
        {
            foreach (var l in new[] { _statPhase, _statFps, _statAnalysis, _statBars, _statCycles, _statPotions, _statTime })
                l.Text = "—";
            return;
        }
        var s = _engine.Stats;
        var state = _engine.State;
        _statPhase.Text = s.BotRunning ? s.Phase.ToTurkish() : "Bot kapalı";
        _statPhase.ForeColor = s.BotRunning ? Theme.Green : Theme.Muted;
        _statFps.Text = $"{s.CaptureFps:0.0}  ·  {(s.CaptureName.StartsWith("Windows") ? "GPU" : "GDI")}";
        _statAnalysis.Text = $"{s.AnalysisMs:0.00} ms";
        _statBars.Text = $"{Pct(state?.Hp)} / {Pct(state?.Mp)} / {Pct(state?.TargetHp)}";
        _statCycles.Text = s.Cycles.ToString("N0");
        _statPotions.Text = s.Potions.ToString("N0");
        _statTime.Text = s.BotUptime.ToString(@"hh\:mm\:ss");
    }

    private static string Mark(BarSpec? bar) => bar == null ? "✗" : "✓";

    private static string Pct(double? value) => value is double v ? $"%{v * 100:0}" : "—";

    private void UpdateControls()
    {
        bool connected = _engine != null;
        bool running = _engine?.BotRunning == true;
        _windows.Enabled = !connected;
        _connect.Enabled = connected || _windows.SelectedItem is WindowInfo;
        _connect.Text = connected ? "■  Bağlantıyı kes" : "▶  Bağlan";
        _connect.BackColor = connected ? Theme.Danger : Theme.Primary;
        _connect.FlatAppearance.MouseOverBackColor = connected ? Theme.DangerHover : Theme.PrimaryHover;
        _startBot.Enabled = connected;
        _startBot.Text = running ? "❚❚  Duraklat  (F10)" : "▶  Başlat  (F10)";
        _stopBot.Enabled = running;
        bool recording = _engine?.IsRecording == true;
        _record.Enabled = connected;
        _record.Text = recording ? "■ Bitir" : "⏺ Kaydet";
        _record.BackColor = recording ? Theme.Danger : Theme.Secondary;
        _record.FlatAppearance.MouseOverBackColor = recording ? Theme.DangerHover : Theme.SecondaryHover;
        _replay.Enabled = !_replaying;
        _dryRun.Enabled = !running;
        _background.Enabled = !running;
        bool hasSelection = connected && _preview.Selection != null;
        for (int i = 0; i < 3; i++)
            _calibrationButtons[i].Enabled = hasSelection;
        _calibrationHint.Text = !connected
            ? "Kalibrasyon için önce oyuna bağlan."
            : hasSelection
                ? "Seçili alanı ne olarak kaydedelim? (Bar seçerken bar dolu olmalı)"
                : _profile.HpBar == null
                    ? "HP/MP barları otomatik aranıyor… Bulunamazsa barı fareyle seçip aşağıdan işaretle."
                    : "Barlar hazır. Yanlışsa barı fareyle seçip elle işaretle ya da 'Sıfırla / yeniden bul'.";
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
                WriteLog($"Yeni sürüm {update.Version} hazır; botu durdurduğunda (F10/F12) yüklenecek.");
            _pendingUpdate = update;
            return;
        }
        _updating = true;
        SetStatus("GÜNCELLENİYOR", Theme.Yellow);
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
            SetStatus("HAZIR", Theme.Green);
            WriteLog("Güncelleme uygulanamadı: " + ex.Message);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private void SetStatus(string text, Color color)
    {
        _status.Text = "● " + text;
        _status.ForeColor = color;
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
