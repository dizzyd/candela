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
        ///
        /// With a client attached it waits for the client's light as well, and returns
        /// that once the server's is right: the client relights itself, and was one
        /// step behind the server while every server-side check here passed.
        /// </summary>
        public static async Task<int> Settled(BlockPos pos, int expected)
        {
            try { await Until(() => At(pos) == expected, 100); }
            catch (AssertionException) { }

            int server = At(pos);
            if (server != expected || Capi == null) return server;
            return await OnClientSettled(pos, expected);
        }

        /// <summary>
        /// The same on the client, which keeps its own copy of the light and is what a
        /// player actually sees by.
        /// </summary>
        public static async Task<int> OnClientAt(BlockPos pos)
        {
            await OnClient();
            int level = Capi.World.BlockAccessor.GetLightLevel(pos, EnumLightLevelType.OnlyBlockLight);
            await OnServer();
            return level;
        }

        private static async Task<int> OnClientSettled(BlockPos pos, int expected)
        {
            for (int i = 0; i < 100; i++)
            {
                if (await OnClientAt(pos) == expected) return expected;
                await Ticks(1);
            }
            return await OnClientAt(pos);
        }
    }
}
