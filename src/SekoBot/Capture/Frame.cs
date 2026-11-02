using System.Buffers;

namespace SekoBot.Capture;

/// <summary>
/// One BGRA frame of the game's client area. Buffers come from a shared pool,
/// so a frame must be disposed exactly once by whoever owns it last.
/// </summary>
internal sealed class Frame : IDisposable
{
    private byte[]? _data;
    private readonly bool _pooled;

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public long Sequence { get; init; }

    public byte[] Data => _data ?? throw new ObjectDisposedException(nameof(Frame));

    private Frame(int width, int height, byte[] data, bool pooled)
    {
        _pooled = pooled;
        Width = width;
        Height = height;
        Stride = width * 4;
        _data = data;
    }

    public static Frame Rent(int width, int height, long sequence) =>
        new(width, height, ArrayPool<byte>.Shared.Rent(width * height * 4), true) { Sequence = sequence };

    /// <summary>Test helper / small frames.</summary>
    public static Frame FromArray(int width, int height, byte[] data, long sequence = 0) =>
        new(width, height, data, false) { Sequence = sequence };

    /// <summary>Independent copy (own buffer) that outlives this frame.</summary>
    public Frame Clone()
    {
        var copy = Rent(Width, Height, Sequence);
        Buffer.BlockCopy(Data, 0, copy.Data, 0, Stride * Height);
        return copy;
    }

    public (byte B, byte G, byte R) Pixel(int x, int y)
    {
        int i = y * Stride + x * 4;
        var d = Data;
        return (d[i], d[i + 1], d[i + 2]);
    }

    public void Dispose()
    {
        var data = Interlocked.Exchange(ref _data, null);
        if (data != null && _pooled)
            ArrayPool<byte>.Shared.Return(data);
    }
}
