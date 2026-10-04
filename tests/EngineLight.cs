using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// The block light the engine has stored at a position - what actually lights the
    /// world - as opposed to what the block's GetLightHsv says now.
    ///
    /// The two differ exactly when a light change has not been propagated, which is
    /// the bug these exist to catch: GetLightHsv reads the block entity live and was
    /// right all along while snuffed candles went on lighting the room.
    /// </summary>
    public static class EngineLight
    {
        public static int At(BlockPos pos) => Sapi.World.BlockAccessor.GetLightLevel(pos, EnumLightLevelType.OnlyBlockLight);

        /// <summary>
        /// The light at <paramref name="pos"/> once it has had time to become
        /// <paramref name="expected"/>. Relighting is spread over ticks, so this waits;
        /// it returns whatever is there when it stops, for the caller to assert on.
        /// </summary>
        public static async Task<int> Settled(BlockPos pos, int expected)
        {
            try { await Until(() => At(pos) == expected, 100); }
            catch (AssertionException) { }
            return At(pos);
        }
    }
}
