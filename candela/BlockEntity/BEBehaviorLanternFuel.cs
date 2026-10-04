using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// The candle burning inside a lantern.
///
/// A behavior on vanilla's BELantern rather than a replacement for it: the lantern's
/// block entity class, and with it everything vanilla does with lanterns, is left
/// alone. CandleStory replaced the lantern's class and lost hanging from ceilings.
///
/// A lantern holds one candle. Putting another in takes the old one out, part-burned
/// as a stub if there is enough of it left to be one.
/// </summary>
public class BEBehaviorLanternFuel : BlockEntityBehavior, IIgnitable
{
    private readonly Flame flame = new();

    /// <summary>The bunch code of the candle inside, which stands for its kind.</summary>
    public string Candle { get; private set; } = DefaultCandle;

    public const string DefaultCandle = "game:bunchocandles";

    public Flame Flame => flame;

    public BEBehaviorLanternFuel(BlockEntity blockentity) : base(blockentity)
    {
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        if (api.Side != EnumAppSide.Server) return;

        // A lantern from before Candela was installed, or one from the creative
        // inventory, has a new beeswax candle in it.
        flame.Initialize(api, KindHours(Candle));
        Blockentity.RegisterGameTickListener(OnBurnTick, 4000, api.World.Rand.Next(4000));
    }

    public override void OnBlockPlaced(ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        if (Api?.Side != EnumAppSide.Server || !LanternStack.HasFuel(byItemStack)) return;

        Candle = LanternStack.Candle(byItemStack);
        flame.SetFuel(LanternStack.Fuel(byItemStack), LanternStack.Snuffed(byItemStack));
        Changed();
    }

    private void OnBurnTick(float dt)
    {
        if (flame.Burn(Api.World.Calendar.TotalHours, 1)) Changed();
        else Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    /// <summary>The light the lantern gives, from what vanilla says it would.</summary>
    public byte[] LightHsv(byte[] full) => LanternStack.Adjust(Api.World, full, Candle, flame.Flaming, flame.Spent);

    /// <summary>
    /// Puts <paramref name="slot"/>'s candle in, handing back what is left of the old
    /// one. Server side; returns false if the held item is not a candle.
    /// </summary>
    public bool TryRefuel(IPlayer byPlayer, ItemSlot slot)
    {
        JsonObject attrs = slot.Itemstack?.Collectible.Attributes?["candela"];
        string candle = attrs?["bunch"].AsString();
        if (candle == null || !attrs["burnHours"].Exists) return false;

        ItemStack old = BlockCandelaCandles.KindOf(Api.World, Candle)?.CandleForHours(Api.World, flame.Fuel);
        if (old != null && !byPlayer.InventoryManager.TryGiveItemstack(old, slotNotifyEffect: true))
        {
            Api.World.SpawnItemEntity(old, Pos);
        }

        Candle = candle;
        flame.SetFuel(attrs["burnHours"].AsDouble());
        flame.TryIgnite(Api.World.Calendar.TotalHours);

        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) slot.TakeOut(1);
        slot.MarkDirty();

        Changed();
        return true;
    }

    public void Snuff()
    {
        if (flame.Snuff()) Changed();
    }

    public bool TryIgnite()
    {
        if (!flame.TryIgnite(Api.World.Calendar.TotalHours)) return false;
        Changed();
        return true;
    }

    /// <summary>Writes the candle into a lantern item, so that picking it up keeps what was left.</summary>
    public void WriteTo(ItemStack stack) => LanternStack.Write(stack, flame.Fuel, Candle, flame.Snuffed);

    private void Changed()
    {
        Blockentity.MarkDirty(true);
        // Re-placing the same block is what makes the engine ask GetLightHsv again;
        // vanilla does the same when a lantern's glass changes.
        Api.World.BlockAccessor.ExchangeBlock(Blockentity.Block.Id, Pos);
    }

    private double KindHours(string candle) => BlockCandelaCandles.KindOf(Api.World, candle)?.BurnHours ?? 48;

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        CandleInfo.Append(dsc, flame, flame.Fuel);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        flame.ToTreeAttributes(tree);
        tree.SetString("candela:candle", Candle);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        flame.FromTreeAttributes(tree);
        Candle = tree.GetString("candela:candle", DefaultCandle);
    }

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting) => flame.OnTryIgniteBlock(secondsIgniting);

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        if (TryIgnite()) handling = EnumHandling.PreventDefault;
    }

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) => flame.OnTryIgniteStack(secondsIgniting);
}

/// <summary>
/// The candle a lantern carries while it is an item: in hand, in a chest, or just
/// crafted. Absent attributes mean a new beeswax candle, which is what a lantern
/// from before Candela or from the creative inventory has.
/// </summary>
public static class LanternStack
{
    private const string FuelKey = "candela:fuel";
    private const string CandleKey = "candela:candle";
    private const string SnuffedKey = "candela:snuffed";

    public static bool HasFuel(ItemStack stack) => stack?.Attributes.HasAttribute(FuelKey) == true;

    public static double Fuel(ItemStack stack) => stack.Attributes.GetDouble(FuelKey);

    public static string Candle(ItemStack stack) => stack.Attributes.GetString(CandleKey, BEBehaviorLanternFuel.DefaultCandle);

    public static bool Snuffed(ItemStack stack) => stack.Attributes.GetBool(SnuffedKey);

    public static void Write(ItemStack stack, double fuel, string candle, bool snuffed)
    {
        stack.Attributes.SetDouble(FuelKey, fuel);
        stack.Attributes.SetString(CandleKey, candle);
        stack.Attributes.SetBool(SnuffedKey, snuffed);
    }

    /// <summary>
    /// A lantern's light, from what vanilla gives it: less for a sooty candle, the
    /// dim floor once spent, none when out. A copy - vanilla hands out the block
    /// entity's own array.
    /// </summary>
    public static byte[] Adjust(IWorldAccessor world, byte[] full, string candle, bool flaming, bool spent)
    {
        if (!flaming) return [0, 0, 0];

        int dim = BlockCandelaCandles.KindOf(world, candle)?.LanternDim ?? 0;
        byte[] light = [full[0], full[1], (byte)Math.Max(1, full[2] - dim)];
        if (spent) light[2] = (byte)Math.Max(2, light[2] / 3);
        return light;
    }
}
