using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Something held that takes wax from a pot on the fire - a dipping rod, a candle
/// mould - by holding right-click on the firepit (BlockBehaviorDipVat).
/// </summary>
public interface IWaxWorker
{
    /// <summary>
    /// Why <paramref name="held"/> cannot take wax from <paramref name="firepit"/> now,
    /// as the suffix of its <c>ingameerror-</c> lang key, or null if it can - in which
    /// case <paramref name="waxSlot"/> is the pot slot to take from and
    /// <paramref name="portions"/> how many.
    /// </summary>
    string CannotWork(IWorldAccessor world, ItemStack held, BlockEntityFirepit firepit, out ItemSlot waxSlot, out int portions);

    /// <summary>What <paramref name="held"/> becomes once it has taken the wax.</summary>
    ItemStack Worked(IWorldAccessor world, ItemStack held, string wax);

    /// <summary>Whether <paramref name="held"/> takes <paramref name="wax"/> at all, for the interaction help.</summary>
    bool Takes(ItemStack held, string wax);
}
