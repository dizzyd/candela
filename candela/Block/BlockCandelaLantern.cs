using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Vanilla's lantern, burning a candle.
///
/// A subclass patched in over BlockLantern, so placement, hanging, glass and lining
/// all stay vanilla's. What it adds reads the candle from the lantern block entity's
/// <see cref="BEBehaviorLanternFuel"/>, or from the item's attributes while it is an
/// item: the light, the candle a crafted lantern starts with, carrying the candle
/// through being picked up, and the interactions to refuel, snuff and light it.
///
/// A coloured candle's flame is part of the model, so the lantern gets a coloured
/// copy of it (<see cref="FlameMeshes"/>): placed, through <see cref="LanternFlamePatch"/>;
/// in hand, through <see cref="OnBeforeRender"/>; and on a shelf or in a display case,
/// through <see cref="IContainedMeshSource"/>. A plain one is vanilla's mesh.
///
/// Not <see cref="ColouredFlameMeshes"/>, as candles and chandeliers use: a lantern's
/// mesh is made by vanilla's GenMesh, which picks its metal, lining and glass.
/// </summary>
public class BlockCandelaLantern : BlockLantern, IContainedMeshSource
{
    private WorldInteraction[] extraInteractions;

    private System.Lazy<Shape> flameShape;

    // By material, lining, glass and flame colour, as vanilla keys its own. Placed
    // ones are filled on the tesselation thread.
    private readonly ConcurrentDictionary<string, MeshData> colouredMeshes = new();
    private readonly Dictionary<string, MultiTextureMeshRef> colouredMeshRefs = new();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        if (api is ICoreClientAPI capi)
        {
            flameShape = new(() => Vintagestory.API.Common.Shape.TryGet(capi, Shape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json")));
        }

        extraInteractions = ObjectCacheUtil.GetOrCreate(api, "candelaLanternInteractions", () =>
        {
            ItemStack[] candles = api.World.Collectibles.Where(CandleWax.IsCandle).Select(c => new ItemStack(c)).ToArray();
            ItemStack[] torches = api.World.SearchBlocks(new AssetLocation("game:torch-*-lit-*")).Select(b => new ItemStack(b)).ToArray();

            return new WorldInteraction[]
            {
                new() { ActionLangCode = "candela:blockhelp-refuel", MouseButton = EnumMouseButton.Right, Itemstacks = candles },
                new() { ActionLangCode = "candela:blockhelp-snuff", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift", RequireFreeHand = true },
                new() { ActionLangCode = "candela:blockhelp-light", MouseButton = EnumMouseButton.Right, Itemstacks = torches },
            };
        });
    }

    /// <summary>The lantern with a <paramref name="flameColour"/> flame, or null for a plain one.</summary>
    public MeshData ColouredMesh(ICoreClientAPI capi, ITesselatorAPI tesselator, string material, string lining, string glass, string flameColour)
    {
        if (FlameColours.Get(flameColour) == null || flameShape?.Value is not Shape shape) return null;
        return colouredMeshes.GetOrAdd($"{material}-{lining}-{glass}-{flameColour}", _ =>
        {
            // GenMesh keeps the metal, lining and glass it is making in fields on the
            // block while it works, and this runs on the tesselation thread for placed
            // lanterns and the main thread for held ones: one at a time, or a lantern
            // could be made with another's metal and kept so. Vanilla's own two callers
            // share the hazard, but not one cache.
            lock (this) return GenMesh(capi, material, lining, glass, FlameMeshes.Recoloured(shape, _ => flameColour), tesselator);
        });
    }

    /// <summary>A lantern on a shelf, in a display case or on the ground: its flame in its colour.</summary>
    MeshData IContainedMeshSource.GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        ItemStack stack = slot.Itemstack;
        string flameColour = LanternStack.Look(stack).Flame;
        MeshData coloured = flameColour == null || api is not ICoreClientAPI capi ? null
            : ColouredMesh(capi, capi.Tesselator, stack.Attributes.GetString("material"), stack.Attributes.GetString("lining"),
                stack.Attributes.GetString("glass", "quartz"), flameColour);
        // A copy: the holder moves the mesh into place, and this one is cached.
        return coloured?.Clone() ?? GenMesh(slot, targetAtlas, atBlockPos);
    }

    /// <summary>Vanilla's key, and the flame colour: without it a blue lantern and a plain one would share a mesh.</summary>
    string IContainedMeshSource.GetMeshCacheKey(ItemSlot slot) =>
        LanternStack.Look(slot.Itemstack).Flame is string flameColour ? GetMeshCacheKey(slot) + "-" + flameColour : GetMeshCacheKey(slot);

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        string flameColour = LanternStack.Look(itemstack).Flame;
        if (flameColour == null)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            return;
        }

        string material = itemstack.Attributes.GetString("material");
        string lining = itemstack.Attributes.GetString("lining");
        string glass = itemstack.Attributes.GetString("glass", "quartz");
        string key = $"{material}-{lining}-{glass}-{flameColour}";
        if (!colouredMeshRefs.TryGetValue(key, out MultiTextureMeshRef meshRef))
        {
            MeshData mesh = ColouredMesh(capi, capi.Tesselator, material, lining, glass, flameColour);
            if (mesh == null)
            {
                base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
                return;
            }
            colouredMeshRefs[key] = meshRef = capi.Render.UploadMultiTextureMesh(mesh);
        }

        renderinfo.ModelRef = meshRef;
        renderinfo.CullFaces = false;
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        foreach (MultiTextureMeshRef meshRef in colouredMeshRefs.Values) meshRef.Dispose();
        colouredMeshRefs.Clear();
    }

    public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
    {
        byte[] full = base.GetLightHsv(blockAccessor, pos, stack);

        if (pos != null)
        {
            var fuel = blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorLanternFuel>();
            return fuel != null ? fuel.LightHsv(full) : full;
        }

        // In hand: the candle does not burn there, but it shows what is left of it.
        // The burnout mode lives on the server, so a spent one is shown guttering.
        if (stack != null && LanternStack.HasFuel(stack) && api != null)
        {
            return LanternStack.Adjust(api.World, full, LanternStack.BunchCode(stack), !LanternStack.Snuffed(stack), LanternStack.Fuel(stack) <= 0,
                LanternStack.Look(stack).Flame, stack.Attributes.GetString("glass"));
        }
        return full;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        ItemStack stack = base.OnPickBlock(world, pos);
        world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorLanternFuel>()?.WriteTo(stack);
        return stack;
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        if (outputSlot.Itemstack == null) return;

        // The candle it was made with is the candle it starts out burning.
        foreach (ItemSlot slot in allInputSlots)
        {
            CollectibleObject candle = slot.Itemstack?.Collectible;
            if (CandleWax.HoursOf(candle) is not double hours) continue;

            LanternStack.Write(outputSlot.Itemstack, hours, CandleWax.BunchOf(candle), snuffed: false, CandleLook.Of(slot.Itemstack));
            return;
        }
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        ItemStack held = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
        bool shift = byPlayer.Entity.Controls.ShiftKey;

        bool snuff = held == null && shift;
        bool light = held?.Block is BlockTorch && held.Block.Variant["state"] == "lit";
        bool refuel = !shift && CandleWax.HoursOf(held?.Collectible) != null;

        if (!snuff && !light && !refuel) return base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;
        if (world.Side != EnumAppSide.Server) return true;

        var fuel = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorLanternFuel>();
        if (fuel == null) return true;

        if (snuff) fuel.Snuff();
        else if (light)
        {
            if (fuel.TryIgnite()) world.PlaySoundAt(new AssetLocation("game:sounds/torch-ignite"), blockSel.Position, 0, byPlayer);
        }
        else if (fuel.TryRefuel(byPlayer, byPlayer.InventoryManager.ActiveHotbarSlot))
        {
            world.PlaySoundAt(new AssetLocation("game:sounds/block/plate"), blockSel.Position, -0.4, byPlayer);
        }

        return true;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        ItemStack stack = inSlot.Itemstack;
        if (!LanternStack.HasFuel(stack)) return;

        BlockCandelaCandles kind = BlockCandelaCandles.KindOf(world, LanternStack.BunchCode(stack));
        string candleName = kind == null ? "?" : kind.CandleForHours(world, kind.BurnHours, LanternStack.Look(stack))?.GetName() ?? "?";
        double hours = LanternStack.Fuel(stack);

        dsc.AppendLine(hours > 0
            ? Lang.Get("candela:lantern-candle", candleName, System.Math.Max(1, (int)System.Math.Round(hours)))
            : Lang.Get("candela:lantern-candle-spent", candleName));
    }

    /// <summary>
    /// Vanilla's info, then the candle's. Added here rather than by the block entity
    /// behavior, which never gets asked: BELantern.GetBlockInfo writes the materials
    /// and does not call base, where behaviors would run.
    /// </summary>
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        string info = base.GetPlacedBlockInfo(world, pos, forPlayer);
        var fuel = world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorLanternFuel>();
        if (fuel == null) return info;

        var dsc = new StringBuilder(info);
        if (dsc.Length > 0) dsc.AppendLine();
        fuel.AppendInfo(dsc);
        return dsc.ToString().TrimEnd();
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer).Append(extraInteractions);
    }
}
