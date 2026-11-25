using System.Drawing.Drawing2D;

namespace SekoBot.UI;

internal static class Theme
{
    // PARDUS: near-black blue-grey surfaces, gold for what matters, one colour per status everywhere.
    public static readonly Color Background = Color.FromArgb(11, 14, 20);
    /// <summary>The left menu and the window's title strip (a shade darker than the page).</summary>
    public static readonly Color Rail = Color.FromArgb(9, 12, 17);
    public static readonly Color Surface = Color.FromArgb(16, 20, 28);
    public static readonly Color Inset = Color.FromArgb(8, 11, 16);
    public static readonly Color Border = Color.FromArgb(30, 37, 50);
    public static readonly Color BorderStrong = Color.FromArgb(40, 49, 67);
    public static readonly Color Gold = Color.FromArgb(232, 180, 76);
    public static readonly Color GoldLight = Color.FromArgb(247, 214, 140);
    public static readonly Color Primary = Gold;
    public static readonly Color PrimaryHover = Color.FromArgb(243, 201, 104);
    /// <summary>Text on gold.</summary>
    public static readonly Color OnGold = Color.FromArgb(35, 20, 0);
    public static readonly Color Secondary = Color.FromArgb(22, 27, 37);
    public static readonly Color SecondaryHover = Color.FromArgb(30, 36, 50);
    public static readonly Color Danger = Color.FromArgb(96, 34, 44);
    public static readonly Color DangerHover = Color.FromArgb(124, 44, 56);
    public static readonly Color Text = Color.FromArgb(233, 237, 244);
    public static readonly Color Muted = Color.FromArgb(155, 165, 182);
    /// <summary>Quieter than <see cref="Muted"/>: captions, times.</summary>
    public static readonly Color Faint = Color.FromArgb(98, 108, 126);
    public static readonly Color Accent = Gold;
    public static readonly Color Blue = Color.FromArgb(90, 168, 255);
    public static readonly Color Violet = Color.FromArgb(169, 139, 255);
    public static readonly Color Green = Color.FromArgb(61, 220, 132);
    public static readonly Color Yellow = Color.FromArgb(255, 181, 71);
    public static readonly Color Red = Color.FromArgb(255, 93, 108);
    public const float CardRadius = 12;

    /// <summary>A rectangle with rounded corners.</summary>
    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(nint hwnd, string? app, string? idList);

    /// <summary>Dark scroll bars on a list or text box (Windows 10/11's own dark look; the default ones are white).</summary>
    public static void DarkScrollBars(Control control)
    {
        void Apply() { try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } }
        if (control.IsHandleCreated)
            Apply();
        control.HandleCreated += (_, _) => Apply();
    }

    public static Label Label(Control parent, string text, Rectangle bounds, float size,
        Color color, FontStyle style = FontStyle.Regular, ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        var label = new Label
        {
            Text = text,
            Bounds = bounds,
            AutoSize = false,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            BackColor = parent.BackColor,
            AutoEllipsis = true,
            TextAlign = align
        };
        parent.Controls.Add(label);
        return label;
    }

    public static Button Button(Control parent, string text, Rectangle bounds, Color normal, Color hover)
    {
        var button = new ProButton
        {
            FollowBackColor = true,
            Text = text,
            Bounds = bounds,
            ForeColor = normal == Primary ? OnGold : Text,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.BackColor = normal;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(hover, 0.1f);
        parent.Controls.Add(button);
        return button;
    }

    /// <summary>A big button of the control bar: its action on top, its key (the text after a line break)
    /// smaller under it, both centred, the same for every mode.</summary>
    public static Button BigButton(Control parent, string text, Rectangle bounds, Color normal, Color hover)
    {
        var button = new CaptionButton
        {
            Text = text,
            Bounds = bounds,
            BackColor = normal,
            ForeColor = Text,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false,
            Hover = hover,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(hover, 0.1f);
        parent.Controls.Add(button);
        return button;
    }

    /// <summary>A window button drawn as a shape (font glyphs like × sit low): "close" an X, "min" a dash.</summary>
    public static Button WindowButton(Control parent, string kind, Rectangle bounds, Color normal, Color hover)
    {
        var button = new ShapeButton
        {
            Kind = kind,
            Bounds = bounds,
            BackColor = normal,
            ForeColor = Text,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hover;
        parent.Controls.Add(button);
        return button;
    }

    private sealed class ShapeButton : Button
    {
        private bool _over;
        public string Kind { get; set; } = "close";

        protected override void OnMouseEnter(EventArgs e) { _over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _over = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var brush = new SolidBrush(_over ? FlatAppearance.MouseOverBackColor : BackColor))
                e.Graphics.FillRectangle(brush, ClientRectangle);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float cx = Width / 2f, cy = Height / 2f, u = Math.Min(Width, Height) * 0.17f;
            using var pen = new Pen(ForeColor, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            if (Kind == "min")
                e.Graphics.DrawLine(pen, cx - u, cy, cx + u, cy);
            else
            {
                e.Graphics.DrawLine(pen, cx - u, cy - u, cx + u, cy + u);
                e.Graphics.DrawLine(pen, cx - u, cy + u, cx + u, cy - u);
            }
        }
    }

    private sealed class CaptionButton : Button
    {
        private bool _over;
        private static readonly Font KeyFont = new("Segoe UI", 8f);
        public Color Hover { get; set; }

        protected override void OnMouseEnter(EventArgs e) { _over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _over = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var back = _over && Enabled ? FlatAppearance.MouseOverBackColor : BackColor;
            if (!Enabled)
                back = ControlPaint.Dark(BackColor, 0.35f);
            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, ClientRectangle);
            string[] lines = Text.Split('\n', 2);
            var main = Enabled ? ForeColor : Color.FromArgb(150, ForeColor);
            var sub = Enabled ? Color.FromArgb(200, ForeColor) : Color.FromArgb(110, ForeColor);
            const TextFormatFlags centre = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (lines.Length == 1)
            {
                TextRenderer.DrawText(e.Graphics, lines[0], Font, ClientRectangle, main, centre);
                return;
            }
            int h1 = TextRenderer.MeasureText(lines[0], Font).Height, h2 = TextRenderer.MeasureText(lines[1], KeyFont).Height;
            int top = (Height - h1 - h2 + 2) / 2;
            TextRenderer.DrawText(e.Graphics, lines[0], Font, new Rectangle(4, top, Width - 8, h1), main, centre);
            TextRenderer.DrawText(e.Graphics, lines[1], KeyFont, new Rectangle(4, top + h1 - 2, Width - 8, h2), sub, centre);
        }
    }

    public static NumericUpDown Number(Control parent, Rectangle bounds, decimal min, decimal max,
        decimal value, int decimals = 0, decimal step = 1)
    {
        var number = new NumericUpDown
        {
            Bounds = bounds,
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Increment = step,
            Value = Math.Clamp(value, min, max),
            BackColor = Inset,
            ForeColor = Text,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10f),
            TextAlign = HorizontalAlignment.Center
        };
        parent.Controls.Add(number);
        return number;
    }
}

/// <summary>Rounded card with a thin border (its corners in the colour behind it); a caption on top when given.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        DoubleBuffered = true;
        BackColor = Theme.Surface;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    /// <summary>The small caption at the top left ("CANLI GÖRÜNTÜ"); empty: none.</summary>
    public string Caption { get; set; } = "";
    /// <summary>Text at the top right, beside the caption.</summary>
    public string CaptionRight { get => _captionRight; set { if (_captionRight == value) return; _captionRight = value; Invalidate(); } }
    private string _captionRight = "";
    /// <summary>A faint gold light from the right (the main card at the top).</summary>
    public bool Glow { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float radius = Theme.CardRadius * DeviceDpi / 96f * 0.84f;
        using var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), radius);
        if (Glow)
        {
            using var fill = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Color.FromArgb(21, 27, 38), BackColor, 0f);
            g.FillPath(fill, path);
            using var light = new GraphicsPath();
            light.AddEllipse(Width * 0.62f, -Height * 1.1f, Width * 0.5f, Height * 2.2f);
            using var glow = new PathGradientBrush(light) { CenterColor = Color.FromArgb(22, Theme.Gold), SurroundColors = [Color.FromArgb(0, Theme.Gold)] };
            var clip = g.Clip;
            g.SetClip(path);
            g.FillPath(glow, light);
            g.Clip = clip;
        }
        else
        {
            using var fill = new SolidBrush(BackColor);
            g.FillPath(fill, path);
        }
        using var pen = new Pen(Theme.Border, 1f);
        g.DrawPath(pen, path);
        if (Caption.Length > 0)
        {
            using var font = new Font("Segoe UI", Math.Max(6f, Font.Size * 0.82f), FontStyle.Bold);
            var row = CaptionRow;
            TextRenderer.DrawText(g, Caption, font, row, Theme.Faint, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (_captionRight.Length > 0)
            {
                using var right = new Font("Segoe UI", Math.Max(6f, Font.Size * 0.86f));
                TextRenderer.DrawText(g, _captionRight, right, row, Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Where the caption line is (the card's top).</summary>
    public Rectangle CaptionRow
    {
        get
        {
            int h = (int)(Font.Height * 2.3f), pad = (int)(Font.Height * 1.1f);
            return new Rectangle(pad, 0, Width - 2 * pad, h);
        }
    }
}

/// <summary>A labelled fill bar (HP / MP / target).</summary>
internal sealed class Meter : Control
{
    private double? _value;

    public Meter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Inset;
    }

    public Color Fill { get; set; } = Theme.Green;
    public string Caption { get; set; } = "";

    public double? Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Invalidate();
        }
    }

    /// <summary>The value's text at the right (empty: the percentage).</summary>
    public string Detail { get => _detail; set { if (_detail == value) return; _detail = value; Invalidate(); } }
    private string _detail = "";

    protected override void OnPaint(PaintEventArgs e)
    {
        // The caption and the value on a line, a slim rounded bar under them.
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font(Font.FontFamily, Math.Max(6f, Font.Size), FontStyle.Bold);
        using var light = new Font(Font.FontFamily, Math.Max(6f, Font.Size));
        float barH = Math.Max(5, Height * 0.24f);
        var text = new Rectangle(0, 0, Width, (int)(Height - barH - 3));
        TextRenderer.DrawText(g, Caption, font, text, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        string value = _detail.Length > 0 && _value != null ? _detail : _value is double p ? $"%{p * 100:0}" : "—";
        TextRenderer.DrawText(g, value, light, text, Theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        var track = new RectangleF(0.5f, Height - barH - 0.5f, Width - 1, barH);
        using (var path = Theme.RoundRect(track, barH / 2))
        {
            using var back = new SolidBrush(Theme.Inset);
            g.FillPath(back, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        if (_value is double v && v > 0)
        {
            float w = (float)(Math.Clamp(v, 0, 1) * (Width - 1));
            if (w < barH)
                w = barH;
            using var path = Theme.RoundRect(track with { Width = w }, barH / 2);
            using var brush = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Fill, ControlPaint.Light(Fill, 0.25f), 0f);
            g.FillPath(brush, path);
        }
    }
}

/// <summary>Button that shows a key; click it, then press the new key.</summary>
internal sealed class KeyPicker : Button
{
    private bool _waiting, _swallowUp;
    private Keys _key;

    public KeyPicker()
    {
        BackColor = Theme.Inset;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        TabStop = false;
        FlatAppearance.BorderColor = Theme.Border;
        FlatAppearance.MouseOverBackColor = Theme.SecondaryHover;
    }

    public event Action<Keys>? KeyChanged;

    public Keys Key
    {
        get => _key;
        set
        {
            _key = value;
            if (!_waiting) Text = Name(value);
        }
    }

    public static string Name(Keys key) => key switch
    {
        Keys.None => "Yok",
        Keys.Space => "Boşluk",
        Keys.Escape => "Esc",
        Keys.Return => "Enter",
        Keys.Back => "Geri sil",
        >= Keys.D0 and <= Keys.D9 => ((int)(key - Keys.D0)).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (int)(key - Keys.NumPad0),
        Keys.ShiftKey => "Shift",
        Keys.ControlKey => "Ctrl",
        Keys.Menu => "Alt",
        _ => key.ToString()
    };

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _waiting = true;
        Text = "Tuşa bas…";
        BackColor = Theme.Primary;
        Focus();
    }

    protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
    {
        if (_waiting) e.IsInputKey = true;
        base.OnPreviewKeyDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_waiting)
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = e.SuppressKeyPress = true;
        _swallowUp = true;
        _waiting = false;
        BackColor = Theme.Inset;
        Key = e.KeyCode;
        KeyChanged?.Invoke(e.KeyCode);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_swallowUp)
        {
            _swallowUp = false;
            e.Handled = true;
            return;
        }
        base.OnKeyUp(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (!_waiting) return;
        _waiting = false;
        BackColor = Theme.Inset;
        Text = Name(_key);
    }
}
