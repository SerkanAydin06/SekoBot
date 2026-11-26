using System.Drawing.Drawing2D;

namespace SekoBot.UI;

/// <summary>The window's look: line icons drawn by hand (the same set everywhere), rounded shapes.</summary>
internal static class Icons
{
    /// <summary>Draws icon <paramref name="name"/> (24-unit line drawing) centred in <paramref name="r"/>.</summary>
    public static void Draw(Graphics g, string name, RectangleF r, Color color, float width = 1.9f)
    {
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = Math.Min(r.Width, r.Height) / 24f;
        g.TranslateTransform(r.X + (r.Width - 24 * k) / 2, r.Y + (r.Height - 24 * k) / 2);
        g.ScaleTransform(k, k);
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        void L(params float[] p) { if (p.Length >= 4) g.DrawLines(pen, Points(p)); }
        void C(float x, float y, float rad) => g.DrawEllipse(pen, x - rad, y - rad, 2 * rad, 2 * rad);
        void F(float x, float y, float rad) => g.FillEllipse(brush, x - rad, y - rad, 2 * rad, 2 * rad);
        void R(float x, float y, float w, float h, float rr) { using var p = Theme.RoundRect(new RectangleF(x, y, w, h), rr); g.DrawPath(pen, p); }
        void P(params float[] p) => g.FillPolygon(brush, Points(p));
        switch (name)
        {
            case "grid": R(3, 3, 7, 9, 1.5f); R(14, 3, 7, 5, 1.5f); R(14, 12, 7, 9, 1.5f); R(3, 16, 7, 5, 1.5f); break;
            case "sword": L(14.5f, 17.5f, 3, 6, 3, 3, 6, 3, 17.5f, 14.5f); L(13, 19, 19, 13); L(16, 16, 20, 20); L(19, 21, 21, 19); break;
            case "target": C(12, 12, 9); C(12, 12, 5); F(12, 12, 1.3f); break;
            case "bag": L(5, 8, 19, 8, 18, 20, 6, 20, 5, 8); g.DrawArc(pen, 9, 3, 6, 6, 180, 180); L(9, 6, 9, 8); L(15, 6, 15, 8); break;
            case "flask": L(9, 3, 15, 3); L(10, 3, 10, 9, 5, 18, 6.5f, 21, 17.5f, 21, 19, 18, 14, 9, 14, 3); L(7.5f, 15, 16.5f, 15); break;
            case "pin": g.DrawArc(pen, 5, 2.5f, 14, 14, 135, 270); L(7, 14.5f, 12, 21, 17, 14.5f); C(12, 9.5f, 2.5f); break;
            case "bell": g.DrawArc(pen, 6, 5, 12, 12, 180, 180); L(6, 11, 6, 16, 4, 18, 20, 18, 18, 16, 18, 11); g.DrawArc(pen, 10, 18, 4, 4, 0, 180); break;
            case "key": R(2, 6, 20, 12, 2); F(6, 10, 1); F(10, 10, 1); F(14, 10, 1); F(18, 10, 1); L(7, 14, 17, 14); break;
            case "cog":
                C(12, 12, 3); C(12, 12, 7);
                L(12, 2, 12, 5); L(12, 19, 12, 22); L(2, 12, 5, 12); L(19, 12, 22, 12);
                L(4.9f, 4.9f, 7, 7); L(17, 17, 19.1f, 19.1f); L(4.9f, 19.1f, 7, 17); L(17, 7, 19.1f, 4.9f); break;
            case "globe": C(12, 12, 9); L(3, 12, 21, 12); g.DrawEllipse(pen, 8, 3, 8, 18); break;
            case "layers": L(12, 3, 21, 8, 12, 13, 3, 8, 12, 3); L(3, 13, 12, 18, 21, 13); break;
            case "more": F(5, 12, 1.7f); F(12, 12, 1.7f); F(19, 12, 1.7f); break;
            case "min": L(5, 12, 19, 12); break;
            case "x": L(6, 6, 18, 18); L(18, 6, 6, 18); break;
            case "cart": C(9, 20, 1.4f); C(18, 20, 1.4f); L(2.5f, 3, 5.5f, 3, 8, 15.5f, 18.5f, 15.5f, 21.5f, 8, 6.2f, 8); break;
            case "fish": g.DrawEllipse(pen, 6.5f, 7, 14.5f, 10); L(6.5f, 12, 2, 8, 2, 16, 6.5f, 12); F(17, 11, 0.9f); break;
            case "play": P(7, 4.5f, 7, 19.5f, 19, 12); break;
            case "pause": L(8, 5, 8, 19); L(16, 5, 16, 19); break;
            case "stop": using (var p = Theme.RoundRect(new RectangleF(6, 6, 12, 12), 2)) g.FillPath(brush, p); break;
            case "plug": L(9, 2, 9, 8); L(15, 2, 15, 8); L(6, 11, 6, 8, 18, 8, 18, 11); g.DrawArc(pen, 6, 5, 12, 12, 0, 180); L(12, 17, 12, 22); break;
            case "skull": g.DrawEllipse(pen, 4, 3, 16, 14.5f); L(8, 17, 8, 21, 16, 21, 16, 17); F(9, 10.5f, 1.6f); F(15, 10.5f, 1.6f); break;
            case "coin": C(12, 12, 8.5f); L(12, 6.5f, 12, 17.5f); L(15, 9.5f, 10.5f, 9.5f, 9.5f, 10.8f, 10.5f, 12, 13.5f, 12, 14.5f, 13.2f, 13.5f, 14.5f, 9, 14.5f); break;
            case "bolt": L(13, 2, 4, 14, 11, 14, 10, 22, 19, 10, 12, 10, 13, 2); break;
            case "swap": L(7, 4, 3, 8, 7, 12); L(3, 8, 17, 8); L(17, 12, 21, 16, 17, 20); L(21, 16, 7, 16); break;
            case "alert": L(12, 3, 2, 20, 22, 20, 12, 3); L(12, 10, 12, 14); F(12, 17, 1.1f); break;
            case "walk": C(13, 4, 2); L(9, 21, 12, 14, 15, 16, 15, 21); L(8, 12, 10, 7, 14, 8, 17, 12); break;
            case "down": L(6, 9, 12, 15, 18, 9); break;
            case "check": L(5, 12, 10, 17, 19, 7); break;
            case "folder": L(3, 6, 3, 19, 21, 19, 21, 8, 11, 8, 9, 5, 4, 5, 3, 6); break;
            case "list": L(8, 6, 21, 6); L(8, 12, 21, 12); L(8, 18, 21, 18); F(3.5f, 6, 1.1f); F(3.5f, 12, 1.1f); F(3.5f, 18, 1.1f); break;
            case "refresh": g.DrawArc(pen, 4, 4, 16, 16, 300, 290); L(20, 3, 20, 8, 15, 8); break;
            case "expand": L(15, 3, 21, 3, 21, 9); L(9, 21, 3, 21, 3, 15); L(21, 3, 14, 10); L(3, 21, 10, 14); break;
            case "shrink": L(14, 4, 14, 10, 20, 10); L(10, 20, 10, 14, 4, 14); L(14, 10, 21, 3); L(10, 14, 3, 21); break;
            case "user": C(12, 8, 4); g.DrawArc(pen, 4, 13, 16, 16, 180, 180); break;
            case "map": L(3, 6, 9, 3, 15, 6, 21, 3, 21, 18, 15, 21, 9, 18, 3, 21, 3, 6); L(9, 3, 9, 18); L(15, 6, 15, 21); break;
            case "eye": g.DrawEllipse(pen, 2, 6, 20, 12); C(12, 12, 3); break;
            case "spark": L(12, 3, 12, 7); L(12, 17, 12, 21); L(3, 12, 7, 12); L(17, 12, 21, 12); L(6, 6, 8.5f, 8.5f); L(15.5f, 15.5f, 18, 18); L(6, 18, 8.5f, 15.5f); L(15.5f, 8.5f, 18, 6); break;
        }
        g.Restore(saved);
    }

    private static PointF[] Points(float[] p)
    {
        var points = new PointF[p.Length / 2];
        for (int i = 0; i < points.Length; i++)
            points[i] = new PointF(p[2 * i], p[2 * i + 1]);
        return points;
    }
}

/// <summary>How a status reads: its colour on every part of the window (header, panel, small bar).</summary>
internal enum Tone { Neutral, Green, Amber, Red, Gold, Blue, Violet }

internal static class Tones
{
    public static Color Of(Tone tone) => tone switch
    {
        Tone.Green => Theme.Green,
        Tone.Amber => Theme.Yellow,
        Tone.Red => Theme.Red,
        Tone.Gold => Theme.Gold,
        Tone.Blue => Theme.Accent,
        Tone.Violet => Theme.Violet,
        _ => Theme.Muted,
    };

    /// <summary>The tone a text colour stands for (the status line is still set with a colour).</summary>
    public static Tone From(Color color) =>
        color.ToArgb() == Theme.Green.ToArgb() ? Tone.Green
        : color.ToArgb() == Theme.Yellow.ToArgb() ? Tone.Amber
        : color.ToArgb() == Theme.Red.ToArgb() ? Tone.Red
        : color.ToArgb() == Theme.Accent.ToArgb() ? Tone.Blue
        : Tone.Neutral;

    public static Color Soft(Color c, int alpha = 30) => Color.FromArgb(alpha, c);
}

/// <summary>Base of the hand-drawn controls: double buffered, its parent's colour round its rounded shape.</summary>
internal abstract class Drawn : Control
{
    protected Drawn()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    /// <summary>Pixels per design unit (the window is laid out on a 96-dpi grid, then made smaller and scaled).</summary>
    protected float U => Font.Height / 15f;

    protected void Clear(Graphics g) => g.Clear(Parent?.BackColor ?? Theme.Background);

    protected static readonly TextFormatFlags LeftMid = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
    protected static readonly TextFormatFlags RightMid = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
    protected static readonly TextFormatFlags CenterMid = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

    protected Font Sized(float k, FontStyle style = FontStyle.Regular) => new(Font.FontFamily, Math.Max(6f, Font.Size * k), style);
}

/// <summary>The status line as a rounded pill: a dot and the text, coloured by its tone (set with ForeColor like a label).</summary>
internal sealed class Pill : Label
{
    public Pill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        AutoSize = false;
    }

    /// <summary>The icon in front instead of the dot (empty: the dot).</summary>
    public string Icon { get; set; } = "";

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var tone = Tones.From(ForeColor);
        var c = tone == Tone.Neutral ? Theme.Muted : ForeColor;
        string text = Text.Replace("\n", " ");
        int pad = Height / 2;
        int textW = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
        int w = Math.Min(Width - 1, textW + pad * 2 + Height / 2 + 4);
        var r = new RectangleF(0.5f, 0.5f, w, Height - 1);
        using (var path = Theme.RoundRect(r, r.Height / 2))
        {
            using var fill = new SolidBrush(tone == Tone.Neutral ? Theme.Secondary : Tones.Soft(c, 32));
            g.FillPath(fill, path);
            using var pen = new Pen(tone == Tone.Neutral ? Theme.Border : Tones.Soft(c, 80));
            g.DrawPath(pen, path);
        }
        float d = Height * 0.26f;
        if (Icon.Length > 0)
            Icons.Draw(g, Icon, new RectangleF(pad - 2, (Height - Height * 0.5f) / 2, Height * 0.5f, Height * 0.5f), c);
        else
        {
            using var halo = new SolidBrush(Tones.Soft(c, 50));
            g.FillEllipse(halo, pad - d * 0.5f, Height / 2f - d, d * 2, d * 2);
            using var dot = new SolidBrush(c);
            g.FillEllipse(dot, pad, Height / 2f - d / 2, d, d);
        }
        var tr = new Rectangle((int)(pad + Height * 0.5f + 2), 0, w - (int)(pad + Height * 0.5f + 2) - pad / 2, Height);
        TextRenderer.DrawText(g, text, Font, tr, tone == Tone.Neutral ? Theme.Text : c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>A small rounded box with an icon or a coloured dot and its text ("Tuva · Kanal 5"); a label (Text, ForeColor = the dot).</summary>
internal sealed class Chip : Label
{
    public Chip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        AutoSize = false;
    }

    public string Icon { get; set; } = "";

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        string text = Text.TrimStart('●', '○', ' ');
        bool off = Text.StartsWith('○');
        using var bold = new Font(Font, FontStyle.Bold);
        int textW = TextRenderer.MeasureText(text, bold, Size.Empty, TextFormatFlags.NoPrefix).Width;
        int lead = (int)(Height * 0.9f);
        int w = Math.Min(Width - 1, textW + lead + Height / 2);
        var r = new RectangleF(Width - 1 - w + 0.5f, 0.5f, w, Height - 1);
        using (var path = Theme.RoundRect(r, Height * 0.25f))
        {
            using var fill = new SolidBrush(Theme.Surface);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        float x = r.X + Height * 0.35f;
        if (Icon.Length > 0)
            Icons.Draw(g, Icon, new RectangleF(x, Height * 0.27f, Height * 0.46f, Height * 0.46f), Theme.Gold);
        else
        {
            var c = off ? Theme.Muted : ForeColor;
            float d = Height * 0.24f;
            using var halo = new SolidBrush(Tones.Soft(c, 45));
            g.FillEllipse(halo, x - d * 0.4f, Height / 2f - d * 0.9f, d * 1.8f, d * 1.8f);
            using var dot = new SolidBrush(c);
            g.FillEllipse(dot, x, Height / 2f - d / 2, d, d);
        }
        TextRenderer.DrawText(g, text, bold, new Rectangle((int)(r.X + lead), 0, (int)(r.Right - r.X - lead - 2), Height),
            off ? Theme.Muted : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>How a <see cref="ProButton"/> looks.</summary>
internal enum ButtonKind { Gold, Ghost, Danger, Quiet, Amber }

/// <summary>
/// The window's buttons: rounded, an icon in front, the text, and the key under it in a small box
/// ("Durdur\nF11": the part after the line break). Gold: the main action; Ghost: the others; Danger: stop.
/// </summary>
internal sealed class ProButton : Button
{
    private bool _over, _down;

    public ProButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }

    /// <summary>The look picked from BackColor (the buttons made with Theme.Button, told their state by colour).</summary>
    public bool FollowBackColor { get; set; }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        if (!FollowBackColor)
            return;
        int c = BackColor.ToArgb();
        Kind = c == Theme.Primary.ToArgb() || c == Theme.PrimaryHover.ToArgb() ? ButtonKind.Gold
             : c == Theme.Danger.ToArgb() || c == Theme.DangerHover.ToArgb() ? ButtonKind.Danger
             : c == Theme.Surface.ToArgb() ? ButtonKind.Quiet
             : c == Theme.Yellow.ToArgb() ? ButtonKind.Amber
             : ButtonKind.Ghost;
    }
    private ButtonKind _kind = ButtonKind.Ghost;
    public string Icon { get => _icon; set { _icon = value; Invalidate(); } }
    private string _icon = "";

    protected override void OnMouseEnter(EventArgs e) { _over = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _over = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        float radius = Math.Min(Height * 0.22f, 9 * DeviceDpi / 96f);
        Color text;
        using (var path = Theme.RoundRect(r, radius))
        {
            switch (_kind)
            {
                case ButtonKind.Gold:
                {
                    var top = _over ? Color.FromArgb(247, 214, 120) : Color.FromArgb(243, 201, 104);
                    var bottom = _down ? Color.FromArgb(200, 140, 40) : Color.FromArgb(216, 158, 49);
                    using var fill = new LinearGradientBrush(new RectangleF(0, 0, Width, Height + 1), top, bottom, 90f);
                    g.FillPath(fill, path);
                    text = Color.FromArgb(35, 20, 0);
                    break;
                }
                case ButtonKind.Danger:
                {
                    using var fill = new SolidBrush(Tones.Soft(Theme.Red, _over ? 46 : 30));
                    g.FillPath(fill, path);
                    using var pen = new Pen(Tones.Soft(Theme.Red, 90));
                    g.DrawPath(pen, path);
                    text = Color.FromArgb(255, 141, 152);
                    break;
                }
                case ButtonKind.Amber:
                {
                    using var fill = new SolidBrush(Tones.Soft(Theme.Yellow, _over ? 46 : 30));
                    g.FillPath(fill, path);
                    using var pen = new Pen(Tones.Soft(Theme.Yellow, 100));
                    g.DrawPath(pen, path);
                    text = Theme.Yellow;
                    break;
                }
                case ButtonKind.Quiet:
                {
                    if (_over)
                    {
                        using var fill = new SolidBrush(Theme.Secondary);
                        g.FillPath(fill, path);
                    }
                    text = _over ? Theme.Text : Theme.Muted;
                    break;
                }
                default:
                {
                    using var fill = new SolidBrush(_over ? Theme.SecondaryHover : Theme.Secondary);
                    g.FillPath(fill, path);
                    using var pen = new Pen(_over ? Color.FromArgb(54, 65, 90) : Theme.BorderStrong);
                    g.DrawPath(pen, path);
                    text = Theme.Text;
                    break;
                }
            }
        }
        if (!Enabled)
            text = Color.FromArgb(110, text);
        string[] parts = Text.Split('\n', 2);
        string main = parts[0], key = parts.Length > 1 ? parts[1] : "";
        using var font = new Font(Font, FontStyle.Bold);
        using var keyFont = new Font("Consolas", Math.Max(6f, Font.Size * 0.72f), FontStyle.Bold);
        float icon = _icon.Length > 0 ? Height * 0.36f : 0, gap = Height * 0.18f;
        int mainW = TextRenderer.MeasureText(main, font, Size.Empty, TextFormatFlags.NoPrefix).Width;
        var keySize = key.Length > 0 ? TextRenderer.MeasureText(key, keyFont, Size.Empty, TextFormatFlags.NoPrefix) : Size.Empty;
        float keyW = key.Length > 0 ? keySize.Width + Height * 0.1f : 0;
        float total = icon + (icon > 0 ? gap : 0) + mainW + (keyW > 0 ? gap + keyW : 0);
        float x = Math.Max(Height * 0.3f, (Width - total) / 2);
        if (icon > 0)
        {
            Icons.Draw(g, _icon, new RectangleF(x, (Height - icon) / 2, icon, icon), text, 2.2f);
            x += icon + gap;
        }
        TextRenderer.DrawText(g, main, font, new Rectangle((int)x, 0, (int)Math.Min(mainW + 2, Width - x), Height), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        x += mainW + gap;
        if (keyW > 0 && x + keyW < Width - 2)
        {
            var kr = new RectangleF(x, (Height - keySize.Height - 2) / 2f, keyW, keySize.Height + 2);
            using (var path = Theme.RoundRect(kr, 3))
            {
                using var fill = new SolidBrush(Color.FromArgb(_kind == ButtonKind.Gold ? 40 : 90, 0, 0, 0));
                g.FillPath(fill, path);
            }
            TextRenderer.DrawText(g, key, keyFont, Rectangle.Round(kr), Color.FromArgb(_kind == ButtonKind.Gold ? 200 : 170, text), CenterMid);
        }
    }

    private static readonly TextFormatFlags CenterMid = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
}

/// <summary>A small square button with an icon (the window's corner: mini, menu, minimize, close).</summary>
internal sealed class IconButton : Button
{
    private bool _over;

    public IconButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public string Icon { get; set; } = "more";
    /// <summary>A border and a fill (the menu buttons); none: the window buttons.</summary>
    public bool Boxed { get; set; }
    /// <summary>Red under the mouse (close).</summary>
    public bool Danger { get; set; }
    /// <summary>Drawn as pressed (its menu is open).</summary>
    public bool On { get => _on; set { _on = value; Invalidate(); } }
    private bool _on;

    protected override void OnMouseEnter(EventArgs e) { _over = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _over = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(r, Height * 0.25f))
        {
            if (Danger && _over)
            {
                using var fill = new SolidBrush(Theme.Red);
                g.FillPath(fill, path);
            }
            else if (Boxed || _over || _on)
            {
                using var fill = new SolidBrush(_over || _on ? Theme.Secondary : Theme.Surface);
                g.FillPath(fill, path);
                using var pen = new Pen(_over || _on ? Theme.BorderStrong : Theme.Border);
                g.DrawPath(pen, path);
            }
        }
        float s = Height * 0.46f;
        Icons.Draw(g, Icon, new RectangleF((Width - s) / 2, (Height - s) / 2, s, s), Danger && _over ? Color.White : _over || _on ? Theme.Text : Theme.Muted);
    }
}

/// <summary>A menu line on the left (an icon, the text) or a tab of the mode switch on top; the chosen one lit.</summary>
internal sealed class NavButton : Button
{
    private bool _over;

    public NavButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public string Icon { get; set; } = "";
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Invalidate(); } }
    private bool _selected;
    /// <summary>A tab of the mode switch (the chosen one raised) instead of a menu line (the chosen one gold).</summary>
    public bool Segment { get; set; }
    /// <summary>A small mark at the right ("!"), coloured amber; empty: none.</summary>
    public string Badge { get => _badge; set { if (_badge == value) return; _badge = value; Invalidate(); } }
    private string _badge = "";

    protected override void OnMouseEnter(EventArgs e) { _over = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _over = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Rail);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Color text = Theme.Muted, icon = Theme.Muted;
        using (var path = Theme.RoundRect(r, Height * (Segment ? 0.22f : 0.2f)))
        {
            if (_selected && Segment)
            {
                using var fill = new SolidBrush(Theme.Secondary);
                g.FillPath(fill, path);
                using var pen = new Pen(Theme.BorderStrong);
                g.DrawPath(pen, path);
                text = Theme.Text;
                icon = Theme.Gold;
            }
            else if (_selected)
            {
                using var fill = new SolidBrush(Tones.Soft(Theme.Gold, 30));
                g.FillPath(fill, path);
                text = Theme.GoldLight;
                icon = Theme.Gold;
            }
            else if (_over)
            {
                using var fill = new SolidBrush(Segment ? Tones.Soft(Theme.Secondary, 160) : Theme.Surface);
                g.FillPath(fill, path);
                text = icon = Theme.Text;
            }
        }
        using var font = new Font(Font, _selected ? FontStyle.Bold : FontStyle.Regular);
        float s = Height * (Segment ? 0.42f : 0.4f);
        float x = Segment ? 0 : Height * 0.3f;
        if (Segment)
        {
            int textW = TextRenderer.MeasureText(Text, font, Size.Empty, TextFormatFlags.NoPrefix).Width;
            x = (Width - (s + Height * 0.2f + textW)) / 2;
        }
        if (Icon.Length > 0)
        {
            Icons.Draw(g, Icon, new RectangleF(x, (Height - s) / 2, s, s), icon);
            x += s + Height * (Segment ? 0.2f : 0.3f);
        }
        TextRenderer.DrawText(g, Text, font, new Rectangle((int)x, 0, Width - (int)x - 4, Height), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (_badge.Length > 0)
        {
            using var small = new Font(Font.FontFamily, Font.Size * 0.78f, FontStyle.Bold);
            var size = TextRenderer.MeasureText(_badge, small);
            var br = new RectangleF(Width - size.Width - Height * 0.4f, (Height - size.Height) / 2f, size.Width + 4, size.Height);
            using (var path = Theme.RoundRect(br, 4))
            {
                using var fill = new SolidBrush(Tones.Soft(Theme.Yellow, 36));
                g.FillPath(fill, path);
            }
            TextRenderer.DrawText(g, _badge, small, Rectangle.Round(br), Theme.Yellow, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>PARDUS's mark: a gold, leopard-spotted "P".</summary>
internal sealed class Mark : Drawn
{
    protected override void OnPaint(PaintEventArgs e)
    {
        Clear(e.Graphics);
        Paint(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1));
    }

    /// <summary>The mark in <paramref name="r"/> (also on the small bar).</summary>
    public static void Paint(Graphics g, RectangleF r)
    {
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundRect(r, r.Width * 0.28f);
        using (var fill = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(r.X + r.Width * 0.32f, r.Y + r.Height * 0.26f),
            CenterColor = Color.FromArgb(255, 230, 168),
            SurroundColors = [Color.FromArgb(170, 116, 24)],
        })
            g.FillPath(fill, path);
        using var spot = new SolidBrush(Color.FromArgb(95, 60, 30, 0));
        float d = r.Width * 0.12f;
        g.FillEllipse(spot, r.X + r.Width * 0.7f - d / 2, r.Y + r.Height * 0.7f - d / 2, d, d);
        g.FillEllipse(spot, r.X + r.Width * 0.22f - d * 0.4f, r.Y + r.Height * 0.76f - d * 0.4f, d * 0.8f, d * 0.8f);
        g.FillEllipse(spot, r.X + r.Width * 0.78f - d * 0.35f, r.Y + r.Height * 0.26f - d * 0.35f, d * 0.7f, d * 0.7f);
        // Drawn with GDI+ (it follows the graphics' shift: the small bar draws itself lower with its item list open).
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", Math.Max(6f, r.Height * 0.5f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Color.FromArgb(42, 26, 2));
        using var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("P", font, ink, new RectangleF(r.X, r.Y + r.Height * 0.03f, r.Width, r.Height), centre);
        g.Restore(saved);
    }
}

/// <summary>The character's badge: its initials in a rounded square, the level in a gold tag at the corner.</summary>
internal sealed class Avatar : Drawn
{
    public string Initials { get => _initials; set { if (_initials == value) return; _initials = value; Invalidate(); } }
    private string _initials = "?";
    public int Level { get => _level; set { if (_level == value) return; _level = value; Invalidate(); } }
    private int _level;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Clear(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float box = Height * 0.86f;
        var r = new RectangleF(0.5f, 0.5f, box, box);
        using (var path = Theme.RoundRect(r, box * 0.27f))
        {
            using var fill = new LinearGradientBrush(r, Color.FromArgb(42, 51, 70), Color.FromArgb(22, 27, 38), 45f);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.BorderStrong);
            g.DrawPath(pen, path);
        }
        using var font = new Font(Font.FontFamily, Math.Max(7f, Font.Size * 1.1f), FontStyle.Bold);
        TextRenderer.DrawText(g, _initials, font, Rectangle.Round(r), _initials == "?" ? Theme.Muted : Theme.GoldLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (_level > 0)
        {
            using var small = new Font(Font.FontFamily, Math.Max(6f, Font.Size * 0.66f), FontStyle.Bold);
            string text = _level.ToString();
            var size = TextRenderer.MeasureText(text, small);
            var tag = new RectangleF(Width - size.Width - 6, Height - size.Height - 1, size.Width + 5, size.Height);
            using var path = Theme.RoundRect(tag, 4);
            using var fill = new SolidBrush(Theme.Gold);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Surface, 2);
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, text, small, Rectangle.Round(tag), Color.FromArgb(36, 21, 0), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>
/// One number of the session on its own card: a caption, the number big, a line under it, and a bar or a little
/// curve of the last hour (experience per hour). The font is the number's; the rest is sized from it.
/// </summary>
internal sealed class StatCard : Drawn
{
    public string Caption { get; set; } = "";
    public string Corner { get; set; } = "";
    public Color CornerColor { get; set; } = Theme.Muted;
    public string Value { get; set; } = "—";
    public string Unit { get; set; } = "";
    public Color ValueColor { get; set; } = Theme.Text;
    public string Sub { get; set; } = "";
    public Color SubColor { get; set; } = Theme.Muted;
    /// <summary>0..1: the bar at the bottom; null: none.</summary>
    public double? Bar { get; set; }
    public Color BarColor { get; set; } = Theme.Gold;
    /// <summary>Values for the little curve (instead of the bar); fewer than two: none.</summary>
    public IReadOnlyList<double> Curve { get; set; } = [];

    /// <summary>Sets everything and draws again only when something changed.</summary>
    public void Show(string value, string unit = "", string sub = "", Color? subColor = null, double? bar = null, string corner = "", Color? cornerColor = null, Color? valueColor = null)
    {
        var sc = subColor ?? Theme.Muted;
        var cc = cornerColor ?? Theme.Muted;
        var vc = valueColor ?? Theme.Text;
        if (Value == value && Unit == unit && Sub == sub && SubColor == sc && Bar == bar && Corner == corner && CornerColor == cc && ValueColor == vc)
            return;
        (Value, Unit, Sub, SubColor, Bar, Corner, CornerColor, ValueColor) = (value, unit, sub, sc, bar, corner, cc, vc);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Clear(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(r, Theme.CardRadius * DeviceDpi / 96f))
        {
            using var fill = new SolidBrush(Theme.Surface);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        float pad = Height * 0.15f;
        using var cap = Sized(0.42f, FontStyle.Bold);
        using var unit = Sized(0.5f);
        using var sub = Sized(0.5f);
        int capH = TextRenderer.MeasureText("Ag", cap).Height;
        var capRect = new Rectangle((int)pad, (int)(pad * 0.8f), (int)(Width - 2 * pad), capH);
        TextRenderer.DrawText(g, Caption, cap, capRect, Theme.Muted, LeftMid);
        if (Corner.Length > 0)
            TextRenderer.DrawText(g, Corner, cap, capRect, CornerColor, RightMid);
        int valueH = TextRenderer.MeasureText("0", Font).Height;
        int y = capRect.Bottom;
        int valueW = TextRenderer.MeasureText(Value, Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
        TextRenderer.DrawText(g, Value, Font, new Rectangle((int)pad - 2, y, (int)(Width - 2 * pad), valueH), ValueColor, LeftMid);
        if (Unit.Length > 0)
            TextRenderer.DrawText(g, Unit, unit, new Rectangle((int)pad - 4 + valueW, y + 2, (int)(Width - 2 * pad - valueW), valueH), Theme.Muted, LeftMid);
        y += valueH - 2;
        float bottom = Height - pad;
        if (Curve.Count >= 2)
        {
            float top = y + 2, h = bottom - top, w = Width - 2 * pad;
            double min = Curve.Min(), max = Curve.Max();
            if (max - min < 1e-9) { max += 1; min -= 1; }
            var points = Curve.Select((v, i) => new PointF(pad + w * i / (Curve.Count - 1), top + h - (float)((v - min) / (max - min)) * h * 0.9f - h * 0.05f)).ToArray();
            using var area = new GraphicsPath();
            area.AddLines(points);
            area.AddLine(points[^1], new PointF(points[^1].X, bottom));
            area.AddLine(new PointF(points[^1].X, bottom), new PointF(points[0].X, bottom));
            area.CloseFigure();
            using (var fill = new LinearGradientBrush(new RectangleF(0, top, Width, h + 1), Color.FromArgb(80, Theme.Gold), Color.FromArgb(0, Theme.Gold), 90f))
                g.FillPath(fill, area);
            using var pen = new Pen(Theme.Gold, 1.6f * DeviceDpi / 96f) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, points);
            return;
        }
        int subH = TextRenderer.MeasureText("Ag", sub).Height;
        if (Sub.Length > 0)
            TextRenderer.DrawText(g, Sub, sub, new Rectangle((int)pad, y, (int)(Width - 2 * pad), subH), SubColor, LeftMid);
        if (Bar is double b)
        {
            float bh = Math.Max(3, Height * 0.04f);
            var track = new RectangleF(pad, bottom - bh, Width - 2 * pad, bh);
            using (var path = Theme.RoundRect(track, bh / 2))
            using (var fill = new SolidBrush(Theme.Inset))
                g.FillPath(fill, path);
            float fw = (float)(track.Width * Math.Clamp(b, 0, 1));
            if (fw >= bh)
            {
                using var path = Theme.RoundRect(track with { Width = fw }, bh / 2);
                using var fill = new SolidBrush(BarColor);
                g.FillPath(fill, path);
            }
        }
    }
}

/// <summary>"ŞİMDİ": the important things of the last minutes, one line each (time, a coloured icon, the text).</summary>
internal sealed class Feed : Drawn
{
    public sealed record Line(DateTime At, Tone Tone, string Icon, string Text);

    private readonly List<Line> _lines = [];
    public string Empty { get; set; } = "Bot çalışınca önemli olaylar burada görünür.";

    public void Add(Line line)
    {
        _lines.Insert(0, line);
        if (_lines.Count > 30)
            _lines.RemoveAt(_lines.Count - 1);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Clear(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_lines.Count == 0)
        {
            TextRenderer.DrawText(g, Empty, Font, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        using var time = new Font("Consolas", Math.Max(6f, Font.Size * 0.86f));
        int row = (int)(Font.Height * 1.75f);
        int timeW = TextRenderer.MeasureText("00:00", time).Width;
        float box = row * 0.66f;
        for (int i = 0; i * row + row <= Height + 2 && i < _lines.Count; i++)
        {
            var line = _lines[i];
            int y = i * row;
            TextRenderer.DrawText(g, line.At.ToString("HH:mm"), time, new Rectangle(0, y, timeW, row), Theme.Muted, LeftMid);
            var c = Tones.Of(line.Tone);
            var br = new RectangleF(timeW + 6, y + (row - box) / 2, box, box);
            using (var path = Theme.RoundRect(br, box * 0.28f))
            using (var fill = new SolidBrush(Tones.Soft(c, 34)))
                g.FillPath(fill, path);
            float s = box * 0.6f;
            Icons.Draw(g, line.Icon, new RectangleF(br.X + (box - s) / 2, br.Y + (box - s) / 2, s, s), c, 2.2f);
            int x = (int)(br.Right + 8);
            TextRenderer.DrawText(g, line.Text, Font, new Rectangle(x, y, Width - x, row), Theme.Text, LeftMid);
        }
    }
}

/// <summary>Dark menus (the default ones are white).</summary>
internal sealed class DarkMenu : ToolStripProfessionalRenderer
{
    public DarkMenu() : base(new Colors()) { RoundedEdges = false; }

    public static void Use(ContextMenuStrip menu)
    {
        menu.Renderer = new DarkMenu();
        menu.BackColor = Color.FromArgb(18, 23, 34);
        menu.ForeColor = Theme.Text;
        menu.ShowImageMargin = false;
        menu.Padding = new Padding(4);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? (e.Item.Selected ? Theme.Text : Color.FromArgb(200, 208, 222)) : Theme.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
    }

    private sealed class Colors : ProfessionalColorTable
    {
        private static readonly Color Back = Color.FromArgb(18, 23, 34), Hover = Theme.Secondary;
        public override Color ToolStripDropDownBackground => Back;
        public override Color MenuBorder => Theme.BorderStrong;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Back;
    }
}

/// <summary>An on / off switch with its text (the settings' check boxes).</summary>
internal sealed class Toggle : CheckBox
{
    private bool _over;

    public Toggle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        AutoSize = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _over = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _over = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Small enough for the old check boxes' places (short words beside them).
        float h = Math.Min(Height - 6, Font.Height * 0.9f), w = h * 1.7f, y = (Height - h) / 2f;
        var track = new RectangleF(1, y, w, h);
        using (var path = Theme.RoundRect(track, h / 2))
        {
            using var fill = new SolidBrush(Checked ? (_over ? Theme.PrimaryHover : Theme.Gold) : (_over ? Color.FromArgb(52, 62, 84) : Theme.BorderStrong));
            g.FillPath(fill, path);
        }
        float inset = Math.Max(2, h * 0.16f), knob = h - 2 * inset;
        float kx = Checked ? track.Right - knob - inset : track.X + inset;
        using (var k = new SolidBrush(Checked ? Theme.OnGold : Color.FromArgb(201, 209, 222)))
            g.FillEllipse(k, kx, y + inset, knob, knob);
        int gap = (int)(w + h * 0.4f + 1);
        var text = new Rectangle(gap, 0, Width - gap, Height);
        TextRenderer.DrawText(g, Text, Font, text, Enabled ? (Checked ? Theme.Text : Theme.Muted) : Theme.Faint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>A slider with its value in a small box at the right (drag, the mouse wheel or the arrow keys).</summary>
internal sealed class Slider : Control
{
    private decimal _value;
    private bool _dragging, _over;

    public Slider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; } = 100;
    public decimal Step { get; set; } = 1;
    public int Decimals { get; set; }
    public event EventHandler? ValueChanged;

    // ── Typing the value (the user: hard to hit the exact number with the slider) ──
    private TextBox? _edit;

    private void BeginEdit()
    {
        if (_edit != null)
            return;
        var box = Rectangle.Round(new RectangleF(Width - BoxWidth + 3, 3, BoxWidth - 6, Height - 6));
        _edit = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = Theme.Inset, ForeColor = Theme.GoldLight, TextAlign = HorizontalAlignment.Center,
            Font = new Font("Consolas", Math.Max(6f, Font.Size * 0.95f), FontStyle.Bold), Text = _value.ToString("0." + new string('#', Math.Max(0, Decimals))),
        };
        _edit.Bounds = new Rectangle(box.X, box.Y + (box.Height - _edit.PreferredHeight) / 2, box.Width, _edit.PreferredHeight);
        _edit.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EndEdit(true); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; EndEdit(false); }
        };
        _edit.LostFocus += (_, _) => EndEdit(true);
        Controls.Add(_edit);
        _edit.Focus();
        _edit.SelectAll();
        Invalidate();
    }

    private void EndEdit(bool keep)
    {
        if (_edit is not { } edit)
            return;
        _edit = null;
        string text = edit.Text.Trim().Replace(" ", "").Replace(".", "").Replace(',', '.');
        if (Decimals > 0)
            text = edit.Text.Trim().Replace(',', '.');
        if (keep && decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal typed))
        {
            // As typed (not snapped to the slider's steps), only kept inside the range.
            decimal v = Math.Round(Math.Clamp(typed, Minimum, Maximum), Decimals);
            if (v != _value)
            {
                _value = v;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        Controls.Remove(edit);
        edit.Dispose();
        Invalidate();
    }

    public decimal Value
    {
        get => _value;
        set
        {
            decimal v = Math.Clamp(Step > 0 ? Minimum + Math.Round((value - Minimum) / Step) * Step : value, Minimum, Maximum);
            if (v == _value)
                return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int BoxWidth => (int)(Font.Height * 3.4f);
    private RectangleF Track
    {
        get
        {
            float h = Math.Max(4, Font.Height * 0.3f);
            float pad = Font.Height * 0.5f;
            return new RectangleF(pad, (Height - h) / 2f, Width - BoxWidth - pad * 2.2f, h);
        }
    }

    private void SetFromX(int x)
    {
        var t = Track;
        double share = Math.Clamp((x - t.X) / Math.Max(1, t.Width), 0, 1);
        Value = Minimum + (decimal)share * (Maximum - Minimum);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.X >= Width - BoxWidth)
        {
            BeginEdit();
            return;
        }
        Focus();
        if (e.Button == MouseButtons.Left && e.X < Width - BoxWidth)
        {
            _dragging = true;
            Capture = true;
            SetFromX(e.X);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = e.X >= Width - BoxWidth ? Cursors.IBeam : Cursors.Hand;
        if (_dragging)
            SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta) * Step;
        if (e is HandledMouseEventArgs h)
            h.Handled = true;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Right or Keys.Up) Value += Step;
        if (e.KeyCode is Keys.Left or Keys.Down) Value -= Step;
    }

    protected override void OnMouseEnter(EventArgs e) { _over = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _over = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var t = Track;
        using (var path = Theme.RoundRect(t, t.Height / 2))
        {
            using var back = new SolidBrush(Theme.Inset);
            g.FillPath(back, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        float share = Maximum > Minimum ? (float)((_value - Minimum) / (Maximum - Minimum)) : 0;
        float x = t.X + t.Width * share;
        if (x - t.X > 1)
        {
            using var path = Theme.RoundRect(t with { Width = x - t.X }, t.Height / 2);
            using var fill = new SolidBrush(Theme.Gold);
            g.FillPath(fill, path);
        }
        float knob = Font.Height * 0.95f;
        using (var halo = new SolidBrush(Tones.Soft(Theme.Gold, _over || _dragging || Focused ? 90 : 55)))
            g.FillEllipse(halo, x - knob * 0.85f, Height / 2f - knob * 0.85f, knob * 1.7f, knob * 1.7f);
        using (var k = new SolidBrush(Color.White))
            g.FillEllipse(k, x - knob / 2, Height / 2f - knob / 2, knob, knob);
        var box = new RectangleF(Width - BoxWidth + 0.5f, 1.5f, BoxWidth - 1.5f, Height - 3);
        using (var path = Theme.RoundRect(box, 5))
        {
            using var fill = new SolidBrush(Theme.Inset);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        if (_edit != null)
        {
            using var focus = Theme.RoundRect(box, 5);
            using var pen = new Pen(Tones.Soft(Theme.Gold, 170));
            g.DrawPath(pen, focus);
            return;
        }
        using var font = new Font("Consolas", Math.Max(6f, Font.Size * 0.95f), FontStyle.Bold);
        TextRenderer.DrawText(g, _value.ToString("N" + Decimals), font, Rectangle.Round(box), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>A rounded dark box round a text box (the native border is light and square).</summary>
internal sealed class Field : Panel
{
    private readonly TextBox _box;

    public Field(TextBox box)
    {
        _box = box;
        DoubleBuffered = true;
        BackColor = Theme.Inset;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Controls.Add(box);
        box.GotFocus += (_, _) => Invalidate();
        box.LostFocus += (_, _) => Invalidate();
        Cursor = Cursors.IBeam;
        Click += (_, _) => box.Focus();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = Math.Max(4, Height / 4);
        _box.Bounds = new Rectangle(pad + 2, (Height - _box.PreferredHeight) / 2, Width - 2 * pad - 4, _box.PreferredHeight);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height * 0.22f);
        using var fill = new SolidBrush(Theme.Inset);
        g.FillPath(fill, path);
        using var pen = new Pen(_box.Focused ? Tones.Soft(Theme.Gold, 160) : Theme.BorderStrong);
        g.DrawPath(pen, path);
    }
}

/// <summary>The fishing chart: one column per pull time, as high as its share caught; the best one gold.</summary>
internal sealed class PullChart : Drawn
{
    private List<(string Name, double Rate, int Caught, int Pulls)> _bins = [];
    private int _best = -1;

    public void Show(List<(string Name, double Rate, int Caught, int Pulls)> bins, int best)
    {
        if (best == _best && bins.SequenceEqual(_bins))
            return;
        (_bins, _best) = (bins, best);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Clear(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_bins.Count == 0)
            return;
        using var small = Sized(0.86f);
        using var bold = Sized(0.9f, FontStyle.Bold);
        int labelH = TextRenderer.MeasureText("0", small).Height;
        float gap = Width * 0.012f, w = (Width - gap * (_bins.Count - 1)) / _bins.Count;
        float top = labelH + 2, bottom = Height - labelH - 2, h = bottom - top;
        for (int i = 0; i < _bins.Count; i++)
        {
            var (name, rate, caught, pulls) = _bins[i];
            float x = i * (w + gap);
            float bh = pulls > 0 ? Math.Max(3, (float)(h * Math.Clamp(rate, 0, 1))) : 3;
            var bar = new RectangleF(x, bottom - bh, w, bh);
            using (var path = Theme.RoundRect(bar, Math.Min(5, w / 4)))
            {
                Brush fill = i == _best
                    ? new LinearGradientBrush(new RectangleF(x, bar.Y - 1, w, bh + 2), Theme.GoldLight, Theme.Gold, 90f)
                    : new SolidBrush(pulls > 0 ? Theme.BorderStrong : Theme.Border);
                using (fill)
                    g.FillPath(fill, path);
            }
            string value = pulls > 0 ? $"%{rate * 100:0}" : "—";
            TextRenderer.DrawText(g, value, bold, new Rectangle((int)x, (int)(bar.Y - labelH - 1), (int)w, labelH), i == _best ? Theme.GoldLight : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, name, small, new Rectangle((int)x, (int)bottom + 2, (int)w, labelH), Theme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>Lists drawn as rows: a coloured mark for what the line is about, the text, a thin line under it.</summary>
internal static class Rows
{
    public static void Style(ListBox list)
    {
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = (int)(list.Font.Height * 2.0f);
        list.FontChanged += (_, _) => list.ItemHeight = (int)(list.Font.Height * 2.0f);
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= list.Items.Count)
                return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(selected ? Theme.Secondary : Theme.Surface))
                g.FillRectangle(back, e.Bounds);
            using (var line = new Pen(Theme.Border))
                g.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            string text = list.Items[e.Index]?.ToString() ?? "";
            string l = text.ToLower(new System.Globalization.CultureInfo("tr-TR"));
            var tone = l.StartsWith("henüz") ? Tone.Neutral
                     : l.Contains("kelepir") || l.Contains("yakaland") || l.Contains("✓") ? Tone.Green
                     : l.Contains("aranan") ? Tone.Gold
                     : l.Contains("kaçtı") || l.Contains("kaçan") || l.Contains("✗") ? Tone.Red
                     : Tone.Blue;
            float d = e.Font!.Height * 0.4f;
            if (tone != Tone.Neutral)
                using (var dot = new SolidBrush(Tones.Of(tone)))
                    g.FillEllipse(dot, e.Bounds.X + d * 0.6f, e.Bounds.Y + (e.Bounds.Height - d) / 2f, d, d);
            int x = e.Bounds.X + (int)(d * 2.2f);
            TextRenderer.DrawText(g, text, e.Font, new Rectangle(x, e.Bounds.Y, e.Bounds.Width - x - 4, e.Bounds.Height),
                tone == Tone.Neutral ? Theme.Faint : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        };
    }
}
