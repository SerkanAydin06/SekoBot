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

    public RoadMap(string map, double startX, double startY, int columns, int rows, BitArray road, int picture = 0, bool biggest = false)
    {
        Picture = picture;
        Biggest = biggest;
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
    /// <summary>How wide the M map picture was (pixels) when these roads were read (0: not known, an older file).</summary>
    public int Picture { get; }
    /// <summary>Roads read off a picture this wide at least are kept; from a smaller one (thin roads lost, seen on c2 at
    /// 307 px: the town's and most side roads missing) they are read again once the M map can be shown big.</summary>
    internal const int GoodPicture = 600;
    /// <summary>Read with the game grown to the screen: as big as the M map gets here (not read again).</summary>
    public bool Biggest { get; }
    public bool Good => Picture >= GoodPicture || Biggest;

    public bool OnRoad(double x, double y)
    {
        int c = (int)Math.Floor((x - StartX) / Cell), r = (int)Math.Floor((y - StartY) / Cell);
        return c >= 0 && r >= 0 && c < Columns && r < Rows && _road[r * Columns + c];
    }

    // ── The middle of the road (the user: "yolu ortalayıp gitsin") ──

    /// <summary>A step on the road's middle line costs this; off it, <see cref="EdgeStep"/> more per cell towards the edge.</summary>
    internal const float MiddleCost = 1f, EdgeStep = 0.6f;
    private float[]? _cost;

    /// <summary>
    /// What a step on each cell costs (1 on the road's middle, more towards its edges, 0 off the road): the distance of
    /// every road cell to the road's edge, the middle being where it is the largest across the road.
    /// </summary>
    private float[] Costs()
    {
        if (_cost != null)
            return _cost;
        int w = Columns, h = Rows, n = w * h;
        var depth = new int[n];
        const int Far = 1 << 20;
        for (int i = 0; i < n; i++)
            depth[i] = _road[i] ? Far : 0;
        // Two passes: cells to the edge (a road cell beside a non-road one is 1).
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (depth[i] == 0)
                    continue;
                int d = Far;
                if (x > 0) d = Math.Min(d, depth[i - 1] + 1); else d = 1;
                if (y > 0) d = Math.Min(d, depth[i - w] + 1); else d = 1;
                depth[i] = d;
            }
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                if (depth[i] == 0)
                    continue;
                int d = depth[i];
                if (x < w - 1) d = Math.Min(d, depth[i + 1] + 1); else d = 1;
                if (y < h - 1) d = Math.Min(d, depth[i + w] + 1); else d = 1;
                depth[i] = d;
            }
        var cost = new float[n];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (depth[i] == 0)
                    continue;
                // The deepest across the road here (within 2 cells): the middle line.
                int most = depth[i];
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h)
                            most = Math.Max(most, depth[ny * w + nx]);
                    }
                cost[i] = MiddleCost + EdgeStep * (most - depth[i]);
            }
        return _cost = cost;
    }

    /// <summary>What a step at (x, y) costs on the road (0: not on the road).</summary>
    public float CostAt(double x, double y)
    {
        int c = (int)Math.Floor((x - StartX) / Cell), r = (int)Math.Floor((y - StartY) / Cell);
        return c >= 0 && r >= 0 && c < Columns && r < Rows ? Costs()[r * Columns + c] : 0;
    }

    /// <summary>The cheapest road step within a square of <paramref name="size"/> around (x, y) (0: no road in it) — a
    /// coarse cell of a long way holds a narrow road even when its middle point misses it.</summary>
    public float CostIn(double x, double y, double size)
    {
        if (size <= Cell * 1.5)
            return CostAt(x, y);
        var costs = Costs();
        int c0 = (int)Math.Floor((x - size / 2 - StartX) / Cell), c1 = (int)Math.Floor((x + size / 2 - StartX) / Cell);
        int r0 = (int)Math.Floor((y - size / 2 - StartY) / Cell), r1 = (int)Math.Floor((y + size / 2 - StartY) / Cell);
        float best = 0;
        for (int r = Math.Max(0, r0); r <= Math.Min(Rows - 1, r1); r++)
            for (int c = Math.Max(0, c0); c <= Math.Min(Columns - 1, c1); c++)
                if (costs[r * Columns + c] is > 0 and var v && (best == 0 || v < best))
                    best = v;
        return best;
    }


    /// <summary>
    /// The roads read off the M map as it is drawn in <paramref name="frame"/> at <paramref name="drawn"/> (client
    /// pixels). The picture covers the world from -offset to texture / scale - offset on each side.
    /// </summary>
    public static RoadMap? FromPicture(string map, Frame frame, Rectangle drawn, double offsetX, double offsetY, double scaleX, double scaleY, double texture, bool biggest = false)
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
        var result = new RoadMap(map, startX, startY, columns, rows, road, drawn.Width, biggest);
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
        // The windows grow with the picture (set on ~300-px pictures: a road drawn wider would be its own surroundings).
        double size = Math.Max(1, w / 307.0);
        var groundAround = Box(ground, w, h, (int)Math.Round(7 * size));
        var lightAround = Box(light, w, h, (int)Math.Round(4 * size));
        var road = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var (r, g, b) = pixels[i];
            road[i] = light[i] - lightAround[i] > 7 && groundAround[i] > 0.35 && ground[i] == 0 && b <= r + 12;
        }
        // Bridges: brown planks over the water (r > g > b; seen on c2 ~ 85 72 52 between water ~ 57 77 99) — the road
        // rule misses them (darker than the road, water around), and the roads of the two banks stayed apart.
        var water = new double[n];
        for (int i = 0; i < n; i++)
            water[i] = pixels[i].B > pixels[i].R + 20 ? 1 : 0;
        var waterAround = Box(water, w, h, (int)Math.Round(3 * size));
        for (int i = 0; i < n; i++)
        {
            var (r, g, b) = pixels[i];
            if (r > g + 5 && g > b + 5 && r - b >= 18 && r is >= 55 and <= 130 && waterAround[i] > 0.12)
                road[i] = true;
        }
        // Specks dropped: a road is a long line, many pixels joined.
        var label = new int[n];
        var stack = new Stack<int>();
        int parts = 0;
        for (int i = 0; i < n; i++)
        {
            if (!road[i] || label[i] != 0)
                continue;
            var part = new List<int>();
            stack.Push(i);
            label[i] = ++parts;
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
                            label[k] = parts;
                            stack.Push(k);
                        }
                    }
            }
            if (part.Count < MinRoadPixels * size)
                foreach (int j in part)
                {
                    road[j] = false;
                    label[j] = 0;
                }
        }
        JoinGaps(road, label, w, h, (int)Math.Round(GapPixels * size));
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
    /// <summary>Two roads coming this close (pixels, on a ~300-px picture: ~15 m) are joined: the picture breaks a road
    /// where it crosses a river (the bridge), meets the town's square or passes under a mark.</summary>
    internal const double GapPixels = 12;

    /// <summary>
    /// Closes the gaps of the road net: where a road line ends (its tip), it is carried on in the way it was going to
    /// the nearest road ahead within <paramref name="reach"/> that isn't already close along the roads (seen on c2:
    /// every bridge left a gap — the roads on both banks joined elsewhere — and the way cut across the fields).
    /// </summary>
    internal static void JoinGaps(bool[] road, int[] label, int w, int h, int reach)
    {
        var links = new List<(int A, int B)>();
        var done = new List<int>();
        var near = new Dictionary<int, int>();
        var queue = new Queue<int>();
        for (int i = 0; i < road.Length; i++)
        {
            if (!road[i])
                continue;
            int x = i % w, y = i / w;
            // A tip: the road pixels around lie to one side of it.
            double sx = 0, sy = 0;
            int count = 0;
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && road[ny * w + nx])
                    {
                        sx += dx;
                        sy += dy;
                        count++;
                    }
                }
            if (count < 3)
                continue;
            double ax = -sx / count, ay = -sy / count, len = Math.Sqrt(ax * ax + ay * ay);
            if (len < 1.2 || done.Any(d => Math.Abs(d % w - x) <= 4 && Math.Abs(d / w - y) <= 4))
                continue;
            done.Add(i);
            ax /= len;
            ay /= len;
            // What is close along the roads (not a gap).
            near.Clear();
            queue.Clear();
            near[i] = 0;
            queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int j = queue.Dequeue(), steps = near[j];
                if (steps >= reach * 3)
                    continue;
                int jx = j % w, jy = j / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = jx + dx, ny = jy + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        int k = ny * w + nx;
                        if (road[k] && near.TryAdd(k, steps + 1))
                            queue.Enqueue(k);
                    }
            }
            int best = -1, bestD = int.MaxValue;
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int nx = x + dx, ny = y + dy, d = dx * dx + dy * dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || d > reach * reach || d >= bestD || d < 4)
                        continue;
                    int k = ny * w + nx;
                    // Ahead (within ~35° of the way the road was going) and not reached along the roads.
                    if (!road[k] || near.ContainsKey(k) || (dx * ax + dy * ay) < 0.8 * Math.Sqrt(d))
                        continue;
                    best = k;
                    bestD = d;
                }
            if (best >= 0)
                links.Add((i, best));
        }
        foreach (var (a, b) in links)
        {
            int ax = a % w, ay = a / w, bx = b % w, by = b / w;
            int steps = Math.Max(1, Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)));
            for (int t = 0; t <= steps; t++)
                road[(ay + (int)Math.Round((by - ay) * (double)t / steps)) * w + ax + (int)Math.Round((bx - ax) * (double)t / steps)] = true;
        }
    }

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
        public int Picture { get; set; }
        public bool Biggest { get; set; }
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
            Map = Map, StartX = StartX, StartY = StartY, Columns = Columns, Rows = Rows, Picture = Picture, Biggest = Biggest, Bits = Convert.ToBase64String(bytes),
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
            var roads = new RoadMap(map, s.StartX, s.StartY, s.Columns, s.Rows, bits, s.Picture, s.Biggest);
            roads.RoadCells = roads.Count();
            return roads;
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or UnauthorizedAccessException) { }
        return null;
    }
}
