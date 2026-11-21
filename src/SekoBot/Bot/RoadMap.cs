using System.Collections;
using System.Text.Json;
using SekoBot.Capture;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// The roads of a map, taken off the game's M map picture (the brown ways drawn on it; seen 2026-11-21 on c3:
/// the whole road net stands out by its colour). Kept per map on disk (haritalar\[map].yollar.json), shared by
/// every SekoBot. Ways found across the map keep to the roads where they can (off-road ground costs more), so
/// the bot walks where people walk: no trees, rocks or slopes in the way.
/// </summary>
internal sealed class RoadMap
{
    /// <summary>The road grid's cell (world units, 1 m).</summary>
    internal const double Cell = 100;

    private readonly BitArray _road;

    public RoadMap(string map, double startX, double startY, int columns, int rows, BitArray road)
    {
        Map = map;
        StartX = startX;
        StartY = startY;
        Columns = columns;
        Rows = rows;
        _road = road;
    }

    public string Map { get; }
    public double StartX { get; }
    public double StartY { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int RoadCells { get; private set; }

    public bool OnRoad(double x, double y)
    {
        int c = (int)Math.Floor((x - StartX) / Cell), r = (int)Math.Floor((y - StartY) / Cell);
        return c >= 0 && r >= 0 && c < Columns && r < Rows && _road[r * Columns + c];
    }


    /// <summary>
    /// The roads read off the M map as it is drawn in <paramref name="frame"/> at <paramref name="drawn"/> (client
    /// pixels). The picture covers the world from -offset to texture / scale - offset on each side.
    /// </summary>
    public static RoadMap? FromPicture(string map, Frame frame, Rectangle drawn, double offsetX, double offsetY, double scaleX, double scaleY, double texture)
    {
        if (drawn.Width < 50 || drawn.Height < 50 || drawn.Right > frame.Width || drawn.Bottom > frame.Height || drawn.Left < 0 || drawn.Top < 0)
            return null;
        // The picture may be taller than wide (c2: 307 × 409 drawn, 756 wide): its height in texture units follows its shape.
        double textureY = texture * drawn.Height / drawn.Width;
        double startX = -offsetX, startY = -offsetY, endX = texture / scaleX - offsetX, endY = textureY / scaleY - offsetY;
        int columns = (int)Math.Ceiling((endX - startX) / Cell), rows = (int)Math.Ceiling((endY - startY) / Cell);
        if (columns is <= 0 or > 4000 || rows is <= 0 or > 4000)
            return null;
        int w = drawn.Width, h = drawn.Height;
        var pixels = new (byte R, byte G, byte B)[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (b, g, r) = frame.Pixel(drawn.Left + x, drawn.Top + y);
                pixels[y * w + x] = (r, g, b);
            }
        var roadPixels = RoadPixels(pixels, w, h);
        var road = new BitArray(columns * rows);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                int px = Math.Clamp((int)((c + 0.5) / columns * w), 0, w - 1), py = Math.Clamp((int)((r + 0.5) / rows * h), 0, h - 1);
                if (roadPixels[py * w + px])
                    road[r * columns + c] = true;
            }
        var result = new RoadMap(map, startX, startY, columns, rows, road);
        result.RoadCells = result.Count();
        double share = (double)result.RoadCells / (columns * rows);
        return share is > MinShare and < MaxShare ? result : null;
    }

    /// <summary>A road net covering less / more of the map than this is no road net (a wrong picture).</summary>
    internal const double MinShare = 0.01, MaxShare = 0.4;

    /// <summary>
    /// Which pixels of a map picture are road, whatever the map's colours (c3: brown on dark olive, c2: light grey on
    /// dark green): the map's ground is its commonest colour; roads are thin lines through it, lighter than right
    /// around them, neither ground nor water. Small specks (rock edges) are dropped; the lines are widened a pixel so
    /// they stay joined when sampled.
    /// </summary>
    internal static bool[] RoadPixels((byte R, byte G, byte B)[] pixels, int w, int h)
    {
        int n = w * h;
        // The ground: the commonest colour (in steps of 16), not water.
        var counts = new Dictionary<int, int>();
        foreach (var (r, g, b) in pixels)
            if (b <= r + 12)
            {
                int key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        if (counts.Count == 0)
            return new bool[n];
        int top = counts.MaxBy(kv => kv.Value).Key;
        double gr = ((top >> 8) << 4) + 8, gg = (((top >> 4) & 15) << 4) + 8, gb = ((top & 15) << 4) + 8;
        var ground = new double[n];
        var light = new double[n];
        for (int i = 0; i < n; i++)
        {
            var (r, g, b) = pixels[i];
            ground[i] = Math.Abs(r - gr) + Math.Abs(g - gg) + Math.Abs(b - gb) < 40 ? 1 : 0;
            light[i] = (r + g + b) / 3.0;
        }
        var groundAround = Box(ground, w, h, 7);
        var lightAround = Box(light, w, h, 4);
        var road = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var (r, g, b) = pixels[i];
            road[i] = light[i] - lightAround[i] > 7 && groundAround[i] > 0.35 && ground[i] == 0 && b <= r + 12;
        }
        // Specks dropped: a road is a long line, many pixels joined.
        var label = new int[n];
        var stack = new Stack<int>();
        for (int i = 0; i < n; i++)
        {
            if (!road[i] || label[i] != 0)
                continue;
            var part = new List<int>();
            stack.Push(i);
            label[i] = 1;
            while (stack.Count > 0)
            {
                int j = stack.Pop();
                part.Add(j);
                int x = j % w, y = j / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        int k = ny * w + nx;
                        if (road[k] && label[k] == 0)
                        {
                            label[k] = 1;
                            stack.Push(k);
                        }
                    }
            }
            if (part.Count < MinRoadPixels)
                foreach (int j in part)
                    road[j] = false;
        }
        // A pixel wider.
        var wide = new bool[n];
        for (int i = 0; i < n; i++)
            if (road[i])
            {
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h)
                            wide[ny * w + nx] = true;
                    }
            }
        return wide;
    }

    /// <summary>Fewer joined road pixels than this: a speck, not a road.</summary>
    internal const int MinRoadPixels = 15;

    /// <summary>The mean of each pixel's square neighbourhood (2r+1 wide).</summary>
    private static double[] Box(double[] a, int w, int h, int r)
    {
        var sum = new double[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                sum[(y + 1) * (w + 1) + x + 1] = a[y * w + x] + sum[y * (w + 1) + x + 1] + sum[(y + 1) * (w + 1) + x] - sum[y * (w + 1) + x];
        var mean = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - r), y0 = Math.Max(0, y - r), x1 = Math.Min(w, x + r + 1), y1 = Math.Min(h, y + r + 1);
                double total = sum[y1 * (w + 1) + x1] - sum[y0 * (w + 1) + x1] - sum[y1 * (w + 1) + x0] + sum[y0 * (w + 1) + x0];
                mean[y * w + x] = total / ((x1 - x0) * (y1 - y0));
            }
        return mean;
    }

    /// <summary>The picture as it was seen (for a look when no roads came out of it).</summary>
    public static void SavePicture(Frame frame, Rectangle drawn, string file)
    {
        var r = Rectangle.Intersect(drawn, new Rectangle(0, 0, frame.Width, frame.Height));
        if (r.Width <= 0 || r.Height <= 0)
            return;
        using var bmp = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                var (b, g, rd) = frame.Pixel(r.Left + x, r.Top + y);
                bmp.SetPixel(x, y, Color.FromArgb(rd, g, b));
            }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }

    private int Count()
    {
        int n = 0;
        for (int i = 0; i < _road.Length; i++)
            if (_road[i])
                n++;
        return n;
    }

    // ── On disk: haritalar\[map].yollar.json ──

    private sealed class Saved
    {
        public string Map { get; set; } = "";
        public double StartX { get; set; }
        public double StartY { get; set; }
        public int Columns { get; set; }
        public int Rows { get; set; }
        public string Bits { get; set; } = "";
    }

    public static string FileOf(string folder, string map) => MapTerrain.Stem(folder, map) + ".yollar.json";

    public void Save(string folder)
    {
        var bytes = new byte[(_road.Length + 7) / 8];
        _road.CopyTo(bytes, 0);
        Directory.CreateDirectory(folder);
        File.WriteAllText(FileOf(folder, Map), JsonSerializer.Serialize(new Saved
        {
            Map = Map, StartX = StartX, StartY = StartY, Columns = Columns, Rows = Rows, Bits = Convert.ToBase64String(bytes),
        }));
    }

    public static RoadMap? Load(string folder, string map)
    {
        try
        {
            string file = FileOf(folder, map);
            if (!File.Exists(file) || JsonSerializer.Deserialize<Saved>(File.ReadAllText(file)) is not { } s || s.Map != map)
                return null;
            var bits = new BitArray(Convert.FromBase64String(s.Bits)) { Length = s.Columns * s.Rows };
            var roads = new RoadMap(map, s.StartX, s.StartY, s.Columns, s.Rows, bits);
            roads.RoadCells = roads.Count();
            return roads;
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or UnauthorizedAccessException) { }
        return null;
    }
}
