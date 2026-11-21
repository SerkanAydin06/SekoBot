using System.Drawing.Drawing2D;
using SekoBot.Native;

namespace SekoBot.UI;

/// <summary>What the small bar shows (filled by the main window).</summary>
/// <param name="GameWindow">The game window the bot plays (0 when none): the bar is tied to it.</param>
/// <param name="HpPotions">HP and MP potions left in the bag (the ones on the potion keys), when known.</param>
internal sealed record MiniInfo(string Name, string Status, Color StatusColor, double? Hp, double? Mp, string ExpRate, string Mobs,
    bool Running, Rectangle? Game, nint GameWindow = 0, int? HpPotions = null, int? MpPotions = null, bool Attack = true, string? Market = null,
    string Exp10 = "", string Elapsed = "", IReadOnlyList<(string Name, int Picked, int Thrown, int Sold)>? Items = null, int Level = 0);

/// <summary>
/// The bot made small: one bar (420×84) under its own game window, so many bots fit on one screen
/// next to their games. It shows the character, what the bot is doing, HP/MP and experience per
/// hour and the potions left, with start, stop and "big again" buttons. It is tied to its game
/// window (owned by it): it comes to the front with the game, goes when the game is minimized and
/// moves with it at once; it never takes the focus from the game. Double click (or ⤢): the full window again.
/// </summary>
internal sealed class MiniBar : Form
{
    /// <summary>One slim row (34 high): 6 games 480 high fit a 1920×1032 screen with a bar under each — the game's
    /// screens are cut off below 480 (seen: the bag's bottom out of the window, the bot clicking beside the items).</summary>
    public const int LogicalWidth = 420, LogicalHeight = 34;

    private readonly Func<MiniInfo> _read;
    private readonly Action _start, _stop, _big, _arrange, _attack, _close, _fill;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 300 };
    private MiniInfo _info = new("", "", Theme.Muted, null, null, "—", "—", false, null);
    private int _hover = -1;
    private readonly ToolTip _tip = new();

    // ── This run's items: the bar grows upwards to list them (the same window, no new one) ──
    private bool _itemsOpen;
    private int _itemsScroll;
    /// <summary>Rows of the item list shown at once (more: the mouse wheel scrolls).</summary>
    private const int ItemRows = 8;
    private const int ItemRowHeight = 17, ItemsHeader = 24;
    /// <summary>How much taller the bar is with the list open (96-dpi units).</summary>
    private const int ItemsLogicalHeight = ItemsHeader + ItemRows * ItemRowHeight + 20;
    /// <summary>The list's height now (pixels): 0 when closed. The bar itself is drawn under it.</summary>
    private int Top => _itemsOpen ? (int)(ItemsLogicalHeight * S) : 0;

    /// <summary>Changes the channel now (the bot running).</summary>
    public Action? ChangeChannel { get; set; }

    public MiniBar(Func<MiniInfo> read, Action start, Action stop, Action big, Action arrange, Action attack, Action close, Action fill)
    {
        _fill = fill;
        _close = close;
        _attack = attack;
        _read = read;
        _start = start;
        _stop = stop;
        _big = big;
        _arrange = arrange;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        ClientSize = new Size(LogicalWidth, LogicalHeight);
        DoubleBuffered = true;
        Text = "SekoBot";
        _menu = new ContextMenuStrip { ShowImageMargin = false, Font = new Font("Segoe UI", 9.5f) };
        _menu.Opening += (_, _) => FillMenu();
        _menu.Closed += (_, _) => { _menuClosedAt = DateTime.Now; Invalidate(); };
        ContextMenuStrip = _menu;
        _timer.Tick += (_, _) => { Pull(true); CloseMenuWhenLeft(); };
        FormClosed += (_, _) =>
        {
            _timer.Dispose();
            Tie(0);
        };
    }

    /// <summary>The bar's height on the screen (pixels) at <paramref name="dpi"/>.</summary>
    public static int HeightAt(int dpi) => (int)Math.Ceiling(LogicalHeight * dpi / 96.0);

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x08000000 | 0x00000080;   // WS_EX_NOACTIVATE: clicks don't take the focus from the game; WS_EX_TOOLWINDOW: not in Alt+Tab
            return p;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            Pull(true);
            _timer.Start();
        }
        else
            _timer.Stop();
    }

    private nint _tiedTo, _hook;
    private Win32.WinEventProc? _onMove;

    /// <summary>Ties the bar to the game window: owned by it (above it, hidden with it), told at once when it moves.</summary>
    private void Tie(nint game)
    {
        if (game == _tiedTo || IsDisposed)
            return;
        if (_hook != 0)
            Win32.UnhookWinEvent(_hook);
        _hook = 0;
        _tiedTo = game;
        if (!IsHandleCreated)
            return;
        Win32.SetWindowLongPtr(Handle, Win32.GWLP_HWNDPARENT, game);
        // Tied: above its game only (not above every other program); alone: on top so it can be seen.
        TopMost = game == 0;
        if (game == 0)
            return;
        Win32.GetWindowThreadProcessId(game, out uint pid);
        _onMove = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (hwnd == _tiedTo && idObject == 0 && Visible)
                Pull(true);
        };
        _hook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE, 0, _onMove, pid, 0, Win32.WINEVENT_OUTOFCONTEXT);
    }

    private void Pull(bool place)
    {
        _info = _read();
        Tie(_info.GameWindow);
        if (place)
        {
            // Exactly as wide as its game (the band under it is the bar's), so it never reaches over the next one.
            // The game minimized: the bar went with it (Windows hides what a window owns); nothing to place.
            if (_info.GameWindow != 0 && _info.Game is not { IsEmpty: false })
            {
                Invalidate();
                return;
            }
            int width = _info.Game is { IsEmpty: false } g
                ? Math.Clamp(g.Width, (int)(MinWidth * S), (int)(MaxWidth * S))
                : (int)(LogicalWidth * S);
            if (Width != width)
                Width = width;
            int height = HeightAt(DeviceDpi) + Top;
            if (Height != height)
                Height = height;
            var barSize = new Size(Width, HeightAt(DeviceDpi));
            var at = _info.Game is { IsEmpty: false } game
                ? WindowLayout.BarPlace(game, barSize, Screen.FromRectangle(game).WorkingArea)
                : Stacked();
            // The list opens above the bar: the bar row stays where it was.
            at.Y = Math.Max(Screen.FromPoint(at).WorkingArea.Top, at.Y - Top);
            if (Location != at)
                Location = at;
        }
        Invalidate();
    }

    /// <summary>No game yet: bottom right of the screen, one above the other by bot number.</summary>
    private Point Stacked()
    {
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
        return new Point(work.Right - Width - 8, work.Bottom - Height * Instance.Number - 8);
    }

    private float S => DeviceDpi / 96f;
    /// <summary>The bar's width now, in 96-dpi units.</summary>
    private float W => ClientSize.Width / S;
    private const int MaxWidth = 10000; // as wide as its game, also a game made big (the user: the bar stayed short under it)
    /// <summary>Narrowest the bar gets (small games in a big grid): still everything readable.</summary>
    private const int MinWidth = 440;

    /// <summary>Four buttons with words (the user: icons alone were hard to tell apart): start / pause, stop,
    /// this run's items, the menu with the rest.</summary>
    private const int StartButton = 0, ItemsButton = 2, FillButton = 3, ArrangeButton = 4, MenuButton = 5;
    private static int ButtonWidth(int kind) => kind switch { StartButton => 72, ItemsButton => 68, FillButton => 60, ArrangeButton => 46, _ => 60 };
    private const int ButtonGap = 4;
    /// <summary>The buttons shown: start (stop while running), this run's items (not for a market bot), the game
    /// big, arrange, the menu.</summary>
    private int[] Kinds => _info.Market != null && !_info.Market.StartsWith("🎣", StringComparison.Ordinal)
        ? [StartButton, FillButton, ArrangeButton, MenuButton]
        : [StartButton, ItemsButton, FillButton, ArrangeButton, MenuButton];
    private int ButtonsWidth => Kinds.Sum(ButtonWidth) + ButtonGap * (Kinds.Length - 1) + 6;
    /// <summary>A button by its kind, in the bar's own coordinates (under the item list).</summary>
    private Rectangle Button(int kind)
    {
        int x = (int)W - ButtonsWidth;
        foreach (int k in Kinds)
        {
            if (k == kind)
                return new((int)(x * S), (int)(4 * S), (int)(ButtonWidth(k) * S), (int)(26 * S));
            x += ButtonWidth(k) + ButtonGap;
        }
        return Rectangle.Empty;
    }

    private readonly ContextMenuStrip _menu;
    private DateTime _menuClosedAt = DateTime.MinValue, _menuLeftAt = DateTime.MinValue;

    /// <summary>The menu under its button (opened by the mouse coming over the button, or a click).</summary>
    private void OpenMenu()
    {
        if (_menu.Visible)
            return;
        _menuLeftAt = DateTime.MinValue;
        _menu.Show(this, new Point(Button(MenuButton).Left, Button(MenuButton).Bottom + Top));
        Invalidate();
    }

    /// <summary>The menu closes once the mouse has been off both it and its button for a moment.</summary>
    private void CloseMenuWhenLeft()
    {
        if (!_menu.Visible)
            return;
        var at = Cursor.Position;
        var button = RectangleToScreen(Button(MenuButton) with { Y = Button(MenuButton).Y + Top });
        button.Inflate(6, 6);
        var menu = _menu.Bounds;
        menu.Inflate(6, 6);
        if (button.Contains(at) || menu.Contains(at))
        {
            _menuLeftAt = DateTime.MinValue;
            return;
        }
        if (_menuLeftAt == DateTime.MinValue)
            _menuLeftAt = DateTime.Now;
        else if (DateTime.Now - _menuLeftAt > TimeSpan.FromMilliseconds(400))
            _menu.Close();
    }

    /// <summary>The menu's lines, written for what this bot does now (its tab's own action first).</summary>
    private void FillMenu()
    {
        _menu.Items.Clear();
        void Add(string text, Action act) => _menu.Items.Add(text, null, (_, _) => { act(); Pull(false); });
        if (_info.Running)
        {
            Add("❚❚  Duraklat (kaldığı yerden sürer)", _start);
            if (ChangeChannel != null && _info.Market == null)
                Add("🔀  Kanal değiştir (botlarımızın olmadığı en boş kanala)", ChangeChannel);
            _menu.Items.Add(new ToolStripSeparator());
        }
        bool fishing = _info.Market?.StartsWith("🎣", StringComparison.Ordinal) == true;
        // Fishing has no action of its own here (BALIK TUT is the start button).
        if (_info.Market != null && !fishing)
            Add("🔍  Pazar penceresini aç", _attack);
        else if (_info.Market == null)
            Add(_info.Attack ? "✋  Saldırıyı kapat (sadece yerdeki eşyaları topla)" : "⚔  Saldırıyı aç", _attack);
        if (_menu.Items.Count > 0)
            _menu.Items.Add(new ToolStripSeparator());
        foreach (ToolStripItem item in WindowLayout.AllMenu().Items.Cast<ToolStripItem>().ToList())
            _menu.Items.Add(item);
        _menu.Items.Add(new ToolStripSeparator());
        Add("⛶  Bu oyunu büyüt (tekrar: hepsini diz)", _fill);
        Add("⊞  Tüm oyunları ve botları diz", _arrange);
        Add("⤢  SekoBot panelini aç (ayarlar, günlük)", _big);
        _menu.Items.Add(new ToolStripSeparator());
        Add("✕  Bu SekoBot'u kapat", _close);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = S;
        var info = _info;
        using (var border = new Pen(Theme.Border))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        // A strip on the left: green while the bot runs.
        using (var strip = new SolidBrush(info.Running ? Theme.Green : Theme.Secondary))
            g.FillRectangle(strip, 0, 0, 4 * s, Height);
        if (_itemsOpen)
        {
            DrawItems(g, s, info);
            g.TranslateTransform(0, Top);
        }

        // One slim row: the name, how long and the version above; what it is doing under it; HP and MP as two
        // thin lines at the bottom. The rest (potions, experience) in the tip when the mouse is on the bar.
        using var nameFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 7.5f);
        using var smallBold = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis;
        const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform;
        float textW = W - ButtonsWidth - 16;
        // Every bar shows its SekoBot's number (the 1st too), so they can be told apart.
        string name = $"{Instance.Number} · " + (info.Name.Length > 0 ? info.Name : "SekoBot") + (info.Level > 0 ? $" (Sv {info.Level})" : "");
        var nameRow = new Rectangle((int)(10 * s), (int)(1 * s), (int)(textW * s), (int)(16 * s));
        string tail = info.Elapsed.Length > 0 ? "⏱ " + info.Elapsed : "";
        TextRenderer.DrawText(g, tail, small, nameRow, info.Running && info.Elapsed.Length > 0 ? Theme.Green : Theme.Muted, Right);
        nameRow.Width -= TextRenderer.MeasureText(tail, small).Width + (int)(6 * s);
        // The version right before the time (the user: "botun süresinin hemen sol tarafına").
        string version = "v" + BuildInfo.Version;
        TextRenderer.DrawText(g, version, small, nameRow, Theme.Muted, Right);
        nameRow.Width -= TextRenderer.MeasureText(version, small).Width + (int)(6 * s);
        TextRenderer.DrawText(g, "◆ " + name, nameFont, nameRow, Theme.Text, Left);
        bool market = info.Market != null;
        string status = market ? info.Market! : info.Attack ? info.Status : "✋ Sadece eşya · " + info.Status;
        var statusRow = new Rectangle((int)(10 * s), (int)(16 * s), (int)(textW * s), (int)(13 * s));
        if (!market)
        {
            // Potions left and the experience per hour, always in sight (the user: both matter).
            string Count(int? n) => n is int c ? c.ToString("N0") : "—";
            bool low = info.HpPotions is < 20 || info.MpPotions is < 10;
            string exp = info.ExpRate is { Length: > 0 } rate && rate != "—" ? rate + "/sa" : "";
            if (exp.Length > 0)
            {
                TextRenderer.DrawText(g, exp, smallBold, statusRow, Theme.Yellow, Right);
                statusRow.Width -= TextRenderer.MeasureText(exp, smallBold).Width + (int)(4 * s);
            }
            string pots = $"HP {Count(info.HpPotions)} · MP {Count(info.MpPotions)}" + (exp.Length > 0 ? " ·" : "");
            TextRenderer.DrawText(g, pots, smallBold, statusRow, low ? Theme.Red : Theme.Text, Right);
            statusRow.Width -= TextRenderer.MeasureText(pots, smallBold).Width + (int)(6 * s);
        }
        TextRenderer.DrawText(g, status, small, statusRow, market ? Theme.Text : info.Attack ? info.StatusColor : Theme.Accent, Left);
        if (!market)
        {
            // HP and MP: two thin lines under the text, red / blue (darker when few potions are left).
            float barW = textW / 2 - 3;
            bool low = info.HpPotions is < 20 || info.MpPotions is < 10;
            void Line(double? v, Color c, float x)
            {
                using var back = new SolidBrush(Theme.Inset);
                g.FillRectangle(back, x * s, 30 * s, barW * s, 3 * s);
                if (v is double f)
                    using (var front = new SolidBrush(c))
                        g.FillRectangle(front, x * s, 30 * s, barW * s * (float)Math.Clamp(f, 0, 1), 3 * s);
            }
            Line(info.Hp, low ? ControlPaint.Dark(Theme.Red) : Theme.Red, 10);
            Line(info.Mp, Theme.Accent, 10 + barW + 6);
        }
        DrawButtons(g, s, info);
    }

    /// <summary>This run's items (picked up, thrown away / sold), above the bar.</summary>
    private void DrawItems(Graphics g, float s, MiniInfo info)
    {
        var rows = (info.Items ?? []).OrderByDescending(r => r.Picked).ThenBy(r => r.Name).ToList();
        _itemsScroll = Math.Clamp(_itemsScroll, 0, Math.Max(0, rows.Count - ItemRows));
        using var head = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var font = new Font("Segoe UI", 8.5f);
        float right = W - 12, soldW = 52, thrownW = 52, pickedW = 52;
        var header = new Rectangle((int)(14 * s), (int)(4 * s), (int)((W - 28) * s), (int)(ItemsHeader * s - 6 * s));
        string title = rows.Count == 0 ? "BU TUR EŞYALAR · bot başlayınca dolar" : $"BU TUR EŞYALAR · {rows.Count} çeşit, {rows.Sum(r => r.Picked):N0} alındı";
        TextRenderer.DrawText(g, title, head, header, Theme.Accent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "Alınan", head, new Rectangle((int)((right - soldW - thrownW - pickedW) * s), header.Y, (int)(pickedW * s), header.Height), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
        TextRenderer.DrawText(g, "Atılan", head, new Rectangle((int)((right - soldW - thrownW) * s), header.Y, (int)(thrownW * s), header.Height), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
        TextRenderer.DrawText(g, "Satılan", head, new Rectangle((int)((right - soldW) * s), header.Y, (int)(soldW * s), header.Height), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
        using (var inset = new SolidBrush(Theme.Inset))
            g.FillRectangle(inset, 10 * s, ItemsHeader * s, (W - 20) * s, ItemRows * ItemRowHeight * s + 2 * s);
        for (int i = 0; i < ItemRows && _itemsScroll + i < rows.Count; i++)
        {
            var (name, picked, thrown, sold) = rows[_itemsScroll + i];
            int y = (int)((ItemsHeader + 1 + i * ItemRowHeight) * s), h = (int)(ItemRowHeight * s);
            TextRenderer.DrawText(g, name, font, new Rectangle((int)(16 * s), y, (int)((right - soldW - thrownW - pickedW - 22) * s), h), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, picked > 0 ? picked.ToString("N0") : "", font, new Rectangle((int)((right - soldW - thrownW - pickedW) * s), y, (int)(pickedW * s), h), Theme.Text,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
            TextRenderer.DrawText(g, thrown > 0 ? thrown.ToString("N0") : "", font, new Rectangle((int)((right - soldW - thrownW) * s), y, (int)(thrownW * s), h), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
            TextRenderer.DrawText(g, sold > 0 ? sold.ToString("N0") : "", font, new Rectangle((int)((right - soldW) * s), y, (int)(soldW * s), h), Theme.Green,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
        if (rows.Count > ItemRows)
            TextRenderer.DrawText(g, $"{_itemsScroll + 1}–{Math.Min(rows.Count, _itemsScroll + ItemRows)} / {rows.Count} (tekerlek)", font,
                new Rectangle((int)(14 * s), (int)((ItemsHeader + ItemRows * ItemRowHeight + 3) * s), (int)((W - 28) * s), (int)(15 * s)), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.PreserveGraphicsTranslateTransform);
        using var line = new Pen(Theme.Border);
        g.DrawLine(line, 4 * s, Top - 1, Width, Top - 1);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_itemsOpen || e.Y >= Top)
            return;
        _itemsScroll -= Math.Sign(e.Delta) * 3;
        Invalidate();
    }

    private void DrawButtons(Graphics g, float s, MiniInfo info)
    {
        bool fishing = info.Market?.StartsWith("🎣", StringComparison.Ordinal) == true;
        string startText = info.Running ? "■  Durdur" : fishing ? "▶  Balık tut" : info.Market != null ? "▶  Takip" : "▶  Başlat";
        using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (int kind in Kinds)
        {
            var (text, back) = kind switch
            {
                StartButton => (startText, info.Running ? Theme.Danger : Theme.Primary),
                ItemsButton => ("🎒 Eşyalar", _itemsOpen ? Theme.Primary : Theme.Secondary),
                FillButton => ("⛶ Büyüt", Theme.Secondary),
                ArrangeButton => ("⊞ Diz", Theme.Secondary),
                _ => ("☰ Menü", _menu.Visible ? Theme.Primary : Theme.Secondary),
            };
            var r = Button(kind);
            using var brush = new SolidBrush(_hover == kind ? ControlPaint.Light(back, 0.25f) : back);
            using var path = Rounded(r, 6 * s);
            g.FillPath(brush, path);
            TextRenderer.DrawText(g, text, font, r, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    private static GraphicsPath Rounded(Rectangle r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private int HitButton(Point p)
    {
        p.Y -= Top;
        foreach (int kind in Kinds)
            if (Button(kind).Contains(p))
                return kind;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitButton(e.Location);
        // The menu opens as the mouse comes over its button (no click needed).
        if (hit == MenuButton && hit != _hover && DateTime.Now - _menuClosedAt > TimeSpan.FromMilliseconds(350))
            OpenMenu();
        if (hit != _hover)
        {
            _hover = hit;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            _tip.SetToolTip(this, hit switch
            {
                StartButton => _info.Running ? $"Durdur ({(Instance.IsFirst ? "F11 / " : "")}Ctrl+Shift+{Math.Min(Instance.Number, 9)}); duraklatmak için Menü"
                                             : "Başlat (F10)",
                ItemsButton => _itemsOpen ? "Eşya listesini kapat" : "Bu tur alınan, atılan ve satılan eşyalar (şerit yukarı doğru açılır)",
                FillButton => "Bu oyunu ekrana büyüt (bot altında kalır); tekrar bas: hepsini diz",
                ArrangeButton => "Tüm oyunları ve botları diz",
                MenuButton => "Tüm botlar, saldırı aç / kapat, paneli aç, kapat",
                _ => Details(),
            });
            Invalidate();
        }
    }

    /// <summary>What the slim bar has no room for: HP / MP, potions, experience (the tip on the bar).</summary>
    private string Details()
    {
        var i = _info;
        string Count(int? n) => n is int c ? c.ToString("N0") : "—";
        string Pct(double? v) => v is double f ? $"%{f * 100:0}" : "—";
        var lines = new List<string> { $"{Instance.Number} · {(i.Name.Length > 0 ? i.Name : "SekoBot")}  ·  v{BuildInfo.Version}", i.Status };
        if (i.Market != null)
            lines.Add(i.Market);
        else
        {
            lines.Add($"HP {Pct(i.Hp)} · MP {Pct(i.Mp)}  ·  İksir HP {Count(i.HpPotions)} · MP {Count(i.MpPotions)}");
            lines.Add($"{i.ExpRate} EXP/sa" + (i.Exp10.Length > 0 ? $" · son 10 dk {i.Exp10}" : "") + $" · {i.Mobs} mob");
        }
        lines.Add("Çift tık: SekoBot panelini aç · sağ tık: menü");
        return string.Join("\n", lines);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left)
            return;
        switch (HitButton(e.Location))
        {
            case StartButton:
                if (_info.Running)
                    _stop();
                else
                    _start();
                break;
            case ItemsButton:
                _itemsOpen = !_itemsOpen;
                _itemsScroll = 0;
                _hover = -1;
                Pull(true);
                return;
            case FillButton: _fill(); break;
            case ArrangeButton: _arrange(); break;
            case MenuButton:
                // A click with the menu open (it closed on that same click): stays closed.
                if (_menu.Visible || DateTime.Now - _menuClosedAt < TimeSpan.FromMilliseconds(350))
                    _menu.Close();
                else
                    OpenMenu();
                return;
        }
        Pull(false);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitButton(e.Location) < 0 && e.Y >= Top)
            _big();
    }
}
