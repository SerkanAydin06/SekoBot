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

    /// <summary>The ground picture's pixels per road cell (sharp at every zoom, the roads soft-edged).</summary>
    private const int Detail = 3;

    /// <summary>Metres from the middle to the nearest edge (the mouse wheel and the +/− buttons change it).</summary>
    private double _radius = 70;
    /// <summary>The whole map in view, not the character's surroundings.</summary>
    private bool _whole;
    /// <summary>Dragged away from the character (world units); null: following the character.</summary>
    private (double X, double Y)? _pan;
    private Point? _dragFrom;
    private (double X, double Y) _dragCentre;
    private double _scale, _cx, _cy;

    /// <summary>What is on the map now, in words (for the window's bar).</summary>
    public string Counts { get; private set; } = "";

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
        Cursor = Cursors.SizeAll;
    }

    // ── Moving about: wheel / buttons zoom, dragging moves the view, double click: the whole map ──

    public void ZoomBy(double k)
    {
        _whole = false;
        _radius = Math.Clamp(_radius * k, 12, 450);
        Invalidate();
    }

    public void CenterOnMe()
    {
        _pan = null;
        _whole = false;
        Invalidate();
    }

    public void ToggleWhole() { _whole = !_whole; _pan = null; Invalidate(); }
    public bool Whole => _whole;
    public bool Following => _pan == null && !_whole;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomBy(e.Delta > 0 ? 0.8 : 1.25);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        ToggleWhole();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _scale <= 0)
            return;
        _dragFrom = e.Location;
        _dragCentre = (_cx, _cy);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragFrom is not { } from || _scale <= 0)
            return;
        if (Math.Abs(e.X - from.X) + Math.Abs(e.Y - from.Y) < 3 && _pan == null)
            return;
        _whole = false;
        _pan = (_dragCentre.X - (e.X - from.X) / _scale, _dragCentre.Y - (e.Y - from.Y) / _scale);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragFrom = null;
    }

    // ── The ground: the game's own map picture, the mountains and the roads, drawn once per map ──

    private Bitmap? Ground(string map)
    {
        var roads = _roadsOf(map);
        _roads = roads;
        if (roads == null)
            return null;
        if (_ground != null && _groundMap == map && _groundRoads == roads.RoadCells)
            return _ground;
        _ground?.Dispose();
        _ground = null;
        int w = roads.Columns, h = roads.Rows;
        // Masks a pixel a cell: the roads, the mountains.
        // Smooth outlines (the cells' steps blurred and drawn sharp again): roads and mountains as drawn by hand.
        using var roadCells = Mask(roads, w, h, roads.OnRoad);
        using var mountainCells = Mask(roads, w, h, roads.Mountain);
        using var roadMask = Smooth(roadCells, w * Detail, h * Detail, Detail + 1);
        using var mountainMask = Smooth(mountainCells, w * Detail, h * Detail, Detail + 2);
        var ground = new Bitmap(w * Detail, h * Detail, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(ground))
        {
            g.Clear(Color.FromArgb(20, 27, 24));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var all = new Rectangle(0, 0, ground.Width, ground.Height);
            // The game's picture, a little darker and calmer (the marks on it must stand out).
            try
            {
                string file = RoadMap.PictureOf(Path.Combine(ProfileStore.Directory, "haritalar"), map);
                if (File.Exists(file))
                {
                    using var picture = new Bitmap(file);
                    using var calm = new ImageAttributes();
                    calm.SetColorMatrix(new ColorMatrix(
                    [
                        [0.50f, 0.04f, 0.04f, 0, 0],
                        [0.06f, 0.52f, 0.06f, 0, 0],
                        [0.04f, 0.04f, 0.56f, 0, 0],
                        [0, 0, 0, 1, 0],
                        [0.02f, 0.03f, 0.05f, 0, 1],
                    ]));
                    g.DrawImage(picture, all, 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel, calm);
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException) { }
            // A mask drawn in a colour (its white made that colour, soft at its edges).
            void Tinted(Bitmap mask, Color color, int dx = 0, int dy = 0)
            {
                using var tint = new ImageAttributes();
                tint.SetColorMatrix(new ColorMatrix(
                [
                    [0, 0, 0, 0, 0],
                    [0, 0, 0, 0, 0],
                    [0, 0, 0, 0, 0],
                    [0, 0, 0, color.A / 255f, 0],
                    [color.R / 255f, color.G / 255f, color.B / 255f, 0, 1],
                ]));
                g.DrawImage(mask, new Rectangle(dx, dy, ground.Width, ground.Height), 0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel, tint);
            }
            // Mountains: violet, hatched (never walked into).
            Tinted(mountainMask, Color.FromArgb(150, 46, 34, 82));
            using (var hatched = Hatched(mountainMask, ground.Width, ground.Height))
                g.DrawImage(hatched, 0, 0);
            // Roads: a dark edge under them, then the road, sand-coloured, soft-edged, a light top edge.
            foreach (var (dx, dy) in new[] { (-2, 0), (2, 0), (0, -2), (0, 2) })
                Tinted(roadMask, Color.FromArgb(120, 20, 16, 10), dx, dy);
            Tinted(roadMask, Color.FromArgb(235, 176, 150, 104));
            Tinted(roadMask, Color.FromArgb(60, 236, 214, 160), 0, -1);
        }
        _ground = ground;
        _groundMap = map;
        _groundRoads = roads.RoadCells;
        return ground;
    }

    private static Bitmap Mask(RoadMap roads, int w, int h, Func<double, double, bool> on)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new int[w];
        int white = Color.White.ToArgb();
        for (int y = 0; y < h; y++)
        {
            double wy = roads.StartY + (y + 0.5) * RoadMap.Cell;
            for (int x = 0; x < w; x++)
                row[x] = on(roads.StartX + (x + 0.5) * RoadMap.Cell, wy) ? white : 0;
            System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, w);
        }
        bmp.UnlockBits(data);
        return bmp;
    }

    /// <summary>
    /// The cells' mask made <paramref name="width"/> × <paramref name="height"/> with a smooth outline: drawn big, blurred
    /// (a box of <paramref name="radius"/> twice), then the edge made crisp again (a soft step at half way).
    /// </summary>
    private static Bitmap Smooth(Bitmap cells, int width, int height, int radius)
    {
        var big = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(cells, new Rectangle(0, 0, width, height), 0, 0, cells.Width, cells.Height, GraphicsUnit.Pixel);
        }
        var data = big.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int n = width * height;
        var px = new int[n];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, n);
        var a = new float[n];
        for (int i = 0; i < n; i++)
            a[i] = ((uint)px[i] >> 24) / 255f;
        var t = new float[n];
        for (int pass = 0; pass < 2; pass++)
        {
            // Rows, then columns: running sums.
            for (int y = 0; y < height; y++)
            {
                float sum = 0;
                int row = y * width;
                for (int x = -radius; x < width; x++)
                {
                    if (x + radius < width) sum += a[row + x + radius];
                    if (x - radius - 1 >= 0) sum -= a[row + x - radius - 1];
                    if (x >= 0) t[row + x] = sum / (2 * radius + 1);
                }
            }
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int y = -radius; y < height; y++)
                {
                    if (y + radius < height) sum += t[(y + radius) * width + x];
                    if (y - radius - 1 >= 0) sum -= t[(y - radius - 1) * width + x];
                    if (y >= 0) a[y * width + x] = sum / (2 * radius + 1);
                }
            }
        }
        for (int i = 0; i < n; i++)
        {
            float v = Math.Clamp((a[i] - 0.38f) / 0.24f, 0, 1);
            v = v * v * (3 - 2 * v);
            px[i] = (int)((uint)(v * 255) << 24 | 0xFFFFFFu);
        }
        System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, n);
        big.UnlockBits(data);
        return big;
    }

    /// <summary>Thin diagonal lines where the mask is (the mountains), nothing elsewhere.</summary>
    private static Bitmap Hatched(Bitmap mask, int width, int height)
    {
        var big = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(mask, new Rectangle(0, 0, width, height), 0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel);
        }
        var data = big.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var row = new int[width];
        int line = Color.FromArgb(34, 210, 190, 255).ToArgb();
        for (int y = 0; y < height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, width);
            for (int x = 0; x < width; x++)
                row[x] = (uint)row[x] >> 24 >= 128 && (x + y) % 9 == 0 ? line : 0;
            System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, width);
        }
        big.UnlockBits(data);
        return big;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Inset);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float u = Math.Max(1f, Font.Height / 15f);
        var world = _world();
        if (world == null || world.MapName.Length == 0 || BotEngine.IsLoginScreen(world.MapName))
        {
            using var big = Sized(1.2f, FontStyle.Bold);
            string what = !_connected() ? "Oyun bağlı değil" : world != null && BotEngine.IsLoginScreen(world.MapName) ? "Oyun giriş ekranında" : "Harita okunuyor…";
            TextRenderer.DrawText(g, what, big, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Counts = "";
            return;
        }
        var ground = Ground(world.MapName);
        var me = world.Player;
        // World → screen: the character in the middle (or where it was dragged to, or the whole map); Y downwards.
        if (_whole && _roads != null)
        {
            double mw = _roads.Columns * RoadMap.Cell, mh = _roads.Rows * RoadMap.Cell;
            _scale = Math.Min((Width - 24 * u) / mw, (Height - 24 * u) / mh);
            (_cx, _cy) = (_roads.StartX + mw / 2, _roads.StartY + mh / 2);
        }
        else
        {
            _scale = Math.Min(Width, Height) / 2.0 / (_radius * 100);
            (_cx, _cy) = _pan ?? (me.X, me.Y);
        }
        double scale = _scale;
        PointF At(double x, double y) => new((float)(Width / 2.0 + (x - _cx) * scale), (float)(Height / 2.0 + (y - _cy) * scale));
        bool near = scale * 100 >= 3.2; // close enough for names
        if (ground != null && _roads != null)
        {
            var tl = At(_roads.StartX, _roads.StartY);
            var size = new SizeF((float)(_roads.Columns * RoadMap.Cell * scale), (float)(_roads.Rows * RoadMap.Cell * scale));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(ground, new RectangleF(tl, size));
            g.PixelOffsetMode = PixelOffsetMode.Default;
            using var edge = new Pen(Color.FromArgb(90, Theme.BorderStrong), 1.5f * u);
            g.DrawRectangle(edge, tl.X, tl.Y, size.Width, size.Height);
        }
        else
        {
            using var note = Sized(0.95f);
            TextRenderer.DrawText(g, "Bu haritanın yolları henüz bilinmiyor.", note, new Rectangle(0, Height - (int)(70 * u), Width, (int)(24 * u)), Theme.Faint, TextFormatFlags.HorizontalCenter);
        }
        // A light grid every 50 m (100 m far out): distances at a glance.
        {
            double step = scale * 5000 >= 60 * u ? 5000 : 10000;
            using var grid = new Pen(Color.FromArgb(16, 255, 255, 255), 1f);
            double x0 = Math.Floor((_cx - Width / 2.0 / scale) / step) * step, y0 = Math.Floor((_cy - Height / 2.0 / scale) / step) * step;
            for (double x = x0; x < _cx + Width / 2.0 / scale; x += step)
                g.DrawLine(grid, At(x, 0).X, 0, At(x, 0).X, Height);
            for (double y = y0; y < _cy + Height / 2.0 / scale; y += step)
                g.DrawLine(grid, 0, At(0, y).Y, Width, At(0, y).Y);
        }

        using var label = Sized(0.82f, FontStyle.Bold);
        // Names drawn last, over every mark (a mob dot hid part of "Farm alanı").
        var labels = new List<Action>();
        void Tag(string text, PointF at, Color color, float below) => labels.Add(() => DrawTag(text, at, color, below));
        void DrawTag(string text, PointF at, Color color, float below)
        {
            var sz = TextRenderer.MeasureText(text, label);
            var box = new RectangleF(at.X - sz.Width / 2f - 5 * u, at.Y + below, sz.Width + 10 * u, sz.Height + 2 * u);
            using (var path = Theme.RoundRect(box, box.Height / 2))
            using (var back = new SolidBrush(Color.FromArgb(200, 8, 11, 16)))
                g.FillPath(back, path);
            TextRenderer.DrawText(g, text, label, Rectangle.Round(box), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        // The saved farm area: a gold ring, softly filled.
        var p = _profile();
        if ((p.MySpot ?? p.FarmSpot) is { } spot && spot.MapName == world.MapName && p.UseFarmSpot)
        {
            var c = At(spot.X, spot.Y);
            float r = (float)(Math.Max(p.LeashMeters, 5) * 100 * scale);
            if (r > 2)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(c.X - r, c.Y - r, 2 * r, 2 * r);
                    using var fill = new PathGradientBrush(path) { CenterColor = Color.FromArgb(8, Theme.Gold), SurroundColors = [Color.FromArgb(48, Theme.Gold)] };
                    g.FillPath(fill, path);
                }
                using var pen = new Pen(Color.FromArgb(210, Theme.Gold), 1.8f * u) { DashStyle = DashStyle.Dash };
                g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r);
                if (r > 18 * u)
                    Tag("Farm alanı", new PointF(c.X, c.Y - r), Theme.GoldLight, -9 * u);
            }
        }

        // The shops known on this map: a small round badge, the name when close.
        foreach (var shop in NpcBook.Shared.On(world.MapName))
        {
            if (!NpcBook.IsShop(shop.Vnum))
                continue;
            var c = At(shop.X, shop.Y);
            if (c.X < -20 || c.Y < -20 || c.X > Width + 20 || c.Y > Height + 20)
                continue;
            float s = 7f * u;
            using (var back = new SolidBrush(Color.FromArgb(235, 20, 26, 36)))
                g.FillEllipse(back, c.X - s, c.Y - s, 2 * s, 2 * s);
            using (var ring = new Pen(Theme.Green, 1.6f * u))
                g.DrawEllipse(ring, c.X - s, c.Y - s, 2 * s, 2 * s);
            Icons.Draw(g, "cart", new RectangleF(c.X - s * 0.65f, c.Y - s * 0.65f, s * 1.3f, s * 1.3f), Theme.Green, 1.6f);
            if (near)
                Tag(NpcBook.Shops.GetValueOrDefault(shop.Vnum, "Satıcı"), c, Theme.Green, s + 3 * u);
        }

        // The way the bot is walking: a glow under a bright line, arrows along it, a pin at its end.
        if (_way() is { Count: > 0 } way)
        {
            var points = new List<PointF> { At(me.X, me.Y) };
            points.AddRange(way.Select(v => At(v.X, v.Y)));
            using (var glow = new Pen(Color.FromArgb(60, Theme.Blue), 7f * u) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(glow, [.. points]);
            using (var line = new Pen(Color.FromArgb(235, 130, 190, 255), 2.2f * u) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(line, [.. points]);
            float every = 60 * u, carried = every / 2;
            using var arrow = new SolidBrush(Color.FromArgb(235, 210, 232, 255));
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Sqrt(dx * dx + dy * dy);
                if (len < 1)
                    continue;
                float ux = dx / len, uy = dy / len, at = carried;
                for (; at < len; at += every)
                {
                    float x = a.X + ux * at, y = a.Y + uy * at, s = 4.2f * u;
                    g.FillPolygon(arrow, [new PointF(x + ux * s, y + uy * s), new PointF(x - ux * s * 0.6f - uy * s * 0.8f, y - uy * s * 0.6f + ux * s * 0.8f),
                        new PointF(x - ux * s * 0.6f + uy * s * 0.8f, y - uy * s * 0.6f - ux * s * 0.8f)]);
                }
                carried = at - len;
            }
            var end = points[^1];
            float pin = 6f * u;
            using (var pinFill = new SolidBrush(Theme.Blue))
                g.FillEllipse(pinFill, end.X - pin, end.Y - pin, 2 * pin, 2 * pin);
            using (var pinDot = new SolidBrush(Color.White))
                g.FillEllipse(pinDot, end.X - pin * 0.4f, end.Y - pin * 0.4f, pin * 0.8f, pin * 0.8f);
        }

        // Items on the ground: gold diamonds, outlined.
        int drops = 0;
        using (var item = new SolidBrush(Theme.GoldLight))
        using (var itemEdge = new Pen(Color.FromArgb(200, 60, 40, 0), 1f * u))
            foreach (var d in world.Drops ?? [])
            {
                drops++;
                var c = At(d.Position.X, d.Position.Y);
                float s = 4f * u;
                PointF[] diamond = [new(c.X, c.Y - s), new(c.X + s, c.Y), new(c.X, c.Y + s), new(c.X - s, c.Y)];
                g.FillPolygon(item, diamond);
                g.DrawPolygon(itemEdge, diamond);
            }

        // Mobs: amber, red with a halo when hitting the character, Metin stones violet; the target ringed with a cross.
        var target = world.Target;
        int mobs = 0, hitting = 0;
        using var mobEdge = new Pen(Color.FromArgb(210, 10, 12, 18), 1.2f * u);
        foreach (var m in world.Mobs)
        {
            if (m.Hp <= 0)
                continue;
            mobs++;
            var c = At(m.Position.X, m.Position.Y);
            var color = m.AttacksMe ? Theme.Red : m.IsMetin ? Theme.Violet : Theme.Yellow;
            float s = (m.IsMetin ? 6f : 4.2f) * u;
            if (m.AttacksMe)
            {
                hitting++;
                using var halo = new SolidBrush(Color.FromArgb(70, Theme.Red));
                g.FillEllipse(halo, c.X - s * 2, c.Y - s * 2, s * 4, s * 4);
            }
            using var b = new SolidBrush(color);
            if (m.IsMetin)
            {
                PointF[] stone = [new(c.X, c.Y - s), new(c.X + s, c.Y), new(c.X, c.Y + s), new(c.X - s, c.Y)];
                g.FillPolygon(b, stone);
                g.DrawPolygon(mobEdge, stone);
            }
            else
            {
                g.FillEllipse(b, c.X - s, c.Y - s, 2 * s, 2 * s);
                g.DrawEllipse(mobEdge, c.X - s, c.Y - s, 2 * s, 2 * s);
            }
            if (target != null && target.Id == m.Id)
            {
                float r = s + 5 * u;
                using var ring = new Pen(Color.White, 1.8f * u);
                g.DrawEllipse(ring, c.X - r, c.Y - r, 2 * r, 2 * r);
                g.DrawLine(ring, c.X - r - 3 * u, c.Y, c.X - r + 3 * u, c.Y);
                g.DrawLine(ring, c.X + r - 3 * u, c.Y, c.X + r + 3 * u, c.Y);
                g.DrawLine(ring, c.X, c.Y - r - 3 * u, c.X, c.Y - r + 3 * u);
                g.DrawLine(ring, c.X, c.Y + r - 3 * u, c.X, c.Y + r + 3 * u);
            }
        }

        // Other players: blue; red when of another kingdom or out to fight; violet a GM. Their names when close.
        int own = p.Empire, players = 0;
        foreach (var pl in world.Players ?? [])
        {
            players++;
            var c = At(pl.Position.X, pl.Position.Y);
            bool enemy = pl.TargetsMe || pl.PvpMode != 0 || own > 0 && pl.Empire > 0 && pl.Empire != own;
            var color = pl.IsGm ? Theme.Violet : enemy ? Theme.Red : Theme.Blue;
            float s = 6f * u;
            PointF[] tri = [new(c.X, c.Y - s), new(c.X + s * 0.9f, c.Y + s * 0.7f), new(c.X - s * 0.9f, c.Y + s * 0.7f)];
            using (var b = new SolidBrush(color))
                g.FillPolygon(b, tri);
            g.DrawPolygon(mobEdge, tri);
            if (near && pl.Name.Length > 0)
                Tag(pl.IsGm ? "GM " + pl.Name : pl.Name, c, color, s + 3 * u);
        }

        // The character: where the camera looks (a soft cone), a white arrow with a blue glow.
        {
            var c = At(me.X, me.Y);
            double yaw = (world.Camera?.Yaw ?? -90) * Math.PI / 180;
            float s = 9f * u;
            PointF Turn(double fx, double fy) => new((float)(c.X + fx * Math.Cos(yaw) - fy * Math.Sin(yaw)), (float)(c.Y + fx * Math.Sin(yaw) + fy * Math.Cos(yaw)));
            using (var cone = new GraphicsPath())
            {
                float r = s * 5;
                cone.AddPie(c.X - r, c.Y - r, 2 * r, 2 * r, (float)(yaw * 180 / Math.PI) - 30, 60);
                using var fill = new PathGradientBrush(cone) { CenterPoint = c, CenterColor = Color.FromArgb(70, 255, 255, 255), SurroundColors = [Color.FromArgb(0, 255, 255, 255)] };
                g.FillPath(fill, cone);
            }
            using (var glow = new SolidBrush(Color.FromArgb(70, Theme.Blue)))
                g.FillEllipse(glow, c.X - s * 1.6f, c.Y - s * 1.6f, s * 3.2f, s * 3.2f);
            PointF[] arrowHead = [Turn(s * 1.2, 0), Turn(-s * 0.8, s * 0.75), Turn(-s * 0.35, 0), Turn(-s * 0.8, -s * 0.75)];
            using (var b = new SolidBrush(Color.White))
                g.FillPolygon(b, arrowHead);
            using (var edge = new Pen(Color.FromArgb(30, 60, 110), 1.4f * u) { LineJoin = LineJoin.Round })
                g.DrawPolygon(edge, arrowHead);
        }

        foreach (var draw in labels)
            draw();

        // ── On top: where (top left), the compass (top right), the legend (bottom left), the scale (bottom right) ──
        void Glass(RectangleF r)
        {
            using var path = Theme.RoundRect(r, 8 * u);
            using var back = new SolidBrush(Color.FromArgb(210, 10, 13, 19));
            g.FillPath(back, path);
            using var pen = new Pen(Color.FromArgb(150, Theme.BorderStrong), 1f);
            g.DrawPath(pen, path);
        }
        using var title = Sized(1.05f, FontStyle.Bold);
        using var small = Sized(0.82f);
        {
            string head = world.MapName + (world.Channel > 0 ? $"  ·  Kanal {world.Channel}" : "") + (world.Server.Length > 0 ? $"  ·  {world.Server}" : "");
            string sub = $"X {me.X / 100:0}   Y {me.Y / 100:0}" + (Following ? "" : "   ·   serbest bakış (Ortala ile geri dön)");
            int tw = Math.Max(TextRenderer.MeasureText(head, title).Width, TextRenderer.MeasureText(sub, small).Width) + (int)(22 * u);
            var box = new RectangleF(10 * u, 10 * u, tw, title.Height + small.Height + 14 * u);
            Glass(box);
            TextRenderer.DrawText(g, head, title, new Point((int)(box.X + 11 * u), (int)(box.Y + 6 * u)), Theme.Text, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, sub, small, new Point((int)(box.X + 11 * u), (int)(box.Y + 8 * u + title.Height)), Theme.Muted, TextFormatFlags.NoPrefix);
        }
        {
            float r = 18 * u;
            var c = new PointF(Width - 12 * u - r, 12 * u + r);
            Glass(new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r));
            using var north = new SolidBrush(Theme.Red);
            using var south = new SolidBrush(Theme.Muted);
            g.FillPolygon(north, [new PointF(c.X, c.Y - r * 0.72f), new PointF(c.X + r * 0.22f, c.Y), new PointF(c.X - r * 0.22f, c.Y)]);
            g.FillPolygon(south, [new PointF(c.X, c.Y + r * 0.72f), new PointF(c.X + r * 0.22f, c.Y), new PointF(c.X - r * 0.22f, c.Y)]);
            TextRenderer.DrawText(g, "K", small, new Rectangle((int)(c.X - r), (int)(c.Y + r + 2 * u), (int)(2 * r), small.Height), Theme.Muted, TextFormatFlags.HorizontalCenter);
        }
        DrawLegend(g, small, u, Glass);
        {
            double metres = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, 80 * u / (scale * 100)))));
            foreach (double k in new[] { 5, 2, 1 })
                if (k * metres * 100 * scale <= 140 * u) { metres *= k; break; }
            float len = (float)(metres * 100 * scale);
            var box = new RectangleF(Width - len - 34 * u, Height - 44 * u, len + 24 * u, 34 * u);
            Glass(box);
            float sx = box.X + 12 * u, sy = box.Bottom - 10 * u;
            using (var line = new Pen(Theme.Text, 2f * u))
            {
                g.DrawLine(line, sx, sy, sx + len, sy);
                g.DrawLine(line, sx, sy - 5 * u, sx, sy + 1);
                g.DrawLine(line, sx + len, sy - 5 * u, sx + len, sy + 1);
            }
            TextRenderer.DrawText(g, $"{metres:0} m", small, new Rectangle((int)sx, (int)(box.Y + 3 * u), (int)len, small.Height), Theme.Text, TextFormatFlags.HorizontalCenter);
        }
        Counts = $"{mobs} mob" + (hitting > 0 ? $" ({hitting} saldırıyor)" : "") + $"  ·  {players} oyuncu  ·  {drops} eşya yerde";
    }

    /// <summary>The legend in the bottom left corner: each mark drawn as on the map, two columns.</summary>
    private void DrawLegend(Graphics g, Font small, float u, Action<RectangleF> glass)
    {
        (string Text, Action<PointF> Mark)[] keys =
        [
            ("Sen", at => { using var b = new SolidBrush(Color.White); g.FillPolygon(b, [new PointF(at.X + 6 * u, at.Y), new PointF(at.X - 4 * u, at.Y + 4 * u), new PointF(at.X - 2 * u, at.Y), new PointF(at.X - 4 * u, at.Y - 4 * u)]); }),
            ("Mob", at => { using var b = new SolidBrush(Theme.Yellow); g.FillEllipse(b, at.X - 4 * u, at.Y - 4 * u, 8 * u, 8 * u); }),
            ("Sana vuran mob", at => { using var b = new SolidBrush(Theme.Red); g.FillEllipse(b, at.X - 4 * u, at.Y - 4 * u, 8 * u, 8 * u); }),
            ("Metin taşı", at => { using var b = new SolidBrush(Theme.Violet); g.FillPolygon(b, [new PointF(at.X, at.Y - 5 * u), new PointF(at.X + 5 * u, at.Y), new PointF(at.X, at.Y + 5 * u), new PointF(at.X - 5 * u, at.Y)]); }),
            ("Oyuncu / düşman", at => { using var b = new SolidBrush(Theme.Blue); using var r = new SolidBrush(Theme.Red); g.FillPolygon(b, [new PointF(at.X - 4 * u, at.Y - 5 * u), new PointF(at.X, at.Y + 4 * u), new PointF(at.X - 8 * u, at.Y + 4 * u)]); g.FillPolygon(r, [new PointF(at.X + 5 * u, at.Y - 5 * u), new PointF(at.X + 9 * u, at.Y + 4 * u), new PointF(at.X + 1 * u, at.Y + 4 * u)]); }),
            ("Yerdeki eşya", at => { using var b = new SolidBrush(Theme.GoldLight); g.FillPolygon(b, [new PointF(at.X, at.Y - 4 * u), new PointF(at.X + 4 * u, at.Y), new PointF(at.X, at.Y + 4 * u), new PointF(at.X - 4 * u, at.Y)]); }),
            ("Satıcı", at => { using var ring = new Pen(Theme.Green, 1.6f * u); g.DrawEllipse(ring, at.X - 5 * u, at.Y - 5 * u, 10 * u, 10 * u); }),
            ("Gidilen yol", at => { using var l = new Pen(Color.FromArgb(235, 130, 190, 255), 2.2f * u); g.DrawLine(l, at.X - 7 * u, at.Y, at.X + 7 * u, at.Y); }),
            ("Yol", at => { using var b = new SolidBrush(Color.FromArgb(176, 150, 104)); g.FillRectangle(b, at.X - 7 * u, at.Y - 3 * u, 14 * u, 6 * u); }),
            ("Dağ (girilmez)", at => { using var b = new SolidBrush(Color.FromArgb(120, 70, 150)); g.FillRectangle(b, at.X - 7 * u, at.Y - 4 * u, 14 * u, 8 * u); }),
        ];
        float row = small.Height + 5 * u, colW = 0;
        foreach (var k in keys)
            colW = Math.Max(colW, TextRenderer.MeasureText(k.Text, small).Width);
        int perCol = (int)Math.Ceiling(keys.Length / 2.0);
        float itemW = 26 * u + colW, boxW = 2 * itemW + 20 * u, boxH = perCol * row + 14 * u;
        if (Width < boxW * 1.6f || Height < boxH * 2.2f)
            return;
        var box = new RectangleF(10 * u, Height - boxH - 10 * u, boxW, boxH);
        glass(box);
        for (int i = 0; i < keys.Length; i++)
        {
            float x = box.X + 10 * u + i / perCol * itemW, y = box.Y + 7 * u + i % perCol * row;
            keys[i].Mark(new PointF(x + 8 * u, y + row / 2));
            TextRenderer.DrawText(g, keys[i].Text, small, new Point((int)(x + 22 * u), (int)(y + (row - small.Height) / 2)), Theme.Muted, TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ground?.Dispose();
        base.Dispose(disposing);
    }
}

// <summary>A window of its own for the live map / the game view / the computer's details (dark, resizable).</summary>
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
