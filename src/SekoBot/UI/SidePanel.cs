using System.Drawing.Drawing2D;
using SekoBot.Native;

namespace SekoBot.UI;

/// <summary>
/// The small mode with one game on the computer (the user: one game per device now): a panel as tall as the game,
/// on its right — who, what the bot does now, HP / MP, the six tiles of the panel's summary (bag, potions, break,
/// place, around, this session) and the buttons: start / stop, the live map, the full window, close. Tied to the game
/// window (above it, hidden with it, moved with it); it never takes the focus from the game.
/// </summary>
internal sealed class SidePanel : Form
{
    /// <summary>What the bot does now and the tiles (the panel's "DURUM").</summary>
    internal sealed record Summary(string Now, Tone NowTone, IReadOnlyList<StatusSummary.Tile> Tiles);

    private readonly Func<MiniInfo> _read;
    private readonly Func<Summary> _summary;
    private readonly Action _start, _stop, _map, _big, _close;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private MiniInfo _info = new("", "", Theme.Muted, null, null, "—", "—", false, null);
    private Summary _now = new("", Tone.Neutral, []);
    private int _hover = -1;
    private nint _tiedTo, _hook;
    private Win32.WinEventProc? _onMove;

    private const int StartStop = 0, Map = 1, Big = 2, Close = 3;

    public SidePanel(Func<MiniInfo> read, Func<Summary> summary, Action start, Action stop, Action map, Action big, Action close)
    {
        (_read, _summary, _start, _stop, _map, _big, _close) = (read, summary, start, stop, map, big, close);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Background;
        ClientSize = new Size(WindowLayout.SidePanelWidth, 600);
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        Text = "PARDUS";
        AppIcon.Use(this);
        _timer.Tick += (_, _) => Pull();
        FormClosed += (_, _) =>
        {
            _timer.Dispose();
            Tie(0);
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x08000000 | 0x00000080; // no activation (the game keeps the focus), not in Alt+Tab
            return p;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            Pull();
            _timer.Start();
        }
        else
            _timer.Stop();
    }

    /// <summary>Put beside the game again (after the game was arranged).</summary>
    public void Place()
    {
        if (Visible)
            Pull();
    }

    private float S => DeviceDpi / 96f;

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
        TopMost = game == 0;
        if (game == 0)
            return;
        Win32.GetWindowThreadProcessId(game, out uint pid);
        _onMove = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (hwnd == _tiedTo && idObject == 0 && Visible)
                Pull();
        };
        _hook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE, 0, _onMove, pid, 0, Win32.WINEVENT_OUTOFCONTEXT);
    }

    private void Pull()
    {
        _info = _read();
        _now = _summary();
        Tie(_info.GameWindow);
        if (_info.Game is { IsEmpty: false } game)
        {
            var work = Screen.FromRectangle(game).WorkingArea;
            int width = (int)(WindowLayout.SidePanelWidth * S);
            int height = Math.Max((int)(420 * S), game.Height);
            // On the game's right; no room there: on its left; neither: over its right edge.
            int x = game.Right + width <= work.Right ? game.Right : game.Left - width >= work.Left ? game.Left - width : work.Right - width;
            var bounds = new Rectangle(x, game.Top, width, Math.Min(height, work.Bottom - game.Top));
            if (Bounds != bounds)
                Bounds = bounds;
        }
        else if (_info.GameWindow == 0)
        {
            var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
            var bounds = new Rectangle(work.Right - (int)(WindowLayout.SidePanelWidth * S), work.Top, (int)(WindowLayout.SidePanelWidth * S), (int)(620 * S));
            if (Bounds != bounds)
                Bounds = bounds;
        }
        Invalidate();
    }

    // ── The buttons: two rows of two at the bottom ──

    private RectangleF ButtonRect(int i)
    {
        float pad = 12 * S, gap = 8 * S, h = 36 * S, w = (Width - 2 * pad - gap) / 2;
        float bottom = Height - pad;
        int row = i / 2, col = i % 2;
        return new RectangleF(pad + col * (w + gap), bottom - (2 - row) * h - (1 - row) * gap, w, h);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        for (int i = 0; i < 4; i++)
            if (ButtonRect(i).Contains(e.Location))
                hover = i;
        Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
        if (hover != _hover)
        {
            _hover = hover;
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
        for (int i = 0; i < 4; i++)
            if (ButtonRect(i).Contains(e.Location))
            {
                switch (i)
                {
                    case StartStop: if (_info.Running) _stop(); else _start(); break;
                    case Map: _map(); break;
                    case Big: _big(); break;
                    case Close: _close(); break;
                }
                Pull();
            }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = S, pad = 12 * s;
        using var title = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 8f);
        using var tiny = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        // A gold line at the left edge (where it meets the game).
        using (var edge = new SolidBrush(Theme.Border))
            g.FillRectangle(edge, 0, 0, Math.Max(1, s), Height);

        // ── Who ──
        float y = pad;
        float av = 40 * s;
        var avatar = new RectangleF(pad, y, av, av);
        using (var path = Theme.RoundRect(avatar, 10 * s))
        using (var fill = new LinearGradientBrush(avatar, Theme.GoldLight, Theme.Gold, 45f))
            g.FillPath(fill, path);
        string initials = new(_info.Name.Where(char.IsLetterOrDigit).Take(2).ToArray());
        TextRenderer.DrawText(g, initials.Length > 0 ? initials.ToUpperInvariant() : "?", bold, Rectangle.Round(avatar), Theme.OnGold,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        float tx = pad + av + 10 * s, tw = Width - tx - pad;
        TextRenderer.DrawText(g, _info.Name.Length > 0 ? _info.Name : "Oyun bağlı değil", title, Rectangle.Round(new RectangleF(tx, y - 2 * s, tw, 24 * s)),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string sub = string.Join("  ·  ", new[] { _info.Level > 0 ? $"Sv {_info.Level}" : "", _info.Place, _info.Elapsed }.Where(t => t.Length > 0));
        TextRenderer.DrawText(g, sub, small, Rectangle.Round(new RectangleF(tx, y + 20 * s, tw, 18 * s)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        y += av + 12 * s;

        // ── What it does now ──
        float nowH = 30 * s;
        var strip = new RectangleF(pad, y, Width - 2 * pad, nowH);
        using (var path = Theme.RoundRect(strip, 8 * s))
        using (var fill = new SolidBrush(Theme.Inset))
            g.FillPath(fill, path);
        var tone = _now.NowTone == Tone.Neutral ? Theme.Muted : Tones.Of(_now.NowTone);
        float d = 8 * s;
        using (var dot = new SolidBrush(tone))
            g.FillEllipse(dot, strip.X + 10 * s, strip.Y + (nowH - d) / 2, d, d);
        TextRenderer.DrawText(g, _now.Now.Length > 0 ? _now.Now : _info.Status, bold, Rectangle.Round(new RectangleF(strip.X + 24 * s, strip.Y, strip.Width - 30 * s, nowH)),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        y += nowH + 12 * s;

        // ── HP / MP ──
        void Bar(string name, double? share, Color color, string right)
        {
            TextRenderer.DrawText(g, name, tiny, new Point((int)pad, (int)y), Theme.Muted, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, right, tiny, Rectangle.Round(new RectangleF(pad, y, Width - 2 * pad, 14 * s)), Theme.Text, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            var track = new RectangleF(pad, y + 16 * s, Width - 2 * pad, 6 * s);
            using (var path = Theme.RoundRect(track, 3 * s))
            using (var back = new SolidBrush(Theme.Inset))
                g.FillPath(back, path);
            if (share is double v && v > 0.01)
            {
                using var path = Theme.RoundRect(track with { Width = (float)(track.Width * Math.Clamp(v, 0, 1)) }, 3 * s);
                using var fill = new SolidBrush(color);
                g.FillPath(fill, path);
            }
            y += 30 * s;
        }
        Bar("HP", _info.Hp, Color.FromArgb(255, 79, 96), _info.Hp is double hp ? $"%{hp * 100:0}" : "—");
        Bar("MP", _info.Mp, Color.FromArgb(61, 139, 255), _info.Mp is double mp ? $"%{mp * 100:0}" : "—");
        string rate = _info.ExpRate.Length > 0 && _info.ExpRate != "—" ? $"Saatte {_info.ExpRate} EXP" : "";
        if (rate.Length > 0)
        {
            TextRenderer.DrawText(g, rate, small, new Point((int)pad, (int)y), Theme.Muted, TextFormatFlags.NoPrefix);
            y += 20 * s;
        }
        y += 4 * s;

        // ── The tiles: two a row, as much room as is left above the buttons ──
        float buttonsTop = ButtonRect(0).Top - 12 * s;
        int count = _now.Tiles.Count;
        if (count > 0)
        {
            int rows = (count + 1) / 2;
            float gap = 8 * s, cw = (Width - 2 * pad - gap) / 2;
            float ch = Math.Min(64 * s, (buttonsTop - y - gap * (rows - 1)) / rows);
            if (ch >= 40 * s)
                for (int i = 0; i < count; i++)
                {
                    var t = _now.Tiles[i];
                    var cell = new RectangleF(pad + i % 2 * (cw + gap), y + i / 2 * (ch + gap), cw, ch);
                    using (var path = Theme.RoundRect(cell, 8 * s))
                    {
                        using var fill = new SolidBrush(Theme.Surface);
                        g.FillPath(fill, path);
                        using var pen = new Pen(Theme.Border);
                        g.DrawPath(pen, path);
                    }
                    float ix = cell.X + 10 * s, iw = cell.Width - 20 * s, iy = cell.Y + (ch - 44 * s) / 2;
                    var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                    TextRenderer.DrawText(g, t.Caption, tiny, Rectangle.Round(new RectangleF(ix, iy, iw, 14 * s)), Theme.Faint, flags);
                    TextRenderer.DrawText(g, t.Value, bold, Rectangle.Round(new RectangleF(ix, iy + 13 * s, iw, 18 * s)), t.Tone == Tone.Neutral ? Theme.Text : Tones.Of(t.Tone), flags);
                    TextRenderer.DrawText(g, t.Note, small, Rectangle.Round(new RectangleF(ix, iy + 30 * s, iw, 15 * s)), Theme.Muted, flags);
                }
        }

        // ── The buttons ──
        (string Text, string Icon, bool Gold, bool Danger)[] buttons =
        [
            (_info.Running ? "Durdur" : "Başlat", _info.Running ? "stop" : "play", !_info.Running, _info.Running),
            ("Canlı harita", "map", false, false),
            ("Büyük pencere", "expand", false, false),
            ("Kapat", "x", false, false),
        ];
        for (int i = 0; i < buttons.Length; i++)
        {
            var r = ButtonRect(i);
            var (text, icon, gold, danger) = buttons[i];
            bool over = _hover == i;
            Color back = gold ? (over ? Theme.PrimaryHover : Theme.Gold) : danger ? (over ? Theme.DangerHover : Theme.Danger) : over ? Theme.SecondaryHover : Theme.Secondary;
            Color fore = gold ? Theme.OnGold : danger ? Color.FromArgb(255, 200, 206) : Theme.Text;
            using (var path = Theme.RoundRect(r, 8 * s))
            {
                using var fill = new SolidBrush(back);
                g.FillPath(fill, path);
                if (!gold && !danger)
                {
                    using var pen = new Pen(over ? Theme.BorderStrong : Theme.Border);
                    g.DrawPath(pen, path);
                }
            }
            float isz = 14 * s;
            var textSize = TextRenderer.MeasureText(text, bold);
            float total = isz + 6 * s + textSize.Width, x0 = r.X + (r.Width - total) / 2;
            Icons.Draw(g, icon, new RectangleF(x0, r.Y + (r.Height - isz) / 2, isz, isz), fore);
            TextRenderer.DrawText(g, text, bold, new Point((int)(x0 + isz + 6 * s), (int)(r.Y + (r.Height - textSize.Height) / 2)), fore, TextFormatFlags.NoPrefix);
        }
    }
}
