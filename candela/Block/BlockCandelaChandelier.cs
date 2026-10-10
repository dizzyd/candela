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
/// worth an hour per candle. Vanilla takes beeswax candles only; this takes tallow
/// too, one wax or the other, as a bunch does: an empty chandelier takes either, and
/// its first candle decides (<see cref="BECandles.BunchCode"/>). Part-burned stubs go
/// in too, which is a use for them. Unlike vanilla, a candle can be taken out again,
/// which is how spent ones are cleared.
///
/// Its candles and their flames are part of its model, so dyed or coloured candles get
/// a copy of it in their looks (<see cref="CandleMeshes"/>); all plain, it is drawn as
/// vanilla draws it.
/// </summary>
public class BlockCandelaChandelier : Block, ICandleHolder
{
    public const int MaxCandles = 8;

    public int Quantity { get; private set; }

    /// <summary>Hours a new beeswax candle burns for; a chandelier of tallow ones asks its block entity.</summary>
    public double BurnHours => CandelaConfig.Current.HoursFor("beeswax") ?? 48;

    /// <summary>The kind of candle a chandelier from before Candela, or from vanilla, holds.</summary>
    public string DefaultBunchCode => Attributes?["candela"]["bunch"].AsString(BEBehaviorLanternFuel.DefaultBunchCode) ?? BEBehaviorLanternFuel.DefaultBunchCode;

    private WorldInteraction[] interactions;

    private ColouredCandleMeshes coloured;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        string type = Variant["type"] ?? "candle0";
        Quantity = type.StartsWith("candle") ? type.Substring("candle".Length).ToInt(0) : 0;
        if (api is ICoreClientAPI capi) coloured = new ColouredCandleMeshes(capi, this);

        interactions = ObjectCacheUtil.GetOrCreate(api, "candelaChandelierInteractions", () =>
        {
            ItemStack[] candles = api.World.Collectibles.Where(CandleWax.IsCandle).Select(c => new ItemStack(c)).ToArray();
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

    /// <summary>
    /// Whether this candle goes in, whole or stub: any while the chandelier is empty,
    /// and after that only those of its candles' kind. <paramref name="be"/> null - one
    /// from before Candela - holds beeswax.
    /// </summary>
    public bool AcceptsCandle(CollectibleObject candle, BECandles be) =>
        CandleWax.IsCandle(candle) && Quantity < MaxCandles
        && (Quantity == 0 || CandleWax.BunchOf(candle) == (be?.BunchCode ?? DefaultBunchCode));

    /// <summary>
    /// The chandelier with <paramref name="be"/>'s candles in their looks and wax, or
    /// null while they are plain beeswax.
    /// </summary>
    public MeshData ColouredMesh(ITesselatorAPI tesselator, BECandles be)
    {
        string wax = be.Kind?.Wax;
        return coloured?.For(tesselator, be.Looks.ToArray(), wax == "beeswax" ? null : wax);
    }

    /// <summary>
    /// Vanilla's light for this many candles, less what their wax costs in the open -
    /// tallow's one level, as a bunch of it gives - and in their flames' colour.
    /// </summary>
    public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
    {
        byte[] full = base.GetLightHsv(blockAccessor, pos, stack);
        if (pos == null || blockAccessor.GetBlockEntity(pos) is not BECandles be) return full;

        int dim = be.Kind?.OpenDim ?? 0;
        if (dim > 0 && full[2] > 0) full = [full[0], full[1], (byte)System.Math.Max(1, full[2] - dim)];
        return be.LightHsv(full);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        ItemStack held = slot?.Itemstack;
        bool shift = byPlayer.Entity.Controls.ShiftKey;

        bool add = held != null && AcceptsCandle(held.Collectible, world.BlockAccessor.GetBlockEntity(blockSel.Position) as BECandles);
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
            if (Quantity == 0) be.SetKind(CandleWax.BunchOf(held.Collectible));
            be.AddCandle(CandleWax.HoursOf(held.Collectible) ?? 0, CandleLook.Of(held));
            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) slot.TakeOut(1);
            slot.MarkDirty();
            world.BlockAccessor.ExchangeBlock(WithCandles(world, Quantity + 1).BlockId, pos);
            world.PlaySoundAt(new AssetLocation("game:sounds/block/plate"), pos, -0.4, byPlayer);
        }
        else if (take)
        {
            double hours = be.TakeCandle(out CandleLook look);
            ItemStack candle = be.Kind?.CandleForHours(world, hours, look);
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

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BECandles be) return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        be.Settle();

        var drops = new List<ItemStack> { new(WithCandles(world, 0)) };
        drops.AddRange(BlockCandelaCandles.CandlesOf(world, be, be.Kind));
        return drops.ToArray();
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
