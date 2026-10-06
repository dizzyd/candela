using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// A fired clay mould of four wells. Set down on the ground, it is filled as vanilla's
/// tool moulds are: hold right-click on it with a pot of molten wax lifted off the
/// fire (<see cref="CarriedWax"/>). The wax sets in it, and right-clicking it holding
/// flax fibres for the wicks - or right-clicking with it in hand and the fibres in the
/// other - takes out four candles and leaves it empty; each use wears it, and it
/// cracks in the end.
///
/// Half the wicks a dipping rod's four candles take, and a mould lasts
/// <c>durability</c> uses: it saves flax, and costs clay and a firing.
///
/// Set down, it is in ground storage, four to a block, as it is to fire it in a pit
/// kiln; ground storage hands clicks on a stored item to that item
/// (IContainedInteractable).
///
/// While its candles set it steams a little, less and less, and stops once they have
/// - on the ground; in an inventory the setting bar says the same.
///
/// What it holds is the item's <c>state</c> variant - raw, fired (empty), tallow,
/// beeswax - so a full one looks full. The time it was filled is an attribute, as a
/// rod's last dip is.
/// </summary>
public class ItemCandleMould : Item, IContainedInteractable, IGroundStoredParticleEmitter
{
    public const int CandlesPerFill = 4;

    /// <summary>Game hours the candles take to set before they will come out whole.</summary>
    public const double SetHours = 0.5;

    /// <summary>Flax fibres a fill's candles take for their wicks.</summary>
    public const int WicksPerFill = 2;

    private static readonly AssetLocation Wick = new("game:flaxfibers");

    /// <summary>Real seconds right-click is held to pour.</summary>
    private const float PourSeconds = 1f;

    private const string FilledAttr = "candela:filledHours";

    public string State => Variant["state"];

    public bool IsEmpty => State == "fired";

    public bool IsFilled => State is "tallow" or "beeswax";

    /// <summary>
    /// Portions of molten wax one fill takes: as many as dipping the same four candles
    /// takes in tallow, and in beeswax the three a candle vanilla's pot recipe does.
    /// </summary>
    public static int PortionsFor(string wax) => wax == "beeswax" ? 12 : 6;

    public static bool HasSet(IWorldAccessor world, ItemStack stack) => SetProgress(world, stack) >= 1;

    /// <summary>How far the candles in it have set, from 0 just poured to 1 ready.</summary>
    public static double SetProgress(IWorldAccessor world, ItemStack stack)
    {
        if (!stack.Attributes.HasAttribute(FilledAttr)) return 1;
        return Math.Clamp((world.Calendar.TotalHours - stack.Attributes.GetDouble(FilledAttr)) / SetHours, 0, 1);
    }

    private WorldInteraction[] pourHelp, takeHelp;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        SettingBar.Register(api, this, (world, stack) => IsFilled ? SetProgress(world, stack) : 1);

        ItemStack[] pots = api.World.Blocks.Where(b => b is BlockCookingContainer).Select(b => new ItemStack(b)).ToArray();
        pourHelp = [new WorldInteraction { ActionLangCode = "candela:blockhelp-pourmould", MouseButton = EnumMouseButton.Right, Itemstacks = pots }];
        Item wick = api.World.GetItem(Wick);
        takeHelp = wick == null ? [] : [new WorldInteraction { ActionLangCode = "candela:blockhelp-takecandles", MouseButton = EnumMouseButton.Right, Itemstacks = [new ItemStack(wick, WicksPerFill)] }];
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        SettingBar.Dispose();
    }

    /// <summary><paramref name="empty"/> filled with <paramref name="wax"/> now.</summary>
    public ItemStack Filled(IWorldAccessor world, ItemStack empty, string wax)
    {
        ItemStack filled = InState(world, empty, wax);
        filled.Attributes.SetDouble(FilledAttr, world.Calendar.TotalHours);
        return filled;
    }

    /// <summary><paramref name="stack"/> as the same mould in another state, its wear kept.</summary>
    private ItemStack InState(IWorldAccessor world, ItemStack stack, string state)
    {
        var moved = new ItemStack(world.GetItem(CodeWithVariant("state", state)));
        moved.Attributes = stack.Attributes.Clone();
        moved.Attributes.RemoveAttribute(FilledAttr);
        return moved;
    }

    /// <summary>The candles a full mould of <paramref name="wax"/> gives.</summary>
    public static ItemStack Candles(IWorldAccessor world, string wax) =>
        new(world.GetItem(new AssetLocation(wax == "beeswax" ? "game:candle" : "candela:candle-tallow")), CandlesPerFill);

    /// <summary>
    /// Why <paramref name="pot"/> cannot be poured into this mould now, as the suffix
    /// of its <c>ingameerror-</c> lang key, or null if it can.
    /// </summary>
    private string CannotPour(IWorldAccessor world, ItemStack pot, out string key, out ItemMoltenWax wax, out int portions)
    {
        portions = 0;
        wax = null;
        ItemStack molten = CarriedWax.MoltenIn(world, pot, out key);

        if (IsFilled) return "mouldfull";
        if (!IsEmpty) return "mouldraw";
        if (molten == null) return "nowax";
        wax = (ItemMoltenWax)molten.Collectible;
        if (!wax.IsWorkable(world, molten)) return "waxcold";

        portions = PortionsFor(wax.Wax);
        if (molten.StackSize < portions) return "notenoughwax-" + wax.Wax;
        return null;
    }

    private static bool HoldingPot(IPlayer byPlayer) =>
        byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible is BlockCookingContainer;

    // ----- set down on the ground -----

    public bool OnContainedInteractStart(BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
        // Shift is ground storage's own: set down, pick up.
        if (byPlayer.Entity.Controls.ShiftKey) return false;
        IWorldAccessor world = byPlayer.Entity.World;
        ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;

        if (!HoldingPot(byPlayer))
        {
            // An empty one is picked up, as anything on the ground is.
            if (!IsFilled || (!hand.Empty && !IsWick(hand))) return false;

            // Claimed from here on, or ground storage would hand over a full mould.
            if (!HasSet(world, slot.Itemstack)) Error(world, "mouldsetting");
            else if (!HasWicks(hand)) Error(world, "needwicks");
            else if (world.Side == EnumAppSide.Server && TakeWicks(byPlayer.Entity, hand)) KnockOut(world, byPlayer.Entity, slot, be);
            return true;
        }

        // Claimed even when it cannot pour, or ground storage would pick the mould up.
        string error = CannotPour(world, hand.Itemstack, out _, out _, out _);
        if (error != null) Error(world, error);
        return true;
    }

    public bool OnContainedInteractStep(float secondsUsed, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
        return HoldingPot(byPlayer) && IsEmpty && secondsUsed < PourSeconds;
    }

    public void OnContainedInteractStop(float secondsUsed, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
        IWorldAccessor world = byPlayer.Entity.World;
        if (secondsUsed < PourSeconds || world.Side != EnumAppSide.Server || !HoldingPot(byPlayer)) return;

        ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (CannotPour(world, hand.Itemstack, out string key, out ItemMoltenWax wax, out int portions) != null) return;

        CarriedWax.TakeFrom(world, hand.Itemstack, key, portions);
        hand.MarkDirty();

        slot.Itemstack = Filled(world, slot.Itemstack, wax.Wax);
        slot.MarkDirty();
        be.MarkDirty(true);

        world.PlaySoundAt(new AssetLocation("game:sounds/effect/squish1"), be.Pos, 0, byPlayer);
    }

    public bool OnContainedInteractCancel(float secondsUsed, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason) => true;

    public WorldInteraction[] GetContainedInteractionHelp(BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (IsEmpty) return pourHelp;
        if (IsFilled && HasSet(byPlayer.Entity.World, slot.Itemstack)) return takeHelp;
        return [];
    }

    // ----- in hand -----

    /// <summary>
    /// Right-click with a full, set mould, the wicks in the other hand: take the
    /// candles out. Its behaviors go first, so shift-right-click still sets it down on
    /// the ground, full or not.
    /// </summary>
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
        if (!IsFilled || !firstEvent || handling != EnumHandHandling.NotHandled) return;

        handling = EnumHandHandling.PreventDefault;
        IWorldAccessor world = byEntity.World;

        if (!HasSet(world, slot.Itemstack))
        {
            Error(world, "mouldsetting");
            return;
        }
        if (!HasWicks(byEntity.LeftHandItemSlot))
        {
            Error(world, "needwicksoffhand");
            return;
        }
        if (world.Side == EnumAppSide.Server && TakeWicks(byEntity, byEntity.LeftHandItemSlot)) KnockOut(world, byEntity, slot, null);
    }

    private static bool IsWick(ItemSlot slot) => slot?.Itemstack?.Collectible.Code.Equals(Wick) == true;

    private static bool HasWicks(ItemSlot slot) => IsWick(slot) && slot.StackSize >= WicksPerFill;

    /// <summary>The wicks out of <paramref name="slot"/> - none in creative. False if they are not there.</summary>
    private static bool TakeWicks(EntityAgent byEntity, ItemSlot slot)
    {
        if (!HasWicks(slot)) return false;
        if ((byEntity as EntityPlayer)?.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative) return true;

        slot.TakeOut(WicksPerFill);
        slot.MarkDirty();
        return true;
    }

    /// <summary>The candles out to <paramref name="byEntity"/>, and the mould left empty and a use more worn.</summary>
    private void KnockOut(IWorldAccessor world, EntityAgent byEntity, ItemSlot slot, BlockEntityContainer be)
    {
        ItemStack candles = Candles(world, State);
        if (!byEntity.TryGiveItemStack(candles)) world.SpawnItemEntity(candles, byEntity.Pos.XYZ);

        slot.Itemstack = InState(world, slot.Itemstack, "fired");
        slot.MarkDirty();
        world.PlaySoundAt(new AssetLocation("game:sounds/block/ceramicplace"), byEntity, null);

        // Last, so a mould that cracks on this use is gone after giving its candles.
        DamageItem(world, byEntity, slot);

        if (be == null) return;
        be.MarkDirty(true);
        // The last mould on the spot cracked: no ground storage left holding nothing.
        if (be.Inventory.Empty) world.BlockAccessor.SetBlock(0, be.Pos);
    }

    // ----- steam while it sets -----

    private static SimpleParticleProperties steam;

    /// <summary>
    /// Asked on every client particle tick for every mould on the ground, and true now
    /// and then while it sets: one tick in five just poured, tapering to none as the
    /// wax firms up.
    /// </summary>
    public bool ShouldSpawnGSParticles(IWorldAccessor world, ItemStack stack)
    {
        if (!IsFilled) return false;
        double setting = 1 - SetProgress(world, stack);
        return setting > 0 && world.Rand.NextDouble() < 0.2 * setting;
    }

    public void DoSpawnGSParticles(IAsyncParticleManager manager, BlockPos pos, Vec3f offset)
    {
        steam ??= new SimpleParticleProperties(
            1, 1,
            ColorUtil.ToRgba(110, 240, 240, 240),
            new Vec3d(), new Vec3d(),
            new Vec3f(-0.02f, 0.06f, -0.02f), new Vec3f(0.04f, 0.1f, 0.04f),
            2f, -0.005f, 0.25f, 0.45f,
            EnumParticleModel.Quad)
        {
            SelfPropelled = true,
            WindAffected = true,
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -55),
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 0.4f),
        };

        // Somewhere over the wells, from just above the wax.
        steam.MinPos.Set(pos.X + 0.38 + offset.X, pos.InternalY + 0.35 + offset.Y, pos.Z + 0.38 + offset.Z);
        steam.AddPos.Set(0.24, 0.05, 0.24);
        manager.Spawn(steam);
    }

    private void Error(IWorldAccessor world, string error) =>
        (world.Api as ICoreClientAPI)?.TriggerIngameError(this, error, Lang.Get("candela:ingameerror-" + error));

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        if (IsEmpty) dsc.AppendLine(Lang.Get("candela:candlemould-empty", PortionsFor("tallow"), PortionsFor("beeswax")));
        else if (IsFilled && !HasSet(world, inSlot.Itemstack)) dsc.AppendLine(Lang.Get("candela:candlemould-setting"));
        else if (IsFilled) dsc.AppendLine(Lang.Get("candela:candlemould-set"));
    }
}
