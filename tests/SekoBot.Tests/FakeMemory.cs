using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Memory made of byte arrays, for testing memory readers without a game.</summary>
internal sealed class FakeMemory : IMemorySource
{
    public readonly List<(long Base, byte[] Data)> Blocks = [];

    public byte[] Add(long baseAddress, int size)
    {
        var data = new byte[size];
        Blocks.Add((baseAddress, data));
        return data;
    }

    public IReadOnlyList<MemoryRegion> Regions() => Blocks.Select(b => new MemoryRegion(b.Base, b.Data.Length)).ToList();

    public bool Read(long address, byte[] buffer, int count)
    {
        foreach (var (start, data) in Blocks)
        {
            if (address >= start && address + count <= start + data.Length)
            {
                Array.Copy(data, address - start, buffer, 0, count);
                return true;
            }
        }
        return false;
    }

    public void WriteFloat(long address, float value) => Write(address, BitConverter.GetBytes(value));
    public void WriteDouble(long address, double value) => Write(address, BitConverter.GetBytes(value));
    public void WriteLong(long address, long value) => Write(address, BitConverter.GetBytes(value));

    public void Write(long address, byte value) => Write(address, new[] { value });

    public void Write(long address, byte[] bytes)
    {
        foreach (var (start, data) in Blocks)
            if (address >= start && address + bytes.Length <= start + data.Length)
            {
                Array.Copy(bytes, 0, data, address - start, bytes.Length);
                return;
            }
        throw new ArgumentOutOfRangeException(nameof(address));
    }
}
