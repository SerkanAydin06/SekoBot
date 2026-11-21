namespace SekoBot.Memory;

/// <summary>
/// The character's class, from its animation object (playerMove.anim: Warrior_C, Ninja_C, Sura_C, Shaman_C…):
/// only the weapons and armour it can wear are picked up (the user: no daggers, fans or bells for a warrior).
/// </summary>
internal sealed partial class GameWorldReader
{
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
