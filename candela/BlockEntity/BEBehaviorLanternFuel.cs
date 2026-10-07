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

    /// <summary>
    /// The kind of candle inside, as the code of the bunch block that stands for it
    /// (<see cref="BlockCandelaCandles.KindOf"/>) - not the candle item's code.
    /// </summary>
    public string BunchCode { get; private set; } = DefaultBunchCode;

    public const string DefaultBunchCode = "game:bunchocandles";

    /// <summary>How the candle looks: its flame colour and wax dye.</summary>
    public CandleLook Look { get; private set; }

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
        flame.Initialize(api, KindHours(BunchCode));
        Blockentity.RegisterGameTickListener(OnBurnTick, 4000, api.World.Rand.Next(4000));

        // The time it was unloaded burned at once, rather than up to four seconds on.
        Blockentity.RegisterDelayedCallback(_ => Settle(), 0);
    }

    public override void OnBlockPlaced(ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        if (Api?.Side != EnumAppSide.Server || !LanternStack.HasFuel(byItemStack)) return;

        byte[] light = Relight.Capture(Blockentity);
        BunchCode = LanternStack.BunchCode(byItemStack);
        Look = CandleLook.Of(byItemStack);
        flame.SetFuel(LanternStack.Fuel(byItemStack), LanternStack.Snuffed(byItemStack));
        Changed(light);
    }

    private void OnBurnTick(float dt)
    {
        if (!Settle()) Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    /// <summary>
    /// Burns the time since the last burn. Server side. Everything that changes or
    /// reads out the candle calls this first - see <see cref="Flame"/>. Returns true
    /// if the light changed.
    /// </summary>
    public bool Settle()
    {
        if (Api?.Side != EnumAppSide.Server) return false;

        byte[] light = Relight.Capture(Blockentity);
        if (!flame.Burn(Api.World.Calendar.TotalHours, 1)) return false;
        Changed(light);
        return true;
    }

    /// <summary>The light the lantern gives, from what vanilla says it would.</summary>
    public byte[] LightHsv(byte[] full) =>
        LanternStack.Adjust(Api.World, full, BunchCode, flame.Flaming, flame.Spent, Look.Flame, (Blockentity as BELantern)?.glass);

    /// <summary>
    /// Puts <paramref name="slot"/>'s candle in, handing back what is left of the old
    /// one. Server side; returns false if the held item is not a candle.
    /// </summary>
    public bool TryRefuel(IPlayer byPlayer, ItemSlot slot)
    {
        CollectibleObject held = slot.Itemstack?.Collectible;
        if (CandleWax.HoursOf(held) is not double hours) return false;
        string bunchCode = CandleWax.BunchOf(held);
        CandleLook look = CandleLook.Of(slot.Itemstack);

        Settle();
        byte[] light = Relight.Capture(Blockentity);
        ItemStack old = BlockCandelaCandles.KindOf(Api.World, BunchCode)?.CandleForHours(Api.World, flame.Fuel, Look);
        if (old != null && !byPlayer.InventoryManager.TryGiveItemstack(old, slotNotifyEffect: true))
        {
            Api.World.SpawnItemEntity(old, Pos);
        }

        BunchCode = bunchCode;
        Look = look;
        flame.SetFuel(hours);
        flame.TryIgnite(Api.World.Calendar.TotalHours);

        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) slot.TakeOut(1);
        slot.MarkDirty();

        Changed(light);
        return true;
    }

    public void Snuff()
    {
        Settle();
        byte[] light = Relight.Capture(Blockentity);
        if (flame.Snuff()) Changed(light);
    }

    public bool TryIgnite()
    {
        Settle();
        byte[] light = Relight.Capture(Blockentity);
        if (!flame.TryIgnite(Api.World.Calendar.TotalHours)) return false;
        Changed(light);
        return true;
    }

    /// <summary>Writes the candle into a lantern item, so that picking it up keeps what was left.</summary>
    public void WriteTo(ItemStack stack)
    {
        Settle();
        LanternStack.Write(stack, flame.Fuel, BunchCode, flame.Snuffed, Look);
    }

    /// <summary>State that affects the light has changed; <paramref name="lightBefore"/> is what it was.</summary>
    private void Changed(byte[] lightBefore)
    {
        Blockentity.MarkDirty(true);
        Relight.After(Blockentity, lightBefore);
    }

    private double KindHours(string bunchCode) => BlockCandelaCandles.KindOf(Api.World, bunchCode)?.BurnHours ?? 48;

    /// <summary>
    /// The candle's line of block info. Not a GetBlockInfo override: vanilla's
    /// BELantern.GetBlockInfo does not call base, so behaviors' never run - see
    /// BlockCandelaLantern.GetPlacedBlockInfo.
    /// </summary>
    public void AppendInfo(StringBuilder dsc) => CandleInfo.Append(dsc, flame, flame.FuelAt(Api.World.Calendar.TotalHours, 1));

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        flame.ToTreeAttributes(tree);
        tree.SetString("candela:candle", BunchCode);
        SaveOrRemove(tree, FlameColours.Attr, Look.Flame);
        SaveOrRemove(tree, WaxDyes.Attr, Look.Dye);
    }

    private static void SaveOrRemove(ITreeAttribute tree, string key, string value)
    {
        if (value != null) tree.SetString(key, value);
        else tree.RemoveAttribute(key);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        bool flamingBefore = flame.Flaming, spentBefore = flame.Spent;
        CandleLook lookBefore = Look;
        byte[] lightBefore = Api != null && Blockentity.Block != null ? Relight.Capture(Blockentity) : null;

        flame.FromTreeAttributes(tree);
        BunchCode = tree.GetString("candela:candle", DefaultBunchCode);
        Look = new CandleLook(FlameColours.Get(tree.GetString(FlameColours.Attr))?.Code, WaxDyes.Get(tree.GetString(WaxDyes.Attr)));

        if (Api?.Side == EnumAppSide.Client)
        {
            Relight.Synced(Blockentity, lightBefore);
            return;
        }

        // State restored onto a running block entity - a schematic pasted - needs the
        // light recomputed; see BECandles.FromTreeAttributes.
        if (lightBefore != null && (flame.Flaming != flamingBefore || flame.Spent != spentBefore || Look.Flame != lookBefore.Flame))
        {
            Blockentity.RegisterDelayedCallback(_ => Changed(lightBefore), 0);
        }
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
    // Named before the field held a bunch code; kept for the stacks already saved.
    private const string BunchCodeKey = "candela:candle";
    private const string SnuffedKey = "candela:snuffed";

    public static bool HasFuel(ItemStack stack) => stack?.Attributes.HasAttribute(FuelKey) == true;

    public static double Fuel(ItemStack stack) => stack.Attributes.GetDouble(FuelKey);

    public static string BunchCode(ItemStack stack) => stack.Attributes.GetString(BunchCodeKey, BEBehaviorLanternFuel.DefaultBunchCode);

    public static bool Snuffed(ItemStack stack) => stack.Attributes.GetBool(SnuffedKey);

    /// <summary>How the lantern's candle looks - on the lantern's stack, under the candle's own attributes.</summary>
    public static CandleLook Look(ItemStack stack) => CandleLook.Of(stack);

    public static void Write(ItemStack stack, double fuel, string bunchCode, bool snuffed, CandleLook look)
    {
        stack.Attributes.SetDouble(FuelKey, fuel);
        stack.Attributes.SetString(BunchCodeKey, bunchCode);
        stack.Attributes.SetBool(SnuffedKey, snuffed);
        look.Stamp(stack);
    }

    /// <summary>
    /// A lantern's light, from what vanilla gives it: less for a sooty candle, the
    /// dim floor once spent, none when out, and the candle's flame colour unless the
    /// glass is coloured - <paramref name="full"/> already carries the glass's, and the
    /// glass wins. A copy - vanilla hands out the block entity's own array.
    /// </summary>
    public static byte[] Adjust(IWorldAccessor world, byte[] full, string bunchCode, bool flaming, bool spent, string flameColour, string glass)
    {
        if (!flaming) return [0, 0, 0];

        int dim = BlockCandelaCandles.KindOf(world, bunchCode)?.LanternDim ?? 0;
        byte[] light = [full[0], full[1], (byte)Math.Max(1, full[2] - dim)];
        if (spent) light[2] = (byte)Math.Max(2, light[2] / 3);
        return IsColouredGlass(glass) ? light : FlameColours.Tint(light, flameColour);
    }

    /// <summary>
    /// Whether vanilla colours a lantern's light for this glass - asked of vanilla's
    /// own table rather than copied from it, so glass it adds later counts too.
    /// </summary>
    public static bool IsColouredGlass(string glass)
    {
        if (glass == null) return false;
        byte[] probe = [255, 255, 0];
        BELantern.setLightColor([255, 255, 0], probe, glass);
        return probe[0] != 255 || probe[1] != 255;
    }
}
