using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace candela;

/// <summary>
/// A stick with wicks hung from it, built up into tapers one dip at a time.
///
/// How many coats it has is the item's <c>layers</c> variant rather than an
/// attribute, so each stage can have its own shape and the handbook can show them.
/// The time of the last dip is an attribute: it gates the next one, since a coat
/// laid over one that has not set just melts it off again.
/// </summary>
public class ItemDippingRod : Item
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
    public static bool HasSet(IWorldAccessor world, ItemStack stack)
    {
        if (!stack.Attributes.HasAttribute(LastDipAttr)) return true;
        return world.Calendar.TotalHours - stack.Attributes.GetDouble(LastDipAttr) >= SetHours;
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
