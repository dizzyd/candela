using System.Collections.Generic;
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
public class BlockCandelaCandles : BlockBunchOCandles, ICandleHolder
{
    /// <summary>Hours one new candle of this kind burns for, from the config.</summary>
    public double BurnHours => CandelaConfig.Current.HoursFor(Wax) ?? 48;

    /// <summary>The kind of candle, as the config names it: beeswax, tallow.</summary>
    public string Wax { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Light levels a lantern loses burning this kind of candle - tallow sooting the glass.</summary>
    public int LanternDim { get; private set; }

    /// <summary>
    /// The block standing for a kind of candle, by the bunch code its items carry
    /// (<c>game:bunchocandles</c>, <c>candela:tallowcandles</c>): what it burns for,
    /// and what a part-burned one comes back as.
    /// </summary>
    public static BlockCandelaCandles KindOf(IWorldAccessor world, string bunchCode) =>
        bunchCode == null ? null : world.GetBlock(new AssetLocation(bunchCode + "-1")) as BlockCandelaCandles;

    private string candleCode;
    private string stubPrefix;
    private Vec3f[][] wicksByRotation;
    private int rotations;
    private WorldInteraction[] extraInteractions;
    private ColouredFlameMeshes coloured;

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
        Wax = attrs?["wax"].AsString("beeswax");
        candleCode = attrs?["candle"].AsString("game:candle");
        stubPrefix = attrs?["stub"].AsString();
        LanternDim = attrs?["lanternDim"].AsInt(0) ?? 0;

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

        if (api is ICoreClientAPI capi) coloured = new ColouredFlameMeshes(capi, this);

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

    /// <summary>
    /// The bunch, unrotated and full height, with <paramref name="be"/>'s candles' tips in
    /// their colours - the model lists its candles in the order of <see cref="BunchWicks"/>,
    /// as the particle flames are - or null while all are plain.
    /// </summary>
    public MeshData ColouredMesh(ITesselatorAPI tesselator, BECandles be) => coloured?.For(tesselator, be.Colours.ToArray());

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

        for (int i = 0; i < ParticleProperties.Length; i++)
        {
            for (int j = 0; j < Quantity; j++)
            {
                AdvancedParticleProperties bps = Tinted(i, be?.ColourOf(j));
                bps.WindAffectednesAtPos = windAffectednessAtPos;
                Vec3f dp = wicks[j];
                bps.basePos.X = pos.X + dp.X - 1 / 64f;
                bps.basePos.Y = pos.InternalY + dp.Y * height;
                bps.basePos.Z = pos.Z + dp.Z;
                manager.Spawn(bps);
            }
        }
    }

    // Only ever touched from the async particle thread.
    private readonly Dictionary<(int, string), AdvancedParticleProperties> tintedParticles = new();

    /// <summary>
    /// Particle set <paramref name="index"/>, recoloured for a <paramref name="flameColour"/>
    /// candle. Only the flames: anything unsaturated - the smoke - stays as it is.
    /// </summary>
    private AdvancedParticleProperties Tinted(int index, string flameColour)
    {
        AdvancedParticleProperties plain = ParticleProperties[index];
        if (FlameColours.Get(flameColour) is not FlameColours.Colour colour || plain.HsvaColor == null || plain.HsvaColor[1].avg <= 0) return plain;

        if (!tintedParticles.TryGetValue((index, colour.Code), out AdvancedParticleProperties tinted))
        {
            tinted = plain.Clone();
            tinted.HsvaColor[0] = NatFloat.createUniform(colour.ParticleHue, 4);
            tintedParticles[(index, colour.Code)] = tinted;
        }
        return tinted;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;

        ItemStack held = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
        bool shift = byPlayer.Entity.Controls.ShiftKey;

        bool light = held?.Block is BlockTorch && held.Block.Variant["state"] == "lit";
        bool snuff = held == null && shift;
        // Shift-click with a candle means "add". When the candle could not be added -
        // a full bunch, or tallow on beeswax - the click falls through to here, and
        // must not take one off instead.
        bool take = !light && !shift && (held == null || held.Collectible is ItemCandle || held.Collectible is ItemPlaceableCandle);

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
        double hours = be.TakeCandle(out string flameColour);
        ItemStack candle = CandleForHours(world, hours, flameColour);

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
        be.Settle();
        return CandlesOf(world, be, this);
    }

    /// <summary>
    /// <paramref name="be"/>'s candles as <paramref name="kind"/>'s items, each an equal
    /// share of the pool, a stack to each flame colour.
    /// </summary>
    public static ItemStack[] CandlesOf(IWorldAccessor world, BECandles be, BlockCandelaCandles kind)
    {
        if (kind == null || be.Quantity <= 0) return [];
        double share = be.Fuel / be.Quantity;
        return be.Colours.GroupBy(c => c)
            .Select(g =>
            {
                ItemStack candle = kind.CandleForHours(world, share, g.Key);
                if (candle != null) candle.StackSize = g.Count();
                return candle;
            })
            .Where(candle => candle != null)
            .ToArray();
    }

    /// <summary>
    /// The candle a share of <paramref name="hours"/> comes back as, burning
    /// <paramref name="flameColour"/>: a whole one only if it is untouched, otherwise the
    /// largest stub it still fills. Rounded down, so taking candles off and putting
    /// them back never makes wax.
    /// </summary>
    public ItemStack CandleForHours(IWorldAccessor world, double hours, string flameColour)
    {
        if (BurnHours <= 0) return null;
        double fraction = hours / BurnHours;

        if (fraction >= 0.999) return Candle(candleCode);
        if (stubPrefix == null) return null;

        foreach (int quarter in new[] { 75, 50, 25 })
        {
            if (fraction >= quarter / 100.0) return Candle(stubPrefix + quarter);
        }
        return null;

        ItemStack Candle(string code) => FlameColours.Stamp(new ItemStack(world.GetItem(new AssetLocation(code))), flameColour);
    }

    /// <summary>
    /// The bunch's block entity, created if it has none - as a bunch placed before
    /// Candela was installed does not.
    /// </summary>
    public BECandles EnsureBlockEntity(IWorldAccessor world, BlockPos pos) => CandleHolders.EnsureBlockEntity(world, pos, EntityClass);

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer).Append(extraInteractions);
    }
}
