using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace candela;

/// <summary>
/// The flames vanilla draws as part of a model, in a flame colour. A chandelier's
/// candle tips, a lantern's candle top and the tips on a bunch's candles are small
/// cubes that sample the orange corner of <c>block/candle</c>, not particles, so
/// tinting particles (<see cref="BlockCandelaCandles"/>) never reaches them. Instead
/// the shape is copied with those cubes' faces pointed at
/// <c>candela:block/flame-{colour}</c> - the candle texture turned to the flame's hue
/// by tools/tint.py - which the patches add to the block's textures under
/// <see cref="TextureCode"/>, so the atlas has them.
/// </summary>
public static class FlameMeshes
{
    /// <summary>The block texture code for <paramref name="flameColour"/>'s flame.</summary>
    public static string TextureCode(string flameColour) => "candela-flame-" + flameColour;

    /// <summary>
    /// A copy of <paramref name="shape"/> with its flames, in the order they appear,
    /// burning <paramref name="colourOf"/> their index; null keeps one orange. The
    /// original is left alone - it is the shape vanilla draws plain ones from.
    /// </summary>
    public static Shape Recoloured(Shape shape, System.Func<int, string> colourOf)
    {
        Shape copy = shape.Clone();
        int index = 0;
        foreach (ShapeElement element in copy.Elements) Visit(element, inCandle: false, colourOf, ref index);
        return copy;
    }

    /// <summary>
    /// A flame is a child of a candle, both textured with it, that glows or is named as
    /// one: a bunch's tips ("top") do not glow, and vanilla's six-candle chandelier
    /// forgot one of its ("tip") - and a candle must still find its own flame there, or
    /// every one after it would move. The small lantern's candle has a "material-grid"
    /// child textured with it too, which is neither.
    /// </summary>
    private static void Visit(ShapeElement element, bool inCandle, System.Func<int, string> colourOf, ref int index)
    {
        bool candle = UsesCandle(element);
        if (inCandle && candle && (Glows(element) || NamedAsFlame(element)))
        {
            if (FlameColours.Get(colourOf(index)) is FlameColours.Colour colour) Paint(element, colour.Code);
            index++;
            return;
        }
        if (element.Children == null) return;
        foreach (ShapeElement child in element.Children) Visit(child, candle, colourOf, ref index);
    }

    private static bool UsesCandle(ShapeElement element) => Array.Exists(element.FacesResolved ?? [], f => f?.Texture == "candle");

    private static bool NamedAsFlame(ShapeElement element) =>
        element.Name is string name && (name.StartsWith("tip") || name.StartsWith("top") || name.StartsWith("flame"));

    private static bool Glows(ShapeElement element) => Array.Exists(element.FacesResolved ?? [], f => f?.Texture == "candle" && f.Glow > 0);

    /// <summary>
    /// New faces rather than changed ones: Shape.Clone copies the face array but shares
    /// the faces in it with the original. The glow stays vanilla's.
    /// </summary>
    private static void Paint(ShapeElement flame, string flameColour)
    {
        ShapeElementFace[] faces = flame.FacesResolved;
        for (int i = 0; i < faces.Length; i++)
        {
            ShapeElementFace face = faces[i];
            if (face?.Texture != "candle") continue;
            faces[i] = new ShapeElementFace
            {
                Texture = TextureCode(flameColour),
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
/// One block's mesh with its candles' flames coloured, made from its own shape and
/// textures, and kept by the candles' colours in order. Filled on the tesselation
/// thread. The shape is read the first time a coloured candle needs it, not as the
/// game loads, so a world without treated wicks never reads it.
/// </summary>
public class ColouredFlameMeshes
{
    private readonly Block block;
    private readonly Lazy<Shape> shape;

    // Never emptied: a world makes a handful of combinations, not the 6^9 there could be.
    private readonly ConcurrentDictionary<string, MeshData> byColours = new();

    public ColouredFlameMeshes(ICoreClientAPI capi, Block block)
    {
        this.block = block;
        shape = new(() => Shape.TryGet(capi, block.Shape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json")));
    }

    /// <summary>The block with its candles burning <paramref name="colours"/>, or null while all are plain.</summary>
    public MeshData For(ITesselatorAPI tesselator, IReadOnlyList<string> colours)
    {
        if (colours.All(c => c == null) || shape.Value == null) return null;

        return byColours.GetOrAdd(string.Join(",", colours), _ =>
        {
            CompositeShape cs = block.Shape;
            tesselator.TesselateShape(block, FlameMeshes.Recoloured(shape.Value, i => i < colours.Count ? colours[i] : null), out MeshData mesh,
                new Vec3f(cs.rotateX, cs.rotateY, cs.rotateZ), cs.QuantityElements);
            return mesh;
        });
    }
}
