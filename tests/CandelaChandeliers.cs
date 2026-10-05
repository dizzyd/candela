using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using static Candela.Tests.Hands;

namespace Candela.Tests
{
    /// <summary>
    /// Chandeliers, whose candles burned forever and made them the way around upkeep:
    /// now one pool like a bunch, beeswax only as in vanilla, with candles that can be
    /// taken out again - and, with bunches, what happens to the fuel when the block
    /// under or over one is taken away.
    /// </summary>
    public class CandelaChandeliers
    {
        static BlockPos Chandelier => P(8, 1, 8);

        const double BeeswaxHours = 432;

        [BeforeEach, AfterEach]
        public void DefaultConfig()
        {
            CandelaConfig.Current.AssignFrom(new CandelaConfig());
            // No test can set the wind, and a gust over an open plot snuffs a flame
            // mid-test: it then burns nothing. CandelaWeather turns this back on.
            CandelaConfig.Current.WeatherPutsOut = false;
        }

        [VsTest]
        public void ChandeliersArePatched()
        {
            for (int n = 0; n <= 8; n++)
            {
                var block = Sapi.World.GetBlock(new AssetLocation("game:chandelier-candle" + n)) as BlockCandelaChandelier;
                Assert.NotNull(block, "chandelier-candle" + n);
                Assert.Equal(n, block.Quantity);
                Assert.Equal("CandelaCandles", block.EntityClass);
            }
        }

        [VsTest]
        public async Task AFullChandelierStartsWithEightNewCandles()
        {
            var be = await Place(8);

            Assert.Equal(8 * BeeswaxHours, be.Fuel);
            Assert.Equal(24, Light()[2], "vanilla's light for eight candles");
        }

        /// <summary>No candles is no light - not the dim floor a burned-down one gets.</summary>
        [VsTest]
        public async Task AnEmptyChandelierStaysDark()
        {
            await Place(0);
            Assert.Equal(0, Light()[2]);
        }

        [VsTest]
        public async Task ItBurnsDownAndGutters()
        {
            var be = await Place(8);

            await Burn(10);
            Assert.Close(be.Fuel, 8 * BeeswaxHours - 8 * 10, 1);

            await Burn(BeeswaxHours);
            Assert.True(be.Spent);
            Assert.Equal(8, Light()[2], "a third of 24");
            Assert.Equal(8, await EngineLight.Settled(Chandelier, 8), "the world is still lit as by new candles");
        }

        [VsTest]
        public async Task ItGoesDarkInDarkMode()
        {
            CandelaConfig.Current.BurnoutMode = BurnoutMode.Dark;
            await Place(4);

            await Burn(BeeswaxHours + 1);

            Assert.Equal(0, Light()[2]);
            Assert.Equal(0, await EngineLight.Settled(Chandelier, 0), "the world is still lit by a dark chandelier");
        }

        [VsTest]
        public async Task BreakingOneGivesBackWhatIsLeft()
        {
            await Place(4);
            await Burn(BeeswaxHours * 0.4);

            ItemStack[] drops = World.GetBlock(Chandelier).GetDrops(Sapi.World, Chandelier, null);

            Assert.Equal(2, drops.Length);
            Assert.Equal("game:chandelier-candle0", drops[0].Collectible.Code.ToString());
            Assert.Equal("candela:candlestub-beeswax-50", drops[1].Collectible.Code.ToString());
            Assert.Equal(4, drops[1].StackSize);
        }

        /// <summary>
        /// UnstableFalling carries the block entity's state with the falling block:
        /// EntityBlockFalling saves it with ToTreeAttributes, and where it lands sets the
        /// block - which initialises a new block entity, new candles and all - and then
        /// restores the saved state onto it with FromTreeAttributes. This replays that
        /// handoff - position rewrite included - rather than the fall, whose entity is
        /// only simulated near a player.
        ///
        /// It checks the light as well as the fuel. The landed block is lit as new; a
        /// spent chandelier once kept that light until something else changed.
        /// </summary>
        [VsTest(TimeoutMs = 60000)]
        public async Task AFallenChandelierLandsWithWhatItHad()
        {
            var hanging = P(8, 5, 8);
            World.SetBlock("game:planks-oak-ud", P(8, 6, 8));
            World.SetBlock("game:chandelier-candle8", hanging);
            await Ticks(2);
            var be = World.BE<BECandles>(hanging);
            await World.TickNow(hanging);
            await Hours(BeeswaxHours + 1);
            await World.TickNow(hanging);
            Assert.True(be.Spent);

            var saved = new Vintagestory.API.Datastructures.TreeAttribute();
            be.ToTreeAttributes(saved);
            World.SetBlock("game:air", hanging);

            World.SetBlock("game:chandelier-candle8", Chandelier);
            await Ticks(2);
            var landed = World.BE<BECandles>(Chandelier);
            // As EntityBlockFalling does: the saved position is the old one, and
            // FromTreeAttributes would move the block entity back there.
            saved.SetInt("posx", Chandelier.X);
            saved.SetInt("posy", Chandelier.Y);
            saved.SetInt("posz", Chandelier.Z);
            landed.FromTreeAttributes(saved, Sapi.World);
            await Ticks(4);

            Assert.True(landed.Spent, "the landed chandelier came down with new candles");

            // The light the engine has in the chunk, not what GetLightHsv would say now:
            // the second is read live from the block entity and was always right.
            Assert.Equal(8, await EngineLight.Settled(Chandelier, 8), "the landed chandelier should gutter, not shine as new");
        }

        // Supports and ceilings in these are planks, not rock: 1.22's rock collapses when
        // nothing holds it up, and took the bunch or chandelier down with it now and then.

        /// <summary>
        /// A bunch is only Unstable: it breaks rather than falls, through GetDrops, so
        /// its candles come back as what was left of them rather than new.
        /// </summary>
        [VsTest(TimeoutMs = 60000)]
        public async Task ABunchThatLosesItsFootingDropsStubs()
        {
            // With a client attached the dropped items are gone by the time they are
            // counted - picked up by the player the harness stands in the plot, or
            // unloaded; which has not been pinned down. Headless they stay put.
            if (Player.Me != null) Skip("dropped items vanish in a client run before they can be counted; runs headless");

            var bunch = P(8, 3, 8);
            var support = P(8, 2, 8);
            World.SetBlock("game:planks-oak-ud", support);
            World.SetBlock("game:bunchocandles-3", bunch);
            await Ticks(2);
            await World.TickNow(bunch);
            await Hours(BeeswaxHours * 0.4);
            await World.TickNow(bunch);

            World.SetBlock("game:air", support);
            Sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(support);
            await Until(() => World.BlockCode(bunch) == "game:air", 200, "the bunch to break");
            await Ticks(4);

            var dropped = Sapi.World.GetEntitiesAround(bunch.ToVec3d().Add(0.5, 0, 0.5), 4, 4, e => e is EntityItem)
                .Cast<EntityItem>().Select(e => e.Itemstack).ToArray();
            Log("  dropped: " + string.Join(", ", dropped.Select(s => s.StackSize + "x " + s.Collectible.Code)));
            Assert.Equal(3, dropped.Where(s => s.Collectible.Code.ToString() == "candela:candlestub-beeswax-50").Sum(s => s.StackSize));
            Assert.False(dropped.Any(s => s.Collectible.Code.ToString() == "game:candle"), "a part-burned bunch dropped new candles");
        }

        // ----- with a player -----

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task CandlesAndStubsGoInAndTallowDoesNot()
        {
            var be = await Place(2);
            await Burn(10);
            double before = be.Fuel;

            await Player.Hold("game:candle");
            await Interact.UseBlock(Chandelier);
            await Ticks(4);
            Assert.Equal("game:chandelier-candle3", World.BlockCode(Chandelier));
            Assert.True(ReferenceEquals(be, World.BE<BECandles>(Chandelier)), "adding a candle replaced the block entity");
            Assert.Close(be.Fuel, before + BeeswaxHours, 0.5);

            await Player.Hold("candela:candlestub-beeswax-50");
            await Interact.UseBlock(Chandelier);
            await Ticks(4);
            Assert.Equal("game:chandelier-candle4", World.BlockCode(Chandelier));
            Assert.Close(be.Fuel, before + BeeswaxHours * 1.5, 0.5);

            await Player.Hold("candela:candle-tallow");
            await Interact.UseBlock(Chandelier);
            await Ticks(4);
            Assert.Equal("game:chandelier-candle4", World.BlockCode(Chandelier), "a chandelier took a tallow candle");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ACandleComesOutAsWhatIsLeftOfIt()
        {
            var be = await Place(2);
            await Burn(BeeswaxHours * 0.4);
            await EmptyHand();

            await Interact.UseBlock(Chandelier);
            await Ticks(4);

            Assert.Equal("game:chandelier-candle1", World.BlockCode(Chandelier));
            Assert.Close(be.Fuel, BeeswaxHours * 0.6, 0.5);
            Assert.True(PlayerHas("candela:candlestub-beeswax-50"), "no half-burned stub came back");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ShiftClickSnuffsAndATorchRelights()
        {
            var be = await Place(4);
            await EmptyHand();

            await ShiftUse(Chandelier);
            Assert.True(be.Snuffed, "shift-click with a free hand should snuff");
            Assert.Equal("game:chandelier-candle4", World.BlockCode(Chandelier), "snuffing took a candle");
            Assert.Equal(0, Light()[2]);
            Assert.Equal(0, await EngineLight.Settled(Chandelier, 0), "the world is still lit by a snuffed chandelier");

            await Player.Hold("game:torch-basic-lit-up");
            await Interact.UseBlock(Chandelier);
            await Ticks(4);
            Assert.False(be.Snuffed, "a lit torch should relight it");
        }

        /// <summary>
        /// An empty chandelier has never been snuffed, so its flame stands "lit" - but
        /// there is no candle in it to light a torch from, or for a firestarter to light.
        /// </summary>
        [VsTest]
        public async Task AnEmptyChandelierLightsNothing()
        {
            var be = await Place(0);

            Assert.Equal(EnumIgniteState.NotIgnitable, be.OnTryIgniteStack(null, Chandelier, null, 2));
            Assert.Equal(EnumIgniteState.NotIgnitable, be.OnTryIgniteBlock(null, Chandelier, 3));
            be.Snuff();
            Assert.False(be.TryIgnite(), "an empty chandelier was lit");
        }

        /// <summary>A spent candle gutters on, and a torch still lights from it.</summary>
        [VsTest]
        public async Task ASpentCandleStillLightsATorch()
        {
            var be = await Place(1);
            await Burn(BeeswaxHours + 1);

            Assert.True(be.Spent);
            Assert.Equal(EnumIgniteState.IgniteNow, be.OnTryIgniteStack(null, Chandelier, null, 2));
        }

        // ----- helpers -----

        static async Task<BECandles> Place(int candles)
        {
            World.SetBlock("game:chandelier-candle" + candles, Chandelier);
            await Ticks(2);
            var be = World.BE<BECandles>(Chandelier);
            Assert.NotNull(be, "the chandelier has no block entity");
            return be;
        }

        static byte[] Light() => World.GetBlock(Chandelier).GetLightHsv(Sapi.World.BlockAccessor, Chandelier);

        static async Task Burn(double hours)
        {
            await World.TickNow(Chandelier);
            await Hours(hours);
            await World.TickNow(Chandelier);
        }
    }
}
