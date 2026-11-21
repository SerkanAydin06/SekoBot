namespace SekoBot.Memory;

/// <summary>
/// A map's ground heights as the game keeps them (m2data.heiMAP: map name → its height grid, one
/// whole number per cell, row by row along Y; the grid covers heiMapStart…heiMapEnd). Seen on c3:
/// 2167 × 2167 cells of 30 world units.
/// </summary>
internal sealed record HeightGrid(string Map, double StartX, double StartY, double Cell, int Columns, int Rows, int[] Heights)
{
    /// <summary>The height at a world point (null outside the grid).</summary>
    public int? At(double x, double y)
    {
        int c = (int)Math.Floor((x - StartX) / Cell), r = (int)Math.Floor((y - StartY) / Cell);
        return c < 0 || r < 0 || c >= Columns || r >= Rows ? null : Heights[r * Columns + c];
    }
}

internal sealed partial class GameWorldReader
{
    private const int HeiEntrySize = 56, HeiPoses = 16, HeiMulti = 40;
    private const int MaxHeightCells = 20_000_000;

    /// <summary>
    /// The height grid of <paramref name="map"/> (null when the game hasn't it loaded or it can't be read).
    /// Only ever reads.
    /// </summary>
    public HeightGrid? ReadHeights(string map)
    {
        try
        {
            if (!_located || map.Length == 0)
                return null;
            long instance = Pointer(_moduleBase + _gameInstance);
            long cls = Pointer(instance + ObjClass);
            if (ClassName(cls) != "m2data_C")
                return null;
            int at = Offset(cls, "m2data_C", "heiMAP");
            if (at <= 0)
                return null;
            long set = instance + at;
            long data = Pointer(set);
            int count = Int32(set + 8) ?? 0;
            if (!MemoryReads.IsPointer(data) || count is <= 0 or > 64)
                return null;
            for (int i = 0; i < count; i++)
            {
                long entry = data + (long)i * HeiEntrySize;
                if (!ReadFString(entry, 64).Equals(map, StringComparison.OrdinalIgnoreCase))
                    continue;
                long poses = Pointer(entry + HeiPoses);
                int cells = Int32(entry + HeiPoses + 8) ?? 0, columns = Int32(entry + HeiMulti) ?? 0;
                if (!MemoryReads.IsPointer(poses) || cells is <= 0 or > MaxHeightCells || columns <= 0 || cells % columns != 0)
                    return null;
                int Field(string name) => Offset(cls, "m2data_C", name) is int o and > 0 ? Int32(instance + o) ?? 0 : 0;
                int sx = Field("heiMapStartX"), sy = Field("heiMapStartY"), ex = Field("heiMapEndX");
                double cell = ex > sx ? (double)(ex - sx) / columns : 30;
                if (cell is < 5 or > 500)
                    cell = 30;
                var heights = new int[cells];
                var bytes = new byte[1 << 20];
                for (long off = 0; off < cells * 4L; off += bytes.Length)
                {
                    int len = (int)Math.Min(bytes.Length, cells * 4L - off);
                    if (!_memory.Read(poses + off, bytes, len))
                        return null;
                    Buffer.BlockCopy(bytes, 0, heights, (int)off, len);
                }
                // The game keeps the grid by X first (one run of `multi` values along Y per X; seen on c2: read the
                // other way round every height was wrong). Turned into rows along Y here (index = y row × columns + x).
                int xs = cells / columns, ys = columns;
                var byY = new int[cells];
                for (int ix = 0; ix < xs; ix++)
                    for (int iy = 0; iy < ys; iy++)
                        byY[iy * xs + ix] = heights[ix * ys + iy];
                return new HeightGrid(map, sx, sy, cell, xs, ys, byY);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }
}
