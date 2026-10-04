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
/// </summary>
public class BECandles : BlockEntity, IIgnitable
{
    /// <summary>Burn hours left across every candle in the bunch.</summary>
    public double Fuel { get; private set; }

    /// <summary>Put out by a player, as opposed to burned down.</summary>
    public bool Snuffed { get; private set; }

    /// <summary>
    /// The server's burnout mode, carried to the client with the rest of the state
    /// so that the client lights and draws a spent candle the way the server judges it.
    /// </summary>
    public BurnoutMode Mode { get; private set; } = BurnoutMode.Dim;

    private double lastUpdateHours;
    private bool hasState;

    public int Quantity => (Block as BlockCandelaCandles)?.Quantity ?? 1;

    public double FullHours => (Block as BlockCandelaCandles)?.BurnHours ?? 48;

    public bool Spent => Fuel <= 0 && Mode != BurnoutMode.None;

    /// <summary>Whether there is a flame: not snuffed, and not burned down into the dark.</summary>
    public bool Flaming => !Snuffed && !(Spent && Mode == BurnoutMode.Dark);

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

        CandelaConfig config = CandelaConfig.Current;
        Mode = config.BurnoutMode;
        double now = api.World.Calendar.TotalHours;

        if (!hasState)
        {
            // Placed just now, or a vanilla candle from before Candela was installed
            // getting its block entity at last: either way, new candles.
            Fuel = Quantity * FullHours;
            lastUpdateHours = now;
            hasState = true;
        }
        else
        {
            // Loaded from a saved chunk. How much of the time it spent unloaded it burns
            // through is the UnattendedMode setting - by default none of it.
            lastUpdateHours = config.UnattendedMode switch
            {
                UnattendedMode.LoadedOnly => now,
                UnattendedMode.CappedCatchUp => Math.Max(lastUpdateHours, now - config.CatchUpCapHours),
                _ => lastUpdateHours,
            };
        }

        RegisterGameTickListener(OnBurnTick, 4000, api.World.Rand.Next(4000));
    }

    private void OnBurnTick(float dt)
    {
        double now = Api.World.Calendar.TotalHours;
        double elapsed = now - lastUpdateHours;
        lastUpdateHours = now;

        if (Snuffed || Mode == BurnoutMode.None || Fuel <= 0 || elapsed <= 0) return;

        float heightBefore = HeightFactor;
        Fuel = Math.Max(0, Fuel - elapsed * Quantity);

        if (Fuel <= 0) Changed(lightChanged: true);
        else if (HeightFactor != heightBefore) Changed(lightChanged: false);
        else Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    /// <summary>
    /// The light this bunch gives, given what the block would give new and lit.
    /// </summary>
    public byte[] LightHsv(byte[] full)
    {
        if (!Flaming) return Dark;
        if (!Spent) return full;

        // Guttering: the dim floor that makes upkeep a matter of brightness rather
        // than of light at all.
        return [full[0], full[1], (byte)Math.Max(2, full[2] / 3)];
    }

    private static readonly byte[] Dark = [0, 0, 0];

    public void Snuff()
    {
        if (Snuffed) return;
        Snuffed = true;
        Changed(lightChanged: true);
    }

    /// <summary>Lights the bunch, if it has anything left to light.</summary>
    public bool TryIgnite()
    {
        if (Flaming) return false;
        if (Spent && Mode == BurnoutMode.Dark) return false;

        Snuffed = false;
        lastUpdateHours = Api.World.Calendar.TotalHours;
        Changed(lightChanged: true);
        return true;
    }

    /// <summary>
    /// Adds a candle's hours to the pool. Call before exchanging the block for one
    /// with one more candle, so the light update that exchange triggers sees them.
    /// </summary>
    public void AddFuel(double hours)
    {
        Fuel += hours;
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    /// <summary>Takes one candle's share of the pool, and returns it.</summary>
    public double TakeShare()
    {
        double share = Fuel / Math.Max(1, Quantity);
        Fuel -= share;
        return share;
    }

    /// <summary>Sets the pool outright - for a block just placed from a part-burned candle.</summary>
    public void SetFuel(double hours)
    {
        Fuel = hours;
        hasState = true;
        Changed(lightChanged: true);
    }

    public override void OnExchanged(Block block)
    {
        base.OnExchanged(block);
        if (Api?.Side == EnumAppSide.Server) MarkDirty(true);
    }

    private void Changed(bool lightChanged)
    {
        MarkDirty(true);

        // Re-placing the same block is what makes the engine ask GetLightHsv again -
        // the lantern does the same when its glass changes.
        if (lightChanged) Api.World.BlockAccessor.ExchangeBlock(Block.Id, Pos);
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
        if (Mode == BurnoutMode.None) return;

        if (Spent) dsc.AppendLine(Lang.Get(Mode == BurnoutMode.Dark ? "candela:candles-out" : "candela:candles-guttering"));
        else
        {
            double hours = Fuel / Math.Max(1, Quantity);
            dsc.AppendLine(Lang.Get(Snuffed ? "candela:candles-snuffed" : "candela:candles-burning", Math.Max(1, (int)Math.Round(hours))));
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("candela:fuel", Fuel);
        tree.SetBool("candela:snuffed", Snuffed);
        tree.SetDouble("candela:lastUpdateHours", lastUpdateHours);
        tree.SetInt("candela:mode", (int)Mode);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        float heightBefore = HeightFactor;
        bool flamingBefore = Flaming;

        hasState = tree.HasAttribute("candela:fuel");
        Fuel = tree.GetDouble("candela:fuel");
        Snuffed = tree.GetBool("candela:snuffed");
        lastUpdateHours = tree.GetDouble("candela:lastUpdateHours");
        Mode = (BurnoutMode)tree.GetInt("candela:mode", (int)BurnoutMode.Dim);

        if (Api is ICoreClientAPI && (HeightFactor != heightBefore || Flaming != flamingBefore))
        {
            MarkDirty(true);
        }
    }

    // ----- IIgnitable: firestarters relight a bunch, and a lit one lights a torch -----

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
    {
        if (Flaming) return EnumIgniteState.NotIgnitablePreventDefault;
        if (Spent && Mode == BurnoutMode.Dark) return EnumIgniteState.NotIgnitable;
        return secondsIgniting > 2 ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        if (TryIgnite()) handling = EnumHandling.PreventDefault;
    }

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting)
    {
        if (!Flaming) return EnumIgniteState.NotIgnitable;
        return secondsIgniting > 1 ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }
}
