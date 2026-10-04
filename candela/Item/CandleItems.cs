using System.Text;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Vanilla's candle item, patched to place through <see cref="CandlePlacement"/>.
///
/// Still an ItemCandle, so everything that asks "is this a candle" - the chandelier,
/// the bunch's take-one interaction - keeps working.
/// </summary>
public class ItemCandelaCandle : ItemCandle
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        if (CandlePlacement.TryPlace(slot, byEntity, blockSel)) handHandling = EnumHandHandling.PreventDefault;
    }
}

/// <summary>
/// A candle that is not vanilla's: a tallow candle, or a part-burned stub of either
/// kind taken off a bunch. Places like a candle, carrying the hours its attributes
/// give it.
///
/// Deliberately not an ItemCandle. A chandelier takes any ItemCandle and drops
/// vanilla beeswax candles when broken, and a lantern recipe asking for a candle
/// means a whole beeswax one.
/// </summary>
public class ItemPlaceableCandle : Item
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        if (CandlePlacement.TryPlace(slot, byEntity, blockSel))
        {
            handHandling = EnumHandHandling.PreventDefault;
            return;
        }
        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine(Lang.Get("candela:candle-hours", Attributes["candela"]["burnHours"].AsInt()));
    }
}

/// <summary>
/// Placing a candle, whole or part-burned: onto an existing bunch of the same kind,
/// or as a new one. The same rules as vanilla's ItemCandle - shift-click, a fence
/// takes a single candle - with one difference that matters: adding to a bunch uses
/// ExchangeBlock, which keeps the bunch's <see cref="BECandles"/> and its fuel,
/// where vanilla's SetBlock would replace it with a fresh one.
///
/// Reads from the item's <c>candela</c> attributes: <c>burnHours</c> it carries,
/// <c>bunch</c> the bunch block's code without its quantity, <c>single</c> the
/// candle-on-a-fence block.
/// </summary>
public static class CandlePlacement
{
    public const int MaxBunch = 9;

    public static bool TryPlace(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel)
    {
        if (blockSel == null || byEntity?.World == null || !byEntity.Controls.ShiftKey) return false;

        IWorldAccessor world = byEntity.World;
        CollectibleObject candle = slot.Itemstack.Collectible;
        JsonObject attrs = candle.Attributes?["candela"];
        string bunchCode = attrs?["bunch"].AsString();
        if (bunchCode == null) return false;

        double hours = attrs["burnHours"].AsDouble(48);
        var bunch = new AssetLocation(bunchCode);

        IPlayer player = (byEntity as EntityPlayer)?.Player;
        if (!world.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
        {
            slot.MarkDirty();
            return false;
        }

        Block target = world.BlockAccessor.GetBlock(blockSel.Position);
        Block placed;

        if (target is BlockCandelaCandles existing && target.Code.Domain == bunch.Domain && target.FirstCodePart() == bunch.Path)
        {
            if (existing.Quantity >= MaxBunch) return false;

            placed = world.GetBlock(target.CodeWithVariant("quantity", (existing.Quantity + 1).ToString()));
            if (placed == null) return false;

            if (world.Side == EnumAppSide.Server)
            {
                BECandles be = existing.EnsureBlockEntity(world, blockSel.Position);
                be?.AddFuel(hours);
                world.BlockAccessor.ExchangeBlock(placed.BlockId, blockSel.Position);
            }
        }
        else
        {
            BlockPos at = blockSel.Position.AddCopy(blockSel.Face);
            BlockPos below = at.DownCopy();

            placed = world.BlockAccessor.GetBlock(below) is BlockFence && attrs["single"].Exists
                ? world.GetBlock(new AssetLocation(attrs["single"].AsString()))
                : world.GetBlock(bunch.WithPathAppendix("-1"));
            if (placed == null) return false;

            if (!world.BlockAccessor.GetBlock(at).IsReplacableBy(placed)) return false;
            if (!world.BlockAccessor.GetBlock(below).CanAttachBlockAt(world.BlockAccessor, placed, below, BlockFacing.UP, new Cuboidi(1, 14, 1, 14, 15, 14))) return false;

            world.BlockAccessor.SetBlock(placed.BlockId, at);
            if (world.Side == EnumAppSide.Server) (world.BlockAccessor.GetBlockEntity(at) as BECandles)?.SetFuel(hours);
        }

        if (player?.WorldData.CurrentGameMode != EnumGameMode.Creative) slot.TakeOut(1);
        slot.MarkDirty();

        if (placed.Sounds != null) world.PlaySoundAt(placed.Sounds.Place, blockSel.Position, -0.4, player);
        return true;
    }
}
