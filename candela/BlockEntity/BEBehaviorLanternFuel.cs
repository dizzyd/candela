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
/// A lantern holds one candle, or an oil burner in its place. Putting another in takes
/// the old one out: a candle part-burned as a stub if there is enough of it left to be
/// one, a burner with its oil. A burner's fuel is in litres rather than hours, burned
/// at <see cref="LampOil.LitresPerHour"/>, so its oil keeps when the burn time is
/// changed in the config.
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

    /// <summary>How the candle looks: its flame colour and wax dye. Plain with a burner in.</summary>
    public CandleLook Look { get; private set; }

    /// <summary>An oil burner in the candle's place; <see cref="BunchCode"/> is then unused.</summary>
    public bool HasBurner { get; private set; }

    /// <summary>
    /// The burner's oil, as its item code - the sootier of two poured in together - or
    /// null when it is empty, when the burner is snuffed and cannot be lit.
    /// </summary>
    public string Oil { get; private set; }

    /// <summary>The burner's wick turned down: half the light, and the oil lasts twice as long.</summary>
    public bool WickLow { get; private set; }

    /// <summary>Fuel burned a game hour: an hour of candle, or litres of oil.</summary>
    private double Rate => HasBurner ? LampOil.LitresPerHour(WickLow) : 1;

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
        HasBurner = LanternStack.HasBurner(byItemStack);
        Oil = HasBurner ? BurnerStack.Oil(byItemStack) : null;
        WickLow = HasBurner && BurnerStack.WickLow(byItemStack);
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
        if (!flame.Burn(Api.World.Calendar.TotalHours, Rate)) return false;
        Changed(light);
        return true;
    }

    /// <summary>The light the lantern gives, from what vanilla says it would.</summary>
    public byte[] LightHsv(byte[] full) =>
        LanternStack.Adjust(full, Dim(Api.World), HasBurner && WickLow, flame.Flaming, flame.Spent, Look.Flame, (Blockentity as BELantern)?.glass);

    /// <summary>The light levels its soot costs: the candle's wax, or the burner's oil.</summary>
    private int Dim(IWorldAccessor world) =>
        HasBurner ? LampOil.Dim(world, Oil) : BlockCandelaCandles.KindOf(world, BunchCode)?.LanternDim ?? 0;

    /// <summary>
    /// Puts <paramref name="slot"/>'s candle or oil burner in, handing back the old
    /// one: what is left of a candle, or a burner with its oil. Server side; returns
    /// false if the held item is neither.
    /// </summary>
    public bool TryRefuel(IPlayer byPlayer, ItemSlot slot)
    {
        ItemStack held = slot.Itemstack;
        bool burner = BurnerStack.Is(held);
        double? hours = CandleWax.HoursOf(held?.Collectible);
        if (!burner && hours == null) return false;

        Settle();
        byte[] light = Relight.Capture(Blockentity);
        ItemStack old = HasBurner
            ? BurnerStack.Make(Api.World, Oil, flame.Fuel, WickLow)
            : BlockCandelaCandles.KindOf(Api.World, BunchCode)?.CandleForHours(Api.World, flame.Fuel, Look);
        if (old != null && !byPlayer.InventoryManager.TryGiveItemstack(old, slotNotifyEffect: true))
        {
            Api.World.SpawnItemEntity(old, Pos);
        }

        HasBurner = burner;
        if (burner)
        {
            BunchCode = DefaultBunchCode;
            Look = CandleLook.Plain;
            Oil = BurnerStack.Oil(held);
            WickLow = BurnerStack.WickLow(held);
            flame.SetFuel(Oil == null ? 0 : BurnerStack.Litres(held), snuffed: Oil == null);
        }
        else
        {
            BunchCode = CandleWax.BunchOf(held.Collectible);
            Look = CandleLook.Of(held);
            Oil = null;
            WickLow = false;
            flame.SetFuel(hours.Value);
        }
        if (!HasBurner || Oil != null) flame.TryIgnite(Api.World.Calendar.TotalHours);

        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) slot.TakeOut(1);
        slot.MarkDirty();

        Changed(light);
        return true;
    }

    /// <summary>
    /// Pours lamp oil from the container in <paramref name="slot"/> into the burner, as
    /// much as its fount has room for. Server side. Returns false if there is no burner
    /// in, or the container holds no lamp oil; true when full, though nothing moved.
    /// It is not lit by filling: an empty burner stays out until a flame is put to it.
    /// </summary>
    public bool TryFill(IPlayer byPlayer, ItemSlot slot)
    {
        if (!HasBurner || slot.Itemstack?.Collectible is not BlockLiquidContainerBase container) return false;
        ItemStack content = container.GetContent(slot.Itemstack);
        if (!LampOil.Is(content?.Collectible)) return false;

        Settle();
        double perLitre = LampOil.PortionsPerLitre(content);
        int portions = Math.Min(content.StackSize, (int)((LampOil.BurnerLitres - flame.Fuel) * perLitre + 1e-6));
        if (portions <= 0) return true;

        byte[] light = Relight.Capture(Blockentity);
        OnOne(byPlayer, slot, stack => container.TryTakeContent(stack, portions));

        // Mixed, the sootier oil's smoke is what the glass gets.
        string poured = content.Collectible.Code.ToString();
        if (Oil == null || LampOil.Dim(Api.World, poured) > LampOil.Dim(Api.World, Oil)) Oil = poured;
        flame.AddFuel(portions / perLitre);

        Changed(light);
        return true;
    }

    /// <summary>
    /// Pours the burner's oil out into the empty container in <paramref name="slot"/>, as
    /// much as it holds. Server side. Returns false if there is no oil to pour or the
    /// container is not empty. Emptied, the burner goes out.
    /// </summary>
    public bool TryEmpty(IPlayer byPlayer, ItemSlot slot)
    {
        if (!HasBurner || Oil == null || slot.Itemstack?.Collectible is not BlockLiquidContainerBase container) return false;
        if (container.GetContent(slot.Itemstack) != null || Api.World.GetItem(new AssetLocation(Oil)) is not Item oil) return false;

        Settle();
        byte[] light = Relight.Capture(Blockentity);
        var oilStack = new ItemStack(oil);
        double perLitre = LampOil.PortionsPerLitre(oilStack);
        oilStack.StackSize = (int)(flame.Fuel * perLitre + 1e-6);

        int moved = oilStack.StackSize > 0 ? OnOne(byPlayer, slot, stack => container.TryPutLiquid(stack, oilStack, (float)flame.Fuel)) : 0;
        flame.TakeFuel(moved / perLitre);

        // Less than a portion left is none: no container could take it.
        if (flame.Fuel * perLitre < 1 - 1e-6)
        {
            Oil = null;
            flame.SetFuel(0, snuffed: true);
        }

        Changed(light);
        return true;
    }

    /// <summary>
    /// <paramref name="action"/> on one of the containers in <paramref name="slot"/>,
    /// split off the stack when there are more, as vanilla does with a stack of bowls -
    /// a stack's containers share their contents.
    /// </summary>
    private T OnOne<T>(IPlayer byPlayer, ItemSlot slot, System.Func<ItemStack, T> action)
    {
        if (slot.Itemstack.StackSize == 1)
        {
            T result = action(slot.Itemstack);
            slot.MarkDirty();
            return result;
        }

        ItemStack one = slot.TakeOut(1);
        T split = action(one);
        if (!byPlayer.InventoryManager.TryGiveItemstack(one, slotNotifyEffect: true)) Api.World.SpawnItemEntity(one, byPlayer.Entity.Pos.XYZ);
        slot.MarkDirty();
        return split;
    }

    /// <summary>Turns the burner's wick down, or back up. Server side; false with no burner in.</summary>
    public bool TryTurnWick()
    {
        if (!HasBurner) return false;
        Settle();
        byte[] light = Relight.Capture(Blockentity);
        WickLow = !WickLow;
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
        if (HasBurner && Oil == null) return false;
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
        if (HasBurner) LanternStack.WriteBurner(stack, flame.Fuel, Oil, flame.Snuffed, WickLow);
        else LanternStack.Write(stack, flame.Fuel, BunchCode, flame.Snuffed, Look);
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
    public void AppendInfo(StringBuilder dsc)
    {
        double fuel = flame.FuelAt(Api.World.Calendar.TotalHours, Rate);
        if (!HasBurner)
        {
            dsc.AppendLine(LanternStack.CandleName(Api.World, BunchCode, Look));
            CandleInfo.Append(dsc, flame, fuel);
            return;
        }

        BurnerStack.AppendInfo(Api.World, dsc, Oil, fuel, WickLow, withHours: false);
        if (Oil != null) CandleInfo.Append(dsc, flame, fuel / Rate);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        flame.ToTreeAttributes(tree);
        tree.SetString("candela:candle", BunchCode);
        SaveOrRemove(tree, FlameColours.Attr, Look.Flame);
        SaveOrRemove(tree, WaxDyes.Attr, Look.Dye);
        tree.SetBool("candela:burner", HasBurner);
        SaveOrRemove(tree, "candela:oil", Oil);
        tree.SetBool("candela:wickLow", WickLow);
    }

    private static void SaveOrRemove(ITreeAttribute tree, string key, string value)
    {
        if (value != null) tree.SetString(key, value);
        else tree.RemoveAttribute(key);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        bool flamingBefore = flame.Flaming, spentBefore = flame.Spent, burnerBefore = HasBurner, wickBefore = WickLow;
        CandleLook lookBefore = Look;
        string oilBefore = Oil;
        byte[] lightBefore = Api != null && Blockentity.Block != null ? Relight.Capture(Blockentity) : null;

        flame.FromTreeAttributes(tree);
        BunchCode = tree.GetString("candela:candle", DefaultBunchCode);
        Look = new CandleLook(FlameColours.Get(tree.GetString(FlameColours.Attr))?.Code, WaxDyes.Get(tree.GetString(WaxDyes.Attr)));
        HasBurner = tree.GetBool("candela:burner");
        Oil = tree.GetString("candela:oil");
        WickLow = tree.GetBool("candela:wickLow");

        if (Api?.Side == EnumAppSide.Client)
        {
            Relight.Synced(Blockentity, lightBefore);
            return;
        }

        // State restored onto a running block entity - a schematic pasted - needs the
        // light recomputed; see BECandles.FromTreeAttributes.
        if (lightBefore != null && (flame.Flaming != flamingBefore || flame.Spent != spentBefore || Look.Flame != lookBefore.Flame
            || HasBurner != burnerBefore || Oil != oilBefore || WickLow != wickBefore))
        {
            Blockentity.RegisterDelayedCallback(_ => Changed(lightBefore), 0);
        }
    }

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting) =>
        HasBurner && Oil == null ? EnumIgniteState.NotIgnitable : flame.OnTryIgniteBlock(secondsIgniting);

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        if (TryIgnite()) handling = EnumHandling.PreventDefault;
    }

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) => flame.OnTryIgniteStack(secondsIgniting);
}

/// <summary>
/// The candle a lantern carries while it is an item: in hand, in a chest, or just
/// crafted. Absent attributes mean a new beeswax candle, which is what a lantern
/// from before Candela or from the creative inventory has. With a burner in, the fuel
/// is its litres of oil, and the oil and wick are kept as on a burner (<see cref="BurnerStack"/>).
/// </summary>
public static class LanternStack
{
    private const string FuelKey = "candela:fuel";
    // Named before the field held a bunch code; kept for the stacks already saved.
    private const string BunchCodeKey = "candela:candle";
    private const string SnuffedKey = "candela:snuffed";
    private const string BurnerKey = "candela:burner";

    public static bool HasFuel(ItemStack stack) => stack?.Attributes.HasAttribute(FuelKey) == true;

    public static bool HasBurner(ItemStack stack) => stack?.Attributes.GetBool(BurnerKey) == true;

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
        stack.Attributes.RemoveAttribute(BurnerKey);
        BurnerStack.Write(stack, null, 0, false);
    }

    public static void WriteBurner(ItemStack stack, double litres, string oil, bool snuffed, bool wickLow)
    {
        stack.Attributes.SetDouble(FuelKey, litres);
        stack.Attributes.RemoveAttribute(BunchCodeKey);
        stack.Attributes.SetBool(SnuffedKey, snuffed);
        CandleLook.Plain.Stamp(stack);
        stack.Attributes.SetBool(BurnerKey, true);
        BurnerStack.Write(stack, oil, litres, wickLow);
    }

    /// <summary>"Tallow candle", or a dyed or coloured one's name: what a candle of this kind and look is called whole.</summary>
    public static string CandleName(IWorldAccessor world, string bunchCode, CandleLook look)
    {
        BlockCandelaCandles kind = BlockCandelaCandles.KindOf(world, bunchCode);
        return kind?.CandleForHours(world, kind.BurnHours, look)?.GetName() ?? "?";
    }

    /// <summary>The light levels a lantern stack's soot costs: its candle's wax, or its burner's oil.</summary>
    public static int Dim(IWorldAccessor world, ItemStack stack) =>
        HasBurner(stack) ? LampOil.Dim(world, BurnerStack.Oil(stack)) : BlockCandelaCandles.KindOf(world, BunchCode(stack))?.LanternDim ?? 0;

    /// <summary>
    /// A lantern's light, from what vanilla gives it: <paramref name="dim"/> levels less
    /// for a sooty candle or oil, half for a wick turned down, the dim floor once spent,
    /// none when out, and the candle's flame colour unless the glass is coloured -
    /// <paramref name="full"/> already carries the glass's, and the glass wins. A copy -
    /// vanilla hands out the block entity's own array.
    /// </summary>
    public static byte[] Adjust(byte[] full, int dim, bool wickLow, bool flaming, bool spent, string flameColour, string glass)
    {
        if (!flaming) return [0, 0, 0];

        byte[] light = [full[0], full[1], (byte)Math.Max(1, full[2] - dim)];
        if (wickLow) light[2] = (byte)Math.Max(1, (light[2] + 1) / 2);
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
