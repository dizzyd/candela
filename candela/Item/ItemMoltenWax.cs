using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Wax melted down in a cooking pot - tallow from rendered fat, for dipping and
/// moulds, or beeswax, for moulds. Which wax, and where it sets, are the item's
/// <c>candela</c> attributes.
///
/// It sets back into what it was melted from (rendered fat, beeswax) through an
/// ordinary Harden transition, held at zero for as long as it stays hot. On a burning
/// firepit that is indefinitely (WaxPotPatch.CanHeatInput); once the fire dies, or
/// the wax is taken out of the pot, it cools at <see cref="CooldownSpeed"/> below
/// <see cref="SetsBelow"/> and the transition runs.
/// </summary>
public class ItemMoltenWax : Item
{
    /// <summary>
    /// °C a game hour it cools off the fire. Vanilla cools every item at 120, and holds
    /// it at heat for half an hour after any warming - tallow then took two game hours
    /// to set. A thin pot of wax sheds heat fast, as metal poured into a mould does
    /// (vanilla's 300), so it gets neither: from a fresh cook it sets in about half a
    /// game hour.
    /// </summary>
    public const float CooldownSpeed = 400f;

    /// <summary>"tallow" or "beeswax".</summary>
    public string Wax { get; private set; }

    /// <summary>
    /// Where it sets, and the coolest it can be worked at: below this a dip or a pour
    /// would leave lumps. Tallow melts at roughly 45-50°C, beeswax at 62-64°C.
    /// </summary>
    public float SetsBelow { get; private set; }

    /// <summary>
    /// Portions in a full pot - four slots of six servings - for drawing how full it is:
    /// 48 of tallow at two a lump of fat, 24 of beeswax at one a lump.
    /// </summary>
    public int FullPot { get; private set; }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        Wax = Attributes?["candela"]["wax"].AsString("tallow") ?? "tallow";
        SetsBelow = Attributes?["candela"]["setsBelow"].AsFloat(50f) ?? 50f;
        FullPot = Attributes?["candela"]["fullPot"].AsInt(48) ?? 48;
    }

    public override void SetTemperature(IWorldAccessor world, ItemStack itemstack, float temperature, bool delayCooldown = true)
    {
        base.SetTemperature(world, itemstack, temperature, delayCooldown: false);
        (itemstack?.Attributes["temperature"] as ITreeAttribute)?.SetFloat("cooldownSpeed", CooldownSpeed);
    }

    public override float GetTransitionRateMul(IWorldAccessor world, ItemSlot inSlot, EnumTransitionType transType)
    {
        float mul = base.GetTransitionRateMul(world, inSlot, transType);
        if (transType != EnumTransitionType.Harden || inSlot?.Itemstack == null) return mul;

        return Temperature(world, inSlot) >= SetsBelow ? 0f : mul;
    }

    /// <summary>
    /// What it sets into: the engine rounds the transition ratio at random, so an odd
    /// portion of tallow, at two to the lump, would come back a whole lump half the
    /// time - and melting it down again would make fat. Rounded down instead, as a
    /// part-burned candle comes back as the stub below it.
    /// </summary>
    public override ItemStack OnTransitionNow(ItemSlot slot, TransitionableProperties props)
    {
        ItemStack set = base.OnTransitionNow(slot, props);
        if (props.Type == EnumTransitionType.Harden) set.StackSize = (int)(slot.Itemstack.StackSize * props.TransitionRatio);
        return set;
    }

    /// <summary>Whether the wax in <paramref name="slot"/> is hot enough to work.</summary>
    public bool IsWorkable(IWorldAccessor world, ItemSlot slot) => Temperature(world, slot) >= SetsBelow;

    /// <summary>
    /// How hot the wax in <paramref name="slot"/> is, judged the way
    /// BlockEntityFirepit judges it.
    ///
    /// Cooking turns the pot into its "cooked" variant, which has no
    /// cookingContainerSlots, so for a moment InventorySmelting stops reporting the
    /// slots the wax sits in and the firepit heats the pot instead. The cooked pot has
    /// nothing in its own contents, though, so the next transition check reverts it
    /// to the empty pot (BlockCookedContainer.UpdateAndGetTransitionStates), the slots
    /// reappear, and from then on the firepit heats the wax directly. Each state is
    /// read the way the firepit itself reads it.
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
    /// The firepit slot holding molten wax - of <paramref name="wax"/>, if given - or
    /// null. Searches every cooking slot, visible or not: see <see cref="Temperature"/>
    /// for why freshly cooked wax briefly sits in slots the firepit does not report.
    /// </summary>
    public static ItemSlot FindIn(BlockEntityFirepit firepit, string wax = null)
    {
        if (firepit.Inventory is not InventorySmelting inventory) return null;

        foreach (ItemSlot slot in inventory.Slots)
        {
            if (slot.Itemstack?.Collectible is ItemMoltenWax molten && (wax == null || molten.Wax == wax)) return slot;
        }
        return null;
    }
}
