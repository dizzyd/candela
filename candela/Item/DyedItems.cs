using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace candela;

/// <summary>
/// Items drawn with their wax dyed: candles, stubs, the dipping rod's coats and a
/// filled mould. Each names the texture code its wax is drawn with - "candle" for
/// candles and stubs, "wax" for the rod and the mould - and that code is pointed at
/// <c>candela:block/candle-{wax}-{dye}</c>, the rest of the item's textures left as
/// its shape and JSON give them. Undyed, an item is left to its own mesh.
///
/// In hand and in the inventory through <see cref="Render"/>; on the ground, on a
/// shelf or in a display case through each item's IContainedMeshSource, which asks
/// <see cref="Mesh"/> for the holder's atlas.
/// </summary>
public static class DyedItems
{
    // By item and dye. Main thread only: OnBeforeRender.
    private static readonly Dictionary<string, MultiTextureMeshRef> meshRefs = new();

    /// <summary>
    /// <paramref name="item"/>'s mesh with its <paramref name="waxCode"/> texture the
    /// <paramref name="wax"/> dyed <paramref name="dye"/>, its textures put in
    /// <paramref name="atlas"/>. Null if it has no shape to draw.
    /// </summary>
    public static MeshData Mesh(ICoreClientAPI capi, Item item, string waxCode, string wax, string dye, ITextureAtlasAPI atlas)
    {
        if (Shape.TryGet(capi, item.Shape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json")) is not Shape shape) return null;

        var textures = new Dictionary<string, AssetLocation>();
        foreach (var (code, location) in shape.Textures ?? []) textures[code] = location;
        foreach (var (code, texture) in item.Textures ?? []) textures[code] = texture.Base;
        textures[waxCode] = new AssetLocation($"candela:block/candle-{wax}-{dye}");

        var source = new ContainedTextureSource(capi, atlas, textures, "candela dyed " + item.Code);
        CompositeShape cs = item.Shape;
        capi.Tesselator.TesselateShape("candela dyed " + item.Code, shape, out MeshData mesh, source, new Vec3f(cs.rotateX, cs.rotateY, cs.rotateZ));
        return mesh;
    }

    /// <summary>
    /// For an item's OnBeforeRender: <paramref name="stack"/> drawn dyed, if its wax is.
    /// False if it is not, and the item should draw itself as it always does.
    /// </summary>
    public static bool Render(ICoreClientAPI capi, ItemStack stack, string waxCode, string wax, ref ItemRenderInfo renderinfo)
    {
        if (WaxDyes.Of(stack) is not string dye) return false;

        if (stack.Item is not Item item) return false;
        string key = $"{item.Code}-{dye}";
        if (!meshRefs.TryGetValue(key, out MultiTextureMeshRef meshRef))
        {
            if (Mesh(capi, item, waxCode, wax, dye, capi.ItemTextureAtlas) is not MeshData mesh) return false;
            meshRefs[key] = meshRef = capi.Render.UploadMultiTextureMesh(mesh);
        }
        renderinfo.ModelRef = meshRef;
        return true;
    }

    /// <summary>
    /// For an item's IContainedMeshSource.GenMesh: <paramref name="slot"/>'s item dyed,
    /// or null if it is not, which leaves the holder to draw it as it would.
    /// </summary>
    public static MeshData Contained(ICoreAPI api, ItemSlot slot, string waxCode, string wax, ITextureAtlasAPI targetAtlas) =>
        api is ICoreClientAPI capi && WaxDyes.Of(slot.Itemstack) is string dye && slot.Itemstack.Item is Item item ? Mesh(capi, item, waxCode, wax, dye, targetAtlas) : null;

    /// <summary>For an item's IContainedMeshSource.GetMeshCacheKey: its code, and its dye.</summary>
    public static string CacheKey(ItemSlot slot) =>
        WaxDyes.Of(slot.Itemstack) is string dye ? $"{slot.Itemstack.Collectible.Code}-{dye}" : slot.Itemstack.Collectible.Code.ToString();

    public static void Dispose()
    {
        foreach (MultiTextureMeshRef meshRef in meshRefs.Values) meshRef.Dispose();
        meshRefs.Clear();
    }
}
