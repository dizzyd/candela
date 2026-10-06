using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// A pot of wax on Stone Bake Oven's cooking top (stonebakeoven) as on a firepit: kept
/// hot, drawn open with the wax showing. Its cooking top is its own block entity, not a
/// firepit, so neither of the firepit's patches reaches it - it asks its own
/// canHeatInput, and builds its own pot renderer rather than asking the pot for one.
/// Lifting the pot needs nothing here: its inventory is vanilla's smelting inventory,
/// which <see cref="CarriedWax"/> already patches.
///
/// Patched by name, and only if the mod is there, so Candela does not depend on it.
/// </summary>
public static class StoneBakeOvenCompat
{
    private const string CookingTop = "StoneBakeOven.BlockEntityOvenCookingTop";

    /// <summary>Where the oven draws its pot: half a block and a pixel up (CookingTopPotRenderer).</summary>
    private const float PotY = 0.5625f + 1 / 16f;

    public static void Install(Harmony harmony, ICoreAPI api)
    {
        var cookingTop = AccessTools.TypeByName(CookingTop);
        if (cookingTop == null) return;

        harmony.Patch(AccessTools.Method(cookingTop, "canHeatInput"), postfix: new HarmonyMethod(typeof(StoneBakeOvenCompat), nameof(CanHeatInput)));
        harmony.Patch(AccessTools.Method(cookingTop, "UpdateRenderer"), postfix: new HarmonyMethod(typeof(StoneBakeOvenCompat), nameof(UpdateRenderer)));
        api.Logger.Notification("[candela] Stone Bake Oven found: pots of wax keep hot and show on its cooking top");
    }

    /// <summary>As the firepit's (WaxPotPatch.CanHeatInput): heat a pot of molten wax as a pot that is cooking.</summary>
    private static void CanHeatInput(BlockEntity __instance, ref bool __result)
    {
        if ((__instance as BlockEntityContainer)?.Inventory is not InventorySmelting inventory) return;
        CarriedWax.KeepMark(inventory);

        if (!__result && inventory[1].Itemstack?.Collectible is BlockCookingContainer && ItemMoltenWax.FindIn(inventory) != null)
        {
            __result = true;
        }
    }

    /// <summary>
    /// Its pot renderer wrapped in ours, as the firepit's is: ours draws the pot open
    /// with the wax in it while there is wax, and hands back otherwise.
    /// </summary>
    private static void UpdateRenderer(BlockEntity __instance)
    {
        if (__instance.Api is not ICoreClientAPI capi || (__instance as BlockEntityContainer)?.Inventory is not InventorySmelting inventory) return;

        var renderer = Traverse.Create(__instance).Field("renderer").GetValue<FirepitContentsRenderer>();
        if (renderer?.contentStackRenderer == null || renderer.contentStackRenderer is WaxPotRenderer) return;
        if (inventory[1].Itemstack is not ItemStack pot || pot.Collectible is not BlockCookingContainer) return;

        renderer.contentStackRenderer = new WaxPotRenderer(capi, pot, __instance.Pos, inventory, PotY, renderer.contentStackRenderer);
    }
}
