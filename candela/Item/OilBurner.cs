using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// The oils a lantern's burner burns: liquids with a <c>candelaLampOil</c> attribute,
/// which Candela's patches give vanilla's olive and flax oil. <c>dim</c> is how many
/// light levels the oil's soot costs the lantern, as tallow's does.
///
/// Its own attribute rather than <c>combustibleProps</c>, which Immersive Lighting
/// adds to the same oils: two mods adding the one property would collide.
/// </summary>
public static class LampOil
{
    private const string Attr = "candelaLampOil";

    /// <summary>What a burner's fount holds - as much as vanilla's clay lamp is filled with.</summary>
    public const double BurnerLitres = 0.5;

    public static bool Is(CollectibleObject collectible) => collectible?.Attributes?[Attr].Exists == true;

    /// <summary>The light levels lost to <paramref name="code"/>'s soot; 0 for an oil this world does not have.</summary>
    public static int Dim(IWorldAccessor world, string code) =>
        code == null ? 0 : world.GetItem(new AssetLocation(code))?.Attributes?[Attr]["dim"].AsInt(0) ?? 0;

    /// <summary>Portions to the litre, as the liquid declares it.</summary>
    public static double PortionsPerLitre(ItemStack oil) => BlockLiquidContainerBase.GetContainableProps(oil)?.ItemsPerLitre ?? 100;

    /// <summary>Litres of oil burned in a game hour: half as many with the wick turned down.</summary>
    public static double LitresPerHour(bool wickLow) => (wickLow ? 0.5 : 1) / CandelaConfig.Current.OilBurnHoursPerLitre;

    /// <summary>"Olive oil", from the oil's item code.</summary>
    public static string Name(IWorldAccessor world, string code) =>
        world.GetItem(new AssetLocation(code)) is Item oil ? new ItemStack(oil).GetName() : code;
}

/// <summary>
/// The oil an oil burner carries while it is an item, out of a lantern: which oil, how
/// many litres, and how its wick is set. No oil attribute means an empty burner. A
/// burner that ran dry keeps its oil's name and no litres, so it gutters again as it
/// did when put back - it was spent, not emptied.
/// </summary>
public static class BurnerStack
{
    public const string Code = "candela:oilburner";

    private const string OilKey = "candela:oil";
    private const string LitresKey = "candela:litres";
    private const string WickKey = "candela:wickLow";

    public static bool Is(ItemStack stack) => stack?.Collectible is ItemOilBurner;

    public static string Oil(ItemStack stack) => stack.Attributes.GetString(OilKey);

    public static double Litres(ItemStack stack) => stack.Attributes.GetDouble(LitresKey);

    public static bool WickLow(ItemStack stack) => stack.Attributes.GetBool(WickKey);

    public static void Write(ItemStack stack, string oil, double litres, bool wickLow)
    {
        if (oil != null)
        {
            stack.Attributes.SetString(OilKey, oil);
            stack.Attributes.SetDouble(LitresKey, litres);
        }
        else
        {
            stack.Attributes.RemoveAttribute(OilKey);
            stack.Attributes.RemoveAttribute(LitresKey);
        }

        // Left off when up, so that new burners and ones turned back up stack together.
        if (wickLow) stack.Attributes.SetBool(WickKey, true);
        else stack.Attributes.RemoveAttribute(WickKey);
    }

    /// <summary>A burner holding what a lantern's did.</summary>
    public static ItemStack Make(IWorldAccessor world, string oil, double litres, bool wickLow)
    {
        if (world.GetItem(new AssetLocation(Code)) is not Item burner) return null;
        var stack = new ItemStack(burner);
        Write(stack, oil, litres, wickLow);
        return stack;
    }

    /// <summary>
    /// "Olive oil: 0.42 L, about 1088 hours", or that it is empty or dry. Without the
    /// hours with <paramref name="withHours"/> false: a lantern says those on the line
    /// it gives every flame (<see cref="CandleInfo"/>).
    /// </summary>
    public static void AppendInfo(IWorldAccessor world, StringBuilder dsc, string oil, double litres, bool wickLow, bool withHours = true)
    {
        if (oil == null) dsc.AppendLine(Lang.Get("candela:burner-empty"));
        else if (litres <= 0) dsc.AppendLine(Lang.Get("candela:burner-dry", LampOil.Name(world, oil)));
        else if (!withHours) dsc.AppendLine(Lang.Get("candela:burner-litres", LampOil.Name(world, oil), litres.ToString("0.00")));
        else
        {
            int hours = Math.Max(1, (int)Math.Round(litres / LampOil.LitresPerHour(wickLow)));
            dsc.AppendLine(Lang.Get("candela:burner-oil", LampOil.Name(world, oil), litres.ToString("0.00"), hours));
        }
        if (wickLow) dsc.AppendLine(Lang.Get("candela:burner-wick-low"));
    }
}

/// <summary>
/// A fount and wick that takes the candle's place in a lantern
/// (<see cref="BEBehaviorLanternFuel.TryRefuel"/>), and is filled with lamp oil there.
/// </summary>
public class ItemOilBurner : Item
{
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        ItemStack stack = inSlot.Itemstack;
        BurnerStack.AppendInfo(world, dsc, BurnerStack.Oil(stack), BurnerStack.Litres(stack), BurnerStack.WickLow(stack));
    }
}
