using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SekoBot.Memory;

/// <summary>
/// Read-only character statistics for the user's Mobile2 client. Values and offsets
/// are from the tested Hp_Mp.CT table. No injection, patching, or memory writes.
/// </summary>
internal sealed class CharacterStatsReader : IDisposable
{
    internal const string GameModule = "mobilemmo-Win64-Shipping.exe";
    internal const long HpBaseOffset = 0x04D35448;
    internal static readonly long[] HpPath = [0x18, 0x8, 0x80, 0xC8, 0x1F8, 0x398];
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;

    private readonly uint _processId;
    private readonly object _gate = new();
    private nint _handle;
    private long _moduleBase;
    private DateTime _retryAfterUtc;

    public string Status { get; private set; } = "Bellek bağlantısı bekleniyor";

    public CharacterStatsReader(uint processId) => _processId = processId;

    /// <summary>Returns false unless all four values pass plausibility checks.</summary>
    public bool TryRead(out CharacterStats stats)
    {
        // One reader at a time: Connect must not open two handles, Dispose must not close one mid-read.
        lock (_gate)
            return TryReadLocked(out stats);
    }

    private bool TryReadLocked(out CharacterStats stats)
    {
        stats = default;
        if (!Connect()) return false;

        // CE offsets are displayed in reverse order in the XML file. The last
        // offset is applied to the final pointer without dereferencing it.
        if (!TryResolveHpAddress(out long hpAddress) ||
            !TryReadInt32(hpAddress, out int hp) ||
            !TryReadInt32(hpAddress + 0x8, out int mp) ||
            !TryReadInt32(hpAddress + 0x34, out int maxHp) ||
            !TryReadInt32(hpAddress + 0x54, out int maxMp))
        {
            Status = "Bellek okuması başarısız (pointer veya erişim)";
            return false;
        }

        if (maxHp is <= 0 or > 10_000_000 || maxMp is <= 0 or > 10_000_000 ||
            hp < 0 || mp < 0 || hp > maxHp || mp > maxMp)
        {
            Status = "Bellek değerleri tutarsız, veri kullanılmıyor";
            return false;
        }

        stats = new CharacterStats(hp, mp, maxHp, maxMp);
        Status = "Bellek (HP/MP)";
        return true;
    }

    private bool Connect()
    {
        if (_handle != 0) return true;
        if (DateTime.UtcNow < _retryAfterUtc) return false;
        _retryAfterUtc = DateTime.UtcNow.AddSeconds(3);

        try
        {
            using Process process = Process.GetProcessById((int)_processId);
            ProcessModule? module = process.MainModule;
            if (module == null || !string.Equals(module.ModuleName, GameModule, StringComparison.OrdinalIgnoreCase))
            {
                Status = "Farklı oyun işlemi: bellek okuması kapalı";
                return false;
            }
            _moduleBase = module.BaseAddress.ToInt64();
            _handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, _processId);
            if (_handle == 0)
            {
                Status = "Bellek erişimi reddedildi (yönetici izni gerekebilir)";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            Status = "İşlem erişimi: " + ex.Message;
            return false;
        }
    }

    private bool TryResolveHpAddress(out long address)
    {
        address = 0;
        long cursor = _moduleBase + HpBaseOffset;
        foreach (long offset in HpPath)
        {
            if (!TryReadInt64(cursor, out long next) || next <= 0) return false;
            if (next > long.MaxValue - offset) return false;
            cursor = next + offset;
        }
        if (!TryReadInt64(cursor, out long final) || final <= 0 || final > long.MaxValue - 0x7A4)
            return false;
        address = final + 0x7A4;
        return true;
    }

    private bool TryReadInt64(long address, out long value)
    {
        value = 0;
        var bytes = new byte[8];
        if (!ReadProcessMemory(_handle, (nint)address, bytes, (nuint)8, out nuint count) || count != 8)
            return false;
        value = BitConverter.ToInt64(bytes);
        return true;
    }

    private bool TryReadInt32(long address, out int value)
    {
        value = 0;
        var bytes = new byte[4];
        if (!ReadProcessMemory(_handle, (nint)address, bytes, (nuint)4, out nuint count) || count != 4)
            return false;
        value = BitConverter.ToInt32(bytes);
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle != 0)
            {
                CloseHandle(_handle);
                _handle = 0;
            }
            _retryAfterUtc = DateTime.MaxValue; // never reconnect after dispose
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address,
        [Out] byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

internal readonly record struct CharacterStats(int Hp, int Mp, int MaxHp, int MaxMp)
{
    public double HpFraction => (double)Hp / MaxHp;
    public double MpFraction => (double)Mp / MaxMp;
}
