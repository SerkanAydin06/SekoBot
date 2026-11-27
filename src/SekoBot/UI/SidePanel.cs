using System.Drawing.Drawing2D;
using SekoBot.Native;

namespace SekoBot.UI;

/// <summary>
/// The small mode with one game on the computer: a panel as tall as the game, on its right (the user: one game per
/// device now; the side has the room — a 720-high game leaves little under it on a 1080 screen). On top who and what
/// the bot does, HP / MP and the hour's numbers; under them four pages — DURUM (the panel's six tiles), EŞYALAR
/// (this run's items: picked, thrown, sold; the user missed them), GÜNLÜK (the latest lines of the log, coloured) and
/// HARİTA (the live map) — and the buttons. Tied to the game window (above it, hidden with it, moved with it); it
/// never takes the focus from the game.
/// </summary>
internal sealed class SidePanel : Form
{
    /// <summary>What the bot does now and the tiles (the panel's "DURUM").</summary>
    /// <param name="RunTime">How long this run has played (breaks not counted).</param>
    /// <param name="Run">This run in big numbers (the top card of DURUM).</param>
    /// <param name="Pace">The run's pace in words (per hour).</param>
    internal sealed record Summary(string Now, Tone NowTone, IReadOnlyList<StatusSummary.Tile> Tiles, string RunTime = "",
        IReadOnlyList<(string Caption, string Value, Tone Tone)>? Run = null, string Pace = "");

    /// <summary>A line of the log as the panel shows it.</summary>
    internal readonly record struct Line(DateTime At, string Tag, Tone Tone, string Text);

    private readonly Func<MiniInfo> _read;
    private readonly Func<Summary> _summary;
    private readonly Func<IReadOnlyList<Line>> _log;
    private readonly Action _start, _stop, _big, _close;
    private readonly Control _map;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private MiniInfo _info = new("", "", Theme.Muted, null, null, "—", "—", false, null);
    private Summary _now = new("", Tone.Neutral, []);
    private IReadOnlyList<Line> _lines = [];
    private int _hover = -1, _page, _scroll;
    private nint _tiedTo, _hook;
    private Win32.WinEventProc? _onMove;

    private const int PageStatus = 0, PageItems = 1, PageLog = 2, PageMap = 3;
    private static readonly string[] Pages = ["Durum", "Eşyalar", "Günlük", "Harita"];
    private static readonly string[] PageIcons = ["grid", "bag", "list", "map"];
    /// <summary>Hit areas: the page tabs 0–3, the buttons 10–13.</summary>
    private const int TabBase = 0, ButtonBase = 10;
    private const int StartStop = 0, Big = 1, Arrange = 2, Close = 3;

    public SidePanel(Func<MiniInfo> read, Func<Summary> summary, Func<IReadOnlyList<Line>> log, Control map,
        Action start, Action stop, Action big, Action arrange, Action close)
    {
        (_read, _summary, _log, _map, _start, _stop, _big, _arrangeAction, _close) = (read, summary, log, map, start, stop, big, arrange, close);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Background;
        ClientSize = new Size(WindowLayout.SidePanelWidth, 720);
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        Text = "PARDUS";
        AppIcon.Use(this);
        _map.Visible = false;
        Controls.Add(_map);
        _timer.Tick += (_, _) => Pull();
        FormClosed += (_, _) =>
        {
            _timer.Dispose();
            Tie(0);
        };
    }

    private readonly Action _arrangeAction;

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
        _lines = _log();
        Tie(_info.GameWindow);
        int width = (int)(WindowLayout.SidePanelWidth * S);
        if (_info.Game is { IsEmpty: false } game)
        {
            var work = Screen.FromRectangle(game).WorkingArea;
            int height = Math.Min(Math.Max((int)(560 * S), game.Height), work.Bottom - game.Top);
            // On the game's right; no room there: on its left; neither: over its right edge.
            int x = game.Right + width <= work.Right ? game.Right : game.Left - width >= work.Left ? game.Left - width : work.Right - width;
            var bounds = new Rectangle(x, game.Top, width, height);
            if (Bounds != bounds)
                Bounds = bounds;
        }
        else if (_info.GameWindow == 0)
        {
            var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
            var bounds = new Rectangle(work.Right - width, work.Top, width, Math.Min(work.Height, (int)(720 * S)));
            if (Bounds != bounds)
                Bounds = bounds;
        }
        PlaceMap();
        if (_page == PageMap)
            _map.Invalidate();
        Invalidate();
    }

    // ── Layout (pixels) ──

    private float Pad => 12 * S;
    private float HeaderBottom => 236 * S;
    private RectangleF TabsRect => new(Pad, HeaderBottom, Width - 2 * Pad, 30 * S);
    private RectangleF ButtonRect(int i)
    {
        float gap = 8 * S, h = 34 * S, w = (Width - 2 * Pad - gap) / 2;
        int row = i / 2, col = i % 2;
        return new RectangleF(Pad + col * (w + gap), Height - Pad - (2 - row) * h - (1 - row) * gap, w, h);
    }
    private RectangleF ContentRect
    {
        get
        {
            float top = TabsRect.Bottom + 10 * S, bottom = ButtonRect(0).Top - 10 * S;
            return new RectangleF(Pad, top, Width - 2 * Pad, Math.Max(0, bottom - top));
        }
    }
    private RectangleF TabRect(int i)
    {
        var t = TabsRect;
        float w = (t.Width - 6 * S) / Pages.Length;
        return new RectangleF(t.X + 3 * S + i * w, t.Y + 3 * S, w, t.Height - 6 * S);
    }

    private void PlaceMap()
    {
        bool show = _page == PageMap;
        if (show)
        {
            var c = Rectangle.Round(ContentRect);
            if (_map.Bounds != c)
                _map.Bounds = c;
        }
        if (_map.Visible != show)
            _map.Visible = show;
    }

    // ── Mouse ──

    private int HitTest(Point p)
    {
        for (int i = 0; i < Pages.Length; i++)
            if (TabRect(i).Contains(p))
                return TabBase + i;
        for (int i = 0; i < 4; i++)
            if (ButtonRect(i).Contains(p))
                return ButtonBase + i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = HitTest(e.Location);
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

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_page is PageItems or PageLog)
        {
            _scroll = Math.Max(0, _scroll - Math.Sign(e.Delta) * 3);
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left)
            return;
        int hit = HitTest(e.Location);
        if (hit is >= TabBase and < TabBase + 4)
        {
            _page = hit - TabBase;
            _scroll = 0;
            PlaceMap();
        }
        else if (hit >= ButtonBase)
            switch (hit - ButtonBase)
            {
                case StartStop: if (_info.Running) _stop(); else _start(); break;
                case Big: _big(); break;
                case Arrange: _arrangeAction(); break;
                case Close: _close(); break;
            }
        Pull();
    }

    // ── Drawing ──

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = S, pad = Pad;
        using var title = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        using var bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var big = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 8f);
        using var tiny = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        var left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        // The edge against the game, a soft gold glow at the top.
        using (var edge = new SolidBrush(Theme.Border))
            g.FillRectangle(edge, 0, 0, Math.Max(1, s), Height);
        using (var glow = new LinearGradientBrush(new RectangleF(0, 0, Width, 90 * s), Color.FromArgb(34, Theme.Gold), Color.FromArgb(0, Theme.Gold), 90f))
            g.FillRectangle(glow, 0, 0, Width, 90 * s);

        // ── Who ──
        float y = pad;
        float av = 44 * s;
        var avatar = new RectangleF(pad, y, av, av);
        using (var path = Theme.RoundRect(avatar, 11 * s))
        using (var fill = new LinearGradientBrush(avatar, Theme.GoldLight, Theme.Gold, 45f))
            g.FillPath(fill, path);
        string initials = new(_info.Name.Where(char.IsLetterOrDigit).Take(2).ToArray());
        TextRenderer.DrawText(g, initials.Length > 0 ? initials.ToUpperInvariant() : "?", big, Rectangle.Round(avatar), Theme.OnGold,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        float tx = pad + av + 10 * s, tw = Width - tx - pad;
        TextRenderer.DrawText(g, _info.Name.Length > 0 ? _info.Name : "Oyun bağlı değil", title, Rectangle.Round(new RectangleF(tx, y, tw, 24 * s)), Theme.Text, left);
        string sub = string.Join("  ·  ", new[] { _info.Level > 0 ? $"Sv {_info.Level}" : "", _info.Place, _info.Elapsed }.Where(t => t.Length > 0));
        TextRenderer.DrawText(g, sub, small, Rectangle.Round(new RectangleF(tx, y + 24 * s, tw, 18 * s)), Theme.Muted, left);
        y += av + 10 * s;

        // ── What it does now ──
        float nowH = 30 * s;
        var strip = new RectangleF(pad, y, Width - 2 * pad, nowH);
        var tone = _now.NowTone == Tone.Neutral ? Theme.Muted : Tones.Of(_now.NowTone);
        using (var path = Theme.RoundRect(strip, 8 * s))
        {
            using var fill = new SolidBrush(Theme.Inset);
            g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(70, tone));
            g.DrawPath(pen, path);
        }
        float d = 8 * s;
        using (var halo = new SolidBrush(Color.FromArgb(60, tone)))
            g.FillEllipse(halo, strip.X + 8 * s, strip.Y + (nowH - d * 2) / 2, d * 2, d * 2);
        using (var dot = new SolidBrush(tone))
            g.FillEllipse(dot, strip.X + 12 * s, strip.Y + (nowH - d) / 2, d, d);
        // The why in brackets left out (it is in the log): the line fits.
        string nowText = _now.Now.Length > 0 ? _now.Now : _info.Status;
        if (nowText.IndexOf(" (", StringComparison.Ordinal) is int cut and > 12)
            nowText = nowText[..cut].TrimEnd('.', ';', ',');
        TextRenderer.DrawText(g, nowText, bold, Rectangle.Round(new RectangleF(strip.X + 30 * s, strip.Y, strip.Width - 36 * s, nowH)), Theme.Text, left);
        y += nowH + 10 * s;

        // ── HP / MP side by side ──
        float half = (Width - 2 * pad - 10 * s) / 2;
        void Vital(float x, string name, double? share, Color color)
        {
            TextRenderer.DrawText(g, name, tiny, Rectangle.Round(new RectangleF(x, y, half, 14 * s)), Theme.Muted, left);
            TextRenderer.DrawText(g, share is double v ? $"%{v * 100:0}" : "—", tiny, Rectangle.Round(new RectangleF(x, y, half, 14 * s)), Theme.Text, right);
            var track = new RectangleF(x, y + 16 * s, half, 7 * s);
            using (var path = Theme.RoundRect(track, 3.5f * s))
            using (var back = new SolidBrush(Theme.Inset))
                g.FillPath(back, path);
            if (share is double f && f > 0.01)
            {
                var full = track with { Width = (float)(track.Width * Math.Clamp(f, 0, 1)) };
                using var path = Theme.RoundRect(full, 3.5f * s);
                using var fill = new LinearGradientBrush(full, ControlPaint.Light(color, 0.2f), color, 0f);
                g.FillPath(fill, path);
            }
        }
        Vital(pad, "HP", _info.Hp, Color.FromArgb(255, 79, 96));
        Vital(pad + half + 10 * s, "MP", _info.Mp, Color.FromArgb(61, 139, 255));
        y += 32 * s;

        // ── EXP: how far into the level, how long to the next one (the user: "can ve mananın altına EXP çubuğu,
        // altına seviye atlamak için kalan süre") ──
        {
            float full = Width - 2 * pad;
            string caption = _info.Level > 0 ? $"EXP  ·  Sv {_info.Level} → {_info.Level + 1}" : "EXP";
            TextRenderer.DrawText(g, caption, tiny, Rectangle.Round(new RectangleF(pad, y, full, 14 * s)), Theme.Muted, left);
            TextRenderer.DrawText(g, _info.ExpPercent is double ep ? "%" + ep.ToString("0.00") : "—", tiny,
                Rectangle.Round(new RectangleF(pad, y, full, 14 * s)), Theme.GoldLight, right);
            var track = new RectangleF(pad, y + 16 * s, full, 9 * s);
            using (var path = Theme.RoundRect(track, 4.5f * s))
            using (var back = new SolidBrush(Theme.Inset))
                g.FillPath(back, path);
            if (_info.ExpPercent is double p && p > 0.2)
            {
                var done = track with { Width = (float)(track.Width * Math.Clamp(p / 100, 0, 1)) };
                using var path = Theme.RoundRect(done, 4.5f * s);
                using var fill = new LinearGradientBrush(done, Theme.Gold, Theme.GoldLight, 0f);
                g.FillPath(fill, path);
                using var shine = new SolidBrush(Color.FromArgb(46, 255, 255, 255));
                g.FillRectangle(shine, done.X + 3 * s, done.Y + 1.5f * s, Math.Max(0, done.Width - 6 * s), 2 * s);
            }
            // A tick every tenth, as the game's own bar.
            using (var tick = new Pen(Color.FromArgb(70, Theme.Background), Math.Max(1, s)))
                for (int i = 1; i < 10; i++)
                    g.DrawLine(tick, track.X + track.Width * i / 10, track.Y + 1, track.X + track.Width * i / 10, track.Bottom - 1);
            string eta = _info.ToLevel.Length > 0 ? $"Sv {_info.Level + 1}'e {_info.ToLevel}" : _info.ExpPercent != null ? "Seviyeye kalan süre ölçülüyor…" : "";
            TextRenderer.DrawText(g, eta, small, Rectangle.Round(new RectangleF(pad, y + 27 * s, full, 16 * s)), _info.ToLevel.Length > 0 ? Theme.Text : Theme.Faint, left);
            if (_info.ExpLeft.Length > 0)
                TextRenderer.DrawText(g, $"kalan {_info.ExpLeft} EXP", small, Rectangle.Round(new RectangleF(pad, y + 27 * s, full, 16 * s)), Theme.Muted, right);
        }
        y += 50 * s;

        // ── The hour's numbers: three chips ──
        (string Caption, string Value)[] chips =
        [
            ("SAATTE EXP", _info.ExpRate.Length > 0 ? _info.ExpRate : "—"),
            ("MOB", _info.Mobs.Length > 0 ? _info.Mobs : "—"),
            ("İKSİR HP · MP", _info.HpPotions is int hp && _info.MpPotions is int mp ? $"{hp:N0} · {mp:N0}" : "—"),
        ];
        float cw = (Width - 2 * pad - 16 * s) / 3, ch = 40 * s;
        for (int i = 0; i < chips.Length; i++)
        {
            var r = new RectangleF(pad + i * (cw + 8 * s), y, cw, ch);
            using (var path = Theme.RoundRect(r, 8 * s))
            {
                using var fill = new SolidBrush(Theme.Surface);
                g.FillPath(fill, path);
                using var pen = new Pen(Theme.Border);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, chips[i].Caption, tiny, Rectangle.Round(new RectangleF(r.X + 8 * s, r.Y + 4 * s, r.Width - 12 * s, 14 * s)), Theme.Faint, left);
            TextRenderer.DrawText(g, chips[i].Value, bold, Rectangle.Round(new RectangleF(r.X + 8 * s, r.Y + 18 * s, r.Width - 12 * s, 18 * s)), Theme.Text, left);
        }

        // ── The page tabs ──
        var tabs = TabsRect;
        using (var path = Theme.RoundRect(tabs, 9 * s))
        using (var fill = new SolidBrush(Theme.Inset))
            g.FillPath(fill, path);
        for (int i = 0; i < Pages.Length; i++)
        {
            var r = TabRect(i);
            bool on = _page == i, over = _hover == TabBase + i;
            if (on || over)
            {
                using var path = Theme.RoundRect(r, 7 * s);
                using var fill = new SolidBrush(on ? Theme.Secondary : Color.FromArgb(120, Theme.Secondary));
                g.FillPath(fill, path);
                if (on)
                {
                    using var pen = new Pen(Theme.BorderStrong);
                    g.DrawPath(pen, path);
                }
            }
            var color = on ? Theme.GoldLight : over ? Theme.Text : Theme.Muted;
            float isz = 13 * s;
            var size = TextRenderer.MeasureText(Pages[i], tiny);
            float total = isz + 5 * s + size.Width, x0 = r.X + (r.Width - total) / 2;
            Icons.Draw(g, PageIcons[i], new RectangleF(x0, r.Y + (r.Height - isz) / 2, isz, isz), color, 1.7f);
            TextRenderer.DrawText(g, Pages[i], tiny, new Point((int)(x0 + isz + 5 * s), (int)(r.Y + (r.Height - size.Height) / 2)), color, TextFormatFlags.NoPrefix);
        }

        // ── The page ──
        var content = ContentRect;
        switch (_page)
        {
            case PageStatus: DrawStatus(g, content, bold, small, tiny); break;
            case PageItems: DrawItems(g, content, bold, small, tiny); break;
            case PageLog: DrawLog(g, content, tiny); break;
        }

        // ── The buttons ──
        (string Text, string Icon, bool Gold, bool Danger)[] buttons =
        [
            (_info.Running ? "Durdur" : "Başlat", _info.Running ? "stop" : "play", !_info.Running, _info.Running),
            ("Büyük pencere", "expand", false, false),
            ("Yerleştir", "layers", false, false),
            ("Kapat", "x", false, false),
        ];
        for (int i = 0; i < buttons.Length; i++)
        {
            var r = ButtonRect(i);
            var (text, icon, gold, danger) = buttons[i];
            bool over = _hover == ButtonBase + i;
            Color back = gold ? (over ? Theme.PrimaryHover : Theme.Gold) : danger ? (over ? Theme.DangerHover : Theme.Danger) : over ? Theme.SecondaryHover : Theme.Secondary;
            Color fore = gold ? Theme.OnGold : danger ? Color.FromArgb(255, 200, 206) : i == Close && over ? Theme.Red : Theme.Text;
            using (var path = Theme.RoundRect(r, 9 * s))
            {
                if (gold)
                {
                    using var fill = new LinearGradientBrush(r, over ? Theme.GoldLight : Theme.PrimaryHover, Theme.Gold, 90f);
                    g.FillPath(fill, path);
                }
                else
                {
                    using var fill = new SolidBrush(back);
                    g.FillPath(fill, path);
                }
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

    private void Card(Graphics g, RectangleF r)
    {
        using var path = Theme.RoundRect(r, 9 * S);
        using var fill = new SolidBrush(Theme.Surface);
        g.FillPath(fill, path);
        using var pen = new Pen(Theme.Border);
        g.DrawPath(pen, path);
    }

    /// <summary>
    /// DURUM: on top this run in big numbers (how long it has played, mobs, EXP, yang, deaths and the pace), under it
    /// the tiles two a row, each with its icon, its value and — where it has one — a bar (the bag, the break).
    /// </summary>
    private void DrawStatus(Graphics g, RectangleF area, Font bold, Font small, Font tiny)
    {
        float s = S, gap = 8 * s, y = area.Y;
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        using var number = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var value = new Font("Segoe UI", 11f, FontStyle.Bold);

        // ── This run ──
        if (_now.Run is { Count: > 0 } run)
        {
            float heroH = Math.Min(100 * s, Math.Max(78 * s, area.Height * 0.34f));
            var hero = new RectangleF(area.X, y, area.Width, heroH);
            using (var path = Theme.RoundRect(hero, 10 * s))
            {
                using var fill = new LinearGradientBrush(hero, Color.FromArgb(255, Blend(Theme.Surface, Theme.Gold, 0.10f)), Theme.Surface, 90f);
                g.FillPath(fill, path);
                using var pen = new Pen(Color.FromArgb(90, Theme.Gold));
                g.DrawPath(pen, path);
            }
            float hx = hero.X + 12 * s, hw = hero.Width - 24 * s;
            Icons.Draw(g, "spark", new RectangleF(hx, hero.Y + 9 * s, 13 * s, 13 * s), Theme.GoldLight, 1.7f);
            TextRenderer.DrawText(g, "BU OTURUM", tiny, Rectangle.Round(new RectangleF(hx + 18 * s, hero.Y + 7 * s, hw, 17 * s)), Theme.GoldLight, flags);
            TextRenderer.DrawText(g, _now.RunTime.Length > 0 ? _now.RunTime : "—", bold, Rectangle.Round(new RectangleF(hx, hero.Y + 7 * s, hw, 17 * s)), Theme.Text, right);
            float colW = hw / run.Count, vy = hero.Y + 28 * s;
            for (int i = 0; i < run.Count; i++)
            {
                var (caption, text, tone) = run[i];
                var col = new RectangleF(hx + i * colW, vy, colW, 24 * s);
                TextRenderer.DrawText(g, text, number, Rectangle.Round(col), tone == Tone.Neutral ? Theme.Text : Tones.Of(tone), center);
                TextRenderer.DrawText(g, caption, tiny, Rectangle.Round(col with { Y = vy + 24 * s, Height = 14 * s }), Theme.Faint, center);
                if (i > 0)
                    using (var sep = new Pen(Theme.Border))
                        g.DrawLine(sep, col.X, vy + 4 * s, col.X, vy + 36 * s);
            }
            if (_now.Pace.Length > 0 && heroH >= 86 * s)
                TextRenderer.DrawText(g, _now.Pace, small, Rectangle.Round(new RectangleF(hx, hero.Bottom - 22 * s, hw, 16 * s)), Theme.Muted, center);
            y += heroH + gap;
        }

        // ── The tiles ──
        var tiles = _now.Tiles.Where(t => _now.Run == null || t.Caption != "BU OTURUM").ToList();
        if (tiles.Count % 2 == 1)
            tiles.Add(new("SON 10 DAKİKA", _info.Exp10.Length > 0 ? _info.Exp10 + " EXP" : "—", _info.Exp10.Length > 0 ? "bu sürede kazanılan" : "ölçülüyor", Icon: "bolt"));
        if (tiles.Count == 0)
            return;
        int rows = (tiles.Count + 1) / 2;
        float cw = (area.Width - gap) / 2;
        float ch = Math.Min(96 * s, (area.Bottom - y - gap * (rows - 1)) / rows);
        if (ch < 52 * s)
        {
            // A small screen: each tile one row (its icon, caption and value), two a row.
            float rh = Math.Min(30 * s, (area.Bottom - y - 4 * s * (rows - 1)) / rows);
            if (rh < 18 * s)
                return;
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                var cell = new RectangleF(area.X + i % 2 * (cw + gap), y + i / 2 * (rh + 4 * s), cw, rh);
                Card(g, cell);
                var tone = t.Tone == Tone.Neutral ? Theme.GoldLight : Tones.Of(t.Tone);
                float isz = 12 * s;
                if (t.Icon.Length > 0)
                    Icons.Draw(g, t.Icon, new RectangleF(cell.X + 8 * s, cell.Y + (rh - isz) / 2, isz, isz), tone, 1.6f);
                TextRenderer.DrawText(g, t.Value, bold, Rectangle.Round(new RectangleF(cell.X + 26 * s, cell.Y, cell.Width - 32 * s, rh)),
                    t.Tone == Tone.Neutral ? Theme.Text : tone, flags);
            }
            return;
        }
        bool withNote = ch >= 62 * s;
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var cell = new RectangleF(area.X + i % 2 * (cw + gap), y + i / 2 * (ch + gap), cw, ch);
            Card(g, cell);
            var tone = t.Tone == Tone.Neutral ? Theme.Muted : Tones.Of(t.Tone);
            // A coloured mark on the left when the tile says something (amber, red).
            if (t.Tone != Tone.Neutral)
                using (var mark = new SolidBrush(tone))
                    g.FillRectangle(mark, cell.X + 1, cell.Y + 10 * s, 3 * s, cell.Height - 20 * s);
            float ix = cell.X + 11 * s, iw = cell.Width - 20 * s;
            float content = 18 * s + 22 * s + (withNote ? 16 * s : 0) + (t.Bar != null ? 9 * s : 0);
            float iy = cell.Y + Math.Max(6 * s, (ch - content) / 2);
            // The icon in a small round badge, the caption beside it.
            var badge = new RectangleF(ix, iy, 18 * s, 18 * s);
            using (var b = new SolidBrush(Color.FromArgb(34, t.Tone == Tone.Neutral ? Theme.Gold : tone)))
                g.FillEllipse(b, badge);
            if (t.Icon.Length > 0)
                Icons.Draw(g, t.Icon, RectangleF.Inflate(badge, -4 * s, -4 * s), t.Tone == Tone.Neutral ? Theme.GoldLight : tone, 1.6f);
            TextRenderer.DrawText(g, t.Caption, tiny, Rectangle.Round(new RectangleF(ix + 24 * s, iy, iw - 24 * s, 18 * s)), Theme.Faint, flags);
            TextRenderer.DrawText(g, t.Value, value, Rectangle.Round(new RectangleF(ix, iy + 19 * s, iw, 22 * s)), t.Tone == Tone.Neutral ? Theme.Text : tone, flags);
            float by = iy + 42 * s;
            if (withNote)
            {
                TextRenderer.DrawText(g, t.Note, small, Rectangle.Round(new RectangleF(ix, by, iw, 15 * s)), Theme.Muted, flags);
                by += 19 * s;
            }
            if (t.Bar is double share)
            {
                var track = new RectangleF(ix, Math.Min(by, cell.Bottom - 10 * s), iw, 4 * s);
                using (var path = Theme.RoundRect(track, 2 * s))
                using (var back = new SolidBrush(Theme.Inset))
                    g.FillPath(back, path);
                if (share > 0.01)
                {
                    var done = track with { Width = (float)(track.Width * Math.Clamp(share, 0, 1)) };
                    using var path = Theme.RoundRect(done, 2 * s);
                    using var fill = new SolidBrush(t.Tone == Tone.Neutral ? Theme.Gold : tone);
                    g.FillPath(fill, path);
                }
            }
        }
    }

    private static Color Blend(Color a, Color b, float k) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));

    /// <summary>EŞYALAR: this run's items — picked, thrown, sold — as a table (the wheel scrolls it), the totals under it.</summary>
    private void DrawItems(Graphics g, RectangleF area, Font bold, Font small, Font tiny)
    {
        float s = S;
        Card(g, area);
        var items = _info.Items ?? [];
        var flagsL = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var flagsR = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        if (items.Count == 0)
        {
            TextRenderer.DrawText(g, "Bot çalışınca bu turda alınan, atılan\nve satılan eşyalar burada listelenir.", small, Rectangle.Round(area), Theme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        float num = 46 * s, x = area.X + 10 * s, w = area.Width - 20 * s, nameW = w - 3 * num;
        float rowH = 20 * s, top = area.Y + 8 * s;
        void Row(float ry, string name, string a, string b, string c, Font font, Color color, Color ca, Color cb, Color cc)
        {
            TextRenderer.DrawText(g, name, font, Rectangle.Round(new RectangleF(x, ry, nameW, rowH)), color, flagsL);
            TextRenderer.DrawText(g, a, font, Rectangle.Round(new RectangleF(x + nameW, ry, num, rowH)), ca, flagsR);
            TextRenderer.DrawText(g, b, font, Rectangle.Round(new RectangleF(x + nameW + num, ry, num, rowH)), cb, flagsR);
            TextRenderer.DrawText(g, c, font, Rectangle.Round(new RectangleF(x + nameW + 2 * num, ry, num, rowH)), cc, flagsR);
        }
        Row(top, "EŞYA", "ALINAN", "ATILAN", "SATILAN", tiny, Theme.Faint, Theme.Faint, Theme.Faint, Theme.Faint);
        using (var line = new Pen(Theme.Border))
            g.DrawLine(line, x, top + rowH + 2 * s, x + w, top + rowH + 2 * s);
        float listTop = top + rowH + 6 * s, totalsH = rowH + 8 * s;
        int fit = Math.Max(1, (int)((area.Bottom - listTop - totalsH) / rowH));
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, items.Count - fit));
        for (int i = 0; i < fit && i + _scroll < items.Count; i++)
        {
            var it = items[i + _scroll];
            float ry = listTop + i * rowH;
            if (i % 2 == 1)
                using (var band = new SolidBrush(Color.FromArgb(10, 255, 255, 255)))
                    g.FillRectangle(band, area.X + 4 * s, ry, area.Width - 8 * s, rowH);
            Row(ry, it.Name, it.Picked > 0 ? it.Picked.ToString("N0") : "·", it.Thrown > 0 ? it.Thrown.ToString("N0") : "·", it.Sold > 0 ? it.Sold.ToString("N0") : "·",
                small, Theme.Text, Theme.Green, Theme.Red, Theme.GoldLight);
        }
        if (items.Count > fit)
        {
            // Where in the list (a thin bar on the right).
            float trackH = fit * rowH, barH = Math.Max(14 * s, trackH * fit / items.Count), barY = listTop + (trackH - barH) * _scroll / Math.Max(1, items.Count - fit);
            using var bar = new SolidBrush(Theme.BorderStrong);
            g.FillRectangle(bar, area.Right - 5 * s, barY, 2.5f * s, barH);
        }
        float ty = area.Bottom - totalsH + 2 * s;
        using (var line = new Pen(Theme.Border))
            g.DrawLine(line, x, ty - 2 * s, x + w, ty - 2 * s);
        Row(ty, $"Toplam · {items.Count} çeşit", items.Sum(r => r.Picked).ToString("N0"), items.Sum(r => r.Thrown).ToString("N0"), items.Sum(r => r.Sold).ToString("N0"),
            bold, Theme.Muted, Theme.Green, Theme.Red, Theme.GoldLight);
    }

    /// <summary>
    /// GÜNLÜK: the latest lines, newest at the bottom (the wheel goes back), each with its time and coloured tag and the
    /// whole sentence, broken over as many rows as it needs (the user: "mini modda günlük okunmuyor" — they were cut
    /// to one row). The lines that say something (sale, death, break, a drop…) are tinted in their colour.
    /// </summary>
    private void DrawLog(Graphics g, RectangleF area, Font tiny)
    {
        float s = S;
        Card(g, area);
        using var text = new Font("Segoe UI", 8.75f);
        if (_lines.Count == 0)
        {
            TextRenderer.DrawText(g, "Henüz günlükte bir şey yok.", text, Rectangle.Round(area), Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        float x = area.X + 8 * s, w = area.Width - 16 * s;
        float timeW = TextRenderer.MeasureText("00:00", tiny).Width + 4 * s, tagW = 52 * s;
        float textX = x + timeW + tagW, textW = x + w - textX - 4 * s;
        var wrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
        var one = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _lines.Count - 1));
        float top = area.Y + 5 * s, bottom = area.Bottom - 5 * s;
        var state = g.Save();
        g.SetClip(RectangleF.Inflate(area, -1, -1));
        int i = _lines.Count - 1 - _scroll;
        bool first = true;
        while (i >= 0 && bottom > top)
        {
            var l = _lines[i];
            var size = TextRenderer.MeasureText(g, l.Text, text, new Size((int)textW, 0), wrap);
            float h = Math.Max(17 * s, size.Height) + 7 * s;
            float ry = bottom - h;
            var color = l.Tone == Tone.Neutral ? Theme.Faint : Tones.Of(l.Tone);
            if (l.Tone != Tone.Neutral)
                using (var path = Theme.RoundRect(new RectangleF(area.X + 4 * s, ry + 1 * s, area.Width - 8 * s, h - 2 * s), 6 * s))
                using (var tint = new SolidBrush(Color.FromArgb(20, color)))
                    g.FillPath(tint, path);
            else if (!first)
                using (var sep = new Pen(Color.FromArgb(60, Theme.Border)))
                    g.DrawLine(sep, x, bottom, x + w, bottom);
            float ly = ry + 3.5f * s;
            TextRenderer.DrawText(g, l.At.ToString("HH:mm"), tiny, Rectangle.Round(new RectangleF(x, ly, timeW, 17 * s)), Theme.Faint, one);
            var tag = new RectangleF(x + timeW, ly + 1.5f * s, tagW - 6 * s, 14 * s);
            using (var path = Theme.RoundRect(tag, tag.Height / 2))
            using (var fill = new SolidBrush(Color.FromArgb(44, color)))
                g.FillPath(fill, path);
            TextRenderer.DrawText(g, l.Tag, tiny, Rectangle.Round(tag), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, l.Text, text, Rectangle.Round(new RectangleF(textX, ly + 0.5f * s, textW, size.Height + 2)), l.Tone == Tone.Neutral ? Theme.Muted : Theme.Text, wrap);
            bottom = ry;
            first = false;
            i--;
        }
        g.Restore(state);
        // Gone back in the log: where (a thin bar), and how to come back.
        if (_scroll > 0)
        {
            float trackH = area.Height - 16 * s, barH = Math.Max(14 * s, trackH / Math.Max(1, _lines.Count / 8f));
            float barY = area.Y + 8 * s + (trackH - barH) * (1 - (float)_scroll / Math.Max(1, _lines.Count - 1));
            using (var bar = new SolidBrush(Theme.BorderStrong))
                g.FillRectangle(bar, area.Right - 5 * s, barY, 2.5f * s, barH);
            var pill = new RectangleF(area.X + (area.Width - 150 * s) / 2, area.Bottom - 26 * s, 150 * s, 20 * s);
            using (var path = Theme.RoundRect(pill, pill.Height / 2))
            {
                using var fill = new SolidBrush(Theme.Secondary);
                g.FillPath(fill, path);
                using var pen = new Pen(Theme.BorderStrong);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, $"↓ {_scroll} yeni satır · tekerlek", tiny, Rectangle.Round(pill), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }
}
