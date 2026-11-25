namespace SekoBot.Memory;

/// <summary>
/// The character's class, from its animation object (playerMove.anim: Warrior_C, Ninja_C, Sura_C, Shaman_C…):
/// only the weapons and armour it can wear are picked up (the user: no daggers, fans or bells for a warrior).
/// </summary>
internal sealed partial class GameWorldReader
{
    /// <summary>The character's own kingdom ("ırk": 1, 2, 3); null when not readable.</summary>
    public int? ReadOwnEmpire()
    {
        try
        {
            if (!_located)
                return null;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return null;
            long loader = Pointer(instance + Offset(instanceClass, "m2data_C", "loader"));
            if (!MemoryReads.IsPointer(loader))
                return null;
            int at = Offset(Pointer(loader + ObjClass), "loader", "charData.empire");
            return at > 0 && Int32(loader + at) is int empire and >= 0 and < 10 ? empire : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }

    /// <summary>What the game holds about this computer and its connection (gameManager.Mac, .ipLocGet; the server).</summary>
    internal sealed record Machine(string Mac, string LocationIp, string Server, string ServerIp, string Version, string Language);

    /// <summary>The game's own view of this computer (read only); null when not readable.</summary>
    public Machine? ReadMachine()
    {
        try
        {
            if (!_located)
                return null;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return null;
            string Text(long obj, long cls, string className, string field, int max)
            {
                int at = Offset(cls, className, field);
                return at > 0 ? ReadFString(obj + at, max).Trim() : "";
            }
            long gm = Pointer(instance + Offset(instanceClass, "m2data_C", "gm"));
            long gmClass = MemoryReads.IsPointer(gm) ? Pointer(gm + ObjClass) : 0;
            string mac = gmClass != 0 ? Text(gm, gmClass, "gameManager", "Mac", 40) : "";
            string ip = gmClass != 0 ? Text(gm, gmClass, "gameManager", "ipLocGet", 64) : "";
            return new Machine(mac, ip,
                Text(instance, instanceClass, "m2data_C", "serverName", 32),
                Text(instance, instanceClass, "m2data_C", "selectedServer.ip", 64),
                Text(instance, instanceClass, "m2data_C", "serverSettings.Version", 32),
                Text(instance, instanceClass, "m2data_C", "lang2", 16));
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }

    /// <summary>"warrior", "ninja", "sura", "shaman" (or the class name as read); null when not readable.</summary>
    public string? ReadCharacterClass()
    {
        try
        {
            if (!_located)
                return null;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return null;
            long gm = Pointer(instance + Offset(instanceClass, "m2data_C", "gm"));
            if (!MemoryReads.IsPointer(gm))
                return null;
            long pawn = Pointer(gm + Offset(Pointer(gm + ObjClass), "gameManager", "Player"));
            if (!MemoryReads.IsPointer(pawn))
                return null;
            long anim = Pointer(pawn + Offset(Pointer(pawn + ObjClass), "playerMove", "anim"));
            if (!MemoryReads.IsPointer(anim))
                return null;
            return Loot.ClassOf(ClassName(Pointer(anim + ObjClass)));
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }
}
