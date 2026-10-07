using System.Linq;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// The cooking pot and firepit taught to hold a pot of molten wax once it is cooked:
/// keep the heat it was melted at, say what it holds, stand open with the wax showing
/// (<see cref="WaxPotRenderer"/>), and stay hot while the fire burns.
///
/// Harmony because none of these has a hook: BlockCookingContainer and
/// BlockEntityFirepit are vanilla's own classes. Patched on both sides - the output
/// text and the renderer are the client's - and once per process, since
/// singleplayer's two sides share it.
/// </summary>
[HarmonyPatch(typeof(BlockCookingContainer))]
public static class WaxPotPatch
{
    public const string HarmonyId = "com.dizzyd.candela";

    private static Harmony harmony;

    public static void Install(ICoreAPI api)
    {
        if (harmony != null || Harmony.HasAnyPatches(HarmonyId)) return;
        harmony = new Harmony(HarmonyId);
        harmony.PatchAll(typeof(WaxPotPatch).Assembly);
        StoneBakeOvenCompat.Install(harmony, api);
    }

    public static void Uninstall()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
    }

    // Parameter names match the vanilla signatures; Harmony binds by name and throws at
    // patch time if they drift.

    /// <summary>
    /// The ingredients' temperature and the dye among them, read before DoSmelt loses
    /// them: on the cooksInto path it swaps them for a fresh clone of the output before
    /// reading them, so the wax would come out at 20°C, and undyed.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(nameof(BlockCookingContainer.DoSmelt))]
    private static void DoSmeltPrefix(BlockCookingContainer __instance, IWorldAccessor world, ISlotProvider cookingSlotsProvider, out (float temperature, string dye) __state)
    {
        ItemStack[] stacks = __instance.GetCookingStacks(cookingSlotsProvider, false);
        __state = (BlockCookingContainer.GetIngredientsTemperature(world, stacks), stacks.Select(WaxDyes.OfLiquid).FirstOrDefault(d => d != null));
    }

    /// <summary>
    /// The wax, and the pot, as hot as what was melted, and the wax dyed with what was
    /// cooked in with it - or undyed, which is how stubs remelt. The pot too because
    /// until it reverts to the empty pot the firepit heats it rather than the tallow,
    /// and <see cref="ItemMoltenWax.Temperature"/> judges the wax by it.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(nameof(BlockCookingContainer.DoSmelt))]
    private static void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, (float temperature, string dye) __state)
    {
        foreach (ItemSlot slot in cookingSlotsProvider.Slots)
        {
            if (slot.Itemstack?.Collectible is not ItemMoltenWax wax) continue;

            wax.SetTemperature(world, slot.Itemstack, __state.temperature);
            WaxDyes.Stamp(slot.Itemstack, __state.dye);
            if (inputSlot.Itemstack is ItemStack pot) pot.Collectible.SetTemperature(world, pot, __state.temperature);
            return;
        }
    }

    /// <summary>
    /// What the pot holds. Vanilla asks what its contents would cook into, which for
    /// wax already cooked is nothing: "No matching recipe found".
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(nameof(BlockCookingContainer.GetOutputText))]
    private static void GetOutputText(BlockCookingContainer __instance, IWorldAccessor world, ISlotProvider cookingSlotsProvider, ref string __result)
    {
        foreach (ItemSlot slot in cookingSlotsProvider.Slots)
        {
            if (slot.Itemstack?.Collectible is not ItemMoltenWax wax) continue;

            __result = wax.IsWorkable(world, slot)
                ? Lang.Get("candela:firepit-molten-" + wax.Wax)
                : Lang.Get("candela:firepit-setting-" + wax.Wax);
            return;
        }

        // Before the cook: vanilla holds the portions it will make - servings times
        // the recipe's two a lump of fat - to the pot's limit on servings, and says a
        // full pot of fat "is too small to make 12x molten tallow". The cook itself
        // checks servings, and goes ahead.
        CookingRecipe recipe = __instance.GetMatchingCookingRecipe(world, __instance.GetCookingStacks(cookingSlotsProvider, false), out int servings);
        if (recipe?.CooksInto?.ResolvedItemstack?.Collectible is not ItemMoltenWax) return;
        if (servings < 1 || servings > __instance.MaxServingSize) return;

        __result = Lang.Get("mealcreation-nonfood", servings * recipe.CooksInto.Quantity, recipe.CooksInto.ResolvedItemstack.GetName().ToLower());
    }

    /// <summary>
    /// Every cooking pot on the fire, not only one already holding wax: the firepit asks
    /// once, when the pot changes, and the client can hear of the pot before the wax in
    /// it. The renderer looks for wax every frame instead.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(nameof(BlockCookingContainer.GetRendererWhenInFirepit))]
    private static void GetRendererWhenInFirepit(ItemStack stack, BlockEntityFirepit firepit, bool forOutputSlot, ref IInFirepitRenderer __result)
    {
        if (forOutputSlot || __result == null || firepit.Api is not ICoreClientAPI capi) return;
        __result = new WaxPotRenderer(capi, stack, firepit.Pos, firepit.Inventory as InventorySmelting, 1 / 16f, __result);
    }

    /// <summary>
    /// The firepit heats its input only while there is something to cook, and for a
    /// pot that means a matching recipe - which molten wax is not. A pot of molten wax
    /// is heated, and keeps the fire fed, as a pot that is cooking is.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(typeof(BlockEntityFirepit), nameof(BlockEntityFirepit.canHeatInput))]
    private static void CanHeatInput(BlockEntityFirepit __instance, ref bool __result)
    {
        // Asked every server tick, lit or not: the one place to keep the pot's mark
        // in step with what it holds.
        CarriedWax.KeepMark(__instance.Inventory as InventorySmelting);

        if (!__result && __instance.inputStack?.Collectible is BlockCookingContainer && ItemMoltenWax.FindIn(__instance) != null)
        {
            __result = true;
        }
    }
}
