using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Vanilla's chandelier, with candles that burn down.
///
/// A replacement for BlockChandelier rather than a subclass, because vanilla's is
/// internal. It is small, and everything that matters about it besides candles -
/// hanging, falling, its shapes - is in its JSON and stays: falling in particular is
/// the UnstableFalling behavior, which carries the block entity's state with the
/// falling block, fuel included.
///
/// Its candles are one <see cref="BECandles"/> pool like a bunch's, at one candle's
/// worth an hour per candle. Vanilla takes beeswax candles only, and so does this -
/// part-burned beeswax stubs included, which is a use for them. Unlike vanilla, a
/// candle can be taken out again, which is how spent ones are cleared.
/// </summary>
public class BlockCandelaChandelier : Block, ICandleHolder
{
    public const int MaxCandles = 8;

    public int Quantity { get; private set; }

    public double BurnHours => CandelaConfig.Current.HoursFor(Wax) ?? 48;

    private string Wax => Attributes?["candela"]["wax"].AsString("beeswax") ?? "beeswax";

    private WorldInteraction[] interactions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        string type = Variant["type"] ?? "candle0";
        Quantity = type.StartsWith("candle") ? type.Substring("candle".Length).ToInt(0) : 0;

        interactions = ObjectCacheUtil.GetOrCreate(api, "candelaChandelierInteractions", () =>
        {
            ItemStack[] candles = api.World.Collectibles.Where(c => AcceptsCandle(c)).Select(c => new ItemStack(c)).ToArray();
            ItemStack[] torches = api.World.SearchBlocks(new AssetLocation("game:torch-*-lit-*")).Select(b => new ItemStack(b)).ToArray();

            return new WorldInteraction[]
            {
                new() { ActionLangCode = "blockhelp-chandelier-addcandle", MouseButton = EnumMouseButton.Right, Itemstacks = candles },
                new() { ActionLangCode = "candela:blockhelp-takecandle", MouseButton = EnumMouseButton.Right, RequireFreeHand = true },
                new() { ActionLangCode = "candela:blockhelp-snuff", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift", RequireFreeHand = true },
                new() { ActionLangCode = "candela:blockhelp-light", MouseButton = EnumMouseButton.Right, Itemstacks = torches },
            };
        });
    }

    /// <summary>Whether this kind of candle goes in: whole or stub, of this chandelier's wax.</summary>
    public bool AcceptsCandle(CollectibleObject candle) =>
        CandleWax.IsCandle(candle) && candle.Attributes["candela"]["wax"].AsString() == Wax;

    public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
    {
        byte[] full = base.GetLightHsv(blockAccessor, pos, stack);
        if (pos != null && blockAccessor.GetBlockEntity(pos) is BECandles be) return be.LightHsv(full);
        return full;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        ItemStack held = slot?.Itemstack;
        bool shift = byPlayer.Entity.Controls.ShiftKey;

        bool add = held != null && AcceptsCandle(held.Collectible) && Quantity < MaxCandles;
        bool light = held?.Block is BlockTorch && held.Block.Variant["state"] == "lit";
        bool snuff = held == null && shift;
        bool take = held == null && !shift && Quantity > 0;

        if (!add && !light && !snuff && !take) return base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;
        if (world.Side != EnumAppSide.Server) return true;

        BECandles be = CandleHolders.EnsureBlockEntity(world, blockSel.Position, EntityClass);
        if (be == null) return true;
        BlockPos pos = blockSel.Position;

        if (add)
        {
            be.AddFuel(CandleWax.HoursOf(held.Collectible) ?? 0);
            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) slot.TakeOut(1);
            slot.MarkDirty();
            world.BlockAccessor.ExchangeBlock(WithCandles(world, Quantity + 1).BlockId, pos);
            world.PlaySoundAt(new AssetLocation("game:sounds/block/plate"), pos, -0.4, byPlayer);
        }
        else if (take)
        {
            ItemStack candle = CandleForHours(world, be.TakeShare());
            world.BlockAccessor.ExchangeBlock(WithCandles(world, Quantity - 1).BlockId, pos);
            if (candle != null && !byPlayer.InventoryManager.TryGiveItemstack(candle, slotNotifyEffect: true))
            {
                world.SpawnItemEntity(candle, pos);
            }
        }
        else if (snuff)
        {
            if (be.Flaming) be.Snuff();
        }
        else if (be.TryIgnite())
        {
            world.PlaySoundAt(new AssetLocation("game:sounds/torch-ignite"), pos, 0, byPlayer);
        }

        return true;
    }

    private Block WithCandles(IWorldAccessor world, int count) => world.GetBlock(CodeWithVariant("type", "candle" + count));

    /// <summary>What a share of the pool comes back as - the bunch of the same wax decides.</summary>
    public ItemStack CandleForHours(IWorldAccessor world, double hours) =>
        BlockCandelaCandles.KindOf(world, Attributes?["candela"]["bunch"].AsString("game:bunchocandles"))?.CandleForHours(world, hours);

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BECandles be) return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        be.Settle();

        var drops = new List<ItemStack> { new(WithCandles(world, 0)) };
        if (Quantity > 0 && CandleForHours(world, be.Fuel / Quantity) is ItemStack candle)
        {
            candle.StackSize = Quantity;
            drops.Add(candle);
        }
        return drops.ToArray();
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
