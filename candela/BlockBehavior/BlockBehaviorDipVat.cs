using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Lets a firepit holding a pot of molten tallow serve as a dipping vat: hold
/// right-click on it with a dipping rod to give the rod another coat.
///
/// The firepit is the vat because it is the one place vanilla already keeps a pot's
/// contents hot - BlockEntityFirepit heats the cooking slots themselves while it
/// burns. Anywhere else the tallow would set within a minute or so of real time.
///
/// Patched onto the firepit as a behavior rather than by Harmony: BlockFirepit hands
/// any held item it does not recognise to <c>base.OnBlockInteractStart</c>, which is
/// where behaviors run.
/// </summary>
public class BlockBehaviorDipVat : BlockBehavior
{
    /// <summary>Real seconds right-click is held for one dip.</summary>
    private const float DipSeconds = 0.8f;

    public BlockBehaviorDipVat(Block block) : base(block)
    {
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible is not ItemDippingRod) return false;

        // Claimed even when the dip cannot go ahead, so the rod never falls through
        // to whatever else the firepit would do with it.
        handling = EnumHandling.PreventDefault;

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;

        string error = CannotDip(world, byPlayer, blockSel, out _);
        if (error != null)
        {
            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, error, Lang.Get("candela:ingameerror-" + error));
            return false;
        }

        return true;
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible is not ItemDippingRod) return false;

        handling = EnumHandling.PreventDefault;
        return secondsUsed < DipSeconds;
    }

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        ItemSlot rodSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (rodSlot?.Itemstack?.Collectible is not ItemDippingRod rod) return;

        handling = EnumHandling.PreventDefault;

        if (secondsUsed < DipSeconds || world.Side != EnumAppSide.Server) return;

        // Checked again: the start-of-interaction check ran a second ago, and on the
        // client's view of the firepit as well as the server's.
        if (CannotDip(world, byPlayer, blockSel, out ItemSlot tallowSlot) != null) return;

        tallowSlot.TakeOut(1);
        tallowSlot.MarkDirty();
        world.BlockAccessor.GetBlockEntity(blockSel.Position)?.MarkDirty(true);

        rodSlot.Itemstack = rod.WithAnotherLayer(world);
        rodSlot.MarkDirty();

        world.PlaySoundAt(new AssetLocation("game:sounds/effect/squish1"), blockSel.Position, 0, byPlayer);
    }

    /// <summary>
    /// Why a dip cannot go ahead, as the suffix of its <c>ingameerror-</c> lang key,
    /// or null if it can. <paramref name="tallowSlot"/> is the pot slot to draw from.
    /// </summary>
    private static string CannotDip(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out ItemSlot tallowSlot)
    {
        tallowSlot = null;
        ItemStack rodStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
        var rod = (ItemDippingRod)rodStack.Collectible;

        if (rod.IsFinished) return "rodfinished";
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityFirepit firepit) return "notallow";

        tallowSlot = ItemMoltenTallow.FindIn(firepit);

        if (tallowSlot == null) return "notallow";
        if (ItemMoltenTallow.Temperature(world, tallowSlot) < ItemMoltenTallow.SetsBelow) return "tallowcold";
        if (!ItemDippingRod.HasSet(world, rodStack)) return "coatsetting";

        return null;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)
    {
        // Empty, never null: the HUD asks for this every 15 ms while a firepit is in
        // view, and the base GetPlacedBlockInteractionHelpCount takes .Length of it.
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is not BlockEntityFirepit firepit) return [];
        if (ItemMoltenTallow.FindIn(firepit) == null) return [];

        dipHelp ??=
        [
            new WorldInteraction
            {
                ActionLangCode = "candela:blockhelp-dip",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = DippableRods(world),
            }
        ];
        return dipHelp;
    }

    private WorldInteraction[] dipHelp;

    private static ItemStack[] DippableRods(IWorldAccessor world)
    {
        var stacks = new ItemStack[ItemDippingRod.MaxLayers];
        for (int i = 0; i < stacks.Length; i++)
        {
            stacks[i] = new ItemStack(world.GetItem(new AssetLocation("candela", "dippingrod-" + i)));
        }
        return stacks;
    }
}
