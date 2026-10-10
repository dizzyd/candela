using System.Linq;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// A pot of wax taken off the fire keeps its wax: it goes with the pot, stays hot
/// enough to pour into moulds for <see cref="PourableHours"/>, then sets in it, and
/// goes back into the pot's slots when the pot is set on a firepit again - where a
/// lit fire melts it once more.
///
/// On the fire the wax lives in the firepit's cooking slots, not in the pot, and
/// vanilla throws whatever is in them on the ground when the pot leaves
/// (InventorySmelting.DidModifyItemSlot, discardCookingSlots). So the contents are
/// moved onto the pot as it goes, as an attribute, and back out of it as it comes.
/// Vanilla's cooked pot already carries contents, but as a meal - servings, bowls,
/// eating - which wax is not.
///
/// A pot on the fire holding wax is marked (<see cref="HoldsWax"/>) so that it does
/// not stack with empty pots on the way out: lifted into a stack of them, there would
/// be no one pot to carry the wax.
/// </summary>
[HarmonyPatch(typeof(InventorySmelting))]
public static class CarriedWax
{
    /// <summary>On the pot on the fire, while its slots hold wax or what wax set into.</summary>
    public const string HoldsWax = "candela:holdsWax";

    /// <summary>On a pot off the fire: its slots' contents, by slot index.</summary>
    public const string Contents = "candela:contents";

    /// <summary>Game hours wax carried off in its pot stays hot enough to pour.</summary>
    public const double PourableHours = 3;

    /// <summary>
    /// The molten wax a pot off the fire carries, or null, and the key it is under in
    /// <see cref="Contents"/>.
    /// </summary>
    public static ItemStack MoltenIn(IWorldAccessor world, ItemStack pot, out string key)
    {
        key = null;
        if (pot?.Collectible is not BlockCookingContainer || pot.Attributes[Contents] is not ITreeAttribute contents) return null;

        foreach (var (k, _) in contents)
        {
            ItemStack stack = contents.GetItemstack(k);
            stack?.ResolveBlockOrItem(world);
            if (stack?.Collectible is not ItemMoltenWax) continue;
            key = k;
            return stack;
        }
        return null;
    }

    /// <summary>Takes <paramref name="portions"/> of what is under <paramref name="key"/> out of the pot.</summary>
    public static void TakeFrom(IWorldAccessor world, ItemStack pot, string key, int portions)
    {
        var contents = (ITreeAttribute)pot.Attributes[Contents];
        ItemStack stack = contents.GetItemstack(key);
        stack.ResolveBlockOrItem(world);
        stack.StackSize -= portions;

        if (stack.StackSize > 0) contents.SetItemstack(key, stack);
        else contents.RemoveAttribute(key);
        if (contents.Count == 0) pot.Attributes.RemoveAttribute(Contents);
    }

    /// <summary>
    /// Marks the pot in <paramref name="inventory"/> while it holds wax, and unmarks it
    /// once its slots are empty. Server side, from the firepit's tick (or the oven's),
    /// and as the pot is moved out (<see cref="MarkBeforeMoving"/>).
    /// </summary>
    public static void KeepMark(InventorySmelting inventory)
    {
        if (inventory?.Api?.Side != EnumAppSide.Server) return;
        ItemSlot potSlot = inventory[1];
        if (potSlot.Itemstack?.Collectible is not BlockCookingContainer) return;

        bool marked = potSlot.Itemstack.Attributes.GetBool(HoldsWax);
        if (!marked && ItemMoltenWax.FindIn(inventory) != null)
        {
            potSlot.Itemstack.Attributes.SetBool(HoldsWax, true);
            potSlot.MarkDirty();
        }
        else if (marked && inventory.Slots.All(slot => slot.Empty))
        {
            potSlot.Itemstack.Attributes.RemoveAttribute(HoldsWax);
            potSlot.MarkDirty();
        }
    }

    /// <summary>
    /// The pot leaving: its slots' contents onto it, before vanilla spills them.
    /// <paramref name="extractedStack"/> is the pot itself on every way out - taken,
    /// shift-clicked, swapped - the very stack that lands in the other slot.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(nameof(InventorySmelting.DidModifyItemSlot))]
    private static void PotLeaving(InventorySmelting __instance, ItemSlot slot, ItemStack extractedStack)
    {
        if (__instance.Api?.Side != EnumAppSide.Server || slot != __instance[1]) return;
        if (extractedStack == null || extractedStack == slot.Itemstack || extractedStack.StackSize != 1) return;
        if (extractedStack.Collectible is not BlockCookingContainer) return;
        if (!extractedStack.Attributes.GetBool(HoldsWax) && ItemMoltenWax.FindIn(__instance) == null) return;

        var contents = new TreeAttribute();
        ItemSlot[] slots = __instance.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Empty) continue;
            ItemStack stack = slots[i].Itemstack;
            (stack.Collectible as ItemMoltenWax)?.KeepWarmFor(__instance.Api.World, stack, PourableHours);
            contents.SetItemstack(i.ToString(), stack);
            slots[i].Itemstack = null;
        }

        extractedStack.Attributes.RemoveAttribute(HoldsWax);
        if (contents.Count > 0) extractedStack.Attributes[Contents] = contents;
    }

    /// <summary>
    /// Marked as it is moved out, as well as from a tick: the mark is what keeps it from
    /// merging into a stack of empty pots on the way, and only a firepit's tick (or an
    /// oven's Candela knows) keeps it up otherwise - any other cooker built on the
    /// smelting inventory would hand over an unmarked pot.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.TryPutInto), new[] { typeof(ItemSlot), typeof(ItemStackMoveOperation) }, new[] { ArgumentType.Normal, ArgumentType.Ref })]
    private static void MarkBeforeMoving(ItemSlot __instance)
    {
        if (__instance.Inventory is InventorySmelting inventory && __instance == inventory[1]) KeepMark(inventory);
    }

    /// <summary>
    /// A pot arriving with contents: back into the slots, set by however long it was
    /// off the fire.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(nameof(InventorySmelting.DidModifyItemSlot))]
    private static void PotArriving(InventorySmelting __instance, ItemSlot slot)
    {
        if (__instance.Api?.Side != EnumAppSide.Server || slot != __instance[1]) return;
        ItemStack pot = slot.Itemstack;
        if (pot?.Collectible is not BlockCookingContainer || pot.Attributes[Contents] is not ITreeAttribute contents) return;

        IWorldAccessor world = __instance.Api.World;
        ItemSlot[] slots = __instance.Slots;
        foreach (var (key, _) in contents)
        {
            ItemStack stack = contents.GetItemstack(key);
            stack?.ResolveBlockOrItem(world);
            if (stack?.Collectible == null) continue;

            ItemSlot into = int.TryParse(key, out int i) && i < slots.Length && slots[i].Empty ? slots[i] : slots.FirstOrDefault(s => s.Empty);
            if (into == null)
            {
                world.SpawnItemEntity(stack, __instance.Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                continue;
            }

            into.Itemstack = stack;
            stack.Collectible.UpdateAndGetTransitionStates(world, into);
            // Back to cooling as a pot on a fire does, from the heat it has kept.
            if (into.Itemstack?.Collectible is ItemMoltenWax wax) wax.SetTemperature(world, into.Itemstack, wax.GetTemperature(world, into.Itemstack));
            into.MarkDirty();
        }

        // Before marking the pot dirty, which comes straight back here.
        pot.Attributes.RemoveAttribute(Contents);
        pot.Attributes.SetBool(HoldsWax, true);
        slot.MarkDirty();
    }

    /// <summary>
    /// Its name where ground storage shows it, which vanilla gives as "(Empty)" for
    /// any pot with nothing cooking in it.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(typeof(BlockCookingContainer), nameof(BlockCookingContainer.GetContainedInfo))]
    private static void ContainedInfo(ItemSlot inSlot, ref string __result)
    {
        if (inSlot?.Inventory?.Api?.World is IWorldAccessor world && Describe(world, inSlot.Itemstack) is string holds)
        {
            __result = inSlot.GetStackName() + " (" + holds + ")";
        }
    }

    /// <summary>What <paramref name="pot"/> carries, for a person to read, or null if nothing.</summary>
    public static string Describe(IWorldAccessor world, ItemStack pot)
    {
        if (pot?.Attributes[Contents] is not ITreeAttribute contents) return null;

        var parts = contents.Select(kv => contents.GetItemstack(kv.Key))
            .Where(stack => stack?.ResolveBlockOrItem(world) == true)
            .Select(stack => DescribeStack(world, stack))
            .ToArray();
        return parts.Length > 0 ? CandleInfo.List(parts) : null;
    }

    /// <summary>
    /// "12x Molten tallow", or once it has cooled below its setting point what it
    /// sets into - which it does in fact become when the pot is next on a fire.
    /// </summary>
    private static string DescribeStack(IWorldAccessor world, ItemStack stack)
    {
        if (stack.Collectible is ItemMoltenWax wax && wax.GetTemperature(world, stack) < wax.SetsBelow)
        {
            TransitionableProperties harden = wax.TransitionableProps?.FirstOrDefault(p => p.Type == EnumTransitionType.Harden);
            ItemStack set = harden?.TransitionedStack?.ResolvedItemstack;
            if (set != null)
            {
                int count = (int)(stack.StackSize * harden.TransitionRatio);
                return count > 0 ? $"{count}x {set.GetName()}" : Lang.Get("candela:pot-holds-scrap");
            }
        }
        return $"{stack.StackSize}x {stack.GetName()}";
    }
}
