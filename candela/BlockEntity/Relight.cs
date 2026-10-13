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
/// RemoveBlockLight, and the exchange then places the new light.
///
/// The client keeps its own light and relights itself from the same packets, and
/// there the block change arrives before the block entity's new state: it relit from
/// the state it already had, one step behind - a new bunch at the guttering floor
/// because its fuel had not arrived, a snuffed one at full. So the client does the
/// same again, from <see cref="Synced"/>, once the state that changes the light is in.
/// </summary>
public static class Relight
{
    /// <summary>The block's light now. Call before changing anything that affects it.</summary>
    public static byte[] Capture(BlockEntity be) =>
        (byte[])be.Block.GetLightHsv(be.Api.World.BlockAccessor, be.Pos).Clone();

    /// <summary>
    /// Brings the engine's light in line with the block's, given what it was before.
    /// Server side; the client catches up in <see cref="Synced"/>.
    /// </summary>
    public static void After(BlockEntity be, byte[] before)
    {
        if (be.Api?.Side != EnumAppSide.Server) return;
        Apply(be, before);
    }

    /// <summary>
    /// The client's half: its light brought in line with state just received from the
    /// server. Call from FromTreeAttributes with the light captured before reading it.
    /// Deferred a tick rather than exchanging the block from inside its own
    /// deserialisation.
    /// </summary>
    public static void Synced(BlockEntity be, byte[] before)
    {
        if (be.Api?.Side != EnumAppSide.Client || before == null) return;
        be.RegisterDelayedCallback(_ => Apply(be, before), 0);
    }

    /// <summary>
    /// Server side, once a block entity is up: lights the world as the block says it
    /// should be if the engine has it darker. Worldgen lit placed structures before
    /// their block entities were initialised, which used to come out dark - a trader
    /// camp's lanterns burning with no light - and stayed dark, since nothing about the
    /// candle changed after. Only ever adds light, so a correct one is left be.
    /// </summary>
    public static void Heal(BlockEntity be)
    {
        if (be.Api?.Side != EnumAppSide.Server || be.Block == null) return;

        IBlockAccessor blockAccessor = be.Api.World.BlockAccessor;
        byte[] light = be.Block.GetLightHsv(blockAccessor, be.Pos);
        if (light[2] == 0 || blockAccessor.GetLightLevel(be.Pos, EnumLightLevelType.OnlyBlockLight) >= light[2]) return;

        blockAccessor.ExchangeBlock(be.Block.Id, be.Pos);
    }

    private static void Apply(BlockEntity be, byte[] before)
    {
        if (be.Block == null) return;

        byte[] after = be.Block.GetLightHsv(be.Api.World.BlockAccessor, be.Pos);
        if (after[0] == before[0] && after[1] == before[1] && after[2] == before[2]) return;

        if (before[2] > 0) be.Api.World.BlockAccessor.RemoveBlockLight(before, be.Pos);
        be.Api.World.BlockAccessor.ExchangeBlock(be.Block.Id, be.Pos);
    }
}
