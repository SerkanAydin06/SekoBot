using System.Drawing.Drawing2D;
using SekoBot.Native;

namespace SekoBot.UI;

/// <summary>What the small bar shows (filled by the main window).</summary>
/// <param name="GameWindow">The game window the bot plays (0 when none): the bar is tied to it.</param>
/// <param name="HpPotions">HP and MP potions left in the bag (the ones on the potion keys), when known.</param>
internal sealed record MiniInfo(string Name, string Status, Color StatusColor, double? Hp, double? Mp, string ExpRate, string Mobs,
    bool Running, Rectangle? Game, nint GameWindow = 0, int? HpPotions = null, int? MpPotions = null);

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
    private readonly Action _start, _stop, _big, _arrange;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 300 };
    private MiniInfo _info = new("", "", Theme.Muted, null, null, "—", "—", false, null);
    private int _hover = -1;
    private readonly ToolTip _tip = new();

    public MiniBar(Func<MiniInfo> read, Action start, Action stop, Action big, Action arrange)
    {
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
        menu.Items.Add("⤢  Büyüt", null, (_, _) => _big());
        menu.Items.Add("⊞  Pencereleri diz", null, (_, _) => _arrange());
        menu.Items.Add("▶  Başlat / duraklat", null, (_, _) => _start());
        menu.Items.Add("■  Durdur", null, (_, _) => _stop());
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
            // As wide as its game (the band under it is the bar's), at least the bar's own width.
            // The game minimized: the bar went with it (Windows hides what a window owns); nothing to place.
            if (_info.GameWindow != 0 && _info.Game is not { IsEmpty: false })
            {
                Invalidate();
                return;
            }
            int width = _info.Game is { IsEmpty: false } g
                ? Math.Clamp(g.Width, (int)(LogicalWidth * S), (int)(MaxWidth * S))
                : (int)(LogicalWidth * S);
            if (Width != width)
                Width = width;
            var at = _info.Game is { IsEmpty: false } game
                ? WindowLayout.BarPlace(game, Size, Screen.FromRectangle(game).WorkingArea)
                : Stacked();
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

    /// <summary>The three buttons: start, stop, big.</summary>
    private Rectangle Button(int i) => new((int)((W - 118 + i * 38) * S), (int)(6 * S), (int)(32 * S), (int)(28 * S));

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

        using var nameFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 8.5f);
        using var smallBold = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        string name = (Instance.IsFirst ? "" : $"{Instance.Number} · ") + (info.Name.Length > 0 ? info.Name : "SekoBot");
        TextRenderer.DrawText(g, "◆ " + name, nameFont, new Rectangle((int)(14 * s), (int)(6 * s), (int)((W - 150) * s), (int)(22 * s)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, info.Status, small, new Rectangle((int)(14 * s), (int)(29 * s), (int)((W - 186) * s), (int)(20 * s)), info.StatusColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        // The potions left, under the buttons: red when few.
        string Count(int? n) => n is int c ? c.ToString("N0") : "—";
        bool low = info.HpPotions is < 20 || info.MpPotions is < 10;
        TextRenderer.DrawText(g, $"İksir  HP {Count(info.HpPotions)} · MP {Count(info.MpPotions)}", smallBold,
            new Rectangle((int)((W - 172) * s), (int)(38 * s), (int)(166 * s), (int)(18 * s)), low ? Theme.Red : Theme.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // Wider bar: longer HP and MP bars (the experience stays at the right).
        float hp = Math.Clamp((W - 200) * 0.5f, 110, 300), mp = hp * 0.8f;
        Bar(g, "HP", info.Hp, Theme.Red, new RectangleF(14 * s, 58 * s, hp * s, 14 * s), smallBold);
        Bar(g, "MP", info.Mp, Theme.Accent, new RectangleF((20 + hp) * s, 58 * s, mp * s, 14 * s), smallBold);
        float expAt = 28 + hp + mp;
        TextRenderer.DrawText(g, $"{info.ExpRate} EXP/sa · {info.Mobs} mob", small,
            new Rectangle((int)(expAt * s), (int)(54 * s), (int)((W - expAt - 6) * s), (int)(22 * s)), Theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        string[] glyphs = [info.Running ? "⏸" : "▶", "■", "⤢"];
        Color[] backs = [Theme.Primary, Theme.Danger, Theme.Secondary];
        for (int i = 0; i < 3; i++)
        {
            var r = Button(i);
            using var back = new SolidBrush(_hover == i ? ControlPaint.Light(backs[i], 0.25f) : backs[i]);
            using var path = Rounded(r, 6 * s);
            g.FillPath(back, path);
            using var glyph = new Font("Segoe UI Symbol", 11f, FontStyle.Bold);
            TextRenderer.DrawText(g, glyphs[i], glyph, r, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private static void Bar(Graphics g, string caption, double? value, Color fill, RectangleF r, Font font)
    {
        using (var back = new SolidBrush(Theme.Inset))
            g.FillRectangle(back, r);
        if (value is double v)
            using (var front = new SolidBrush(fill))
                g.FillRectangle(front, r.X, r.Y, r.Width * (float)Math.Clamp(v, 0, 1), r.Height);
        string text = value is double p ? $"{caption} {p * 100:0}%" : $"{caption} —";
        TextRenderer.DrawText(g, text, font, Rectangle.Round(r), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
        for (int i = 0; i < 3; i++)
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
                0 => _info.Running ? "Duraklat (F10)" : "Başlat (F10)",
                1 => "Durdur (F11)",
                2 => "Büyüt (çift tık da olur)",
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
            case 0: _start(); break;
            case 1: _stop(); break;
            case 2: _big(); break;
        }
        Pull(false);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitButton(e.Location) < 0)
            _big();
    }
}
