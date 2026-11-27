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

    /// <summary>The road cell nearest to (x, y), within <paramref name="reach"/> world units; null: none.</summary>
    internal (int C, int R)? NearestCell(double x, double y, double reach)
    {
        int c0 = (int)Math.Floor((x - StartX) / Cell), r0 = (int)Math.Floor((y - StartY) / Cell);
        int most = (int)Math.Ceiling(reach / Cell);
        for (int ring = 0; ring <= most; ring++)
        {
            (int C, int R)? best = null;
            double bestD = double.MaxValue;
            for (int r = r0 - ring; r <= r0 + ring; r++)
                for (int c = c0 - ring; c <= c0 + ring; c++)
                {
                    if (Math.Max(Math.Abs(r - r0), Math.Abs(c - c0)) != ring || c < 0 || r < 0 || c >= Columns || r >= Rows || !_road[r * Columns + c])
                        continue;
                    double d = (c - c0) * (c - c0) + (r - r0) * (r - r0);
                    if (d < bestD)
                        (best, bestD) = ((c, r), d);
                }
            if (best != null)
                return best;
        }
        return null;
    }

    /// <summary>A step inside one of these costs this many times more (a mob on the road: walked round on the road
    /// when there is room, never left for the fields).</summary>
    internal const float SlowCost = 12f;
    /// <summary>The corners of a way kept within this cost of the road's middle (about 2 cells off it).</summary>
    internal const float CornerCost = MiddleCost + EdgeStep * 2;

    /// <summary>
    /// The way along the roads only, from the road cell nearest to <paramref name="from"/> to the one nearest to
    /// <paramref name="to"/>: the corners (world units, at height <paramref name="z"/>), the first where the road is
    /// reached, the last where it is left. Null: no road near, or the two not joined by road. Round places in
    /// <paramref name="slow"/> (mobs, where the bot got stuck) cost more, they are never shut.
    /// </summary>
    internal List<Vec3>? WayAlong(Vec3 from, Vec3 to, double reach, IReadOnlyList<(double X, double Y, double R)>? slow = null)
    {
        if (NearestCell(from.X, from.Y, reach) is not var (sc, sr) || NearestCell(to.X, to.Y, reach) is not var (gc, gr))
            return null;
        var costs = Costs();
        int w = Columns, h = Rows, start = sr * w + sc, goal = gr * w + gc;
        double X(int i) => StartX + (i % w + 0.5) * Cell;
        double Y(int i) => StartY + (i / w + 0.5) * Cell;
        float Slow(int i)
        {
            if (slow == null)
                return 1;
            double x = X(i), y = Y(i);
            foreach (var s in slow)
                if ((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) < s.R * s.R)
                    return SlowCost;
            return 1;
        }
        var cost = new float[w * h];
        Array.Fill(cost, float.MaxValue);
        var came = new int[w * h];
        var open = new PriorityQueue<int, float>();
        float Guess(int i) => (float)Math.Sqrt((i % w - gc) * (i % w - gc) + (i / w - gr) * (i / w - gr)) * MiddleCost;
        cost[start] = 0;
        open.Enqueue(start, Guess(start));
        bool found = false;
        while (open.TryDequeue(out int i, out float pri))
        {
            if (i == goal)
            {
                found = true;
                break;
            }
            if (pri - Guess(i) > cost[i] + 0.01f)
                continue;
            int x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int n = ny * w + nx;
                    if (!_road[n])
                        continue;
                    float c = cost[i] + (dx != 0 && dy != 0 ? 1.4142f : 1f) * costs[n] * Slow(n);
                    if (c < cost[n])
                    {
                        cost[n] = c;
                        came[n] = i;
                        open.Enqueue(n, c + Guess(n));
                    }
                }
        }
        if (!found)
            return null;
        var cells = new List<int>();
        for (int i = goal; i != start; i = came[i])
            cells.Add(i);
        cells.Add(start);
        cells.Reverse();
        // Only the corners: on to the furthest cell in a straight line kept near the road's middle (and off the slow places).
        bool Straight(int a, int b)
        {
            double ax = X(cells[a]), ay = Y(cells[a]), bx = X(cells[b]), by = Y(cells[b]);
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) / (Cell * 0.5)));
            for (int s = 1; s < steps; s++)
            {
                double t = (double)s / steps, px = ax + (bx - ax) * t, py = ay + (by - ay) * t;
                int c = (int)Math.Floor((px - StartX) / Cell), r = (int)Math.Floor((py - StartY) / Cell);
                if (c < 0 || r < 0 || c >= w || r >= h || !_road[r * w + c])
                    return false;
                float here = costs[r * w + c], onWay = costs[cells[Math.Clamp(a + (int)Math.Round(t * (b - a)), a, b)]];
                if (here > Math.Max(CornerCost, onWay) + 0.01f || Slow(r * w + c) > 1 && Slow(cells[Math.Clamp(a + (int)Math.Round(t * (b - a)), a, b)]) == 1)
                    return false;
            }
            return true;
        }
        var way = new List<Vec3> { new(X(start), Y(start), to.Z) };
        int k = 0;
        while (k < cells.Count - 1)
        {
            int far = k + 1;
            while (far + 1 < cells.Count && Straight(k, far + 1))
                far++;
            way.Add(new Vec3(X(cells[far]), Y(cells[far]), to.Z));
            k = far;
        }
        return way;
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
        // The planks over the water first, then every plank joined to them (a bridge's ends lie on the banks, no water
        // around: seen on c2, the north end left 14 px short of the road and the whole bridge cut off as a dead end).
        bool Plank(int i) { var (r, g, b) = pixels[i]; return r > g + 5 && g > b + 5 && r - b >= 18 && r is >= 55 and <= 130; }
        var bridge = new bool[n];
        var planks = new Stack<int>();
        for (int i = 0; i < n; i++)
            if (Plank(i) && waterAround[i] > 0.12)
            {
                bridge[i] = true;
                planks.Push(i);
            }
        while (planks.Count > 0)
        {
            int j = planks.Pop(), x = j % w, y = j / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int k = ny * w + nx;
                    if (!bridge[k] && Plank(k))
                    {
                        bridge[k] = true;
                        planks.Push(k);
                    }
                }
        }
        for (int i = 0; i < n; i++)
            if (bridge[i])
                road[i] = true;
        // Town squares: a round grey open place (the user: the town's square wasn't road; the roads into it were cut off
        // as dead ends, and with them the bridge beside the town).
        var plazas = Plazas(pixels, w, h, size);
        for (int i = 0; i < n; i++)
            if (plazas[i])
                road[i] = true;
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
        // Down to the middle lines, the dead ends cut off (the user: roads leading nowhere are no place to walk), the
        // pieces apart from the net dropped; then as wide as a road again.
        // Widened first: pieces a pixel or two apart (a bridge's planks and the road) join before thinning.
        var joined = new bool[n];
        for (int i = 0; i < n; i++)
            if (road[i])
            {
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h)
                            joined[ny * w + nx] = true;
                    }
            }
        var middle = Thin(joined, w, h);
        PruneDeadEnds(middle, w, h, (int)Math.Round(DeadEndPixels * size));
        DropSmallPieces(middle, w, h, (int)Math.Round(MinNetPixels * size));
        road = new bool[n];
        for (int i = 0; i < n; i++)
            if (middle[i])
            {
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h)
                            road[ny * w + nx] = true;
                    }
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
        // The squares whole (the thinning left only their middle): open ground to walk across.
        for (int i = 0; i < n; i++)
            if (plazas[i])
                wide[i] = true;
        return wide;
    }

    /// <summary>
    /// The town squares of a map picture: a grey, even, roughly round open place (c2: ~33 × 30 px on a 307-px picture,
    /// 88 88 93), filled whole. The grey road lines have the same colour but are long and thin (little of their box filled).
    /// </summary>
    internal static bool[] Plazas((byte R, byte G, byte B)[] pixels, int w, int h, double size)
    {
        int n = w * h;
        var grey = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var (r, g, b) = pixels[i];
            int avg = (r + g + b) / 3;
            grey[i] = Math.Abs(g - r) <= 6 && b - r is >= -4 and <= 10 && avg is >= 80 and <= 115;
        }
        var result = new bool[n];
        var seen = new bool[n];
        var stack = new Stack<int>();
        for (int i = 0; i < n; i++)
        {
            if (!grey[i] || seen[i])
                continue;
            var part = new List<int>();
            seen[i] = true;
            stack.Push(i);
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
                        if (grey[k] && !seen[k])
                        {
                            seen[k] = true;
                            stack.Push(k);
                        }
                    }
            }
            int x0 = part.Min(j => j % w), x1 = part.Max(j => j % w), y0 = part.Min(j => j / w), y1 = part.Max(j => j / w);
            int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
            double fill = (double)part.Count / (bw * bh);
            if (part.Count < PlazaMinPixels * size * size || bw < 18 * size || bh < 18 * size || bw > 70 * size || bh > 70 * size
                || fill < 0.22 || Math.Max(bw, bh) > 1.8 * Math.Min(bw, bh))
                continue;
            // The whole round place (the stalls and people standing on it are holes in its colour).
            double cx = (x0 + x1) / 2.0, cy = (y0 + y1) / 2.0, rx = bw / 2.0, ry = bh / 2.0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double ex = (x - cx) / rx, ey = (y - cy) / ry;
                    if (ex * ex + ey * ey <= 1)
                        result[y * w + x] = true;
                }
        }
        return result;
    }

    internal const int PlazaMinPixels = 150;

    // ── Mountains: never walked into (the user: the rocks drawn on the map are no place to go) ──

    private BitArray? _mountain;
    /// <summary>Whether this map's mountains are known (read off its saved M map picture).</summary>
    public bool HasMountains => _mountain != null;

    public bool Mountain(double x, double y)
    {
        int c = (int)Math.Floor((x - StartX) / Cell), r = (int)Math.Floor((y - StartY) / Cell);
        return _mountain != null && c >= 0 && r >= 0 && c < Columns && r < Rows && _mountain[r * Columns + c];
    }

    /// <summary>How much of a square of <paramref name="size"/> around (x, y) is mountain (0..1).</summary>
    public double MountainShareIn(double x, double y, double size)
    {
        if (_mountain == null)
            return 0;
        if (size <= Cell * 1.5)
            return Mountain(x, y) ? 1 : 0;
        int c0 = (int)Math.Floor((x - size / 2 - StartX) / Cell), c1 = (int)Math.Floor((x + size / 2 - StartX) / Cell);
        int r0 = (int)Math.Floor((y - size / 2 - StartY) / Cell), r1 = (int)Math.Floor((y + size / 2 - StartY) / Cell);
        int all = 0, hits = 0;
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                all++;
                if (c >= 0 && r >= 0 && c < Columns && r < Rows && _mountain[r * Columns + c])
                    hits++;
            }
        return all > 0 ? (double)hits / all : 0;
    }

    /// <summary>The mountains of a map picture: the grey-violet rock (c2: ~95 95 118; red and green alike, blue ~20
    /// above), in wide patches only, a pixel in from their edges (their foot can be walked).</summary>
    internal static bool[] MountainPixels((byte R, byte G, byte B)[] pixels, int w, int h)
    {
        int n = w * h;
        var rock = new double[n];
        for (int i = 0; i < n; i++)
        {
            var (r, g, b) = pixels[i];
            int avg = (r + g + b) / 3;
            rock[i] = Math.Abs(g - r) <= 7 && b - r is >= 10 and <= 34 && avg is >= 72 and <= 160 ? 1 : 0;
        }
        double size = Math.Max(1, w / 307.0);
        var around = Box(rock, w, h, (int)Math.Round(3 * size));
        var patch = new bool[n];
        for (int i = 0; i < n; i++)
            patch[i] = around[i] > 0.45;
        var inner = new bool[n];
        int pad = Math.Max(1, (int)Math.Round(size));
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool all = patch[y * w + x];
                for (int dy = -pad; dy <= pad && all; dy++)
                    for (int dx = -pad; dx <= pad && all; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        all = nx >= 0 && ny >= 0 && nx < w && ny < h && patch[ny * w + nx];
                    }
                inner[y * w + x] = all;
            }
        return inner;
    }

    /// <summary>The picture the roads were read from (kept beside them): the mountains come from it.</summary>
    public static string PictureOf(string folder, string map) => MapTerrain.Stem(folder, map) + ".yollar.png";

    /// <summary>Reads the mountains off the map's saved picture (none when there is no picture). Roads stay walkable.</summary>
    public void ReadMountains(string folder)
    {
        string file = PictureOf(folder, Map);
        if (!File.Exists(file))
            return;
        try
        {
            using var bmp = new Bitmap(file);
            int w = bmp.Width, h = bmp.Height;
            var pixels = new (byte R, byte G, byte B)[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    pixels[y * w + x] = (c.R, c.G, c.B);
                }
            var mask = MountainPixels(pixels, w, h);
            var mountain = new BitArray(Columns * Rows);
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns; c++)
                {
                    int px = Math.Clamp((int)((c + 0.5) / Columns * w), 0, w - 1), py = Math.Clamp((int)((r + 0.5) / Rows * h), 0, h - 1);
                    if (mask[py * w + px] && !_road[r * Columns + c])
                        mountain[r * Columns + c] = true;
                }
            _mountain = mountain;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or OutOfMemoryException) { }
    }

    /// <summary>A road ending nowhere shorter than this (pixels of its middle line, on a ~300-px picture: ~45 m) is cut
    /// off: a stub into a field, the edge of a rock, a house: the way would lead into it and stop.</summary>
    internal const double DeadEndPixels = 30;
    /// <summary>A piece of road apart from the rest with a middle line shorter than this is dropped.</summary>
    internal const double MinNetPixels = 40;

    private static int Neighbours(bool[] a, int w, int h, int x, int y)
    {
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h && a[ny * w + nx])
                    count++;
            }
        return count;
    }

    /// <summary>The middle lines of the roads, one pixel wide (Zhang-Suen thinning).</summary>
    internal static bool[] Thin(bool[] img, int w, int h)
    {
        var a = (bool[])img.Clone();
        var remove = new List<int>();
        bool changed = true;
        bool P(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && a[y * w + x];
        while (changed)
        {
            changed = false;
            for (int pass = 0; pass < 2; pass++)
            {
                remove.Clear();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (!a[y * w + x])
                            continue;
                        bool p2 = P(x, y - 1), p3 = P(x + 1, y - 1), p4 = P(x + 1, y), p5 = P(x + 1, y + 1),
                             p6 = P(x, y + 1), p7 = P(x - 1, y + 1), p8 = P(x - 1, y), p9 = P(x - 1, y - 1);
                        int b = (p2 ? 1 : 0) + (p3 ? 1 : 0) + (p4 ? 1 : 0) + (p5 ? 1 : 0) + (p6 ? 1 : 0) + (p7 ? 1 : 0) + (p8 ? 1 : 0) + (p9 ? 1 : 0);
                        if (b < 2 || b > 6)
                            continue;
                        bool[] ring = [p2, p3, p4, p5, p6, p7, p8, p9, p2];
                        int t = 0;
                        for (int k = 0; k < 8; k++)
                            if (!ring[k] && ring[k + 1])
                                t++;
                        if (t != 1)
                            continue;
                        if (pass == 0 ? (p2 && p4 && p6) || (p4 && p6 && p8) : (p2 && p4 && p8) || (p2 && p6 && p8))
                            continue;
                        remove.Add(y * w + x);
                    }
                foreach (int i in remove)
                    a[i] = false;
                changed |= remove.Count > 0;
            }
        }
        return a;
    }

    /// <summary>
    /// Cuts the branches of the middle lines that end nowhere and are shorter than <paramref name="length"/>: from
    /// every end, followed back to the crossing it comes from. Done twice (a cut can leave a new short end).
    /// </summary>
    internal static void PruneDeadEnds(bool[] a, int w, int h, int length)
    {
        for (int round = 0; round < 2; round++)
        {
            var ends = new List<int>();
            for (int i = 0; i < a.Length; i++)
                if (a[i] && Neighbours(a, w, h, i % w, i / w) == 1)
                    ends.Add(i);
            foreach (int end in ends)
            {
                if (!a[end])
                    continue;
                var branch = new List<int> { end };
                var seen = new HashSet<int> { end };
                int at = end;
                bool reachedCrossing = false;
                while (branch.Count <= length)
                {
                    int x = at % w, y = at / w, next = -1, count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < w && ny < h && a[ny * w + nx] && !seen.Contains(ny * w + nx))
                            {
                                count++;
                                next = ny * w + nx;
                            }
                        }
                    if (count == 0)
                        break; // a piece on its own (dropped later when small)
                    if (count > 1 || Neighbours(a, w, h, next % w, next / w) > 2)
                    {
                        reachedCrossing = true;
                        break;
                    }
                    seen.Add(next);
                    branch.Add(next);
                    at = next;
                }
                if (reachedCrossing && branch.Count < length)
                    foreach (int i in branch)
                        a[i] = false;
            }
        }
    }

    /// <summary>Drops the pieces of middle line (joined pixels) shorter than <paramref name="least"/>.</summary>
    internal static void DropSmallPieces(bool[] a, int w, int h, int least)
    {
        var seen = new bool[a.Length];
        var stack = new Stack<int>();
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i] || seen[i])
                continue;
            var part = new List<int>();
            stack.Push(i);
            seen[i] = true;
            while (stack.Count > 0)
            {
                int j = stack.Pop();
                part.Add(j);
                int x = j % w, y = j / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && a[ny * w + nx] && !seen[ny * w + nx])
                        {
                            seen[ny * w + nx] = true;
                            stack.Push(ny * w + nx);
                        }
                    }
            }
            if (part.Count < least)
                foreach (int j in part)
                    a[j] = false;
        }
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

    /// <summary>The roads the user took off this map (haritalar\[map].yollar-silinen.json): round places (world
    /// units: x, y, radius) where no road counts, whatever the M map shows (seen on c2: a side road leading the way
    /// into a dead end the bot got stuck in).</summary>
    /// <summary>After roads were removed, pieces of road smaller than this (cells) are dropped too.</summary>
    internal const int RemovedLeftover = 150;

    public static string RemovedFileOf(string folder, string map) => MapTerrain.Stem(folder, map) + ".yollar-silinen.json";

    /// <summary>Takes the roads the user removed off this net (kept for every reading of the M map).</summary>
    public void ApplyRemoved(string folder)
    {
        string file = RemovedFileOf(folder, Map);
        if (!File.Exists(file))
            return;
        double[][]? places;
        try { places = JsonSerializer.Deserialize<double[][]>(File.ReadAllText(file)); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return; }
        foreach (var p in places ?? [])
        {
            if (p.Length < 3)
                continue;
            double x = p[0], y = p[1], r = p[2];
            int c0 = (int)Math.Floor((x - r - StartX) / Cell), c1 = (int)Math.Floor((x + r - StartX) / Cell);
            int r0 = (int)Math.Floor((y - r - StartY) / Cell), r1 = (int)Math.Floor((y + r - StartY) / Cell);
            for (int row = Math.Max(0, r0); row <= Math.Min(Rows - 1, r1); row++)
                for (int col = Math.Max(0, c0); col <= Math.Min(Columns - 1, c1); col++)
                {
                    double cx = StartX + (col + 0.5) * Cell - x, cy = StartY + (row + 0.5) * Cell - y;
                    if (cx * cx + cy * cy <= r * r)
                        _road[row * Columns + col] = false;
                }
        }
        // What is left of a removed road (its edges) is no road either: pieces of a few cells dropped.
        var seen = new BitArray(_road.Length);
        var stack = new Stack<int>();
        for (int i = 0; i < _road.Length; i++)
        {
            if (!_road[i] || seen[i])
                continue;
            var part = new List<int>();
            stack.Push(i);
            seen[i] = true;
            while (stack.Count > 0)
            {
                int j = stack.Pop();
                part.Add(j);
                int x = j % Columns, y = j / Columns;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < Columns && ny < Rows && _road[ny * Columns + nx] && !seen[ny * Columns + nx])
                        {
                            seen[ny * Columns + nx] = true;
                            stack.Push(ny * Columns + nx);
                        }
                    }
            }
            if (part.Count < RemovedLeftover)
                foreach (int j in part)
                    _road[j] = false;
        }
        _cost = null;
        RoadCells = Count();
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
            roads.ApplyRemoved(folder);
            roads.RoadCells = roads.Count();
            roads.ReadMountains(folder);
            return roads;
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or UnauthorizedAccessException) { }
        return null;
    }
}
