using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Rendered fat melted down in a cooking pot, ready to dip candles from.
///
/// It sets back into rendered fat through an ordinary Harden transition, held at
/// zero for as long as the tallow stays hot. On a burning firepit that is
/// indefinitely; once the fire dies, or the tallow is taken out of the pot, item
/// cooling (90°C a game hour) takes it below <see cref="SetsBelow"/> and the
/// transition runs.
/// </summary>
public class ItemMoltenTallow : Item
{
    /// <summary>
    /// Tallow melts at roughly 45-50°C. Dipping below that would leave lumps rather
    /// than a coat, so this is both where it sets and the coolest it can be dipped at.
    /// </summary>
    public const float SetsBelow = 50f;

    public override float GetTransitionRateMul(IWorldAccessor world, ItemSlot inSlot, EnumTransitionType transType)
    {
        float mul = base.GetTransitionRateMul(world, inSlot, transType);
        if (transType != EnumTransitionType.Harden || inSlot?.Itemstack == null) return mul;

        return Temperature(world, inSlot) >= SetsBelow ? 0f : mul;
    }

    /// <summary>
    /// How hot the tallow in <paramref name="slot"/> is, judged the way
    /// BlockEntityFirepit judges it.
    ///
    /// Cooking turns the pot into its "cooked" variant, which has no
    /// cookingContainerSlots, so for a moment InventorySmelting stops reporting the
    /// slots the tallow sits in and the firepit heats the pot instead. The cooked pot
    /// has nothing in its own contents, though, so the next transition check reverts
    /// it to the empty pot (BlockCookedContainer.UpdateAndGetTransitionStates), the
    /// slots reappear, and from then on the firepit heats the tallow directly. Each
    /// state is read the way the firepit itself reads it.
    /// </summary>
    public static float Temperature(IWorldAccessor world, ItemSlot slot)
    {
        if (slot.Inventory is InventorySmelting firepit && firepit.CookingSlots.Length == 0 && firepit[1].Itemstack is ItemStack pot)
        {
            return pot.Collectible.GetTemperature(world, pot);
        }
        return slot.Itemstack.Collectible.GetTemperature(world, slot.Itemstack);
    }

    /// <summary>
    /// The firepit slot holding molten tallow, or null. Searches every cooking slot,
    /// visible or not - see <see cref="Temperature"/> for why freshly cooked tallow
    /// briefly sits in slots the firepit does not report.
    /// </summary>
    public static ItemSlot FindIn(BlockEntityFirepit firepit)
    {
        if (firepit.Inventory is not InventorySmelting inventory) return null;

        foreach (ItemSlot slot in inventory.Slots)
        {
            if (slot.Itemstack?.Collectible is ItemMoltenTallow) return slot;
        }
        return null;
    }
}
