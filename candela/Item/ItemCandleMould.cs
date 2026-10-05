using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// A fired clay mould of four wells, filled from a pot of molten wax on the fire
/// (BlockBehaviorDipVat) in one go where a rod takes six dips. The wax sets in it,
/// and right-clicking knocks out four candles and leaves it empty; each use wears it,
/// and it cracks in the end.
///
/// What it holds is the item's <c>state</c> variant - raw, fired (empty), tallow,
/// beeswax - so a full one looks full. The time it was filled is an attribute, as a
/// rod's last dip is.
/// </summary>
public class ItemCandleMould : Item, IWaxWorker
{
    public const int CandlesPerFill = 4;

    /// <summary>Game hours the candles take to set before they will come out whole.</summary>
    public const double SetHours = 0.5;

    private const string FilledAttr = "candela:filledHours";

    public string State => Variant["state"];

    public bool IsEmpty => State == "fired";

    public bool IsFilled => State is "tallow" or "beeswax";

    /// <summary>
    /// Portions of molten wax one fill takes: as many as dipping the same four candles
    /// takes in tallow, and in beeswax the three a candle vanilla's pot recipe does.
    /// </summary>
    public static int PortionsFor(string wax) => wax == "beeswax" ? 12 : 6;

    public static bool HasSet(IWorldAccessor world, ItemStack stack) => SetProgress(world, stack) >= 1;

    /// <summary>How far the candles in it have set, from 0 just poured to 1 ready.</summary>
    public static double SetProgress(IWorldAccessor world, ItemStack stack)
    {
        if (!stack.Attributes.HasAttribute(FilledAttr)) return 1;
        return Math.Clamp((world.Calendar.TotalHours - stack.Attributes.GetDouble(FilledAttr)) / SetHours, 0, 1);
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        SettingBar.Register(api, this, (world, stack) => IsFilled ? SetProgress(world, stack) : 1);
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        SettingBar.Dispose();
    }

    public string CannotWork(IWorldAccessor world, ItemStack held, BlockEntityFirepit firepit, out ItemSlot waxSlot, out int portions)
    {
        waxSlot = ItemMoltenWax.FindIn(firepit);
        portions = 0;

        if (IsFilled) return "mouldfull";
        if (!IsEmpty) return "mouldraw";
        if (waxSlot?.Itemstack.Collectible is not ItemMoltenWax wax) return "nowax";
        if (!wax.IsWorkable(world, waxSlot)) return "waxcold";

        portions = PortionsFor(wax.Wax);
        if (waxSlot.StackSize < portions) return "notenoughwax-" + wax.Wax;
        return null;
    }

    public ItemStack Worked(IWorldAccessor world, ItemStack held, string wax)
    {
        ItemStack filled = InState(world, held, wax);
        filled.Attributes.SetDouble(FilledAttr, world.Calendar.TotalHours);
        return filled;
    }

    public bool Takes(ItemStack held, string wax) => IsEmpty;

    /// <summary><paramref name="stack"/> as the same mould in another state, its wear kept.</summary>
    private ItemStack InState(IWorldAccessor world, ItemStack stack, string state)
    {
        var moved = new ItemStack(world.GetItem(CodeWithVariant("state", state)));
        moved.Attributes = stack.Attributes.Clone();
        moved.Attributes.RemoveAttribute(FilledAttr);
        return moved;
    }

    /// <summary>The candles a full mould of <paramref name="wax"/> gives.</summary>
    public static ItemStack Candles(IWorldAccessor world, string wax) =>
        new(world.GetItem(new AssetLocation(wax == "beeswax" ? "game:candle" : "candela:candle-tallow")), CandlesPerFill);

    /// <summary>
    /// Right-click with a full, set mould: knock the candles out. Its behaviors go
    /// first, so shift-right-click still sets it down on the ground, full or not.
    /// </summary>
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
        if (!IsFilled || !firstEvent || handling != EnumHandHandling.NotHandled) return;

        handling = EnumHandHandling.PreventDefault;
        IWorldAccessor world = byEntity.World;

        if (!HasSet(world, slot.Itemstack))
        {
            (world.Api as Vintagestory.API.Client.ICoreClientAPI)?.TriggerIngameError(this, "mouldsetting", Lang.Get("candela:ingameerror-mouldsetting"));
            return;
        }
        if (world.Side != EnumAppSide.Server) return;

        ItemStack candles = Candles(world, State);
        if (!byEntity.TryGiveItemStack(candles)) world.SpawnItemEntity(candles, byEntity.Pos.XYZ);

        slot.Itemstack = InState(world, slot.Itemstack, "fired");
        slot.MarkDirty();
        world.PlaySoundAt(new AssetLocation("game:sounds/block/ceramicplace"), byEntity, null);

        // Last, so a mould that cracks on this use is gone after giving its candles.
        DamageItem(world, byEntity, slot);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        if (IsEmpty) dsc.AppendLine(Lang.Get("candela:candlemould-empty", PortionsFor("tallow"), PortionsFor("beeswax")));
        else if (IsFilled && !HasSet(world, inSlot.Itemstack)) dsc.AppendLine(Lang.Get("candela:candlemould-setting"));
        else if (IsFilled) dsc.AppendLine(Lang.Get("candela:candlemould-set"));
    }
}
