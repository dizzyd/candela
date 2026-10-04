using Vintagestory.API.Common;

namespace candela;

/// <summary>
/// Telling the engine a block's light has changed when the block itself has not.
///
/// The obvious way - exchanging the block for itself, as vanilla's lantern does when
/// its glass changes - only works for light going up. The lighting task that exchange
/// queues works out the light to remove by asking the old block's GetLightHsv when the
/// task runs, which is after the state has changed: so it removes the new, dimmer light
/// and the old brighter light stays where it was. A snuffed candle went on lighting the
/// room. (ServerSystemRelight.ProcessLightingTask, and its client twin.)
///
/// So the light is captured before the change, removed explicitly with
/// RemoveBlockLight - which the server also sends to clients - and the exchange then
/// places the new light.
/// </summary>
public static class Relight
{
    /// <summary>The block's light now. Call before changing anything that affects it.</summary>
    public static byte[] Capture(BlockEntity be) =>
        (byte[])be.Block.GetLightHsv(be.Api.World.BlockAccessor, be.Pos).Clone();

    /// <summary>
    /// Brings the engine's light in line with the block's, given what it was before.
    /// Server side; the client is told by the server.
    /// </summary>
    public static void After(BlockEntity be, byte[] before)
    {
        if (be.Api?.Side != EnumAppSide.Server) return;

        byte[] after = be.Block.GetLightHsv(be.Api.World.BlockAccessor, be.Pos);
        if (after[0] == before[0] && after[1] == before[1] && after[2] == before[2]) return;

        if (before[2] > 0) be.Api.World.BlockAccessor.RemoveBlockLight(before, be.Pos);
        be.Api.World.BlockAccessor.ExchangeBlock(be.Block.Id, be.Pos);
    }
}
