using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Lets a firepit holding a pot of molten wax serve as a vat: hold right-click on it
/// with something that takes wax (an <see cref="IWaxWorker"/>) - a dipping rod, for
/// another coat; an empty candle mould, to fill it.
///
/// The firepit is the vat because it is the one place vanilla already keeps a pot's
/// contents hot - BlockEntityFirepit heats the cooking slots themselves while it
/// burns. Anywhere else the wax would set within a minute or so of real time.
///
/// Patched onto the firepit as a behavior rather than by Harmony: BlockFirepit hands
/// any held item it does not recognise to <c>base.OnBlockInteractStart</c>, which is
/// where behaviors run. Those run in order, and the firepit's own Container behavior
/// opens its GUI and stops the chain, so the patch inserts this one ahead of it -
/// behind Lockable, so a locked firepit stays locked.
/// </summary>
public class BlockBehaviorDipVat : BlockBehavior
{
    /// <summary>Real seconds right-click is held for one dip or one fill.</summary>
    private const float WorkSeconds = 0.8f;

    public BlockBehaviorDipVat(Block block) : base(block)
    {
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (Held(byPlayer) is not ItemStack held) return false;

        // Claimed even when the work cannot go ahead, so the item never falls through
        // to whatever else the firepit would do with it - Container, next in line,
        // would open the GUI.
        handling = EnumHandling.PreventSubsequent;

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;

        string error = CannotWork(world, held, blockSel, out _, out _);
        if (error != null)
        {
            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, error, Lang.Get("candela:ingameerror-" + error));
            return false;
        }

        return true;
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (Held(byPlayer) == null) return false;

        handling = EnumHandling.PreventDefault;
        return secondsUsed < WorkSeconds;
    }

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (Held(byPlayer) is not ItemStack held) return;

        handling = EnumHandling.PreventDefault;

        if (secondsUsed < WorkSeconds || world.Side != EnumAppSide.Server) return;

        // Checked again: the start-of-interaction check ran a second ago, and on the
        // client's view of the firepit as well as the server's.
        if (CannotWork(world, held, blockSel, out ItemSlot waxSlot, out int portions) != null) return;

        string wax = ((ItemMoltenWax)waxSlot.Itemstack.Collectible).Wax;
        waxSlot.TakeOut(portions);
        waxSlot.MarkDirty();
        world.BlockAccessor.GetBlockEntity(blockSel.Position)?.MarkDirty(true);

        ItemSlot heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        heldSlot.Itemstack = ((IWaxWorker)held.Collectible).Worked(world, held, wax);
        heldSlot.MarkDirty();

        world.PlaySoundAt(new AssetLocation("game:sounds/effect/squish1"), blockSel.Position, 0, byPlayer);
    }

    /// <summary>The held stack, if it is something that takes wax.</summary>
    private static ItemStack Held(IPlayer byPlayer)
    {
        ItemStack held = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
        return held?.Collectible is IWaxWorker ? held : null;
    }

    /// <summary>
    /// Why the held item cannot take wax from this firepit now, as the suffix of its
    /// <c>ingameerror-</c> lang key, or null if it can.
    /// </summary>
    private static string CannotWork(IWorldAccessor world, ItemStack held, BlockSelection blockSel, out ItemSlot waxSlot, out int portions)
    {
        waxSlot = null;
        portions = 0;
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityFirepit firepit) return "nowax";
        return ((IWaxWorker)held.Collectible).CannotWork(world, held, firepit, out waxSlot, out portions);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)
    {
        // Empty, never null: the HUD asks for this every 15 ms while a firepit is in
        // view, and the base GetPlacedBlockInteractionHelpCount takes .Length of it.
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is not BlockEntityFirepit firepit) return [];
        if (ItemMoltenWax.FindIn(firepit)?.Itemstack.Collectible is not ItemMoltenWax wax) return [];

        if (!helpByWax.TryGetValue(wax.Wax, out var help))
        {
            help = helpByWax[wax.Wax] = Help(world, wax.Wax);
        }
        return help;
    }

    private readonly Dictionary<string, WorldInteraction[]> helpByWax = new();

    /// <summary>The help for a pot of <paramref name="wax"/>: one line per kind of thing that takes it.</summary>
    private static WorldInteraction[] Help(IWorldAccessor world, string wax)
    {
        var help = new List<WorldInteraction>();
        foreach (var (lang, prefix) in new[] { ("candela:blockhelp-dip", "dippingrod-"), ("candela:blockhelp-fillmould", "candlemould-") })
        {
            ItemStack[] takers = world.Items
                .Where(item => item.Code?.Domain == "candela" && item.Code.Path.StartsWith(prefix) && item is IWaxWorker)
                .Select(item => new ItemStack(item))
                .Where(stack => ((IWaxWorker)stack.Collectible).Takes(stack, wax))
                .ToArray();
            if (takers.Length == 0) continue;

            help.Add(new WorldInteraction { ActionLangCode = lang, MouseButton = EnumMouseButton.Right, Itemstacks = takers });
        }
        return help.ToArray();
    }
}
