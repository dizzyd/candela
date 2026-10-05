using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// How much candle is left in a bunch, and whether it is burning.
///
/// A bunch is one pool of burn hours rather than one timer per candle: every
/// candle in it is lit together and burns down together, so <c>n</c> candles take
/// <c>n</c> hours from the pool each game hour, and one candle's share is the pool
/// divided by the count. Adding a candle adds its hours; taking one takes a share.
///
/// That only works because quantity changes go through ExchangeBlock, which keeps
/// the block entity. Vanilla's SetBlock would throw the pool away on every candle
/// added or taken - see <see cref="ItemCandelaCandle"/> and
/// <see cref="BlockCandelaCandles"/>, which replace those paths.
///
/// The burning itself is a <see cref="Flame"/>, shared with the lantern.
/// </summary>
public class BECandles : BlockEntity, IIgnitable
{
    private readonly Flame flame = new();

    /// <summary>Burn hours left across every candle in the bunch.</summary>
    public double Fuel => flame.Fuel;

    public bool Snuffed => flame.Snuffed;

    public BurnoutMode Mode => flame.Mode;

    public bool Spent => flame.Spent;

    public bool Flaming => flame.Flaming;

    public int Quantity => (Block as ICandleHolder)?.Quantity ?? 1;

    public double FullHours => (Block as ICandleHolder)?.BurnHours ?? 48;

    /// <summary>
    /// How tall the candles stand, as a fraction of new: quarters while there is fuel,
    /// so the mesh changes four times over a candle's life rather than continuously,
    /// and a stub once it is gone.
    /// </summary>
    public float HeightFactor
    {
        get
        {
            if (Mode == BurnoutMode.None) return 1f;
            double full = Quantity * FullHours;
            if (Fuel <= 0 || full <= 0) return SpentHeight;
            return (float)(Math.Ceiling(GameMath.Clamp(Fuel / full, 0, 1) * 4) / 4);
        }
    }

    public const float SpentHeight = 0.15f;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side != EnumAppSide.Server) return;

        // New, or a vanilla bunch from before Candela was installed getting its
        // block entity at last: either way, new candles.
        flame.Initialize(api, Quantity * FullHours);
        RegisterGameTickListener(OnBurnTick, 4000, api.World.Rand.Next(4000));
    }

    private void OnBurnTick(float dt)
    {
        float heightBefore = HeightFactor;
        bool spentBefore = Spent;
        byte[] light = Relight.Capture(this);

        flame.Burn(Api.World.Calendar.TotalHours, Quantity);

        if (Spent != spentBefore) Changed(light);
        else if (HeightFactor != heightBefore) MarkDirty(true);
        else Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    /// <summary>The light this bunch gives, given what the block would give new and lit.</summary>
    public byte[] LightHsv(byte[] full) => flame.LightHsv(full);

    public void Snuff()
    {
        byte[] light = Relight.Capture(this);
        if (flame.Snuff()) Changed(light);
    }

    /// <summary>Lights the bunch, if it has anything left to light.</summary>
    public bool TryIgnite()
    {
        byte[] light = Relight.Capture(this);
        if (!flame.TryIgnite(Api.World.Calendar.TotalHours)) return false;
        Changed(light);
        return true;
    }

    /// <summary>
    /// Adds a candle's hours to the pool. Call before exchanging the block for one
    /// with one more candle, so the light update that exchange triggers sees them.
    /// </summary>
    public void AddFuel(double hours)
    {
        flame.AddFuel(hours);
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    /// <summary>Takes one candle's share of the pool, and returns it. Call before exchanging for one candle fewer.</summary>
    public double TakeShare() => flame.TakeFuel(Fuel / Math.Max(1, Quantity));

    /// <summary>Sets the pool outright - for a block just placed from a part-burned candle.</summary>
    public void SetFuel(double hours)
    {
        byte[] light = Relight.Capture(this);
        flame.SetFuel(hours);
        Changed(light);
    }

    public override void OnExchanged(Block block)
    {
        base.OnExchanged(block);
        if (Api?.Side == EnumAppSide.Server) MarkDirty(true);
    }

    /// <summary>State that affects the light has changed; <paramref name="lightBefore"/> is what it was.</summary>
    private void Changed(byte[] lightBefore)
    {
        MarkDirty(true);
        Relight.After(this, lightBefore);
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (Block is not BlockCandelaCandles candles || Api is not ICoreClientAPI capi) return false;

        MeshData mesh = capi.TesselatorManager.GetDefaultBlockMesh(Block).Clone();
        int rotation = candles.RotationIndex(Pos);
        if (rotation > 0) mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, rotation * GameMath.PIHALF, 0);

        float height = HeightFactor;
        if (height < 1f) mesh.Scale(new Vec3f(0.5f, 0, 0.5f), 1, height, 1);

        mesher.AddMeshData(mesh);
        return true;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        CandleInfo.Append(dsc, flame, Fuel / Math.Max(1, Quantity));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        flame.ToTreeAttributes(tree);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        float heightBefore = HeightFactor;
        bool flamingBefore = Flaming;
        bool spentBefore = Spent;
        byte[] lightBefore = Api != null && Block != null ? Relight.Capture(this) : null;

        flame.FromTreeAttributes(tree);

        if (Api is ICoreClientAPI)
        {
            if (HeightFactor != heightBefore || Flaming != flamingBefore) MarkDirty(true);
            Relight.Synced(this, lightBefore);
            return;
        }

        // State restored onto a block entity already running: a block that fell and
        // landed, or a schematic pasted. The block was lit for whatever state it was
        // placed with - new candles - so a spent or snuffed one would keep shining at
        // full until something else changed. Deferred a tick rather than exchanging
        // the block from inside its own deserialisation.
        if (lightBefore != null && (Flaming != flamingBefore || Spent != spentBefore))
        {
            RegisterDelayedCallback(_ => Changed(lightBefore), 0);
        }
    }

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting) => flame.OnTryIgniteBlock(secondsIgniting);

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        if (TryIgnite()) handling = EnumHandling.PreventDefault;
    }

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) => flame.OnTryIgniteStack(secondsIgniting);
}

/// <summary>The block info line every candle flame shows.</summary>
public static class CandleInfo
{
    public static void Append(StringBuilder dsc, Flame flame, double hoursPerCandle)
    {
        if (flame.Mode == BurnoutMode.None) return;

        if (flame.Spent) dsc.AppendLine(Lang.Get(flame.Mode == BurnoutMode.Dark ? "candela:candles-out" : "candela:candles-guttering"));
        else dsc.AppendLine(Lang.Get(flame.Snuffed ? "candela:candles-snuffed" : "candela:candles-burning", Math.Max(1, (int)Math.Round(hoursPerCandle))));
    }
}
