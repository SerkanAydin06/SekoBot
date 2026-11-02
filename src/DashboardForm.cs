using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SekoBot;

/// <summary>
/// Compact borderless Windows dashboard. All game-specific behavior remains
/// outside the UI; this version only enumerates, previews and saves windows.
/// </summary>
internal sealed class DashboardForm : Form
{
    private static readonly Color Background = Color.FromArgb(11, 16, 32);
    private static readonly Color Surface = Color.FromArgb(20, 29, 48);
    private static readonly Color PreviewBackground = Color.FromArgb(8, 14, 27);
    private static readonly Color Primary = Color.FromArgb(42, 108, 231);
    private static readonly Color PrimaryHover = Color.FromArgb(64, 130, 255);
    private static readonly Color TextColor = Color.FromArgb(238, 245, 255);
    private static readonly Color Muted = Color.FromArgb(146, 165, 194);
    private static readonly Color Green = Color.FromArgb(87, 220, 174);
    private static readonly Color Red = Color.FromArgb(255, 120, 142);

    private readonly ListBox _windowList;
    private readonly ComboBox _fpsSelector;
    private readonly Button _connectButton;
    private readonly Button _stopButton;
    private readonly Button _saveButton;
    private readonly PictureBox _preview;
    private readonly Label _placeholder;
    private readonly Label _status;
    private readonly Label _scanStatus;
    private readonly Label _connectionStatus;
    private readonly Label _fpsText;
    private readonly Label _sizeText;
    private readonly Label _framesText;
    private readonly RichTextBox _log;
    private readonly System.Windows.Forms.Timer _scanTimer;
    private CancellationTokenSource? _captureCts;
    private bool _autoAttached;
    private long _frameCount;

    public DashboardForm()
    {
        Text = "SekoBot";
        ClientSize = new Size(940, 595);
        MinimumSize = Size;
        MaximumSize = Size;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        BackColor = Background;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;

        var header = new CardPanel { Bounds = new Rectangle(12, 10, 916, 45), BackColor = Surface };
        Controls.Add(header);

        var title = AddLabel(header, "◆  SEKOBOT    /    BOT PANELİ",
            new Rectangle(13, 8, 500, 28), 13.5f, TextColor, FontStyle.Bold);
        title.Cursor = Cursors.SizeAll;
        title.MouseDown += DragWindow;
        header.MouseDown += DragWindow;

        _status = AddLabel(header, "● HAZIR",
            new Rectangle(688, 14, 120, 20), 9f, Green, FontStyle.Bold);

        var minimize = MakeButton("—", new Rectangle(831, 6, 34, 32), Surface, Surface);
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        header.Controls.Add(minimize);

        var close = MakeButton("×", new Rectangle(872, 6, 34, 32), Surface, Color.FromArgb(85, 39, 57));
        close.Font = new Font("Segoe UI", 15f, FontStyle.Regular);
        close.Click += (_, _) => Close();
        header.Controls.Add(close);

        var sidebar = new CardPanel { Bounds = new Rectangle(12, 68, 284, 515), BackColor = Surface };
        Controls.Add(sidebar);
        AddLabel(sidebar, "01  /  PENCERE BAĞLANTISI",
            new Rectangle(15, 14, 254, 20), 8f, PrimaryHover, FontStyle.Bold);
        AddLabel(sidebar, "Oyun pencereleri",
            new Rectangle(15, 34, 254, 27), 12.5f, TextColor, FontStyle.Bold);

        _scanStatus = AddLabel(sidebar, "Windows pencereleri aranıyor...",
            new Rectangle(15, 62, 254, 19), 8.3f, Muted);

        _windowList = new ListBox
        {
            Bounds = new Rectangle(15, 87, 254, 123),
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 37,
            IntegralHeight = false,
            BorderStyle = BorderStyle.None,
            BackColor = PreviewBackground,
            ForeColor = TextColor,
            HorizontalScrollbar = false
        };
        _windowList.DrawItem += PaintWindowItem;
        _windowList.SelectedIndexChanged += (_, _) => UpdateControls();
        _windowList.DoubleClick += (_, _) => StartCapture();
        sidebar.Controls.Add(_windowList);

        var refresh = MakeButton("↻   Pencereleri yenile",
            new Rectangle(15, 220, 254, 34),
            Color.FromArgb(35, 51, 78), Color.FromArgb(49, 69, 101));
        refresh.Click += (_, _) => RefreshWindows();
        sidebar.Controls.Add(refresh);

        AddLabel(sidebar, "02  /  ÖNİZLEME",
            new Rectangle(15, 267, 254, 20), 8f, PrimaryHover, FontStyle.Bold);
        _fpsSelector = new ComboBox
        {
            Bounds = new Rectangle(15, 293, 254, 33),
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = PreviewBackground,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 10f)
        };
        _fpsSelector.Items.AddRange(new object[] { "5 FPS", "10 FPS", "15 FPS" });
        _fpsSelector.SelectedIndex = 1;
        sidebar.Controls.Add(_fpsSelector);

        _connectButton = MakeButton("▶   Pencereye bağlan",
            new Rectangle(15, 340, 254, 36), Primary, PrimaryHover);
        _connectButton.Click += (_, _) => StartCapture();
        sidebar.Controls.Add(_connectButton);

        _stopButton = MakeButton("■   Bağlantıyı durdur",
            new Rectangle(15, 385, 254, 36),
            Color.FromArgb(66, 39, 56), Color.FromArgb(91, 48, 69));
        _stopButton.Click += (_, _) => StopCapture();
        sidebar.Controls.Add(_stopButton);

        _saveButton = MakeButton("▣   Ekran görüntüsü kaydet",
            new Rectangle(15, 430, 254, 36),
            Color.FromArgb(35, 51, 78), Color.FromArgb(49, 69, 101));
        _saveButton.Click += (_, _) => SaveScreenshot();
        sidebar.Controls.Add(_saveButton);

        _connectionStatus = AddLabel(sidebar, "Pencere seçilmedi.",
            new Rectangle(15, 479, 252, 30), 8f, Muted);

        var previewCard = new CardPanel { Bounds = new Rectangle(307, 68, 621, 390), BackColor = Surface };
        Controls.Add(previewCard);
        AddLabel(previewCard, "CANLI PENCERE GÖRÜNTÜSÜ",
            new Rectangle(17, 14, 350, 19), 8.5f, PrimaryHover, FontStyle.Bold);
        AddLabel(previewCard, "Oyun önizlemesi",
            new Rectangle(17, 32, 400, 25), 12.5f, TextColor, FontStyle.Bold);

        var previewHost = new Panel
        {
            Bounds = new Rectangle(16, 65, 589, 255),
            BackColor = PreviewBackground
        };
        previewCard.Controls.Add(previewHost);
        _preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = PreviewBackground
        };
        previewHost.Controls.Add(_preview);

        _placeholder = new Label
        {
            Text = "▣\r\n\r\nOyun penceresi bekleniyor\r\n\r\nPencereden seçip Bağlan'a bas",
            ForeColor = Muted,
            Font = new Font("Segoe UI", 12f),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = PreviewBackground,
            Dock = DockStyle.Fill
        };
        previewHost.Controls.Add(_placeholder);
        _placeholder.BringToFront();

        AddLabel(previewCard, "ANLIK FPS", new Rectangle(20, 332, 124, 16), 8f, Muted);
        _fpsText = AddLabel(previewCard, "—", new Rectangle(20, 348, 124, 30), 13f, TextColor, FontStyle.Bold);
        AddLabel(previewCard, "ÇÖZÜNÜRLÜK", new Rectangle(193, 332, 180, 16), 8f, Muted);
        _sizeText = AddLabel(previewCard, "—", new Rectangle(193, 348, 180, 30), 13f, TextColor, FontStyle.Bold);
        AddLabel(previewCard, "TOPLAM KARE", new Rectangle(421, 332, 160, 16), 8f, Muted);
        _framesText = AddLabel(previewCard, "0", new Rectangle(421, 348, 160, 30), 13f, TextColor, FontStyle.Bold);

        var logCard = new CardPanel { Bounds = new Rectangle(307, 470, 621, 113), BackColor = Surface };
        Controls.Add(logCard);
        AddLabel(logCard, "SİSTEM GÜNLÜĞÜ",
            new Rectangle(16, 10, 250, 18), 8.5f, PrimaryHover, FontStyle.Bold);
        _log = new RichTextBox
        {
            Bounds = new Rectangle(16, 34, 589, 65),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false,
            BackColor = PreviewBackground,
            ForeColor = Muted,
            Font = new Font("Consolas", 8.5f),
            TabStop = false
        };
        logCard.Controls.Add(_log);

        _scanTimer = new System.Windows.Forms.Timer { Interval = 5500 };
        _scanTimer.Tick += (_, _) =>
        {
            if (_captureCts == null)
                RefreshWindows();
        };

        Shown += (_, _) =>
        {
            WriteLog("SekoBot başlatıldı. Godot veya Python gerekmiyor.");
            RefreshWindows();
            _scanTimer.Start();
        };
        FormClosing += (_, _) =>
        {
            _scanTimer.Stop();
            StopCapture();
            var previous = _preview.Image;
            _preview.Image = null;
            previous?.Dispose();
        };
        UpdateControls();
    }

    private static Label AddLabel(Control parent, string text, Rectangle bounds,
        float fontSize, Color color, FontStyle style = FontStyle.Regular)
    {
        var label = new Label
        {
            Text = text,
            Bounds = bounds,
            AutoSize = false,
            Font = new Font("Segoe UI", fontSize, style),
            ForeColor = color,
            BackColor = parent.BackColor,
            AutoEllipsis = true
        };
        parent.Controls.Add(label);
        return label;
    }

    private static Button MakeButton(string text, Rectangle bounds, Color normal, Color hovered)
    {
        var button = new Button
        {
            Text = text,
            Bounds = bounds,
            BackColor = normal,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hovered;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(
            Math.Max(0, hovered.R - 20),
            Math.Max(0, hovered.G - 20),
            Math.Max(0, hovered.B - 20));
        return button;
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        NativeWindows.ReleaseCapture();
        NativeWindows.SendMessage(Handle, 0x00A1, (nint)2, nint.Zero);
    }

    private void PaintWindowItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _windowList.Items.Count)
            return;
        if (_windowList.Items[e.Index] is not WindowInfo window)
            return;

        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var fill = new SolidBrush(selected ? Color.FromArgb(36, 68, 112) : PreviewBackground))
            e.Graphics.FillRectangle(fill, e.Bounds);
        var nameBounds = new Rectangle(e.Bounds.X + 9, e.Bounds.Y + 4,
            e.Bounds.Width - 17, 18);
        using var bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var secondary = new Font("Segoe UI", 7.5f);
        TextRenderer.DrawText(e.Graphics, window.Title, bold, nameBounds, TextColor,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        var details = $"{window.Width} × {window.Height}   •   PID {window.ProcessId}";
        if (window.IsMinimized) details += "   •   Küçültülmüş";
        var detailBounds = new Rectangle(e.Bounds.X + 9, e.Bounds.Y + 21,
            e.Bounds.Width - 17, 14);
        TextRenderer.DrawText(e.Graphics, details, secondary, detailBounds, Muted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void RefreshWindows()
    {
        if (_captureCts != null)
            return;

        try
        {
            var previous = _windowList.SelectedItem as WindowInfo;
            var windows = WindowService.GetOpenWindows();

            _windowList.BeginUpdate();
            try
            {
                _windowList.Items.Clear();
                foreach (var window in windows)
                    _windowList.Items.Add(window);

                int selectedIndex = -1;
                if (previous != null)
                {
                    for (int i = 0; i < windows.Count; i++)
                    {
                        if (windows[i].Handle == previous.Handle &&
                            windows[i].ProcessId == previous.ProcessId)
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }

                if (selectedIndex < 0)
                    selectedIndex = windows.ToList().FindIndex(window => window.IsMetin2);
                if (selectedIndex < 0 && windows.Count > 0)
                    selectedIndex = 0;
                _windowList.SelectedIndex = selectedIndex;
            }
            finally
            {
                _windowList.EndUpdate();
            }

            _scanStatus.Text = windows.Count == 0
                ? "Pencere bulunamadı. Oyunu açıp yenile."
                : $"{windows.Count} pencere bulundu.";
            SetStatus(windows.Count == 0 ? "BEKLENİYOR" : "HAZIR",
                windows.Count == 0 ? Muted : Green);
            UpdateControls();

            if (!_autoAttached && windows.Count(window => window.IsMetin2) == 1)
            {
                _autoAttached = true;
                BeginInvoke((Action)StartCapture);
            }
        }
        catch (Exception ex)
        {
            _scanStatus.Text = "Tarama hatası. Sistem günlüğüne bakın.";
            SetStatus("TARAMA HATASI", Red);
            WriteLog("Pencere tarama hatası: " + ex.Message);
        }
    }

    private int TargetFps => _fpsSelector.SelectedIndex switch { 0 => 5, 2 => 15, _ => 10 };

    private void StartCapture()
    {
        if (_captureCts != null)
            return;
        if (_windowList.SelectedItem is not WindowInfo window)
        {
            WriteLog("Önce bir oyun penceresi seçin.");
            return;
        }
        if (!WindowService.TryGetRegion(window, out _, out string error))
        {
            SetStatus("BAĞLANTI HATASI", Red);
            WriteLog(error);
            return;
        }

        _captureCts = new CancellationTokenSource();
        _fpsText.Text = "—";
        _frameCount = 0;
        _framesText.Text = "0";
        _scanStatus.Text = "Oyun penceresine bağlandı.";
        _connectionStatus.Text = $"{window.Title}\r\nPID {window.ProcessId}  ·  HWND 0x{window.Handle:X}";
        SetStatus("BAĞLANIYOR", Muted);
        WriteLog($"Bağlanılıyor: {window.Title} (PID {window.ProcessId})");
        WriteLog("Not: Oyun görünür olmalı; üstünü başka pencere kapatmamalı.");
        UpdateControls();
        _ = CaptureLoopAsync(window, TargetFps, _captureCts.Token);
    }

    private async Task CaptureLoopAsync(WindowInfo window, int captureFps, CancellationToken token)
    {
        var measure = Stopwatch.StartNew();
        int framesInSample = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var step = Stopwatch.StartNew();
                CaptureResult result = await Task.Run(
                    () => ScreenCapture.Grab(window), token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    result.Frame?.Dispose();
                    break;
                }

                if (result.Frame == null)
                {
                    PostUi(() =>
                    {
                        StopCapture();
                        SetStatus("YAKALAMA HATASI", Red);
                        WriteLog(result.Error);
                    });
                    break;
                }

                var bitmap = result.Frame;
                framesInSample++;
                double fps = 0;
                if (measure.Elapsed.TotalSeconds >= 1.0)
                {
                    fps = framesInSample / measure.Elapsed.TotalSeconds;
                    framesInSample = 0;
                    measure.Restart();
                }
                double displayedFps = fps;
                if (!PostUi(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        bitmap.Dispose();
                        return;
                    }
                    var old = _preview.Image;
                    _preview.Image = bitmap;
                    old?.Dispose();
                    _placeholder.Visible = false;
                    _frameCount++;
                    _sizeText.Text = $"{bitmap.Width} × {bitmap.Height}";
                    _framesText.Text = _frameCount.ToString("N0");
                    if (displayedFps > 0) _fpsText.Text = displayedFps.ToString("0.0");
                    _saveButton.Enabled = true;
                    SetStatus("BAĞLI", Green);
                }))
                {
                    bitmap.Dispose();
                    break;
                }

                var remaining = Math.Max(1, (int)(1000.0 / captureFps - step.Elapsed.TotalMilliseconds));
                await Task.Delay(remaining, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal disconnect.
        }
        catch (Exception ex)
        {
            PostUi(() =>
            {
                StopCapture();
                SetStatus("YAKALAMA HATASI", Red);
                WriteLog("Yakalama döngüsü: " + ex.Message);
            });
        }
    }

    private bool PostUi(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
            return false;
        try
        {
            Invoke(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void StopCapture()
    {
        if (_captureCts == null)
            return;
        _captureCts.Cancel();
        _captureCts = null;
        _scanStatus.Text = "Bağlantı durduruldu.";
        SetStatus("DURDURULDU", Muted);
        WriteLog("Pencere bağlantısı sonlandırıldı.");
        UpdateControls();
    }

    private void SaveScreenshot()
    {
        if (_preview.Image is not Image image)
        {
            WriteLog("Henüz kaydedilecek görüntü bulunmuyor.");
            return;
        }

        try
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures))
                pictures = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            var directory = Path.Combine(pictures, "SekoBot");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "SekoBot_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
            image.Save(path, ImageFormat.Png);
            WriteLog("PNG kaydedildi: " + path);
        }
        catch (Exception ex)
        {
            WriteLog("PNG kaydedilemedi: " + ex.Message);
            SetStatus("KAYDETME HATASI", Red);
        }
    }

    private void UpdateControls()
    {
        if (_windowList == null || _connectButton == null)
            return;
        bool capturing = _captureCts != null;
        _windowList.Enabled = !capturing;
        _fpsSelector.Enabled = !capturing;
        _connectButton.Enabled = !capturing && _windowList.SelectedItem is WindowInfo;
        _stopButton.Enabled = capturing;
        _saveButton.Enabled = _preview.Image != null;
        if (!capturing)
        {
            _connectionStatus.Text = _windowList.SelectedItem is WindowInfo selected
                ? $"{selected.Title}\r\nPID {selected.ProcessId}"
                : "Pencere seçilmedi.";
        }
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = "● " + text;
        _status.ForeColor = color;
    }

    private void WriteLog(string message)
    {
        if (IsDisposed)
            return;
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        if (_log.TextLength > 14000)
            _log.Text = _log.Text[^11000..];
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(46, 64, 92), 1f);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = new GraphicsPath();
            int r = 14;
            path.AddArc(rect.Left, rect.Top, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Top, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            e.Graphics.DrawPath(pen, path);
        }
    }
}
