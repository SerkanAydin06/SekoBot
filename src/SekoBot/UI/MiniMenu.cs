using System.Drawing.Drawing2D;

namespace SekoBot.UI;

/// <summary>
/// The small bar's menu as a panel of its own: who and what at the top (the character, where, its status), the
/// numbers in a grid, then big buttons in groups (this bot, the window, every PARDUS). It never takes the focus
/// from the game; a click anywhere else, Esc or a button closes it.
/// </summary>
internal sealed class MiniMenu : Form
{
    internal sealed record Item(string Icon, string Text, Action Act, ButtonKind Kind = ButtonKind.Ghost, bool Wide = false);
    internal sealed record Group(string Title, IReadOnlyList<Item> Items);

    private readonly MiniInfo _info;
    private readonly IReadOnlyList<Group> _groups;
    private readonly Rectangle _bar;
    private readonly List<(Rectangle Area, Item Item)> _buttons = [];
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 80 };
    private int _hover = -1;
    private bool _wasDown = true;

    private const int LogicalWidth = 360;
    private const int Pad = 14, HeaderHeight = 62, CellHeight = 46, ButtonHeight = 34, Gap = 6, GroupTitle = 24;

    private float S => DeviceDpi / 96f;
    private int L(float v) => (int)Math.Round(v * S);

    /// <param name="bar">The small bar on the screen (the panel opens above it, or below when there is no room).</param>
    public MiniMenu(MiniInfo info, IReadOnlyList<Group> groups, Rectangle bar, Point near)
    {
        _info = info;
        _groups = groups;
        _bar = bar;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(14, 18, 26);
        Text = "PARDUS";
        var size = new Size(L(LogicalWidth), L(HeightNeeded()));
        var work = Screen.FromRectangle(bar).WorkingArea;
        int x = Math.Clamp(near.X - size.Width + L(40), work.Left + 4, work.Right - size.Width - 4);
        int y = bar.Top - size.Height - L(6) >= work.Top ? bar.Top - size.Height - L(6) : Math.Min(bar.Bottom + L(6), work.Bottom - size.Height);
        Bounds = new Rectangle(new Point(x, y), size);
        Layout();
        _watch.Tick += (_, _) => Watch();
        FormClosed += (_, _) => _watch.Dispose();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x08000000 | 0x00000080;   // no focus taken from the game; not in Alt+Tab
            p.ClassStyle |= 0x00020000;             // a shadow
            return p;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Rounded corners on Windows 11.
        try { int round = 2; Native.Win32.DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }
        _watch.Start();
    }

    /// <summary>A click outside the panel and the bar closes it (the mouse going away doesn't: the user wanted clicks).</summary>
    private void Watch()
    {
        bool down = (MouseButtons & (MouseButtons.Left | MouseButtons.Right)) != 0;
        if (down && !_wasDown)
        {
            var at = Cursor.Position;
            if (!Bounds.Contains(at) && !_bar.Contains(at))
                Close();
        }
        _wasDown = down;
        if ((Native.Win32.GetAsyncKeyState(0x1B) & 0x8000) != 0)
            Close();
    }

    private bool Market => _info.Market != null;

    private int HeightNeeded()
    {
        int h = HeaderHeight + 1 + (Market ? 48 : 2 * CellHeight + 1) + 30;
        foreach (var group in _groups)
        {
            int rows = 0, half = 0;
            foreach (var item in group.Items)
            {
                if (item.Wide) { rows += 1 + half; half = 0; }
                else if (++half == 2) { rows++; half = 0; }
            }
            rows += half;
            h += GroupTitle + rows * (ButtonHeight + Gap);
        }
        return h + Pad - Gap + 4;
    }

    private void Layout()
    {
        _buttons.Clear();
        int y = HeaderHeight + 1 + (Market ? 48 : 2 * CellHeight + 1) + 30;
        int colW = (LogicalWidth - 2 * Pad - Gap) / 2;
        foreach (var group in _groups)
        {
            y += GroupTitle;
            int col = 0;
            foreach (var item in group.Items)
            {
                if (item.Wide)
                {
                    if (col == 1) { y += ButtonHeight + Gap; col = 0; }
                    _buttons.Add((new Rectangle(L(Pad), L(y), L(LogicalWidth - 2 * Pad), L(ButtonHeight)), item));
                    y += ButtonHeight + Gap;
                    continue;
                }
                _buttons.Add((new Rectangle(L(Pad + col * (colW + Gap)), L(y), L(colW), L(ButtonHeight)), item));
                if (++col == 2) { col = 0; y += ButtonHeight + Gap; }
            }
            if (col == 1)
                y += ButtonHeight + Gap;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        using (var edge = new Pen(Theme.BorderStrong))
            g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
        var info = _info;
        const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        // ── Who: the badge, the name, where; the status as a pill ──
        var badge = new RectangleF(L(Pad), L(12), L(38), L(38));
        using (var path = Theme.RoundRect(badge, L(10)))
        {
            using var fill = new LinearGradientBrush(badge, Color.FromArgb(42, 51, 70), Color.FromArgb(22, 27, 38), 45f);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.BorderStrong);
            g.DrawPath(pen, path);
        }
        string initials = new(info.Name.Where(char.IsLetterOrDigit).Take(2).ToArray());
        using (var f = new Font("Segoe UI", 10f, FontStyle.Bold))
            TextRenderer.DrawText(g, initials.Length > 0 ? initials.ToUpperInvariant() : "P", f, Rectangle.Round(badge), Theme.GoldLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var tone = !info.Running ? (Tones.From(info.StatusColor) == Tone.Red ? Tone.Red : Tone.Neutral)
                 : Tones.From(info.StatusColor) is Tone.Amber or Tone.Red ? Tones.From(info.StatusColor) : Tone.Green;
        var toneColor = tone == Tone.Neutral ? Theme.Muted : Tones.Of(tone);
        string pill = !info.Running ? (tone == Tone.Red ? "Sorun" : "Durdu") : tone == Tone.Amber ? "Dikkat" : tone == Tone.Red ? "Sorun" : "Çalışıyor";
        using var pillFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        int pillW = TextRenderer.MeasureText(pill, pillFont).Width + L(26);
        var pr = new RectangleF(Width - L(Pad) - pillW, L(20), pillW, L(22));
        using (var path = Theme.RoundRect(pr, pr.Height / 2))
        {
            using var fill = new SolidBrush(tone == Tone.Neutral ? Theme.Secondary : Tones.Soft(toneColor, 34));
            g.FillPath(fill, path);
            using var pen = new Pen(tone == Tone.Neutral ? Theme.BorderStrong : Tones.Soft(toneColor, 90));
            g.DrawPath(pen, path);
        }
        using (var dot = new SolidBrush(toneColor))
            g.FillEllipse(dot, pr.X + L(9), pr.Y + pr.Height / 2 - L(3), L(6), L(6));
        TextRenderer.DrawText(g, pill, pillFont, new Rectangle((int)pr.X + L(18), (int)pr.Y, pillW - L(20), (int)pr.Height), toneColor, Left);
        int textX = (int)badge.Right + L(10), textW = (int)pr.X - textX - L(6);
        using (var nameFont = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            TextRenderer.DrawText(g, $"{Instance.Number} · " + (info.Name.Length > 0 ? info.Name : "PARDUS"), nameFont, new Rectangle(textX, L(11), textW, L(22)), Theme.Text, Left);
        string where = string.Join(" · ", new[] { info.Level > 0 ? $"Seviye {info.Level}" : "", info.Place, "v" + BuildInfo.Version }.Where(s => s.Length > 0));
        using var small = new Font("Segoe UI", 8f);
        TextRenderer.DrawText(g, where, small, new Rectangle(textX, L(32), textW, L(18)), Theme.Faint, Left);
        int y = L(HeaderHeight);
        using (var line = new Pen(Theme.Border))
            g.DrawLine(line, 0, y, Width, y);
        y += 1;

        // ── The numbers ──
        using var capFont = new Font("Segoe UI", 6.8f, FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        if (Market)
        {
            TextRenderer.DrawText(g, info.Market!, small, new Rectangle(L(Pad), y, Width - L(2 * Pad), L(48)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += L(48);
        }
        else
        {
            string Count(int? n) => n is int c ? c.ToString("N0") : "—";
            bool low = info.HpPotions is < 20 || info.MpPotions is < 10;
            (string Cap, string Value, Color Color)[] cells =
            [
                ("EXP / SAAT", info.ExpRate, Theme.GoldLight), ("ÖLDÜRÜLEN", info.Mobs, Theme.Text), ("SÜRE", info.Elapsed.Length > 0 ? info.Elapsed : "—", Theme.Text),
                ("HP İKSİRİ", Count(info.HpPotions), low ? Theme.Red : Theme.Text), ("MP İKSİRİ", Count(info.MpPotions), low ? Theme.Red : Theme.Text),
                ("SON 10 DK", info.Exp10.Length > 0 ? info.Exp10 : "—", Theme.Text),
            ];
            int cw = Width / 3;
            using var cellBack = new SolidBrush(Color.FromArgb(11, 14, 20));
            g.FillRectangle(cellBack, 0, y, Width, L(2 * CellHeight + 1));
            using (var line = new Pen(Theme.Border))
            {
                g.DrawLine(line, 0, y + L(CellHeight), Width, y + L(CellHeight));
                g.DrawLine(line, cw, y, cw, y + L(2 * CellHeight));
                g.DrawLine(line, 2 * cw, y, 2 * cw, y + L(2 * CellHeight));
                g.DrawLine(line, 0, y + L(2 * CellHeight + 1), Width, y + L(2 * CellHeight + 1));
            }
            for (int i = 0; i < cells.Length; i++)
            {
                int cx = (i % 3) * cw + L(12), cy = y + (i / 3) * L(CellHeight + 1);
                TextRenderer.DrawText(g, cells[i].Cap, capFont, new Rectangle(cx, cy + L(6), cw - L(16), L(14)), Theme.Faint, Left);
                TextRenderer.DrawText(g, cells[i].Value, valueFont, new Rectangle(cx, cy + L(20), cw - L(16), L(22)), cells[i].Color, Left);
            }
            y += L(2 * CellHeight + 1);
        }
        // What it is doing now, in its tone.
        string status = info.Attack || Market ? info.Status : "Saldırı kapalı · " + info.Status;
        TextRenderer.DrawText(g, status, small, new Rectangle(L(Pad), y + L(4), Width - L(2 * Pad), L(24)), tone == Tone.Neutral ? Theme.Muted : toneColor, Left);

        // ── The buttons, in groups ──
        using var groupFont = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var buttonFont = new Font("Segoe UI", 8.8f, FontStyle.Bold);
        int index = 0;
        foreach (var group in _groups)
        {
            if (group.Items.Count == 0)
                continue;
            var first = _buttons[index].Area;
            TextRenderer.DrawText(g, group.Title, groupFont, new Rectangle(L(Pad), first.Y - L(GroupTitle) + L(4), Width, L(GroupTitle) - L(6)), Theme.Faint, Left);
            foreach (var _ in group.Items)
            {
                var (area, item) = _buttons[index];
                DrawButton(g, area, item, index == _hover, buttonFont);
                index++;
            }
        }
    }

    private void DrawButton(Graphics g, Rectangle r, Item item, bool over, Font font)
    {
        Color fill, edge, text, icon;
        switch (item.Kind)
        {
            case ButtonKind.Gold:
                (fill, edge, text, icon) = (over ? Theme.PrimaryHover : Theme.Gold, Color.Transparent, Theme.OnGold, Theme.OnGold);
                break;
            case ButtonKind.Danger:
                (fill, edge, text, icon) = (Tones.Soft(Theme.Red, over ? 60 : 36), Tones.Soft(Theme.Red, 110), Color.FromArgb(255, 141, 152), Color.FromArgb(255, 141, 152));
                break;
            case ButtonKind.Amber:
                (fill, edge, text, icon) = (Tones.Soft(Theme.Yellow, over ? 56 : 34), Tones.Soft(Theme.Yellow, 110), Theme.Yellow, Theme.Yellow);
                break;
            case ButtonKind.Quiet:
                (fill, edge, text, icon) = (over ? Theme.Secondary : Color.Transparent, over ? Theme.BorderStrong : Color.Transparent, over ? Theme.Text : Theme.Muted, Theme.Faint);
                break;
            default:
                (fill, edge, text, icon) = (over ? Theme.SecondaryHover : Theme.Secondary, over ? Color.FromArgb(54, 65, 90) : Theme.BorderStrong, Theme.Text, over ? Theme.GoldLight : Theme.Muted);
                break;
        }
        using (var path = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), L(8)))
        {
            if (fill.A > 0)
                using (var b = new SolidBrush(fill))
                    g.FillPath(b, path);
            if (edge.A > 0)
                using (var p = new Pen(edge))
                    g.DrawPath(p, path);
        }
        float s = r.Height * 0.42f;
        Icons.Draw(g, item.Icon, new RectangleF(r.X + L(12), r.Y + (r.Height - s) / 2, s, s), icon, 2f);
        int tx = r.X + L(12) + (int)s + L(9);
        TextRenderer.DrawText(g, item.Text, font, new Rectangle(tx, r.Y, r.Right - tx - L(8), r.Height), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private int Hit(Point p) => _buttons.FindIndex(b => b.Area.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = Hit(e.Location);
        if (hit == _hover)
            return;
        _hover = hit;
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
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
        if (e.Button != MouseButtons.Left || Hit(e.Location) is not (>= 0 and var i))
            return;
        var act = _buttons[i].Item.Act;
        Close();
        act();
    }
}
