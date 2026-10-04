using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace candela;

/// <summary>
/// A block holding some number of candles that burn as one <see cref="BECandles"/>
/// pool: a bunch, a single candle on a fence, a chandelier.
/// </summary>
public interface ICandleHolder
{
    /// <summary>How many candles it holds now.</summary>
    int Quantity { get; }

    /// <summary>Hours one new candle of its kind burns for.</summary>
    double BurnHours { get; }
}

public static class CandleHolders
{
    /// <summary>
    /// The block's <see cref="BECandles"/>, created if it has none - as one placed
    /// before Candela was installed does not. Server side only; null on the client.
    /// </summary>
    public static BECandles EnsureBlockEntity(IWorldAccessor world, BlockPos pos, string entityClass)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is BECandles be) return be;
        if (world.Side != EnumAppSide.Server || entityClass == null) return null;

        world.BlockAccessor.SpawnBlockEntity(entityClass, pos);
        return world.BlockAccessor.GetBlockEntity(pos) as BECandles;
    }
}
