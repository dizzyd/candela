using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// The clay cooking pot, when it carries wax off the fire (<see cref="CarriedWax"/>):
/// drawn with the wax in it - in hand, in an inventory, dropped and set down on the
/// ground - says in its tooltip what it holds, and pours rather than setting a block
/// down. A pot carrying nothing is left to vanilla throughout.
///
/// Patched onto the pot as a behavior: the pot asks its behaviors for each of these,
/// and ground storage finds a mesh source among them (IContainedMeshSource).
/// </summary>
public class CollectibleBehaviorPotOfWax : CollectibleBehavior, IContainedMeshSource
{
    /// <summary>Fill heights a carried pot is drawn at: eighths.</summary>
    private const int FillSteps = 8;

    /// <summary>Where each kind of surface is in the block atlas, by the code of what it shows.</summary>
    private static readonly Dictionary<string, TextureAtlasPosition> surfaces = new();

    private static readonly Dictionary<string, MultiTextureMeshRef> meshRefs = new();

    private ICoreClientAPI capi;

    public CollectibleBehaviorPotOfWax(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        if (api is not ICoreClientAPI clientApi) return;
        capi = clientApi;

        // Into the block atlas now, on the main thread: ground storage builds its
        // meshes on the tesselation thread, which can only look them up.
        foreach (var wax in api.World.Items.OfType<ItemMoltenWax>())
        {
            AddSurface(wax);
            if (SetInto(wax) is Item set) AddSurface(set);
        }

        // Dyed wax shows as vanilla's liquid dye.
        foreach (string dye in WaxDyes.All)
        {
            capi.BlockTextureAtlas.GetOrInsertTexture(new AssetLocation("game:block/liquid/dye/" + dye), out _, out TextureAtlasPosition pos);
            if (pos != null) surfaces[DyeKey(dye)] = pos;
        }
    }

    private static string DyeKey(string dye) => "dye-" + dye;

    /// <summary>The surface molten wax dyed <paramref name="dye"/> shows, or null if it has none.</summary>
    public static TextureAtlasPosition DyeSurface(string dye) => dye != null && surfaces.TryGetValue(DyeKey(dye), out var pos) ? pos : null;

    private void AddSurface(CollectibleObject shown)
    {
        if (surfaces.ContainsKey(shown.Code.ToString())) return;
        CompositeTexture texture = shown.Attributes?["inContainerTexture"].AsObject<CompositeTexture>(null, shown.Code.Domain);
        if (texture == null) return;
        capi.BlockTextureAtlas.GetOrInsertTexture(texture.Base, out _, out TextureAtlasPosition pos);
        if (pos != null) surfaces[shown.Code.ToString()] = pos;
    }

    private static Item SetInto(ItemMoltenWax wax) =>
        wax.TransitionableProps?.FirstOrDefault(p => p.Type == EnumTransitionType.Harden)?.TransitionedStack?.ResolvedItemstack?.Item;

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        foreach (var mesh in meshRefs.Values) mesh.Dispose();
        meshRefs.Clear();
        surfaces.Clear();
    }

    // ----- drawing it -----

    /// <summary>
    /// What a carried pot shows - its contents, or once molten wax has cooled below
    /// its setting point what it sets into - how full, and the key it is cached by.
    /// Null if it carries nothing, or nothing there is a surface for.
    /// </summary>
    private string Shows(ItemStack pot, out TextureAtlasPosition surface, out float fill)
    {
        surface = null;
        fill = 0;
        if (pot?.Attributes[CarriedWax.Contents] is not ITreeAttribute contents) return null;

        ItemStack stack = contents.Select(kv => contents.GetItemstack(kv.Key)).FirstOrDefault(s => s?.ResolveBlockOrItem(capi.World) == true);
        if (stack == null) return null;

        CollectibleObject shown = stack.Collectible;
        string dye = null;
        float full = 24;
        if (shown is ItemMoltenWax wax)
        {
            full = wax.FullPot;
            // Set, it shows as what it set into, which is undyed.
            if (!wax.IsWorkable(capi.World, stack) && SetInto(wax) is Item set) shown = set;
            else dye = WaxDyes.Of(stack);
        }
        string shownKey = dye != null ? DyeKey(dye) : shown.Code.ToString();
        if (!surfaces.TryGetValue(shownKey, out surface)) return null;

        int step = (int)Math.Ceiling(GameMath.Clamp(stack.StackSize / full, 0, 1) * FillSteps);
        fill = step / (float)FillSteps;
        return $"candela-potofwax-{pot.Collectible.Code}-{shownKey}-{step}";
    }

    /// <summary>
    /// As the pot on the fire is drawn (WaxPotRenderer): vanilla's open pot, and the
    /// liquid surface meals in a bowl use, at the height of what is in it.
    /// </summary>
    private MeshData Mesh(ItemStack pot, TextureAtlasPosition surface, float fill)
    {
        Block cooked = capi.World.GetBlock(pot.Collectible.CodeWithVariant("type", "cooked")) ?? pot.Block;
        capi.Tesselator.TesselateShape(cooked, Shape.TryGet(capi, "shapes/block/clay/pot-opened-empty.json"), out MeshData mesh);
        capi.Tesselator.TesselateShape("candela pot of wax", Shape.TryGet(capi, "shapes/block/food/meal/liquid.json"), out MeshData liquid,
            new OneTexture(surface, capi.BlockTextureAtlas.Size));

        liquid.Translate(0, (0.3f + 2.2f * fill) / 16f, 0);
        mesh.AddMeshData(liquid);
        return mesh;
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        if (Shows(itemstack, out var surface, out float fill) is not string key) return;
        if (!meshRefs.TryGetValue(key, out var mesh)) mesh = meshRefs[key] = capi.Render.UploadMultiTextureMesh(Mesh(itemstack, surface, fill));
        renderinfo.ModelRef = mesh;
    }

    // Ground storage: null and the pot's own code for a pot carrying nothing, which
    // is what it would have used without this.

    public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos) =>
        capi != null && Shows(slot.Itemstack, out var surface, out float fill) != null ? Mesh(slot.Itemstack, surface, fill) : null;

    public string GetMeshCacheKey(ItemSlot slot) =>
        (capi != null ? Shows(slot.Itemstack, out _, out _) : null) ?? slot.Itemstack.Collectible.Code.ToString();

    // ----- in hand -----

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (CarriedWax.Describe(world, inSlot?.Itemstack) is string holds) dsc.AppendLine(Lang.Get("candela:pot-holds", holds));
    }

    /// <summary>
    /// A pot of molten wax pours, as a crucible does. The pot's own use animation is
    /// two hands setting a block down, which held over a pour rocks the pot back and
    /// forth.
    /// </summary>
    public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity forEntity, ref EnumHandling bhHandling)
    {
        if (forEntity?.World is not IWorldAccessor world || CarriedWax.MoltenIn(world, activeHotbarSlot?.Itemstack, out _) == null) return null;
        bhHandling = EnumHandling.PreventDefault;
        return "pour";
    }
}

/// <summary>One atlas position for every texture code a shape asks for: a liquid surface.</summary>
public class OneTexture(TextureAtlasPosition pos, Size2i atlasSize) : ITexPositionSource
{
    public TextureAtlasPosition this[string textureCode] => pos;
    public Size2i AtlasSize => atlasSize;
}
