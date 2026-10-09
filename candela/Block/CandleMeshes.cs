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
    /// A copy of a lantern's <paramref name="shape"/> with its candle made an oil
    /// burner: a squat fount of the lantern's own metal, twice the candle's width and
    /// three-eighths its height, standing where it stood, with a flame half as big
    /// again on top. Reshaped from the candle rather than drawn from a shape file of
    /// its own, so it fits each of vanilla's lanterns - the large and the small, whose
    /// candles differ - and any a mod adds in the same pattern.
    /// </summary>
    public static Shape AsBurner(Shape shape)
    {
        Shape copy = shape.Clone();
        foreach (ShapeElement element in copy.Elements)
        {
            if (MakeBurner(element)) break;
        }
        return copy;
    }

    private static bool MakeBurner(ShapeElement element)
    {
        if (UsesCandle(element) && element.Children?.FirstOrDefault(IsFlame) is ShapeElement flame)
        {
            double width = element.To[0] - element.From[0], height = element.To[1] - element.From[1];
            double cx = (element.From[0] + element.To[0]) / 2, cz = (element.From[2] + element.To[2]) / 2;
            double fountHeight = height * 3 / 8;
            element.From = [cx - width, element.From[1], cz - width];
            element.To = [cx + width, element.From[1] + fountHeight, cz + width];
            element.FacesResolved = Metal(element.FacesResolved, 2 * width, fountHeight, 2 * width);

            // Children sit relative to their parent's corner, which has moved out by
            // half the candle's width: the small lantern's candle holder stays where it
            // was, inside the fount, rather than poking out of its corner.
            double shift = width / 2;
            foreach (ShapeElement child in element.Children)
            {
                if (child == flame) continue;
                child.From = [child.From[0] + shift, child.From[1], child.From[2] + shift];
                child.To = [child.To[0] + shift, child.To[1], child.To[2] + shift];
            }

            double flameWidth = (flame.To[0] - flame.From[0]) * 1.5, flameHeight = (flame.To[1] - flame.From[1]) * 1.5;
            double inset = width - flameWidth / 2;
            flame.From = [inset, fountHeight, inset];
            flame.To = [inset + flameWidth, fountHeight + flameHeight, inset + flameWidth];
            return true;
        }
        if (element.Children == null) return false;
        foreach (ShapeElement child in element.Children)
        {
            if (MakeBurner(child)) return true;
        }
        return false;
    }

    /// <summary>
    /// The candle's faces, in the order <see cref="BlockFacing"/> numbers them, as the
    /// lantern's metal - UVs sized to the new box, so the texture is not stretched.
    /// Faces the candle did not draw stay undrawn.
    /// </summary>
    private static ShapeElementFace[] Metal(ShapeElementFace[] faces, double dx, double dy, double dz)
    {
        var metal = new ShapeElementFace[faces.Length];
        for (int i = 0; i < faces.Length; i++)
        {
            ShapeElementFace face = faces[i];
            if (face?.Texture != "candle")
            {
                metal[i] = face;
                continue;
            }

            // North, east, south, west, up, down.
            (double u, double v) = i switch { 0 or 2 => (dx, dy), 1 or 3 => (dz, dy), _ => (dx, dz) };
            metal[i] = new ShapeElementFace { Texture = "material", Uv = [0, 0, (float)u, (float)v], Enabled = face.Enabled };
        }
        return metal;
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
