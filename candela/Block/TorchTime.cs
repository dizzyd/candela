using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Vanilla's torches keeping what is left of them when picked up.
///
/// A placed lit torch already burns down - its <see cref="BlockEntityTorch"/> counts
/// hours off its transient timer - but breaking it drops a new torch, and so does a
/// torch holder, which takes any basic torch in and always gives a new one back. This
/// carries the time left through both, the way a candle stub does: rounded down to
/// the quarter, on the stack, so torches with as much left still stack and picking
/// one up never gains more than <see cref="PartBurned"/>'s grace. Under a quarter
/// left, nothing comes back.
///
/// Nothing else changes. A torch in hand, on the ground or in a holder burns as in
/// vanilla - which is to say not at all - and a stack carrying no mark is a new torch.
/// </summary>
public static class TorchTime
{
    /// <summary>
    /// Quarters left, as 75, 50 or 25: on a torch stack, and on a holder for the torch
    /// it holds. Absent for a new torch.
    /// </summary>
    public const string Attr = "candela:torchLeft";

    /// <summary>The only torch vanilla's holders take, and so the only one they give back.</summary>
    public const string HolderTorch = "game:torch-basic-lit-up";

    // BlockEntityTransient keeps its timer private. Bound by name from Bind, at load:
    // if a game update renames either field, torches go back to vanilla with a warning
    // rather than every lit torch broken throwing. Ref accessors: call one to read the
    // field, assign to the call to write it.
    private static AccessTools.FieldRef<BlockEntityTransient, double> TimerHoursLeft;
    private static AccessTools.FieldRef<BlockEntityTransient, double> TimerLastCheck;

    public static bool Enabled => CandelaConfig.Current.TorchesKeepTheirTime && TimerHoursLeft != null && TimerLastCheck != null;

    public static void Bind(ICoreAPI api)
    {
        TimerHoursLeft = TimerField("transitionHoursLeft");
        TimerLastCheck = TimerField("lastCheckAtTotalDays");
        if (TimerHoursLeft == null || TimerLastCheck == null)
        {
            api.Logger.Warning("[candela] BlockEntityTransient's timer is not where it was - torches will come back new, as in vanilla");
        }
    }

    private static AccessTools.FieldRef<BlockEntityTransient, double> TimerField(string name) =>
        AccessTools.Field(typeof(BlockEntityTransient), name) is FieldInfo { FieldType: var type } field && type == typeof(double)
            ? AccessTools.FieldRefAccess<BlockEntityTransient, double>(field)
            : null;

    /// <summary>How much of a new torch <paramref name="stack"/> is, 1 for a new one.</summary>
    public static double FractionOf(ItemStack stack) => FractionOf(stack?.Attributes);

    /// <summary>The quarters marked on <paramref name="tree"/>; anything but 75, 50 or 25 is a new torch.</summary>
    public static double FractionOf(ITreeAttribute tree) =>
        tree?.TryGetInt(Attr) is int left && left is 25 or 50 or 75 ? left / 100.0 : 1;

    /// <summary>
    /// Stamps <paramref name="torch"/>, in place, with <paramref name="fraction"/> of a
    /// new one, rounded down to the quarter: unmarked if whole. Returns it - or null if
    /// less than a quarter is left, as a candle stub does, which callers must drop.
    /// </summary>
    public static ItemStack Stamp(ItemStack torch, double fraction)
    {
        if (fraction >= 1)
        {
            torch.Attributes.RemoveAttribute(Attr);
            return torch;
        }

        int quarters = (int)Math.Floor(GameMath.Clamp(fraction, 0, 1) * 4);
        if (quarters <= 0) return null;
        torch.Attributes.SetInt(Attr, quarters * 25);
        return torch;
    }

    /// <summary>Every torch in <paramref name="drops"/> stamped, and those with nothing left gone.</summary>
    public static ItemStack[] StampTorches(ItemStack[] drops, double fraction)
    {
        for (int i = 0; i < drops.Length; i++)
        {
            if (drops[i]?.Block is BlockTorch) drops[i] = Stamp(drops[i], fraction);
        }
        return Array.FindAll(drops, s => s != null);
    }

    /// <summary>Hours a new torch of this kind burns, from its transient properties; 0 if it does not burn.</summary>
    public static double FullHours(Block torch) =>
        torch?.Attributes?["transientProps"]["inGameHours"].AsDouble(0) ?? 0;

    public static bool IsLit(Block block) => block is BlockTorch && block.Variant["state"] == "lit";

    /// <summary>
    /// Hours the placed torch <paramref name="be"/> has left, now. Its timer counts down
    /// only when it checks - every second or so, at random - so the time since the last
    /// check is taken off as well.
    /// </summary>
    private static double HoursLeftNow(BlockEntityTorch be)
    {
        double sinceCheck = Math.Max(0, (be.Api.World.Calendar.TotalDays - TimerLastCheck(be)) * be.Api.World.Calendar.HoursPerDay);
        return TimerHoursLeft(be) - sinceCheck;
    }

    /// <summary>The fraction of a new torch the placed torch <paramref name="be"/> has left, now.</summary>
    public static double FractionLeft(BlockEntityTorch be) =>
        FullHours(be.Block) is > 0 and var full ? HoursLeftNow(be) / full : 1;

    /// <summary>
    /// What the placed torch <paramref name="be"/> counts as picked up: what it has left,
    /// with <see cref="PartBurned"/>'s grace, as a candle has. Without it, a torch put
    /// down in the wrong place and picked up a few seconds later would come back a
    /// quarter less - twelve hours gone for a misclick.
    /// </summary>
    public static double FractionPickedUp(BlockEntityTorch be) =>
        PartBurned.Fraction(HoursLeftNow(be), FullHours(be.Block));

    /// <summary>Sets a torch just placed to burn for <paramref name="fraction"/> of a new one.</summary>
    public static void SetFractionLeft(BlockEntityTorch be, double fraction)
    {
        double full = FullHours(be.Block);
        if (full > 0) TimerHoursLeft(be) = full * fraction;
    }

    public static void AppendInfo(Block torch, double fraction, StringBuilder dsc)
    {
        if (fraction >= 1) return;
        dsc.AppendLine(Lang.Get("candela:torch-left", Math.Max(1, (int)Math.Round(FullHours(torch) * fraction))));
    }

    /// <summary>
    /// Gives every torch holder somewhere to keep the torch it holds, on the server once
    /// every mod's assets are in. By class rather than a JSON patch, so another mod's
    /// holder - a copy of vanilla's under its own domain - gets it too.
    /// </summary>
    public static void AddToTorchHolders(ICoreAPI api)
    {
        int added = 0;
        foreach (Block block in api.World.Blocks)
        {
            if (block is not BlockTorchHolder || block.EntityClass == null) continue;
            var types = block.BlockEntityBehaviors ?? [];
            if (Array.Exists(types, b => b.Name == BEBehaviorTorchHolderTime.Name)) continue;

            block.BlockEntityBehaviors = types.Append(new BlockEntityBehaviorType { Name = BEBehaviorTorchHolderTime.Name });
            added++;
        }

        if (added == 0)
        {
            api.Logger.Warning("[candela] found no torch holder with a block entity - holders will give back new torches");
        }
    }
}

/// <summary>The torch a holder holds: how much of a new one it is. Holders do not burn it.</summary>
public class BEBehaviorTorchHolderTime : BlockEntityBehavior
{
    public const string Name = "CandelaTorchHolder";

    /// <summary>
    /// Fraction of a new torch, 1 for a new one or none. Set only from
    /// <see cref="TorchTime.FractionOf(ItemStack)"/>, so it is always 1 or a quarter.
    /// </summary>
    public double Fraction { get; internal set; } = 1;

    public BEBehaviorTorchHolderTime(BlockEntity blockentity) : base(blockentity)
    {
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        // Rounded down to the quarter as a torch stack is, so what is saved loads back.
        if (Fraction < 1) tree.SetInt(TorchTime.Attr, (int)Math.Floor(Fraction * 4) * 25);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Fraction = TorchTime.FractionOf(tree);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (TorchTime.Enabled && Block is BlockTorchHolder { Empty: false })
        {
            TorchTime.AppendInfo(Api.World.GetBlock(new AssetLocation(TorchTime.HolderTorch)), Fraction, dsc);
        }
    }
}

/// <summary>
/// The hooks <see cref="TorchTime"/> needs. Harmony because none of these has one:
/// BlockTorch drops a new torch from its own GetDrops, BlockEntityTransient's
/// OnBlockPlaced does not call base so a block entity behavior is never told what it
/// was placed from, and BlockTorchHolder makes the torch it hands back itself.
/// </summary>
[HarmonyPatch]
public static class TorchTimePatches
{
    // Parameter names match the vanilla signatures; Harmony binds by name.

    /// <summary>A lit torch broken comes back with what was left of it.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(BlockTorch), nameof(BlockTorch.GetDrops))]
    private static void TorchDrops(BlockTorch __instance, IWorldAccessor world, BlockPos pos, ref ItemStack[] __result)
    {
        if (!TorchTime.Enabled || !TorchTime.IsLit(__instance) || __result == null) return;
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityTorch be) return;

        __result = TorchTime.StampTorches(__result, TorchTime.FractionPickedUp(be));
    }

    /// <summary>A torch placed from a part-burned stack burns only what it had left.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(BlockEntityTransient), nameof(BlockEntityTransient.OnBlockPlaced))]
    private static void TorchPlaced(BlockEntityTransient __instance, ItemStack byItemStack)
    {
        if (!TorchTime.Enabled || __instance is not BlockEntityTorch torch) return;
        double fraction = TorchTime.FractionOf(byItemStack);
        if (fraction < 1) TorchTime.SetFractionLeft(torch, fraction);
    }

    /// <summary>
    /// Into and out of a holder. In: vanilla's own, remembering what the torch had.
    /// Out: vanilla's own for a new torch; for a part-burned one, the same steps with
    /// the torch handed back as it went in. What goes in is remembered with the setting
    /// off too, so turning it back on never hands a new torch back part-burned.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(BlockTorchHolder), nameof(BlockTorchHolder.OnBlockInteractStart))]
    private static bool HolderInteract(BlockTorchHolder __instance, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result, out double? __state)
    {
        __state = null;
        var time = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorTorchHolderTime>();
        if (time == null) return true;

        if (__instance.Empty)
        {
            __state = TorchTime.FractionOf(byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack);
            return true;
        }

        if (!TorchTime.Enabled || time.Fraction >= 1) return true;

        // Never null: a holder holds no less than a quarter, and does not burn it.
        ItemStack torch = TorchTime.Stamp(new ItemStack(world.GetBlock(new AssetLocation(TorchTime.HolderTorch))), time.Fraction);
        __result = byPlayer.InventoryManager.TryGiveItemstack(torch, true);
        if (__result) TakeOut(__instance, world, byPlayer, blockSel);
        return false;
    }

    /// <summary>Remembers the torch that went in, and forgets the one that came out - vanilla's way or ours.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(BlockTorchHolder), nameof(BlockTorchHolder.OnBlockInteractStart))]
    private static void HolderInteracted(IWorldAccessor world, BlockSelection blockSel, double? __state)
    {
        var be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        if (be?.Block is not BlockTorchHolder holder || be.GetBehavior<BEBehaviorTorchHolderTime>() is not { } time) return;

        if ((holder.Empty ? 1 : __state) is not double fraction || fraction == time.Fraction) return;
        time.Fraction = fraction;
        be.MarkDirty();
    }

    // Mirrors the else-branch of vanilla's BlockTorchHolder.OnBlockInteractStart, after
    // the torch has been handed over. Check it against vanilla on every game update.
    private static void TakeOut(BlockTorchHolder holder, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        Block empty = world.GetBlock(holder.CodeWithVariant("state", "empty"));
        world.BlockAccessor.ExchangeBlock(empty.BlockId, blockSel.Position);
        if (holder.Sounds?.Place != null) world.PlaySoundAt(holder.Sounds.Place, blockSel.Position, 0.1, byPlayer);
    }

    /// <summary>A filled holder broken drops its torch as it was.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Block), nameof(Block.GetDrops))]
    private static void HolderDrops(Block __instance, IWorldAccessor world, BlockPos pos, ref ItemStack[] __result)
    {
        if (!TorchTime.Enabled || __instance is not BlockTorchHolder || __result == null) return;
        if (world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorTorchHolderTime>() is not { Fraction: < 1 } time) return;

        __result = TorchTime.StampTorches(__result, time.Fraction);
    }

    /// <summary>What is left of a part-burned torch, under vanilla's "burns for 48 hours when placed".</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(BlockTorch), nameof(BlockTorch.GetHeldItemInfo))]
    private static void TorchInfo(BlockTorch __instance, ItemSlot inSlot, StringBuilder dsc)
    {
        if (TorchTime.Enabled && TorchTime.IsLit(__instance)) TorchTime.AppendInfo(__instance, TorchTime.FractionOf(inSlot?.Itemstack), dsc);
    }
}
