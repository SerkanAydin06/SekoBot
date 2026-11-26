using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.UI;

/// <summary>
/// The map as the bot knows it, live: the roads, the mountains it never walks into, the water and fields of the game's
/// own M map, and on it the character (which way the camera looks), the mobs, the items on the ground, the other
/// players (another kingdom in red), the saved farm area and the way the bot is walking. Everything from the game's
/// memory: light to draw (a few times a second), nothing taken from the screen.
/// </summary>
internal sealed class LiveMap : Drawn
{
    private readonly Func<WorldSnapshot?> _world;
    private readonly Func<string, RoadMap?> _roadsOf;
    private readonly Func<IReadOnlyList<Vec3>?> _way;
    private readonly Func<bool> _connected;
    private readonly Func<BotProfile> _profile;
    private Bitmap? _ground;
    private string _groundMap = "";
    private int _groundRoads = -1;
    private RoadMap? _roads;

    /// <summary>Metres from the middle to the nearest edge (the mouse wheel changes it).</summary>
    private double _radius = 70;
    /// <summary>The whole map in view (double click), not the character's surroundings.</summary>
    private bool _whole;

    public LiveMap(Func<BotEngine?> engine, Func<BotProfile> profile)
        : this(() => engine()?.World, map => engine()?.RoadsOf(map), () => engine()?.WayNow, () => engine() != null, profile) { }

    internal LiveMap(Func<WorldSnapshot?> world, Func<string, RoadMap?> roadsOf, Func<IReadOnlyList<Vec3>?> way, Func<bool> connected, Func<BotProfile> profile)
    {
        _world = world;
        _roadsOf = roadsOf;
        _way = way;
        _connected = connected;
        _profile = profile;
        BackColor = Theme.Inset;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _whole = false;
        _radius = Math.Clamp(_radius * (e.Delta > 0 ? 0.8 : 1.25), 15, 400);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        _whole = !_whole;
        Invalidate();
    }

    public void ToggleWhole() { _whole = !_whole; Invalidate(); }
    public bool Whole => _whole;

    /// <summary>The map's ground as one picture: a cell a pixel (the game's picture dimmed, roads and mountains on it).</summary>
    private Bitmap? Ground(string map)
    {
        var roads = _roadsOf(map);
        _roads = roads;
        if (roads == null)
            return null;
        if (_ground != null && _groundMap == map && _groundRoads == roads.RoadCells)
            return _ground;
        _ground?.Dispose();
        int w = roads.Columns, h = roads.Rows;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        Bitmap? picture = null;
        try
        {
            string file = RoadMap.PictureOf(Path.Combine(ProfileStore.Directory, "haritalar"), map);
            if (File.Exists(file))
                picture = new Bitmap(file);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { }
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new int[w];
        for (int y = 0; y < h; y++)
        {
            double wy = roads.StartY + (y + 0.5) * RoadMap.Cell;
            for (int x = 0; x < w; x++)
            {
                double wx = roads.StartX + (x + 0.5) * RoadMap.Cell;
                Color c;
                if (roads.OnRoad(wx, wy))
                    c = Color.FromArgb(150, 136, 112);
                else if (roads.Mountain(wx, wy))
                    c = Color.FromArgb(52, 42, 78);
                else if (picture != null)
                {
                    var p = picture.GetPixel(Math.Clamp(x * picture.Width / w, 0, picture.Width - 1), Math.Clamp(y * picture.Height / h, 0, picture.Height - 1));
                    c = Color.FromArgb(p.R * 55 / 100 + 6, p.G * 55 / 100 + 8, p.B * 55 / 100 + 12);
                }
                else
                    c = Color.FromArgb(22, 30, 24);
                row[x] = c.ToArgb();
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, w);
        }
        bmp.UnlockBits(data);
        picture?.Dispose();
        _ground = bmp;
        _groundMap = map;
        _groundRoads = roads.RoadCells;
        return bmp;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Inset);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var world = _world();
        if (world == null || world.MapName.Length == 0)
        {
            TextRenderer.DrawText(g, !_connected() ? "Oyun bağlı değil" : "Harita okunuyor…", Font, ClientRectangle, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        var ground = Ground(world.MapName);
        var me = world.Player;
        // World → screen: the character in the middle (or the whole map), world Y downwards like the game's map.
        double scale, cx, cy;
        if (_whole && _roads != null)
        {
            double mw = _roads.Columns * RoadMap.Cell, mh = _roads.Rows * RoadMap.Cell;
            scale = Math.Min((Width - 20) / mw, (Height - 20) / mh);
            cx = _roads.StartX + mw / 2;
            cy = _roads.StartY + mh / 2;
        }
        else
        {
            scale = Math.Min(Width, Height) / 2.0 / (_radius * 100);
            cx = me.X;
            cy = me.Y;
        }
        PointF At(double x, double y) => new((float)(Width / 2.0 + (x - cx) * scale), (float)(Height / 2.0 + (y - cy) * scale));
        if (ground != null && _roads != null)
        {
            var tl = At(_roads.StartX, _roads.StartY);
            var size = new SizeF((float)(_roads.Columns * RoadMap.Cell * scale), (float)(_roads.Rows * RoadMap.Cell * scale));
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(ground, new RectangleF(tl, size));
            g.PixelOffsetMode = PixelOffsetMode.Default;
        }
        else
            TextRenderer.DrawText(g, "Bu haritanın yolları henüz okunmadı (M haritası açılınca okunur).", Font,
                new Rectangle(0, Height - 40, Width, 30), Theme.Faint, TextFormatFlags.HorizontalCenter);
        float u = Math.Max(1f, Font.Height / 15f);

        // The saved farm area.
        var p = _profile();
        if ((p.MySpot ?? p.FarmSpot) is { } spot && spot.MapName == world.MapName && p.UseFarmSpot)
        {
            var c = At(spot.X, spot.Y);
            float r = (float)(Math.Max(p.LeashMeters, 5) * 100 * scale);
            using var fill = new SolidBrush(Color.FromArgb(22, Theme.Gold));
            g.FillEllipse(fill, c.X - r, c.Y - r, 2 * r, 2 * r);
            using var pen = new Pen(Color.FromArgb(170, Theme.Gold), 1.5f * u) { DashStyle = DashStyle.Dash };
            g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r);
        }

        // The way the bot is walking.
        if (_way() is { Count: > 0 } way)
        {
            var points = new List<PointF> { At(me.X, me.Y) };
            points.AddRange(way.Select(v => At(v.X, v.Y)));
            using var pen = new Pen(Color.FromArgb(220, Theme.Blue), 2.2f * u) { DashStyle = DashStyle.Dash, LineJoin = LineJoin.Round };
            g.DrawLines(pen, [.. points]);
            var end = points[^1];
            using var dot = new SolidBrush(Theme.Blue);
            g.FillEllipse(dot, end.X - 4 * u, end.Y - 4 * u, 8 * u, 8 * u);
        }

        // Items on the ground.
        using (var item = new SolidBrush(Theme.GoldLight))
            foreach (var d in world.Drops ?? [])
            {
                var c = At(d.Position.X, d.Position.Y);
                float s = 3.2f * u;
                g.FillPolygon(item, [new PointF(c.X, c.Y - s), new PointF(c.X + s, c.Y), new PointF(c.X, c.Y + s), new PointF(c.X - s, c.Y)]);
            }

        // Mobs: the one being hit ringed in red, the ones hitting the character red, the rest amber.
        var target = world.Target;
        foreach (var m in world.Mobs)
        {
            if (m.Hp <= 0)
                continue;
            var c = At(m.Position.X, m.Position.Y);
            var color = m.AttacksMe ? Theme.Red : m.IsMetin ? Theme.Violet : Theme.Yellow;
            float s = (m.IsMetin ? 5f : 3.6f) * u;
            using var b = new SolidBrush(color);
            g.FillEllipse(b, c.X - s, c.Y - s, 2 * s, 2 * s);
            if (target != null && target.Id == m.Id)
            {
                using var ring = new Pen(Theme.Red, 2f * u);
                g.DrawEllipse(ring, c.X - s - 4 * u, c.Y - s - 4 * u, 2 * s + 8 * u, 2 * s + 8 * u);
            }
        }

        // Other players: blue; red when of another kingdom or out to fight (PvP, hitting the character); violet a GM.
        int own = p.Empire;
        foreach (var pl in world.Players ?? [])
        {
            var c = At(pl.Position.X, pl.Position.Y);
            bool enemy = pl.TargetsMe || pl.PvpMode != 0 || own > 0 && pl.Empire > 0 && pl.Empire != own;
            var color = pl.IsGm ? Theme.Violet : enemy ? Theme.Red : Theme.Blue;
            float s = 5f * u;
            using var b = new SolidBrush(color);
            g.FillPolygon(b, [new PointF(c.X, c.Y - s), new PointF(c.X + s * 0.9f, c.Y + s * 0.7f), new PointF(c.X - s * 0.9f, c.Y + s * 0.7f)]);
        }

        // The character: a white arrow the way the camera looks.
        {
            var c = At(me.X, me.Y);
            double yaw = (world.Camera?.Yaw ?? -90) * Math.PI / 180;
            float s = 8f * u;
            PointF Turn(double fx, double fy) => new((float)(c.X + fx * Math.Cos(yaw) - fy * Math.Sin(yaw)), (float)(c.Y + fx * Math.Sin(yaw) + fy * Math.Cos(yaw)));
            using var halo = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
            g.FillEllipse(halo, c.X - s * 1.4f, c.Y - s * 1.4f, s * 2.8f, s * 2.8f);
            PointF[] arrow = [Turn(s * 1.2, 0), Turn(-s * 0.8, s * 0.75), Turn(-s * 0.35, 0), Turn(-s * 0.8, -s * 0.75)];
            using var b = new SolidBrush(Color.White);
            g.FillPolygon(b, arrow);
            using var edge = new Pen(Color.FromArgb(40, 40, 40), 1f);
            g.DrawPolygon(edge, arrow);
        }

        // Where, and how far a stretch of the screen is.
        using var small = Sized(0.86f);
        using var bold = Sized(0.9f, FontStyle.Bold);
        string where = $"{world.MapName}  ·  X {me.X / 100:0}  Y {me.Y / 100:0}";
        var box = new Rectangle((int)(10 * u), (int)(10 * u), TextRenderer.MeasureText(where, bold).Width + (int)(16 * u), (int)(24 * u));
        using (var path = Theme.RoundRect(box, 6 * u))
        using (var back = new SolidBrush(Color.FromArgb(200, 8, 11, 16)))
            g.FillPath(back, path);
        TextRenderer.DrawText(g, where, bold, box, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        double metres = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, 80 / (scale * 100) * 1))));
        foreach (double k in new[] { 5, 2, 1 })
            if (k * metres * 100 * scale <= 120 * u) { metres *= k; break; }
        float len = (float)(metres * 100 * scale);
        float sx = Width - len - 16 * u, sy = Height - 16 * u;
        using (var line = new Pen(Theme.Text, 2f * u))
        {
            g.DrawLine(line, sx, sy, sx + len, sy);
            g.DrawLine(line, sx, sy - 4 * u, sx, sy + 1);
            g.DrawLine(line, sx + len, sy - 4 * u, sx + len, sy + 1);
        }
        TextRenderer.DrawText(g, $"{metres:0} m", small, new Rectangle((int)sx, (int)(sy - 20 * u), (int)len, (int)(16 * u)), Theme.Text, TextFormatFlags.HorizontalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ground?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>A window of its own for the live map / the game view / the computer's details (dark, resizable).</summary>
internal sealed class SideWindow : Form
{
    public SideWindow(string title, Size size)
    {
        Text = "PARDUS · " + title;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = size;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        AppIcon.Use(this);
        MinimumSize = new Size(360, 260);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The dark title bar of Windows 10/11.
        try { int on = 1; Native.Win32.DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)); } catch { }
    }

    /// <summary>Closing only hides it (opened again as it was).</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
