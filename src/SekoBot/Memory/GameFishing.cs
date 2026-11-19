using System.Drawing;

namespace SekoBot.Memory;

/// <summary>
/// Fishing, as the game shows it (found 2026-11-19 by watching the user fish with KAiZER06):
/// <list type="bullet">
/// <item>the character's animation (playerMove.anim, Warrior_C/Ninja_C/…): <c>status</c> "fish" while the rod
/// is in hand, <c>fishing</c> 1 from the cast until the line is back (pulled, or after ~20 s untouched);</item>
/// <item>a bite: the name tag over the head (playerMove.mename → actorName_C) shows the fish emoji,
/// <c>showEmoji</c> = 5, for about 2.5 s; Space then pulls;</item>
/// <item>a catch: the fishing mini game (m2data_C.fishBox, fishOnay_C): four pictures of the catch, turned by
/// <c>datam</c> degrees (0/90/180/270, in the order of <c>images</c>); the upright one (0) lands the fish, a wrong
/// one or too late lets it go. getFishAction then 2 = caught, 1 = got away.</item>
/// </list>
/// </summary>
/// <param name="FloatOut">The float is still drawn (anim._roderFishing): after the line is back it stays ~3 s
/// while the reel-in plays; a cast then goes nowhere (seen 2026-11-19).</param>
/// <param name="Upright">Where the upright picture of the mini game is drawn (client pixels), while it shows.</param>
internal sealed record FishingState(bool RodInHand, bool LineOut, bool Bite, bool Asking, int FishId, bool FloatOut = false,
    Rectangle? Upright = null, int Result = 0);

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
            bool floatOut = MemoryReads.IsPointer(Pointer(anim + Offset(animClass, "Warrior_C", "_roderFishing")));
            int result = Int32(anim + Offset(animClass, "Warrior_C", "getFishAction")) ?? 0;

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
            Rectangle? upright = null;
            long box = Pointer(instance + Offset(instanceClass, "m2data_C", "fishBox"));
            if (MemoryReads.IsPointer(box) && ClassName(Pointer(box + ObjClass)) == "fishOnay_C")
            {
                asking = true;
                long boxClass = Pointer(box + ObjClass);
                fishId = Int32(box + Offset(boxClass, "fishOnay_C", "fishId")) ?? 0;
                long turns = box + Offset(boxClass, "fishOnay_C", "datam");
                long pictures = box + Offset(boxClass, "fishOnay_C", "images");
                int n = Int32(turns + 8) ?? 0;
                if (n is > 0 and <= 8 && Int32(pictures + 8) == n)
                    for (int i = 0; i < n; i++)
                        if (Int32(Pointer(turns) + i * 4) == 0 && Pointer(Pointer(pictures) + i * 8) is long picture && MemoryReads.IsPointer(picture))
                        {
                            upright = DrawnAt(picture);
                            break;
                        }
            }
            return new FishingState(rod, line, bite, asking, fishId, floatOut, upright, result);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException) { }
        return null;
    }
}
