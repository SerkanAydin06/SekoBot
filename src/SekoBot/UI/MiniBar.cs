using System.Drawing.Drawing2D;
using SekoBot.Native;

namespace SekoBot.UI;

/// <summary>What the small bar shows (filled by the main window).</summary>
/// <param name="GameWindow">The game window the bot plays (0 when none): the bar is tied to it.</param>
/// <param name="HpPotions">HP and MP potions left in the bag (the ones on the potion keys), when known.</param>
internal sealed record MiniInfo(string Name, string Status, Color StatusColor, double? Hp, double? Mp, string ExpRate, string Mobs,
    bool Running, Rectangle? Game, nint GameWindow = 0, int? HpPotions = null, int? MpPotions = null, bool Attack = true, string? Market = null,
    string Exp10 = "", string Elapsed = "", IReadOnlyList<(string Name, int Picked, int Thrown)>? Items = null);

/// <summary>
/// The bot made small: one bar (420×84) under its own game window, so many bots fit on one screen
/// next to their games. It shows the character, what the bot is doing, HP/MP and experience per
/// hour and the potions left, with start, stop and "big again" buttons. It is tied to its game
/// window (owned by it): it comes to the front with the game, goes when the game is minimized and
/// moves with it at once; it never takes the focus from the game. Double click (or ⤢): the full window again.
/// </summary>
internal sealed class MiniBar : Form
{
    public const int LogicalWidth = 420, LogicalHeight = 84;

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
        var menu = new ContextMenuStrip();
        menu.Items.Add("⛶  Bu oyunu büyüt", null, (_, _) => _fill());
        menu.Items.Add("⤢  SekoBot'u büyüt", null, (_, _) => _big());
        menu.Items.Add("⊞  Pencereleri diz", null, (_, _) => _arrange());
        menu.Items.Add("▶  Başlat / duraklat", null, (_, _) => _start());
        menu.Items.Add("■  Durdur", null, (_, _) => _stop());
        menu.Items.Add(new ToolStripSeparator());
        foreach (ToolStripItem item in WindowLayout.AllMenu().Items.Cast<ToolStripItem>().ToList())
            menu.Items.Add(item);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("⚔  Saldırı aç / sadece eşya topla", null, (_, _) => _attack());
        menu.Items.Add("✕  SekoBot'u kapat", null, (_, _) => _close());
        ContextMenuStrip = menu;
        _timer.Tick += (_, _) => Pull(true);
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
    private const int MaxWidth = 1100;
    /// <summary>Narrowest the bar gets (small games in a big grid): still everything readable.</summary>
    private const int MinWidth = 440;

    private const int Buttons = 9;
    /// <summary>The buttons: 0 the tab's own, 1 start/pause, 2 stop, 3 all bots, 8 this run's items (drawn 5th),
    /// 4 the game big, 5 arrange all, 6 SekoBot big, 7 close. In the bar's own coordinates (under the item list).</summary>
    private Rectangle Button(int i)
    {
        int at = i <= 3 ? i : i == ItemsButton ? 4 : i + 1;
        return new((int)((W - ButtonsWidth + at * 38) * S), (int)(6 * S), (int)(32 * S), (int)(28 * S));
    }
    private const int ItemsButton = 8;
    private const int ButtonsWidth = Buttons * 38 + 4;
    private readonly ContextMenuStrip _allMenu = WindowLayout.AllMenu();

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

        using var nameFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 8.5f);
        using var smallBold = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        // Every bar shows its SekoBot's number (the 1st too), so they can be told apart.
        string name = $"{Instance.Number} · " + (info.Name.Length > 0 ? info.Name : "SekoBot");
        var nameRow = new Rectangle((int)(14 * s), (int)(6 * s), (int)((W - ButtonsWidth - 32) * s), (int)(22 * s));
        // The version, small, at the right of the name row (which SekoBot runs, at a glance).
        using (var tiny = new Font("Segoe UI", 7.5f))
        {
            string version = "v" + BuildInfo.Version;
            TextRenderer.DrawText(g, version, tiny, nameRow, Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
            nameRow.Width -= TextRenderer.MeasureText(version, tiny).Width + (int)(6 * s);
        }
        if (info.Elapsed.Length > 0)
        {
            // How long it has run, at the right of the name row.
            TextRenderer.DrawText(g, "⏱ " + info.Elapsed, smallBold, nameRow, info.Running ? Theme.Green : Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
            nameRow.Width -= TextRenderer.MeasureText("⏱ " + info.Elapsed, smallBold).Width + (int)(8 * s);
        }
        TextRenderer.DrawText(g, "◆ " + name, nameFont, nameRow, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis);
        bool market = info.Market != null;
        string status = info.Attack || market ? info.Status : "✋ Sadece eşya · " + info.Status;
        TextRenderer.DrawText(g, status, small, new Rectangle((int)(14 * s), (int)(29 * s), (int)((W - ButtonsWidth - 20) * s), (int)(20 * s)), info.Attack || market ? info.StatusColor : Theme.Accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis);
        if (market)
        {
            // The market bot: what it read and found, no HP / potions / experience.
            TextRenderer.DrawText(g, (info.Market.StartsWith("🎣", StringComparison.Ordinal) ? "" : "🛒 ") + info.Market, smallBold, new Rectangle((int)(14 * s), (int)(54 * s), (int)((W - 20) * s), (int)(22 * s)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis);
            DrawButtons(g, s, info);
            return;
        }
        // The potions left, under the buttons: red when few.
        string Count(int? n) => n is int c ? c.ToString("N0") : "—";
        bool low = info.HpPotions is < 20 || info.MpPotions is < 10;
        TextRenderer.DrawText(g, $"İksir  HP {Count(info.HpPotions)} · MP {Count(info.MpPotions)}", smallBold,
            new Rectangle((int)((W - 286) * s), (int)(38 * s), (int)(280 * s), (int)(18 * s)), low ? Theme.Red : Theme.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis);

        // Wider bar: longer HP and MP bars (the experience stays at the right).
        float hp = Math.Clamp((W - 230) * 0.45f, 90, 280), mp = hp * 0.8f;
        Bar(g, "HP", info.Hp, Theme.Red, new RectangleF(14 * s, 58 * s, hp * s, 14 * s), smallBold);
        Bar(g, "MP", info.Mp, Theme.Accent, new RectangleF((20 + hp) * s, 58 * s, mp * s, 14 * s), smallBold);
        float expAt = 28 + hp + mp;
        TextRenderer.DrawText(g, $"{info.ExpRate} EXP/sa" + (info.Exp10.Length > 0 ? $" · son 10 dk {info.Exp10}" : "") + $" · {info.Mobs} mob", small,
            new Rectangle((int)(expAt * s), (int)(54 * s), (int)((W - expAt - 6) * s), (int)(22 * s)), Theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis);

        DrawButtons(g, s, info);
    }

    /// <summary>This run's items (picked up, thrown away / sold), above the bar.</summary>
    private void DrawItems(Graphics g, float s, MiniInfo info)
    {
        var rows = (info.Items ?? []).OrderByDescending(r => r.Picked).ThenBy(r => r.Name).ToList();
        _itemsScroll = Math.Clamp(_itemsScroll, 0, Math.Max(0, rows.Count - ItemRows));
        using var head = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var font = new Font("Segoe UI", 8.5f);
        float right = W - 12, thrownW = 52, pickedW = 52;
        var header = new Rectangle((int)(14 * s), (int)(4 * s), (int)((W - 28) * s), (int)(ItemsHeader * s - 6 * s));
        string title = rows.Count == 0 ? "BU TUR EŞYALAR · bot başlayınca dolar" : $"BU TUR EŞYALAR · {rows.Count} çeşit, {rows.Sum(r => r.Picked):N0} alındı";
        TextRenderer.DrawText(g, title, head, header, Theme.Accent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "Alınan", head, new Rectangle((int)((right - thrownW - pickedW) * s), header.Y, (int)(pickedW * s), header.Height), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
        TextRenderer.DrawText(g, "Atılan", head, new Rectangle((int)((right - thrownW) * s), header.Y, (int)(thrownW * s), header.Height), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
        using (var inset = new SolidBrush(Theme.Inset))
            g.FillRectangle(inset, 10 * s, ItemsHeader * s, (W - 20) * s, ItemRows * ItemRowHeight * s + 2 * s);
        for (int i = 0; i < ItemRows && _itemsScroll + i < rows.Count; i++)
        {
            var (name, picked, thrown) = rows[_itemsScroll + i];
            int y = (int)((ItemsHeader + 1 + i * ItemRowHeight) * s), h = (int)(ItemRowHeight * s);
            TextRenderer.DrawText(g, name, font, new Rectangle((int)(16 * s), y, (int)((right - thrownW - pickedW - 22) * s), h), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, picked > 0 ? picked.ToString("N0") : "", font, new Rectangle((int)((right - thrownW - pickedW) * s), y, (int)(pickedW * s), h), Theme.Text,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
            TextRenderer.DrawText(g, thrown > 0 ? thrown.ToString("N0") : "", font, new Rectangle((int)((right - thrownW) * s), y, (int)(thrownW * s), h), Theme.Muted,
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
        bool market = info.Market != null;
        bool fishing = info.Market?.StartsWith("🎣", StringComparison.Ordinal) == true;
        // 0: the tab's own button · 1: start / pause · 2: stop · 3: all bots · 4: the game big · 5: arrange · 6: SekoBot big · 7: close — the shapes drawn, all centred alike.
        string first = fishing ? "🎣" : market ? "🔍" : info.Attack ? "⚔" : "✋";
        Color[] backs = [market ? Theme.Secondary : info.Attack ? Theme.Secondary : Theme.Accent, Theme.Primary, Theme.Danger, Theme.Secondary, Theme.Secondary, Theme.Secondary, Theme.Secondary, Theme.Surface,
            _itemsOpen ? Theme.Primary : Theme.Secondary];
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 0; i < Buttons; i++)
        {
            var r = Button(i);
            using var back = new SolidBrush(_hover == i ? ControlPaint.Light(backs[i], 0.25f) : backs[i]);
            using var path = Rounded(r, 6 * s);
            g.FillPath(back, path);
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, u = 5.5f * s;
            using var white = new SolidBrush(Theme.Text);
            using var pen = new Pen(Theme.Text, 2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            switch (i)
            {
                case 0:
                    using (var glyph = new Font("Segoe UI Symbol", 11f, FontStyle.Bold))
                        TextRenderer.DrawText(g, first, glyph, r, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
                    break;
                case 1 when info.Running: // pause: two bars
                    g.FillRectangle(white, cx - u, cy - u, u * 0.7f, u * 2);
                    g.FillRectangle(white, cx + u * 0.3f, cy - u, u * 0.7f, u * 2);
                    break;
                case 1: // start: a triangle
                    g.FillPolygon(white, [new PointF(cx - u * 0.7f, cy - u), new PointF(cx - u * 0.7f, cy + u), new PointF(cx + u, cy)]);
                    break;
                case 2: // stop: a square
                    g.FillRectangle(white, cx - u * 0.85f, cy - u * 0.85f, u * 1.7f, u * 1.7f);
                    break;
                case 3: // all bots: two small triangles over a line
                    float t = u * 0.62f;
                    g.FillPolygon(white, [new PointF(cx - u * 1.1f, cy - t - 1), new PointF(cx - u * 1.1f, cy + t - 1), new PointF(cx - u * 1.1f + t * 1.4f, cy - 1)]);
                    g.FillPolygon(white, [new PointF(cx - u * 0.1f, cy - t - 1), new PointF(cx - u * 0.1f, cy + t - 1), new PointF(cx - u * 0.1f + t * 1.4f, cy - 1)]);
                    g.DrawLine(pen, cx - u * 1.1f, cy + u * 1.05f, cx + u * 1.1f, cy + u * 1.05f);
                    break;
                case 4: // the game big: a screen with a bar under it
                    g.DrawRectangle(pen, cx - u, cy - u * 0.9f, u * 2, u * 1.35f);
                    g.DrawLine(pen, cx - u, cy + u * 0.85f, cx + u, cy + u * 0.85f);
                    break;
                case 5: // arrange all: four small windows
                    float q = u * 0.8f;
                    foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                        g.FillRectangle(white, cx + (dx < 0 ? -q - 1 : 1), cy + (dy < 0 ? -q - 1 : 1), q, q);
                    break;
                case 6: // SekoBot big: two corners
                    g.DrawLines(pen, [new PointF(cx - u, cy - u * 0.2f), new PointF(cx - u, cy - u), new PointF(cx - u * 0.2f, cy - u)]);
                    g.DrawLines(pen, [new PointF(cx + u, cy + u * 0.2f), new PointF(cx + u, cy + u), new PointF(cx + u * 0.2f, cy + u)]);
                    break;
                case ItemsButton: // this run's items: a bag
                    g.DrawRectangle(pen, cx - u * 0.9f, cy - u * 0.35f, u * 1.8f, u * 1.35f);
                    g.DrawArc(pen, cx - u * 0.5f, cy - u * 1.05f, u, u * 1.2f, 180, 180);
                    break;
                case 7: // close: an X
                    g.DrawLine(pen, cx - u * 0.8f, cy - u * 0.8f, cx + u * 0.8f, cy + u * 0.8f);
                    g.DrawLine(pen, cx - u * 0.8f, cy + u * 0.8f, cx + u * 0.8f, cy - u * 0.8f);
                    break;
            }
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    private static void Bar(Graphics g, string caption, double? value, Color fill, RectangleF r, Font font)
    {
        using (var back = new SolidBrush(Theme.Inset))
            g.FillRectangle(back, r);
        if (value is double v)
            using (var front = new SolidBrush(fill))
                g.FillRectangle(front, r.X, r.Y, r.Width * (float)Math.Clamp(v, 0, 1), r.Height);
        string text = value is double p ? $"{caption} {p * 100:0}%" : $"{caption} —";
        TextRenderer.DrawText(g, text, font, Rectangle.Round(r), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsTranslateTransform);
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
        for (int i = 0; i < Buttons; i++)
            if (Button(i).Contains(p))
                return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitButton(e.Location);
        if (hit != _hover)
        {
            _hover = hit;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            _tip.SetToolTip(this, hit switch
            {
                0 when _info.Market?.StartsWith("🎣", StringComparison.Ordinal) == true => "Balık tutma",
                0 when _info.Market != null => "Pazar penceresini aç",
                0 => _info.Attack ? "Saldırı açık. Tıkla: sadece yerdeki eşyaları topla" : "Sadece eşya topluyor. Tıkla: saldırıyı aç",
                1 when _info.Market?.StartsWith("🎣", StringComparison.Ordinal) == true => _info.Running ? "Duraklat (sayaçlar korunur)" : "Balık tutmayı başlat / sürdür",
                1 when _info.Market != null => _info.Running ? "Duraklat" : "Sürekli pazar takibini başlat",
                1 => _info.Running ? "Duraklat (F10)" : "Başlat (F10)",
                2 => $"Durdur ve sıfırla ({(Instance.IsFirst ? "F11 / " : "")}Ctrl+Shift+{Math.Min(Instance.Number, 9)})",
                3 => "Tüm botlar: hepsini başlat, duraklat ya da durdur",
                4 => "Bu oyunu büyüt (bot altında kalır); tekrar bas: hepsini diz",
                5 => "Tüm oyunları ve botları diz",
                6 => "SekoBot'u büyüt (çift tık da olur)",
                7 => "SekoBot'u kapat",
                ItemsButton => _itemsOpen ? "Eşya listesini kapat" : "Bu tur toplanan ve atılan eşyalar (şerit yukarı doğru açılır)",
                _ => "Sağ tık: pencereleri diz, büyüt",
            });
            Invalidate();
        }
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
            case 0: _attack(); break;
            case 1: _start(); break;
            case 2: _stop(); break;
            case 3: _allMenu.Show(this, new Point(Button(3).Left, Button(3).Bottom + Top)); return;
            case ItemsButton:
                _itemsOpen = !_itemsOpen;
                _itemsScroll = 0;
                _hover = -1;
                Pull(true);
                return;
            case 4: _fill(); break;
            case 5: _arrange(); break;
            case 6: _big(); break;
            case 7: _close(); return;
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
