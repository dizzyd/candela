using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Vanilla's bunch of candles (and its single candle on a fence), made to burn down.
///
/// Patched in over BlockBunchOCandles as a subclass, so everything vanilla does
/// that this does not override still happens. What it does override all reads the
/// bunch's <see cref="BECandles"/>: the light, the flames, the mesh height, taking a
/// candle off, and what breaking it drops. A bunch with no block entity - one
/// placed before Candela was installed - behaves exactly as vanilla until someone
/// interacts with it, which gives it one.
///
/// Its wick positions are its own copy of vanilla's, which are internal: the flames
/// have to move with the mesh as it shrinks.
/// </summary>
public class BlockCandelaCandles : BlockBunchOCandles
{
    /// <summary>Hours one new candle of this kind burns for.</summary>
    public double BurnHours { get; private set; }

    public int Quantity { get; private set; }

    private string candleCode;
    private string stubPrefix;
    private Vec3f[][] wicksByRotation;
    private int rotations;
    private WorldInteraction[] extraInteractions;

    // Vanilla's BlockBunchOCandles.candleWickPositions, in sixteenths.
    private static readonly Vec3f[] BunchWicks =
    [
        new(3.8f, 4, 3.8f), new(7.8f, 7, 4.8f), new(12.8f, 2, 1.8f),
        new(4.8f, 5, 9.8f), new(7.8f, 2, 8.8f), new(12.8f, 6, 12.8f),
        new(11.8f, 4, 6.8f), new(1.8f, 1, 12.8f), new(6.8f, 4, 13.8f),
    ];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        JsonObject attrs = Attributes?["candela"];
        BurnHours = attrs?["burnHours"].AsDouble(48) ?? 48;
        candleCode = attrs?["candle"].AsString("game:candle");
        stubPrefix = attrs?["stub"].AsString();

        bool single = attrs?["single"].AsBool(false) ?? false;
        Quantity = single ? 1 : Variant["quantity"].ToInt(1);

        // The bunch picks one of four rotations per position; the single candle has none.
        rotations = single ? 1 : 4;
        Vec3f[] wicks = single ? [new Vec3f(7.8f, 4, 7.8f)] : BunchWicks;
        wicksByRotation = new Vec3f[rotations][];
        for (int r = 0; r < rotations; r++)
        {
            var m = new Matrixf().Translate(0.5f, 0.5f, 0.5f).RotateYDeg(r * 90).Translate(-0.5f, -0.5f, -0.5f);
            wicksByRotation[r] = wicks.Select(w =>
            {
                Vec4f v = m.TransformVector(new Vec4f(w.X / 16f, w.Y / 16f, w.Z / 16f, 1));
                return new Vec3f(v.X, v.Y, v.Z);
            }).ToArray();
        }

        extraInteractions = ObjectCacheUtil.GetOrCreate(api, "candelaCandleInteractions", () =>
        {
            ItemStack[] torches = api.World.SearchBlocks(new AssetLocation("game:torch-*-lit-*"))
                .Select(b => new ItemStack(b)).ToArray();
            return new WorldInteraction[]
            {
                new() { ActionLangCode = "candela:blockhelp-snuff", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift", RequireFreeHand = true },
                new() { ActionLangCode = "candela:blockhelp-light", MouseButton = EnumMouseButton.Right, Itemstacks = torches },
            };
        });
    }

    /// <summary>
    /// Which of the bunch's rotations this position uses. The same hash vanilla's
    /// flames use, so a bunch with no block entity - drawn by vanilla - still agrees
    /// with flames drawn here.
    /// </summary>
    public int RotationIndex(BlockPos pos) => rotations == 1 ? 0 : GameMath.MurmurHash3Mod(pos.X, pos.Y, pos.Z, rotations);

    public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
    {
        byte[] full = base.GetLightHsv(blockAccessor, pos, stack);
        if (pos != null && blockAccessor.GetBlockEntity(pos) is BECandles be) return be.LightHsv(full);
        return full;
    }

    public override void OnAsyncClientParticleTick(IAsyncParticleManager manager, BlockPos pos, float windAffectednessAtPos, float secondsTicking)
    {
        if (ParticleProperties == null || ParticleProperties.Length == 0) return;

        var be = manager.BlockAccess.GetBlockEntity(pos) as BECandles;
        if (be != null && !be.Flaming) return;

        // A guttering candle shows a flame only now and then.
        if (be != null && be.Spent && api.World.Rand.NextDouble() > 0.3) return;

        float height = be?.HeightFactor ?? 1f;
        Vec3f[] wicks = wicksByRotation[RotationIndex(pos)];

        foreach (AdvancedParticleProperties bps in ParticleProperties)
        {
            bps.WindAffectednesAtPos = windAffectednessAtPos;
            for (int j = 0; j < Quantity; j++)
            {
                Vec3f dp = wicks[j];
                bps.basePos.X = pos.X + dp.X - 1 / 64f;
                bps.basePos.Y = pos.InternalY + dp.Y * height;
                bps.basePos.Z = pos.Z + dp.Z;
                manager.Spawn(bps);
            }
        }
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;

        ItemStack held = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
        bool shift = byPlayer.Entity.Controls.ShiftKey;

        bool light = held?.Block is BlockTorch && held.Block.Variant["state"] == "lit";
        bool snuff = held == null && shift;
        bool take = !light && !snuff && (held == null || held.Collectible is ItemCandle || held.Collectible is ItemCandleStub);

        if (!light && !snuff && !take) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        // Decided by what is in hand, and acted on only by the server: the client may
        // not have the block entity yet, and one created here would be a guess.
        if (take) world.PlaySoundAt(Sounds.Place, blockSel.Position, -0.4, byPlayer);
        if (world.Side != EnumAppSide.Server) return true;

        BECandles be = EnsureBlockEntity(world, blockSel.Position);
        if (be == null) return true;

        if (light)
        {
            if (be.TryIgnite()) world.PlaySoundAt(new AssetLocation("game:sounds/torch-ignite"), blockSel.Position, 0, byPlayer);
        }
        else if (snuff)
        {
            if (be.Flaming) be.Snuff();
        }
        else
        {
            TakeOneCandle(world, byPlayer, blockSel.Position, be);
        }

        return true;
    }

    private void TakeOneCandle(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, BECandles be)
    {
        ItemStack candle = CandleForHours(world, be.TakeShare());

        Block fewer = Quantity > 1 ? world.GetBlock(CodeWithVariant("quantity", (Quantity - 1).ToString())) : null;
        if (fewer == null)
        {
            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        }
        else
        {
            world.BlockAccessor.ExchangeBlock(fewer.BlockId, pos);
        }

        if (candle != null && !byPlayer.InventoryManager.TryGiveItemstack(candle, slotNotifyEffect: true))
        {
            world.SpawnItemEntity(candle, pos);
        }
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BECandles be) return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);

        ItemStack candle = CandleForHours(world, be.Fuel / Quantity);
        if (candle == null) return [];
        candle.StackSize = Quantity;
        return [candle];
    }

    /// <summary>
    /// The candle a share of <paramref name="hours"/> comes back as: a whole one only
    /// if it is untouched, otherwise the largest stub it still fills. Rounded down, so
    /// taking candles off and putting them back never makes wax.
    /// </summary>
    public ItemStack CandleForHours(IWorldAccessor world, double hours)
    {
        if (BurnHours <= 0) return null;
        double fraction = hours / BurnHours;

        if (fraction >= 0.999) return new ItemStack(world.GetItem(new AssetLocation(candleCode)));
        if (stubPrefix == null) return null;

        foreach (int quarter in new[] { 75, 50, 25 })
        {
            if (fraction >= quarter / 100.0) return new ItemStack(world.GetItem(new AssetLocation(stubPrefix + quarter)));
        }
        return null;
    }

    /// <summary>
    /// The bunch's block entity, created if it has none - as a bunch placed before
    /// Candela was installed does not.
    /// </summary>
    public BECandles EnsureBlockEntity(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is BECandles be) return be;
        if (world.Side != EnumAppSide.Server || EntityClass == null) return null;

        world.BlockAccessor.SpawnBlockEntity(EntityClass, pos);
        return world.BlockAccessor.GetBlockEntity(pos) as BECandles;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer).Append(extraInteractions);
    }
}
