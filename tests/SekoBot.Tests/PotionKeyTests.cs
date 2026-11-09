using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class PotionKeyTests
{
    // As read in the game: small red potion 27001 in three stacks of 200, small blue 27204 on key 2.
    private static CharacterProgress Bag(int key1Slot) => new(0, 1, 0,
        [new InventoryItem(36, 27001, 200), new InventoryItem(42, 27001, 200), new InventoryItem(43, 27001, 200),
         new InventoryItem(20, 27003, 200), new InventoryItem(15, 27204, 115)],
        [key1Slot, 15, 0, 0, 0, -104, -103]);

    [Fact]
    public void Counts_every_stack_of_the_potion_on_the_key()
    {
        var hp = Bag(key1Slot: 42).Potion(1, null);
        Assert.Equal(new PotionKey(27001, 600, true), hp);   // not the other sizes (27003)
        Assert.Equal(600, hp.Drinkable);
        Assert.Equal(115, Bag(42).Potion(2, null).InBag);
    }

    [Fact]
    public void A_key_empty_for_a_moment_still_counts_the_bag()
    {
        // The stack on key 1 ran out; the game moves the key to the next stack (seen: 42 → 43),
        // for a moment the slot reads empty: the potion seen there is still counted.
        var hp = Bag(key1Slot: 0).Potion(1, known: 27001);
        Assert.False(hp.OnKey);
        Assert.Equal(600, hp.InBag);
        Assert.Equal(600, hp.Drinkable);
        // Never seen on the key: nothing known.
        Assert.Equal(new PotionKey(null, 0, false), Bag(0).Potion(1, null));
    }
}
