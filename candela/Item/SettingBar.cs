using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace candela;

/// <summary>
/// A bar under an item in its slot that fills while wax on it sets - a dipping rod's
/// last coat, a mould's candles - and is gone once it has.
///
/// Drawn every frame by an itemstack renderer, as vanilla's liquid litres are, rather
/// than as a durability bar: the slot caches that, and redraws it only when the slot
/// changes - which wax setting does not do. A tinted white texture rather than
/// RenderRectangle, which draws only an outline.
/// </summary>
public static class SettingBar
{
    private static readonly Vec4f Back = new(0.08f, 0.06f, 0.05f, 0.8f);
    private static readonly Vec4f Fill = new(0.91f, 0.72f, 0.38f, 1f);
    private static LoadedTexture white;

    /// <summary>
    /// Draws <paramref name="item"/> in GUI slots as usual, with the bar under it while
    /// <paramref name="progress"/> - 0 just poured, 1 set - is short of 1. Call from
    /// OnLoaded.
    /// </summary>
    public static void Register(ICoreAPI api, CollectibleObject item, System.Func<IWorldAccessor, ItemStack, double> progress)
    {
        if (api is not ICoreClientAPI capi) return;

        capi.Event.RegisterItemstackRenderer(item, (inSlot, renderInfo, modelMat, posX, posY, posZ, size, color, rotate, showStackSize) =>
        {
            capi.Render.RenderMultiTextureMesh(renderInfo.ModelRef, "tex2d");
            if (inSlot.Itemstack == null) return;

            double done = progress(capi.World, inSlot.Itemstack);
            if (done >= 1) return;

            int texture = White(capi).TextureId;

            // posX and posY are the middle of the slot; size is the item's, a little
            // over half the slot's.
            float width = size * 1.25f, height = (float)GuiElement.scaled(4);
            float x = (float)posX - width / 2, y = (float)posY + size * 0.62f - height, z = (float)posZ + 60;
            capi.Render.Render2DTexture(texture, x, y, width, height, z, Back);
            capi.Render.Render2DTexture(texture, x, y, (float)(width * done), height, z + 1, Fill);
        }, EnumItemRenderTarget.Gui);
    }

    /// <summary>Frees the bar's texture. Call from OnUnloaded.</summary>
    public static void Dispose()
    {
        white?.Dispose();
        white = null;
    }

    private static LoadedTexture White(ICoreClientAPI capi)
    {
        if (white != null) return white;

        using var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, 1, 1);
        using (var ctx = new Cairo.Context(surface))
        {
            ctx.SetSourceRGBA(1, 1, 1, 1);
            ctx.Paint();
        }
        white = new LoadedTexture(capi);
        capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref white);
        return white;
    }
}
