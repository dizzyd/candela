using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
public class ItemDippingRod : Item, IWaxWorker
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
    /// The rod as it is after one more coat, stamped with the time it was dipped.
    /// </summary>
    public ItemStack WithAnotherLayer(IWorldAccessor world)
    {
        Item next = world.GetItem(CodeWithVariant("layers", (Layers + 1).ToString()));
        var stack = new ItemStack(next);
        stack.Attributes.SetDouble(LastDipAttr, world.Calendar.TotalHours);
        return stack;
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        SettingBar.Register(api, this, (world, stack) => IsFinished ? 1 : SetProgress(world, stack));
    }

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

    public ItemStack Worked(IWorldAccessor world, ItemStack held, string wax) => WithAnotherLayer(world);

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
