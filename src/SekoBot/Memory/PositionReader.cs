using SekoBot.Config;

namespace SekoBot.Memory;

/// <summary>Reads the character's position from memory and converts it to big-map coordinates.</summary>
internal sealed class PositionReader : IDisposable
{
    private readonly IMemorySource _memory;
    private readonly IDisposable? _owned;
    private readonly PositionSpec _spec;
    private readonly long _moduleBase;
    private readonly bool _absolute;

    internal PositionReader(IMemorySource memory, PositionSpec spec, long moduleBase, bool absolute, IDisposable? owned = null)
    {
        _memory = memory;
        _spec = spec;
        _moduleBase = moduleBase;
        _absolute = absolute;
        _owned = owned;
    }

    public bool Lasting => !_absolute;

    /// <returns>A reader, or null with a status text for the user.</returns>
    public static PositionReader? Create(uint processId, PositionSpec? spec, out string status)
    {
        if (spec == null || spec.Ax == 0 || spec.Ay == 0)
        {
            status = "Koordinat bilinmiyor (haritaya bakılıyor)";
            return null;
        }
        bool sameSession = spec.XAddress != 0 && spec.ProcessId == processId &&
                           spec.ProcessStartTicks != 0 && spec.ProcessStartTicks == ProcessMemory.StartTicksOf(processId);
        if (!sameSession && spec.Node < 0)
        {
            status = "Oyun yeniden açıldı: 'Koordinat bul' ile tekrar bul";
            return null;
        }
        var memory = ProcessMemory.Open(processId, out string error);
        if (memory == null)
        {
            status = "Koordinat okunamıyor: " + error;
            return null;
        }
        var reader = new PositionReader(memory, spec, memory.ModuleBase, absolute: spec.Node < 0, owned: memory);
        if (reader.Read() == null && spec.Node >= 0 && sameSession)
        {
            // The lasting path doesn't work (yet); the plain addresses of this session still may.
            reader.Dispose();
            memory = ProcessMemory.Open(processId, out error);
            if (memory == null)
            {
                status = "Koordinat okunamıyor: " + error;
                return null;
            }
            reader = new PositionReader(memory, spec, memory.ModuleBase, absolute: true, owned: memory);
        }
        status = reader.Lasting ? "Koordinat: bellekten ✓" : "Koordinat: bellekten ✓ (bu oyun oturumu için)";
        return reader;
    }

    /// <summary>Where the two position values are right now.</summary>
    private bool Addresses(out long x, out long y)
    {
        if (_absolute)
        {
            x = _spec.XAddress;
            y = _spec.YAddress;
            return true;
        }
        x = y = 0;
        if (PointerSearch.HpNodes(_memory, _moduleBase) is not { } nodes ||
            PointerSearch.Resolve(_memory, nodes, _spec.Node, _spec.Offsets) is not long baseAddress)
            return false;
        x = baseAddress + _spec.XOffset;
        y = baseAddress + _spec.YOffset;
        return true;
    }

    /// <summary>Value size (4 = float, 8 = double).</summary>
    public int Size => _spec.Size;

    /// <summary>The raw world position: the value at the lower address first.</summary>
    public bool TryReadRaw(out long lowAddress, out double first, out double second)
    {
        first = second = 0;
        lowAddress = 0;
        if (!Addresses(out long x, out long y))
            return false;
        lowAddress = Math.Min(x, y);
        return _memory.TryReadNumber(lowAddress, _spec.Size, out first) &&
               _memory.TryReadNumber(lowAddress + _spec.Size, _spec.Size, out second);
    }

    /// <summary>Position on the big map (0..1 of its width), or null when it can't be read.</summary>
    public PointF? Read()
    {
        if (!Addresses(out long x, out long y))
            return null;
        if (!_memory.TryReadNumber(x, _spec.Size, out double vx) || !_memory.TryReadNumber(y, _spec.Size, out double vy) ||
            !CoordinateSearch.Plausible(vx) || !CoordinateSearch.Plausible(vy))
            return null;
        double mx = _spec.Ax * vx + _spec.Bx, my = _spec.Ay * vy + _spec.By;
        // Far off the map: not our position (wrong address, loading screen).
        if (!double.IsFinite(mx) || !double.IsFinite(my) || mx < -0.5 || mx > 1.5 || my < -0.5 || my > 2)
            return null;
        return new PointF((float)mx, (float)my);
    }

    /// <summary>World units per map width (to turn "a few % of the map" into world units).</summary>
    public double WorldPerMap => 1 / Math.Max(1e-12, (Math.Abs(_spec.Ax) + Math.Abs(_spec.Ay)) / 2);

    public void Dispose() => _owned?.Dispose();
}
