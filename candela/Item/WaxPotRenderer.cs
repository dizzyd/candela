using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// A pot of molten wax on the fire: open, with the wax showing, where vanilla's
/// PotInFirepitRenderer draws any pot in the input slot empty with its lid on - right
/// while something cooks, wrong for a pot being dipped or poured from. On a firepit,
/// and on Stone Bake Oven's cooking top (StoneBakeOvenCompat).
///
/// The firepit only asks for a new renderer when the pot itself changes, not its
/// contents, so this one is given every cooking pot on the fire and outlasts any one
/// batch - cooking, then wax, then dipped out or set back into fat. It wraps vanilla's
/// rather than replacing it, and hands back to it, lid, bobbing and cooking sound,
/// whenever there is no wax.
/// </summary>
public class WaxPotRenderer : IInFirepitRenderer
{
    public double RenderOrder => vanilla.RenderOrder;
    public int RenderRange => vanilla.RenderRange;

    private readonly ICoreClientAPI capi;
    private readonly BlockPos pos;
    private readonly InventorySmelting inventory;
    private readonly float potY;
    private readonly IInFirepitRenderer vanilla;
    private readonly MultiTextureMeshRef potRef;
    private readonly Dictionary<string, MultiTextureMeshRef> liquidRefs = new();
    private readonly Matrixf modelMat = new();

    private bool HasWax => ItemMoltenWax.FindIn(inventory) != null;

    /// <summary>
    /// A pot in <paramref name="inventory"/>'s pot slot at <paramref name="pos"/>, its
    /// base <paramref name="potY"/> above the block's - where <paramref name="vanilla"/>,
    /// the renderer it stands in for, draws it.
    /// </summary>
    public WaxPotRenderer(ICoreClientAPI capi, ItemStack potStack, BlockPos pos, InventorySmelting inventory, float potY, IInFirepitRenderer vanilla)
    {
        this.capi = capi;
        this.pos = pos;
        this.inventory = inventory;
        this.potY = potY;
        this.vanilla = vanilla;

        // Vanilla's open pot. The surface is its liquid one - the one meals in a bowl
        // use - textured from the wax itself; not the MealMeshCache's pot-with-contents,
        // which looks the texture up in the block atlas and drew the tallow as nothing.
        Block cooked = capi.World.GetBlock(potStack.Collectible.CodeWithVariant("type", "cooked"));
        capi.Tesselator.TesselateShape(cooked, Shape.TryGet(capi, "shapes/block/clay/pot-opened-empty.json"), out MeshData pot);
        potRef = capi.Render.UploadMultiTextureMesh(pot);
    }

    /// <summary>The surface of <paramref name="molten"/>: the wax's own, or vanilla's liquid dye if it is dyed.</summary>
    private MultiTextureMeshRef Liquid(ItemStack molten)
    {
        var wax = (ItemMoltenWax)molten.Collectible;
        TextureAtlasPosition dyed = CollectibleBehaviorPotOfWax.DyeSurface(WaxDyes.Of(molten));
        string key = dyed != null ? wax.Wax + "-" + WaxDyes.Of(molten) : wax.Wax;
        if (liquidRefs.TryGetValue(key, out var mesh)) return mesh;

        ITexPositionSource source = dyed != null ? new OneTexture(dyed, capi.BlockTextureAtlas.Size) : capi.Tesselator.GetTextureSource(wax);
        capi.Tesselator.TesselateShape("candela " + key, Shape.TryGet(capi, "shapes/block/food/meal/liquid.json"), out MeshData liquid, source);
        return liquidRefs[key] = capi.Render.UploadMultiTextureMesh(liquid);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        ItemSlot slot = ItemMoltenWax.FindIn(inventory);
        if (slot?.Itemstack.Collectible is not ItemMoltenWax wax)
        {
            vanilla.OnRenderFrame(deltaTime, stage);
            return;
        }

        IRenderAPI rpi = capi.Render;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;

        rpi.GlDisableCullFace();
        rpi.GlToggleBlend(true);

        // Placed as vanilla places a pot on the fire.
        IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
        prog.ViewMatrix = rpi.CameraMatrixOriginf;
        prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
        prog.ModelMatrix = modelMat.Identity()
            .Translate(pos.X - camPos.X + 0.001f, pos.Y - camPos.Y, pos.Z - camPos.Z - 0.001f)
            .Translate(0f, potY, 0f)
            .Values;
        rpi.RenderMultiTextureMesh(potRef, "tex");

        // From just off the bottom to where vanilla puts a full pot's meal.
        float fill = GameMath.Clamp(slot.StackSize / (float)wax.FullPot, 0, 1);
        prog.ModelMatrix = modelMat.Translate(0f, (0.3f + 2.2f * fill) / 16f, 0f).Values;
        rpi.RenderMultiTextureMesh(Liquid(slot.Itemstack), "tex");

        prog.Stop();
    }

    // Wax kept molten does not boil: vanilla's cooking sound is for a pot cooking.
    public void OnUpdate(float temperature) => vanilla.OnUpdate(HasWax ? 0 : temperature);

    public void OnCookingComplete() => vanilla.OnCookingComplete();

    public void Dispose()
    {
        potRef?.Dispose();
        foreach (var mesh in liquidRefs.Values) mesh.Dispose();
        vanilla.Dispose();
    }
}
