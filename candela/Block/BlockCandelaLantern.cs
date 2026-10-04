using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Vanilla's lantern, burning a candle.
///
/// A subclass patched in over BlockLantern, so placement, hanging, glass and lining
/// all stay vanilla's. What it adds reads the candle from the lantern block entity's
/// <see cref="BEBehaviorLanternFuel"/>, or from the item's attributes while it is an
/// item: the light, the candle a crafted lantern starts with, carrying the candle
/// through being picked up, and the interactions to refuel, snuff and light it.
/// </summary>
public class BlockCandelaLantern : BlockLantern
{
    private WorldInteraction[] extraInteractions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        extraInteractions = ObjectCacheUtil.GetOrCreate(api, "candelaLanternInteractions", () =>
        {
            ItemStack[] candles = api.World.Collectibles
                .Where(c => c.Attributes?["candela"]["bunch"].Exists == true && c.Attributes["candela"]["burnHours"].Exists)
                .Select(c => new ItemStack(c)).ToArray();
            ItemStack[] torches = api.World.SearchBlocks(new AssetLocation("game:torch-*-lit-*")).Select(b => new ItemStack(b)).ToArray();

            return new WorldInteraction[]
            {
                new() { ActionLangCode = "candela:blockhelp-refuel", MouseButton = EnumMouseButton.Right, Itemstacks = candles },
                new() { ActionLangCode = "candela:blockhelp-snuff", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift", RequireFreeHand = true },
                new() { ActionLangCode = "candela:blockhelp-light", MouseButton = EnumMouseButton.Right, Itemstacks = torches },
            };
        });
    }

    public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
    {
        byte[] full = base.GetLightHsv(blockAccessor, pos, stack);

        if (pos != null)
        {
            var fuel = blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorLanternFuel>();
            return fuel != null ? fuel.LightHsv(full) : full;
        }

        // In hand: the candle does not burn there, but it shows what is left of it.
        // The burnout mode lives on the server, so a spent one is shown guttering.
        if (stack != null && LanternStack.HasFuel(stack) && api != null)
        {
            return LanternStack.Adjust(api.World, full, LanternStack.Candle(stack), !LanternStack.Snuffed(stack), LanternStack.Fuel(stack) <= 0);
        }
        return full;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        ItemStack stack = base.OnPickBlock(world, pos);
        world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorLanternFuel>()?.WriteTo(stack);
        return stack;
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        if (outputSlot.Itemstack == null) return;

        // The candle it was made with is the candle it starts out burning.
        foreach (ItemSlot slot in allInputSlots)
        {
            JsonObject attrs = slot.Itemstack?.Collectible.Attributes?["candela"];
            if (attrs?["bunch"].Exists != true || !attrs["burnHours"].Exists) continue;

            LanternStack.Write(outputSlot.Itemstack, attrs["burnHours"].AsDouble(), attrs["bunch"].AsString(), snuffed: false);
            return;
        }
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        ItemStack held = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
        bool shift = byPlayer.Entity.Controls.ShiftKey;

        bool snuff = held == null && shift;
        bool light = held?.Block is BlockTorch && held.Block.Variant["state"] == "lit";
        bool refuel = !shift && held?.Collectible.Attributes?["candela"]["bunch"].Exists == true && held.Collectible.Attributes["candela"]["burnHours"].Exists;

        if (!snuff && !light && !refuel) return base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;
        if (world.Side != EnumAppSide.Server) return true;

        var fuel = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorLanternFuel>();
        if (fuel == null) return true;

        if (snuff) fuel.Snuff();
        else if (light)
        {
            if (fuel.TryIgnite()) world.PlaySoundAt(new AssetLocation("game:sounds/torch-ignite"), blockSel.Position, 0, byPlayer);
        }
        else if (fuel.TryRefuel(byPlayer, byPlayer.InventoryManager.ActiveHotbarSlot))
        {
            world.PlaySoundAt(new AssetLocation("game:sounds/block/plate"), blockSel.Position, -0.4, byPlayer);
        }

        return true;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        ItemStack stack = inSlot.Itemstack;
        if (!LanternStack.HasFuel(stack)) return;

        BlockCandelaCandles kind = BlockCandelaCandles.KindOf(world, LanternStack.Candle(stack));
        string candleName = kind == null ? "?" : kind.CandleForHours(world, kind.BurnHours)?.GetName() ?? "?";
        double hours = LanternStack.Fuel(stack);

        dsc.AppendLine(hours > 0
            ? Lang.Get("candela:lantern-candle", candleName, System.Math.Max(1, (int)System.Math.Round(hours)))
            : Lang.Get("candela:lantern-candle-spent", candleName));
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer).Append(extraInteractions);
    }
}
