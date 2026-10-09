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
/// through being picked up, and the interactions to refuel, snuff and light it - and,
/// with an oil burner in the candle's place, to fill it, empty it and turn its wick.
///
/// Its candle and flame are part of the model, so a dyed or coloured candle, or a
/// burner, gets a copy of it in its look (<see cref="CandleMeshes"/>): placed, through <see cref="LanternFlamePatch"/>;
/// in hand, through <see cref="OnBeforeRender"/>; and on a shelf or in a display case,
/// through <see cref="IContainedMeshSource"/>. A plain one is vanilla's mesh.
///
/// Not <see cref="ColouredCandleMeshes"/>, as candles and chandeliers use: a lantern's
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
            ItemStack[] burners = api.World.GetItem(new AssetLocation(BurnerStack.Code)) is Item burner ? [new ItemStack(burner)] : [];

            // A bucket of each lamp oil to pour in, and an empty one to pour it back out.
            var bucket = api.World.GetBlock(new AssetLocation("game:woodbucket")) as BlockLiquidContainerBase;
            ItemStack[] oils = bucket == null ? [] : api.World.Items.Where(LampOil.Is).Select(oil =>
            {
                var stack = new ItemStack(bucket);
                bucket.SetContent(stack, new ItemStack(oil, (int)(LampOil.BurnerLitres * LampOil.PortionsPerLitre(new ItemStack(oil)))));
                return stack;
            }).ToArray();
            ItemStack[] empty = bucket == null ? [] : [new ItemStack(bucket)];

            return new WorldInteraction[]
            {
                new() { ActionLangCode = "candela:blockhelp-refuel", MouseButton = EnumMouseButton.Right, Itemstacks = candles },
                new() { ActionLangCode = "candela:blockhelp-putburner", MouseButton = EnumMouseButton.Right, Itemstacks = burners },
                new() { ActionLangCode = "candela:blockhelp-filloil", MouseButton = EnumMouseButton.Right, Itemstacks = oils,
                    GetMatchingStacks = (wi, bs, _) => Fuel(bs)?.HasBurner == true ? wi.Itemstacks : null },
                new() { ActionLangCode = "candela:blockhelp-emptyoil", MouseButton = EnumMouseButton.Right, Itemstacks = empty,
                    GetMatchingStacks = (wi, bs, _) => Fuel(bs) is { HasBurner: true, Oil: not null } ? wi.Itemstacks : null },
                new() { ActionLangCode = "candela:blockhelp-wick", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl", RequireFreeHand = true,
                    ShouldApply = (_, bs, _) => Fuel(bs)?.HasBurner == true },
                new() { ActionLangCode = "candela:blockhelp-snuff", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift", RequireFreeHand = true },
                new() { ActionLangCode = "candela:blockhelp-light", MouseButton = EnumMouseButton.Right, Itemstacks = torches },
            };
        });
    }

    private BEBehaviorLanternFuel Fuel(BlockSelection sel) =>
        sel == null ? null : api.World.BlockAccessor.GetBlockEntity(sel.Position)?.GetBehavior<BEBehaviorLanternFuel>();

    /// <summary>
    /// The lantern with its candle looking <paramref name="look"/>, or with an oil burner
    /// in its place; null for a plain candle.
    /// </summary>
    public MeshData ColouredMesh(ICoreClientAPI capi, ITesselatorAPI tesselator, string material, string lining, string glass, CandleLook look, bool burner)
    {
        if ((look.IsPlain && !burner) || flameShape?.Value is not Shape shape) return null;
        return colouredMeshes.GetOrAdd($"{material}-{lining}-{glass}-{MeshKey(look, burner)}", _ =>
        {
            // GenMesh keeps the metal, lining and glass it is making in fields on the
            // block while it works, and this runs on the tesselation thread for placed
            // lanterns and the main thread for held ones: one at a time, or a lantern
            // could be made with another's metal and kept so. Vanilla's own two callers
            // share the hazard, but not one cache.
            Shape fuelled = burner ? CandleMeshes.AsBurner(shape) : CandleMeshes.Recoloured(shape, _ => look);
            lock (this) return GenMesh(capi, material, lining, glass, fuelled, tesselator);
        });
    }

    /// <summary>What a lantern's mesh varies by beyond vanilla's materials.</summary>
    private static string MeshKey(CandleLook look, bool burner) => burner ? "burner" : $"{look.Flame}-{look.Dye}";

    /// <summary>The lantern a stack is, its candle in its look or its burner in, or null for a plain candle.</summary>
    private MeshData ColouredMesh(ICoreClientAPI capi, ItemStack stack) =>
        ColouredMesh(capi, capi.Tesselator, stack.Attributes.GetString("material"), stack.Attributes.GetString("lining"),
            stack.Attributes.GetString("glass", "quartz"), LanternStack.Look(stack), LanternStack.HasBurner(stack));

    /// <summary>Whether a stack draws as vanilla's lantern: a plain candle in it.</summary>
    private static bool DrawsPlain(ItemStack stack) => LanternStack.Look(stack).IsPlain && !LanternStack.HasBurner(stack);

    /// <summary>A lantern on a shelf, in a display case or on the ground: its candle in its look.</summary>
    MeshData IContainedMeshSource.GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        MeshData coloured = api is ICoreClientAPI capi ? ColouredMesh(capi, slot.Itemstack) : null;
        // A copy: the holder moves the mesh into place, and this one is cached.
        return coloured?.Clone() ?? GenMesh(slot, targetAtlas, atBlockPos);
    }

    /// <summary>Vanilla's key, and the candle's look or the burner: without it a blue lantern and a plain one would share a mesh.</summary>
    string IContainedMeshSource.GetMeshCacheKey(ItemSlot slot) => DrawsPlain(slot.Itemstack)
        ? GetMeshCacheKey(slot)
        : $"{GetMeshCacheKey(slot)}-{MeshKey(LanternStack.Look(slot.Itemstack), LanternStack.HasBurner(slot.Itemstack))}";

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        if (DrawsPlain(itemstack))
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            return;
        }

        string key = $"{itemstack.Attributes.GetString("material")}-{itemstack.Attributes.GetString("lining")}-{itemstack.Attributes.GetString("glass", "quartz")}-" +
            MeshKey(LanternStack.Look(itemstack), LanternStack.HasBurner(itemstack));
        if (!colouredMeshRefs.TryGetValue(key, out MultiTextureMeshRef meshRef))
        {
            if (ColouredMesh(capi, itemstack) is not MeshData mesh)
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
            bool burner = LanternStack.HasBurner(stack);
            return LanternStack.Adjust(full, LanternStack.Dim(api.World, stack), burner && BurnerStack.WickLow(stack), !LanternStack.Snuffed(stack),
                LanternStack.Fuel(stack) <= 0, LanternStack.Look(stack).Flame, stack.Attributes.GetString("glass"));
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
        ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        ItemStack held = slot?.Itemstack;
        bool shift = byPlayer.Entity.Controls.ShiftKey;
        var fuel = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorLanternFuel>();

        // Both sides decide alike - the client has the burner's state too - so that
        // a click the server will take is not also a vanilla pick-up on the client.
        bool snuff = held == null && shift;
        bool wick = held == null && !shift && byPlayer.Entity.Controls.CtrlKey && fuel?.HasBurner == true;
        bool light = held?.Block is BlockTorch && held.Block.Variant["state"] == "lit";
        bool refuel = !shift && (CandleWax.HoursOf(held?.Collectible) != null || BurnerStack.Is(held));
        Pour pour = shift || fuel?.HasBurner != true ? Pour.None : PourFor(held, fuel);

        if (!snuff && !wick && !light && !refuel && pour == Pour.None) return base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;
        if (world.Side != EnumAppSide.Server || fuel == null) return true;

        if (snuff) fuel.Snuff();
        else if (wick)
        {
            if (fuel.TryTurnWick()) world.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), blockSel.Position, -0.4, byPlayer, randomizePitch: true, range: 8, volume: 0.5f);
        }
        else if (light)
        {
            if (fuel.TryIgnite()) world.PlaySoundAt(new AssetLocation("game:sounds/torch-ignite"), blockSel.Position, 0, byPlayer);
        }
        else if (refuel)
        {
            if (fuel.TryRefuel(byPlayer, slot)) world.PlaySoundAt(new AssetLocation("game:sounds/block/plate"), blockSel.Position, -0.4, byPlayer);
        }
        else if (pour == Pour.In ? fuel.TryFill(byPlayer, slot) : fuel.TryEmpty(byPlayer, slot))
        {
            world.PlaySoundAt(new AssetLocation(pour == Pour.In ? "game:sounds/effect/water-pour" : "game:sounds/effect/water-fill"), blockSel.Position, -0.4, byPlayer);
        }

        return true;
    }

    private enum Pour { None, In, Out }

    /// <summary>
    /// Which way <paramref name="held"/> pours with a burner's lantern: lamp oil in, an
    /// empty container takes the oil out, and anything else is not this mod's.
    /// </summary>
    private static Pour PourFor(ItemStack held, BEBehaviorLanternFuel fuel)
    {
        if (held?.Collectible is not BlockLiquidContainerBase container) return Pour.None;
        ItemStack content = container.GetContent(held);
        if (LampOil.Is(content?.Collectible)) return Pour.In;
        return content == null && fuel.Oil != null ? Pour.Out : Pour.None;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        ItemStack stack = inSlot.Itemstack;
        if (!LanternStack.HasFuel(stack)) return;

        if (LanternStack.HasBurner(stack))
        {
            dsc.AppendLine(Lang.Get("candela:lantern-burner"));
            BurnerStack.AppendInfo(world, dsc, BurnerStack.Oil(stack), LanternStack.Fuel(stack), BurnerStack.WickLow(stack));
            return;
        }

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
