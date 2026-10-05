using System.Threading.Tasks;
using candela;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Tallow candles placed and burned: their own bunch block on the same machinery
    /// as vanilla's beeswax one, with half the burn and a little less light, and kept
    /// apart from beeswax bunches.
    /// </summary>
    public class CandelaTallowCandles
    {
        static BlockPos Bunch => P(8, 1, 8);

        const double TallowHours = 216;

        // Assigned into rather than replaced: ConfigKit, when it is installed, holds
        // this object and would otherwise go on editing one nobody reads.
        [BeforeEach, AfterEach]
        public void DefaultConfig() => CandelaConfig.Current.AssignFrom(new CandelaConfig());

        [VsTest]
        public void TallowBlocksAndItemsLoad()
        {
            for (int q = 1; q <= 9; q++)
            {
                var bunch = Sapi.World.GetBlock(new AssetLocation("candela:tallowcandles-" + q)) as BlockCandelaCandles;
                Assert.NotNull(bunch, "tallowcandles-" + q);
                Assert.Equal(q, bunch.Quantity);
                Assert.Equal(TallowHours, bunch.BurnHours);
            }

            Assert.IsType<BlockCandelaCandles>(Sapi.World.GetBlock(new AssetLocation("candela:tallowcandle")));
            Assert.IsType<ItemPlaceableCandle>(Sapi.World.GetItem(new AssetLocation("candela:candle-tallow")));

            foreach (var (left, hours) in new[] { (75, 162.0), (50, 108.0), (25, 54.0) })
            {
                Item stub = Sapi.World.GetItem(new AssetLocation("candela:candlestub-tallow-" + left));
                Assert.Equal(hours, CandleWax.HoursOf(stub), "candlestub-tallow-" + left);
                Assert.Equal("candela:tallowcandles", stub.Attributes["candela"]["bunch"].AsString());
            }
        }

        [VsTest]
        public async Task ATallowBunchBurnsForHalfAsLong()
        {
            World.SetBlock("candela:tallowcandles-2", Bunch);
            await Ticks(2);
            var be = World.BE<BECandles>(Bunch);
            Assert.Equal(2 * TallowHours, be.Fuel);
            Assert.Equal(7, Light()[2], "one less than the 8 a beeswax pair gives");

            await World.TickNow(Bunch);
            await Hours(TallowHours + 1);
            await World.TickNow(Bunch);

            Assert.True(be.Spent);
            Assert.Equal(2, Light()[2], "guttering: a third of 7, floored at 2");
        }

        [VsTest]
        public async Task TallowComesBackAsTallow()
        {
            World.SetBlock("candela:tallowcandles-3", Bunch);
            await Ticks(2);
            var block = (BlockCandelaCandles)World.GetBlock(Bunch);

            Assert.Equal("candela:candle-tallow", block.CandleForHours(Sapi.World, TallowHours)?.Collectible.Code.ToString());
            Assert.Equal("candela:candlestub-tallow-50", block.CandleForHours(Sapi.World, TallowHours * 0.6)?.Collectible.Code.ToString());
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task PlacingTallowAndAddingAStub()
        {
            World.SetBlock("game:air", Bunch);
            await Player.Hold("candela:candle-tallow", 2);
            await ShiftUse(P(8, 0, 8));

            Assert.Equal("candela:tallowcandles-1", World.BlockCode(Bunch));
            var be = World.BE<BECandles>(Bunch);
            Assert.Equal(TallowHours, be.Fuel);

            await Player.Hold("candela:candlestub-tallow-50");
            await ShiftUse(Bunch);

            Assert.Equal("candela:tallowcandles-2", World.BlockCode(Bunch));
            Assert.Equal(TallowHours * 1.5, be.Fuel);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task TallowDoesNotJoinABeeswaxBunch()
        {
            World.SetBlock("game:bunchocandles-2", Bunch);
            await Ticks(2);
            await Player.Hold("candela:candle-tallow");

            await ShiftUse(Bunch);

            Assert.Equal("game:bunchocandles-2", World.BlockCode(Bunch));
            Assert.Close(2 * 432.0, World.BE<BECandles>(Bunch).Fuel, 0.01);
        }

        /// <summary>Not an assertion: tallow beside beeswax, to judge the colour by eye.</summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task TallowBesideBeeswaxForTheEye()
        {
            await World.SetCalendarTo(500 * 24 + 12);
            World.SetBlock("game:bunchocandles-5", P(7, 1, 8));
            World.SetBlock("candela:tallowcandles-5", P(9, 1, 8));
            await Ticks(10);
            await Player.Teleport(new Vec3d(P(8, 1, 6).X + 0.5, P(8, 1, 6).Y + 0.6, P(8, 1, 6).Z + 0.5));
            await Interact.LookAt(P(8, 1, 8));
            await Frames.Wait(60);
            Log("shot: " + await Shot.Take("results/tallow-vs-beeswax.png"));
        }

        static byte[] Light() => World.GetBlock(Bunch).GetLightHsv(Sapi.World.BlockAccessor, Bunch);

        static async Task ShiftUse(BlockPos pos)
        {
            await Input.KeyDown(GlKeys.ShiftLeft, shift: true);
            try
            {
                await Interact.UseBlock(pos);
            }
            finally
            {
                await Input.KeyUp(GlKeys.ShiftLeft);
            }
            await Ticks(4);
        }
    }
}
