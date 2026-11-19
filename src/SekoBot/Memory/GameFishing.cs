namespace SekoBot.Memory;

/// <summary>
/// Fishing, as the game shows it (found 2026-11-19 by watching the user fish with KAiZER06):
/// <list type="bullet">
/// <item>the character's animation (playerMove.anim, Warrior_C/Ninja_C/…): <c>status</c> "fish" while the rod
/// is in hand, <c>fishing</c> 1 from the cast until the line is back (pulled, or after ~20 s untouched);</item>
/// <item>a bite: the name tag over the head (playerMove.mename → actorName_C) shows the fish emoji,
/// <c>showEmoji</c> = 5, for about 2.5 s; Space then pulls;</item>
/// <item>a catch: the "which picture is right" window (m2data_C.fishBox, fishOnay_C) — the player answers it.</item>
/// </list>
/// </summary>
internal sealed record FishingState(bool RodInHand, bool LineOut, bool Bite, bool Asking, int FishId);

internal sealed partial class GameWorldReader
{
    /// <summary>The name tag's emoji while a fish bites.</summary>
    internal const int BiteEmoji = 5;

    /// <summary>The fishing state now; null when it can't be read.</summary>
    public FishingState? ReadFishing()
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
            long pawnClass = Pointer(pawn + ObjClass);
            long anim = Pointer(pawn + Offset(pawnClass, "playerMove", "anim"));
            if (!MemoryReads.IsPointer(anim))
                return null;
            long animClass = Pointer(anim + ObjClass);
            string status = ReadFString(anim + Offset(animClass, "Warrior_C", "status"), 32);
            bool rod = status == "fish" || Byte(anim + Offset(animClass, "Warrior_C", "haveRod")) is byte r && r != 0;
            bool line = Byte(anim + Offset(animClass, "Warrior_C", "fishing")) is byte f && f != 0;

            bool bite = false;
            long tag = Pointer(pawn + Offset(pawnClass, "playerMove", "mename"));
            if (MemoryReads.IsPointer(tag))
            {
                long widget = Pointer(tag + Offset(Pointer(tag + ObjClass), "actorName", "N"));
                if (MemoryReads.IsPointer(widget))
                    bite = Int32(widget + Offset(Pointer(widget + ObjClass), "actorName_C", "showEmoji")) == BiteEmoji;
            }

            bool asking = false;
            int fishId = 0;
            long box = Pointer(instance + Offset(instanceClass, "m2data_C", "fishBox"));
            if (MemoryReads.IsPointer(box) && ClassName(Pointer(box + ObjClass)) == "fishOnay_C")
            {
                asking = true;
                fishId = Int32(box + Offset(Pointer(box + ObjClass), "fishOnay_C", "fishId")) ?? 0;
            }
            return new FishingState(rod, line, bite, asking, fishId);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }
}
