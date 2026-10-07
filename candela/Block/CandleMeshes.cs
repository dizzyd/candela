using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace candela;

/// <summary>
/// The candles vanilla draws as part of a model, in their looks: a bunch's candles, a
/// chandelier's, a lantern's. Each candle is an element textured with <c>block/candle</c>
/// and its flame a child cube that samples the texture's orange corner - not a
/// particle, so tinting particles (<see cref="BlockCandelaCandles"/>) never reaches it.
///
/// The shape is copied with each candle's faces pointed at its dye's texture and its
/// flame's at its flame colour's, under <see cref="DyeTextureCode"/> and
/// <see cref="FlameTextureCode"/>. The block maps those codes to files - the beeswax or
/// tallow dyes as its wax is, and the flames tools/tint.py turns to each hue - in the
/// textures the patches add, so the atlas has them.
/// </summary>
public static class CandleMeshes
{
    /// <summary>The block texture code for <paramref name="flameColour"/>'s flame.</summary>
    public static string FlameTextureCode(string flameColour) => "candela-flame-" + flameColour;

    /// <summary>The block texture code for wax dyed <paramref name="dye"/>.</summary>
    public static string DyeTextureCode(string dye) => "candela-dye-" + dye;

    /// <summary>
    /// A copy of <paramref name="shape"/> with its candles, in the order they appear,
    /// looking as <paramref name="lookOf"/> their index says; a plain look keeps one as it
    /// was. The original is left alone - it is the shape vanilla draws plain ones from.
    /// </summary>
    public static Shape Recoloured(Shape shape, System.Func<int, CandleLook> lookOf)
    {
        Shape copy = shape.Clone();
        int index = 0;
        foreach (ShapeElement element in copy.Elements) Visit(element, lookOf, ref index);
        return copy;
    }

    /// <summary>
    /// A candle is an element textured with the candle that has a flame among its
    /// children. Every candle is counted, plain or not, so each keeps its place.
    /// </summary>
    private static void Visit(ShapeElement element, System.Func<int, CandleLook> lookOf, ref int index)
    {
        if (UsesCandle(element) && element.Children?.FirstOrDefault(IsFlame) is ShapeElement flame)
        {
            CandleLook look = lookOf(index++);
            if (WaxDyes.Get(look.Dye) is string dye) Paint(element, DyeTextureCode(dye));
            if (FlameColours.Get(look.Flame) is FlameColours.Colour colour) Paint(flame, FlameTextureCode(colour.Code));
            return;
        }
        if (element.Children == null) return;
        foreach (ShapeElement child in element.Children) Visit(child, lookOf, ref index);
    }

    /// <summary>
    /// A flame is textured with the candle and glows or is named as one: a bunch's tips
    /// ("top") do not glow, and vanilla's six-candle chandelier forgot one of its
    /// ("tip") - and a candle must still find its own flame there, or every one after
    /// it would move. The small lantern's candle has a "material-grid" child textured
    /// with it too, which is neither.
    /// </summary>
    private static bool IsFlame(ShapeElement element) => UsesCandle(element) && (Glows(element) || NamedAsFlame(element));

    private static bool UsesCandle(ShapeElement element) => Array.Exists(element.FacesResolved ?? [], f => f?.Texture == "candle");

    private static bool NamedAsFlame(ShapeElement element) =>
        element.Name is string name && (name.StartsWith("tip") || name.StartsWith("top") || name.StartsWith("flame"));

    private static bool Glows(ShapeElement element) => Array.Exists(element.FacesResolved ?? [], f => f?.Texture == "candle" && f.Glow > 0);

    /// <summary>
    /// The element's candle faces pointed at <paramref name="textureCode"/>. New faces
    /// rather than changed ones: Shape.Clone copies the face array but shares the faces
    /// in it with the original. The glow stays vanilla's.
    /// </summary>
    private static void Paint(ShapeElement element, string textureCode)
    {
        ShapeElementFace[] faces = element.FacesResolved;
        for (int i = 0; i < faces.Length; i++)
        {
            ShapeElementFace face = faces[i];
            if (face?.Texture != "candle") continue;
            faces[i] = new ShapeElementFace
            {
                Texture = textureCode,
                Uv = face.Uv,
                ReflectiveMode = face.ReflectiveMode,
                WindMode = face.WindMode,
                WindData = face.WindData,
                Rotation = face.Rotation,
                Glow = face.Glow,
                Enabled = face.Enabled,
            };
        }
    }
}

/// <summary>
/// One block's mesh with its candles in their looks, made from its own shape and
/// textures, and kept by the candles' looks in order. Filled on the tesselation thread.
/// The shape is read the first time a candle that is not plain needs it, not as the
/// game loads, so a world without treated wicks or dyed wax never reads it.
/// </summary>
public class ColouredCandleMeshes
{
    private readonly Block block;
    private readonly Lazy<Shape> shape;

    // Never emptied: a world makes a handful of combinations, not the many there could be.
    private readonly ConcurrentDictionary<string, MeshData> byLooks = new();

    public ColouredCandleMeshes(ICoreClientAPI capi, Block block)
    {
        this.block = block;
        shape = new(() => Shape.TryGet(capi, block.Shape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json")));
    }

    /// <summary>The block with its candles looking <paramref name="looks"/>, or null while all are plain.</summary>
    public MeshData For(ITesselatorAPI tesselator, IReadOnlyList<CandleLook> looks)
    {
        if (looks.All(l => l.IsPlain) || shape.Value == null) return null;

        return byLooks.GetOrAdd(string.Join(";", looks.Select(l => l.Flame + "/" + l.Dye)), _ =>
        {
            CompositeShape cs = block.Shape;
            tesselator.TesselateShape(block, CandleMeshes.Recoloured(shape.Value, i => i < looks.Count ? looks[i] : CandleLook.Plain), out MeshData mesh,
                new Vec3f(cs.rotateX, cs.rotateY, cs.rotateZ), cs.QuantityElements);
            return mesh;
        });
    }
}
