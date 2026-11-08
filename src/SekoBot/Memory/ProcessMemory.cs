using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SekoBot.Memory;

/// <summary>A block of the game's memory that can be read.</summary>
internal readonly record struct MemoryRegion(long Base, long Size);

/// <summary>Read-only view of a process' memory (the real game, or a fake one in tests).</summary>
internal interface IMemorySource
{
    /// <summary>The writable data areas, where game objects live.</summary>
    IReadOnlyList<MemoryRegion> Regions();

    /// <summary>Reads <paramref name="count"/> bytes; false when (part of) the range can't be read.</summary>
    bool Read(long address, byte[] buffer, int count);
}

internal static class MemoryReads
{
    private static readonly ThreadLocal<byte[]> Scratch = new(() => new byte[8]);

    public static bool TryReadInt64(this IMemorySource memory, long address, out long value)
    {
        var bytes = Scratch.Value!;
        value = 0;
        if (!memory.Read(address, bytes, 8))
            return false;
        value = BitConverter.ToInt64(bytes, 0);
        return true;
    }

    /// <summary>A float (size 4) or double (size 8).</summary>
    public static bool TryReadNumber(this IMemorySource memory, long address, int size, out double value)
    {
        var bytes = Scratch.Value!;
        value = 0;
        if (!memory.Read(address, bytes, size))
            return false;
        value = size == 8 ? BitConverter.ToDouble(bytes, 0) : BitConverter.ToSingle(bytes, 0);
        return double.IsFinite(value);
    }

    /// <summary>Looks like a user-space pointer of a 64-bit process.</summary>
    public static bool IsPointer(long value) => value >= 0x10000 && value < 0x7FFF_FFFF_FFFF && (value & 7) == 0;
}

/// <summary>Read-only access to the game process. Never writes or injects anything.</summary>
internal sealed class ProcessMemory : IMemorySource, IDisposable
{
    internal const string GameModule = "mobilemmo-Win64-Shipping.exe";
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint MemPrivate = 0x20000;
    private const uint PageReadWrite = 0x04;
    private const uint PageGuard = 0x100;

    private nint _handle;

    private ProcessMemory(nint handle, long moduleBase, long moduleSize, long startTicks)
    {
        _handle = handle;
        ModuleBase = moduleBase;
        ModuleSize = moduleSize;
        StartTicks = startTicks;
    }

    /// <summary>Where the game's exe is loaded.</summary>
    public long ModuleBase { get; }

    /// <summary>Size of the game's exe in memory.</summary>
    public long ModuleSize { get; }

    /// <summary>When the game process started (UTC ticks), to tell one game session from the next.</summary>
    public long StartTicks { get; }

    public static ProcessMemory? Open(uint processId, out string error)
    {
        error = "";
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var module = process.MainModule;
            if (module == null || !string.Equals(module.ModuleName, GameModule, StringComparison.OrdinalIgnoreCase))
            {
                error = "oyun işlemi tanınmadı";
                return null;
            }
            long start = 0;
            try { start = process.StartTime.ToUniversalTime().Ticks; } catch { }
            nint handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, processId);
            if (handle == 0)
            {
                error = "bellek erişimi reddedildi (SekoBot'u yönetici olarak aç)";
                return null;
            }
            return new ProcessMemory(handle, module.BaseAddress.ToInt64(), module.ModuleMemorySize, start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Start time of a running process (UTC ticks), 0 when unknown.</summary>
    public static long StartTicksOf(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch { return 0; }
    }

    public IReadOnlyList<MemoryRegion> Regions()
    {
        var regions = new List<MemoryRegion>();
        long address = 0x10000;
        int size = Marshal.SizeOf<MemoryBasicInformation>();
        while (address < 0x7FFF_FFFF_FFFF)
        {
            if (VirtualQueryEx(_handle, (nint)address, out var info, (nuint)size) == 0)
                break;
            long regionSize = (long)info.RegionSize;
            if (regionSize <= 0)
                break;
            if (info.State == MemCommit && info.Type == MemPrivate &&
                (info.Protect & 0xFF) == PageReadWrite && (info.Protect & PageGuard) == 0 &&
                regionSize <= int.MaxValue)
                regions.Add(new MemoryRegion(info.BaseAddress, regionSize));
            address = info.BaseAddress + regionSize;
        }
        return regions;
    }

    public bool Read(long address, byte[] buffer, int count) =>
        _handle != 0 && ReadProcessMemory(_handle, (nint)address, buffer, (nuint)count, out nuint read) && read == (nuint)count;

    public void Dispose()
    {
        if (_handle != 0)
        {
            CloseHandle(_handle);
            _handle = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public long BaseAddress;
        public long AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQueryEx(nint process, nint address, out MemoryBasicInformation info, nuint length);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
