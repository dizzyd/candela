using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// A stick with wicks hung from it, built up into tapers one dip at a time.
///
/// How many coats it has is the item's <c>layers</c> variant rather than an
/// attribute, so each stage can have its own shape and the handbook can show them.
/// The time of the last dip is an attribute: it gates the next one, since a coat
/// laid over one that has not set just melts it off again.
/// </summary>
public class ItemDippingRod : Item, IWaxWorker, IContainedMeshSource
{
    public const int MaxLayers = 6;

    /// <summary>Game hours a coat takes to set before the next dip will hold.</summary>
    public const double SetHours = 0.1;

    private const string LastDipAttr = "candela:lastDipHours";

    public int Layers => int.Parse(Variant["layers"]);

    public bool IsFinished => Layers >= MaxLayers;

    /// <summary>
    /// Whether the last coat has set. A rod that has never been dipped has nothing
    /// to wait for.
    /// </summary>
    public static bool HasSet(IWorldAccessor world, ItemStack stack) => SetProgress(world, stack) >= 1;

    /// <summary>How far the last coat has set, from 0 just dipped to 1 ready for the next.</summary>
    public static double SetProgress(IWorldAccessor world, ItemStack stack)
    {
        if (!stack.Attributes.HasAttribute(LastDipAttr)) return 1;
        return Math.Clamp((world.Calendar.TotalHours - stack.Attributes.GetDouble(LastDipAttr)) / SetHours, 0, 1);
    }

    /// <summary>
    /// <paramref name="rod"/> as it is after one more coat of <paramref name="molten"/>,
    /// stamped with the time it was dipped. Its wicks' flame colour stays with it; its
    /// dye is the new coat's, since the outermost coat is the one that shows - an undyed
    /// coat over a dyed one leaves the candles plain.
    /// </summary>
    public ItemStack WithAnotherLayer(IWorldAccessor world, ItemStack rod, ItemStack molten)
    {
        Item next = world.GetItem(CodeWithVariant("layers", (Layers + 1).ToString()));
        var stack = new ItemStack(next);
        stack.Attributes.SetDouble(LastDipAttr, world.Calendar.TotalHours);
        return new CandleLook(FlameColours.Of(rod), WaxDyes.Of(molten)).Stamp(stack);
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        SettingBar.Register(api, this, (world, stack) => IsFinished ? 1 : SetProgress(world, stack));
    }

    /// <summary>A rod made with treated wicks carries their flame colour.</summary>
    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        FlameColours.Stamp(outputSlot.Itemstack, FlameColours.FromInputs(allInputSlots));
    }

    public override string GetHeldItemName(ItemStack itemStack) => CandleLook.Name(itemStack, base.GetHeldItemName(itemStack));

    // Its wax drawn dyed, if it is: in hand, and on the ground or a shelf.

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        if (!DyedItems.Render(capi, itemstack, "wax", "tallow", ref renderinfo)) base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
    }

    public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos) => DyedItems.Contained(api, slot, "wax", "tallow", targetAtlas);

    public string GetMeshCacheKey(ItemSlot slot) => DyedItems.CacheKey(slot);

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        SettingBar.Dispose();
    }

    public string CannotWork(IWorldAccessor world, ItemStack held, BlockEntityFirepit firepit, out ItemSlot waxSlot, out int portions)
    {
        portions = 1;
        waxSlot = ItemMoltenWax.FindIn(firepit, "tallow");

        if (IsFinished) return "rodfinished";
        if (waxSlot == null) return "notallow";
        if (!((ItemMoltenWax)waxSlot.Itemstack.Collectible).IsWorkable(world, waxSlot)) return "tallowcold";
        if (!HasSet(world, held)) return "coatsetting";
        return null;
    }

    public ItemStack Worked(IWorldAccessor world, ItemStack held, ItemStack molten) => WithAnotherLayer(world, held, molten);

    // Tallow only: beeswax is for moulds.
    public bool Takes(ItemStack held, string wax) => wax == "tallow" && !IsFinished;

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        if (IsFinished)
        {
            dsc.AppendLine(Lang.Get("candela:dippingrod-finished"));
        }
        else
        {
            dsc.AppendLine(Lang.Get("candela:dippingrod-layers", Layers, MaxLayers));
            if (!HasSet(world, inSlot.Itemstack)) dsc.AppendLine(Lang.Get("candela:dippingrod-setting"));
        }
    }
}
