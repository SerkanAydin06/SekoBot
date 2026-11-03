using System.Drawing.Drawing2D;

namespace SekoBot.UI;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(11, 16, 32);
    public static readonly Color Surface = Color.FromArgb(20, 29, 48);
    public static readonly Color Inset = Color.FromArgb(8, 14, 27);
    public static readonly Color Border = Color.FromArgb(46, 64, 92);
    public static readonly Color Primary = Color.FromArgb(42, 108, 231);
    public static readonly Color PrimaryHover = Color.FromArgb(64, 130, 255);
    public static readonly Color Secondary = Color.FromArgb(35, 51, 78);
    public static readonly Color SecondaryHover = Color.FromArgb(49, 69, 101);
    public static readonly Color Danger = Color.FromArgb(110, 40, 60);
    public static readonly Color DangerHover = Color.FromArgb(140, 52, 76);
    public static readonly Color Text = Color.FromArgb(238, 245, 255);
    public static readonly Color Muted = Color.FromArgb(146, 165, 194);
    public static readonly Color Accent = Color.FromArgb(96, 150, 255);
    public static readonly Color Green = Color.FromArgb(87, 220, 174);
    public static readonly Color Yellow = Color.FromArgb(255, 205, 96);
    public static readonly Color Red = Color.FromArgb(255, 120, 142);

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
        var button = new Button
        {
            Text = text,
            Bounds = bounds,
            BackColor = normal,
            ForeColor = Text,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(hover, 0.1f);
        parent.Controls.Add(button);
        return button;
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

/// <summary>Rounded card with a thin border.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        DoubleBuffered = true;
        BackColor = Theme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.Border, 1f);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        int r = 14;
        using var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, r, r, 180, 90);
        path.AddArc(rect.Right - r, rect.Top, r, r, 270, 90);
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - r, r, r, 90, 90);
        path.CloseFigure();
        e.Graphics.DrawPath(pen, path);
    }
}

/// <summary>A labelled fill bar (HP / MP / target).</summary>
internal sealed class Meter : Control
{
    private static readonly Font CaptionFont = new("Segoe UI", 8.5f, FontStyle.Bold);
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Inset);
        if (_value is double v)
        {
            int w = (int)Math.Round(Math.Clamp(v, 0, 1) * Width);
            using var brush = new SolidBrush(Color.FromArgb(150, Fill));
            g.FillRectangle(brush, 0, 0, w, Height);
        }
        var text = new Rectangle(8, 0, Width - 16, Height);
        TextRenderer.DrawText(g, Caption, CaptionFont, text, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _value is double p ? $"%{p * 100:0}" : "—", CaptionFont, text, Theme.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
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
