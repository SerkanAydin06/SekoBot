using System.Drawing.Drawing2D;
using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.UI;

/// <summary>
/// Live preview with a debug overlay (calibrated areas, detections, bot phase)
/// and drag-to-select for calibration. Selection is reported in game pixels.
/// </summary>
internal sealed class PreviewControl : Control
{
    private static readonly Font PlaceholderFont = new("Segoe UI", 12f);
    private Point? _dragStart;
    private Rectangle? _selection;

    public PreviewControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Inset;
        Cursor = Cursors.Cross;
    }

    /// <summary>Not owned; the form disposes it.</summary>
    public Bitmap? Image { get; set; }
    public BotProfile? Profile { get; set; }
    public GameState? State { get; set; }
    public string Overlay { get; set; } = "";
    public string Placeholder { get; set; } = "Oyun penceresi bekleniyor";

    public Rectangle? Selection => _selection;
    public event Action? SelectionChanged;

    public void ClearSelection()
    {
        _selection = null;
        SelectionChanged?.Invoke();
        Invalidate();
    }

    private (float Scale, float X, float Y) Transform()
    {
        if (Image == null)
            return (1, 0, 0);
        float scale = Math.Min((float)Width / Image.Width, (float)Height / Image.Height);
        return (scale, (Width - Image.Width * scale) / 2, (Height - Image.Height * scale) / 2);
    }

    private Point ToImage(Point p)
    {
        var (s, ox, oy) = Transform();
        int x = (int)((p.X - ox) / s), y = (int)((p.Y - oy) / s);
        return new Point(Math.Clamp(x, 0, (Image?.Width ?? 1) - 1), Math.Clamp(y, 0, (Image?.Height ?? 1) - 1));
    }

    private RectangleF ToControl(Rectangle r)
    {
        var (s, ox, oy) = Transform();
        return new RectangleF(ox + r.X * s, oy + r.Y * s, r.Width * s, r.Height * s);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Image == null || e.Button != MouseButtons.Left)
            return;
        _dragStart = ToImage(e.Location);
        _selection = null;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not Point start)
            return;
        var now = ToImage(e.Location);
        _selection = Rectangle.FromLTRB(Math.Min(start.X, now.X), Math.Min(start.Y, now.Y),
            Math.Max(start.X, now.X), Math.Max(start.Y, now.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragStart == null)
            return;
        _dragStart = null;
        if (_selection is { Width: < 3 } or { Height: < 3 })
            _selection = null;
        SelectionChanged?.Invoke();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (Image == null)
        {
            TextRenderer.DrawText(g, "▣\n\n" + Placeholder, PlaceholderFont, ClientRectangle,
                Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        var (s, ox, oy) = Transform();
        g.InterpolationMode = s < 1 ? InterpolationMode.Bilinear : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(Image, ox, oy, Image.Width * s, Image.Height * s);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = Image.Width, h = Image.Height;
        using var small = new Font("Segoe UI", 8f, FontStyle.Bold);
        if (Profile is { } p)
        {
            DrawBar(g, p.TargetBar, w, h, Theme.Red, "HEDEF", small);
        }
        if (State is { MobCandidates.Count: > 0 } st)
        {
            // What the bot thinks are mobs: yellow rings, the nearest one thicker.
            using var near = new Pen(Theme.Yellow, 2.5f);
            using var far = new Pen(Color.FromArgb(170, Theme.Yellow), 1.5f);
            for (int i = 0; i < st.MobCandidates.Count; i++)
            {
                var c = ToControl(new Rectangle(st.MobCandidates[i].X - 10, st.MobCandidates[i].Y - 10, 20, 20));
                g.DrawEllipse(i == 0 ? near : far, c);
            }
        }
        if (_selection is Rectangle sel)
        {
            using var pen = new Pen(Color.White, 1.5f) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(pen, Rect(ToControl(sel)));
        }
        if (Overlay.Length > 0)
        {
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            var size = g.MeasureString(Overlay, font);
            using var back = new SolidBrush(Color.FromArgb(170, 8, 14, 27));
            g.FillRectangle(back, 8, 8, size.Width + 12, size.Height + 6);
            using var text = new SolidBrush(Theme.Text);
            g.DrawString(Overlay, font, text, 14, 11);
        }
    }

    private void DrawBar(Graphics g, BarSpec? bar, int w, int h, Color color, string name, Font font)
    {
        if (bar == null)
            return;
        var r = ToControl(bar.Area.ToPixels(w, h));
        using var pen = new Pen(color, 1.5f);
        using var brush = new SolidBrush(color);
        g.DrawRectangle(pen, Rect(r));
        g.DrawString(name, font, brush, r.X, r.Bottom + 1);
    }

    private static Rectangle Rect(RectangleF r) => Rectangle.Round(r);
}
