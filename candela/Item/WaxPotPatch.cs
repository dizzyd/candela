using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Corrections to the vanilla cooking pot for a pot of molten wax, which it was never
/// written to hold once cooked.
///
/// DoSmelt means to give the pot the ingredients' temperature, but on the cooksInto
/// path it has already swapped the ingredients for the output - a fresh clone with no
/// temperature - before it reads them, so tallow cooked from fat at 170°C came out at
/// 20°C, pot and all: "Cold" in the firepit dialog, and already setting. And
/// GetOutputText asks what the pot's contents would cook into, which for tallow is
/// nothing: "No matching recipe found", under a pot that had just finished. On the
/// fire it drew the pot shut and empty - see <see cref="WaxPotRenderer"/>. And the
/// firepit stopped heating it at all - see <see cref="CanHeatInput"/>.
///
/// Harmony rather than a behavior because neither has a hook: BlockCookingContainer is
/// vanilla's own block class. Patched on both sides - the output text is composed on
/// the client - and once per process, since singleplayer's two sides share it.
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
    }

    public static void Uninstall()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
    }

    // Parameter names match the vanilla signatures; Harmony binds by name and throws at
    // patch time if they drift.

    /// <summary>The fat's temperature, read the way DoSmelt means to before it loses it.</summary>
    [HarmonyPrefix, HarmonyPatch(nameof(BlockCookingContainer.DoSmelt))]
    private static void DoSmeltPrefix(BlockCookingContainer __instance, IWorldAccessor world, ISlotProvider cookingSlotsProvider, out float __state)
    {
        __state = BlockCookingContainer.GetIngredientsTemperature(world, __instance.GetCookingStacks(cookingSlotsProvider, false));
    }

    /// <summary>
    /// The wax, and the pot, as hot as what was melted. The pot too because until it reverts
    /// to the empty pot the firepit heats it rather than the tallow, and
    /// <see cref="ItemMoltenWax.Temperature"/> judges the wax by it.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(nameof(BlockCookingContainer.DoSmelt))]
    private static void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, float __state)
    {
        foreach (ItemSlot slot in cookingSlotsProvider.Slots)
        {
            if (slot.Itemstack?.Collectible is not ItemMoltenWax wax) continue;

            wax.SetTemperature(world, slot.Itemstack, __state);
            if (inputSlot.Itemstack is ItemStack pot) pot.Collectible.SetTemperature(world, pot, __state);
            return;
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(BlockCookingContainer.GetOutputText))]
    private static void GetOutputText(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ref string __result)
    {
        foreach (ItemSlot slot in cookingSlotsProvider.Slots)
        {
            if (slot.Itemstack?.Collectible is not ItemMoltenWax wax) continue;

            __result = wax.IsWorkable(world, slot)
                ? Lang.Get("candela:firepit-molten-" + wax.Wax)
                : Lang.Get("candela:firepit-setting-" + wax.Wax);
            return;
        }
    }

    /// <summary>
    /// Every cooking pot on the fire, not only one already holding wax: the firepit asks
    /// once, when the pot changes, and with its dialog open the client hears of each
    /// slot on its own - the pot's change came before the tallow's, and the lid stayed
    /// on. The renderer looks for wax every frame instead.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(nameof(BlockCookingContainer.GetRendererWhenInFirepit))]
    private static void GetRendererWhenInFirepit(ItemStack stack, BlockEntityFirepit firepit, bool forOutputSlot, ref IInFirepitRenderer __result)
    {
        if (forOutputSlot || __result == null || firepit.Api is not ICoreClientAPI capi) return;
        __result = new WaxPotRenderer(capi, stack, firepit, __result);
    }

    /// <summary>
    /// The firepit heats its input only while there is something to cook, and for a
    /// pot that means a matching recipe - which molten wax is not. So the heat stopped
    /// with the cook: the tallow cooled on a fire still burning, and the fire, with
    /// nothing to do, let itself go out at the end of the log. A pot of molten wax is
    /// heated, and keeps the fire fed, as a pot that is cooking is.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(typeof(BlockEntityFirepit), nameof(BlockEntityFirepit.canHeatInput))]
    private static void CanHeatInput(BlockEntityFirepit __instance, ref bool __result)
    {
        if (!__result && __instance.inputStack?.Collectible is BlockCookingContainer && ItemMoltenWax.FindIn(__instance) != null)
        {
            __result = true;
        }
    }
}
