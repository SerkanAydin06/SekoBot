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

    /// <summary>Whether a pixel of the M map picture is road (brown: redder than the grass, not grey rock, not water).</summary>
    internal static bool RoadColour(byte r, byte g, byte b) => r - g >= 9 && r - b >= 20 && r >= 68;

    /// <summary>
    /// The roads read off the M map as it is drawn in <paramref name="frame"/> at <paramref name="drawn"/> (client
    /// pixels). The picture covers the world from -offset to texture / scale - offset on each side.
    /// </summary>
    public static RoadMap? FromPicture(string map, Frame frame, Rectangle drawn, double offsetX, double offsetY, double scaleX, double scaleY, double texture)
    {
        if (drawn.Width < 50 || drawn.Height < 50 || drawn.Right > frame.Width || drawn.Bottom > frame.Height || drawn.Left < 0 || drawn.Top < 0)
            return null;
        double startX = -offsetX, startY = -offsetY, endX = texture / scaleX - offsetX, endY = texture / scaleY - offsetY;
        int columns = (int)Math.Ceiling((endX - startX) / Cell), rows = (int)Math.Ceiling((endY - startY) / Cell);
        if (columns is <= 0 or > 4000 || rows is <= 0 or > 4000)
            return null;
        var road = new BitArray(columns * rows);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                double fx = (c + 0.5) / columns, fy = (r + 0.5) / rows;
                int px = drawn.Left + (int)(fx * drawn.Width), py = drawn.Top + (int)(fy * drawn.Height);
                var (b, g, rd) = frame.Pixel(Math.Clamp(px, 0, frame.Width - 1), Math.Clamp(py, 0, frame.Height - 1));
                if (RoadColour(rd, g, b))
                    road[r * columns + c] = true;
            }
        var result = new RoadMap(map, startX, startY, columns, rows, road);
        result.RoadCells = result.Count();
        return result.RoadCells > 0.02 * columns * rows ? result : null; // not a map picture (nothing road-coloured)
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
