using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// A placed lantern drawn with its candle's flame colour. Harmony because BELantern
/// builds its mesh in OnTesselation without calling base, so a block entity behavior
/// - <see cref="BEBehaviorLanternFuel"/> - is never asked. A plain flame falls through
/// to vanilla's own mesh.
/// </summary>
[HarmonyPatch(typeof(BELantern), nameof(BELantern.OnTesselation))]
public static class LanternFlamePatch
{
    // Parameter names match vanilla's; Harmony binds by name.
    [HarmonyPrefix]
    private static bool Prefix(BELantern __instance, ITerrainMeshPool mesher, ITesselatorAPI tesselator, ref bool __result)
    {
        string flameColour = __instance.GetBehavior<BEBehaviorLanternFuel>()?.Look.Flame;
        if (flameColour == null || __instance.Block is not BlockCandelaLantern block || __instance.Api is not ICoreClientAPI capi) return true;

        MeshData mesh = block.ColouredMesh(capi, tesselator, __instance.material, __instance.lining, __instance.glass, flameColour);
        if (mesh == null) return true;

        // As vanilla turns it: a standing or hanging lantern faces the way it was placed.
        string part = block.LastCodePart();
        if (part == "up" || part == "down") mesh = mesh.Clone().Rotate(0, __instance.MeshAngle, 0);

        mesher.AddMeshData(mesh);
        __result = true;
        return false;
    }
}
