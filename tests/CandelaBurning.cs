using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Vanilla's beeswax candles, made to burn down: the fuel pool, what a spent bunch
    /// gives off under each burnout mode, how much of its unloaded time it catches up,
    /// and the player-facing paths - placing, adding, taking, snuffing, lighting -
    /// that must keep the pool rather than resetting it.
    ///
    /// BECandles reads the burnout mode when it is initialised, so a case that needs
    /// a mode sets <see cref="CandelaConfig.Current"/> before placing anything, and
    /// every case puts the defaults back afterwards.
    /// </summary>
    public class CandelaBurning
    {
        static BlockPos Bunch => P(8, 1, 8);

        // Written out rather than read from the mod, so a changed duration fails here.
        const double BeeswaxHours = 432;

        // Assigned into rather than replaced: ConfigKit, when it is installed, holds
        // this object and would otherwise go on editing one nobody reads.
        [BeforeEach, AfterEach]
        public void DefaultConfig() => CandelaConfig.Current.AssignFrom(new CandelaConfig());

        [VsTest]
        public void VanillaCandlesArePatched()
        {
            for (int q = 1; q <= 9; q++)
            {
                Block bunch = Sapi.World.GetBlock(new AssetLocation("game:bunchocandles-" + q));
                Assert.IsType<BlockCandelaCandles>(bunch);
                Assert.Equal("CandelaCandles", bunch.EntityClass);
                Assert.Equal(q, ((BlockCandelaCandles)bunch).Quantity);
            }

            var single = Sapi.World.GetBlock(new AssetLocation("game:candle"));
            Assert.IsType<BlockCandelaCandles>(single);
            Assert.Equal(1, ((BlockCandelaCandles)single).Quantity);

            Assert.IsType<ItemCandelaCandle>(Sapi.World.GetItem(new AssetLocation("game:candle")));
        }

        [VsTest]
        public void StubsCarryTheirShareOfHours()
        {
            foreach (var (left, hours) in new[] { (75, 324.0), (50, 216.0), (25, 108.0) })
            {
                Item stub = Sapi.World.GetItem(new AssetLocation("candela:candlestub-beeswax-" + left));
                Assert.NotNull(stub, "candlestub-beeswax-" + left);
                Assert.Equal(hours, CandleWax.HoursOf(stub), "candlestub-beeswax-" + left);
            }
        }

        /// <summary>
        /// Burn hours come from the config when they are needed, so a change applies to
        /// the next candle placed, and stubs keep their share of whatever it is now.
        /// </summary>
        [VsTest]
        public async Task BurnHoursFollowTheConfig()
        {
            CandelaConfig.Current.BeeswaxBurnHours = 10;

            var be = await PlaceBunch(2);

            Assert.Equal(20.0, be.Fuel);
            Assert.Equal(10.0, CandleWax.HoursOf(Sapi.World.GetItem(new AssetLocation("game:candle"))));
            Assert.Equal(5.0, CandleWax.HoursOf(Sapi.World.GetItem(new AssetLocation("candela:candlestub-beeswax-50"))));
        }

        [VsTest]
        public async Task ANewBunchStartsFullAndLit()
        {
            var be = await PlaceBunch(3);

            Assert.Equal(3 * BeeswaxHours, be.Fuel);
            Assert.True(be.Flaming);
            Assert.Equal(1f, be.HeightFactor);
            Assert.Equal(9, Light()[2], "a lit bunch of three should give vanilla's light");
        }

        [VsTest]
        public async Task EveryCandleInABunchBurns()
        {
            var be = await PlaceBunch(3);

            await Burn(10);

            Assert.Close(3 * BeeswaxHours - 3 * 10, be.Fuel, 0.5);
        }

        [VsTest]
        public async Task TheMeshShrinksInQuarters()
        {
            var be = await PlaceBunch(1);

            await Burn(BeeswaxHours * 0.3);
            Assert.Equal(0.75f, be.HeightFactor);

            await Burn(BeeswaxHours * 0.3);
            Assert.Equal(0.5f, be.HeightFactor);
        }

        [VsTest]
        public async Task ASnuffedBunchDoesNotBurnOrShine()
        {
            var be = await PlaceBunch(2);
            be.Snuff();
            await Ticks(2);

            await Burn(10);

            Assert.Equal(2 * BeeswaxHours, be.Fuel);
            Assert.Equal(0, Light()[2]);
            Assert.Equal(0, await EngineLight.Settled(Bunch, 0), "the world is still lit by a snuffed bunch");

            Assert.True(be.TryIgnite(), "a snuffed bunch with fuel should light");
            Assert.Equal(8, Light()[2]);
            Assert.Equal(8, await EngineLight.Settled(Bunch, 8), "relighting did not light the world");
        }

        [VsTest]
        public async Task ASpentBunchGuttersByDefault()
        {
            var be = await PlaceBunch(3);

            await Burn(BeeswaxHours + 1);

            Assert.True(be.Spent);
            Assert.True(be.Flaming, "Dim mode keeps a guttering flame");
            Assert.Equal(3, Light()[2], "a third of vanilla's 9");
            Assert.Equal(3, await EngineLight.Settled(Bunch, 3), "the world is still lit as by new candles");
            Assert.Equal(BECandles.SpentHeight, be.HeightFactor);
        }

        [VsTest]
        public async Task ASpentBunchGoesDarkInDarkMode()
        {
            CandelaConfig.Current.BurnoutMode = BurnoutMode.Dark;
            var be = await PlaceBunch(3);

            await Burn(BeeswaxHours + 1);

            Assert.False(be.Flaming);
            Assert.Equal(0, Light()[2]);
            Assert.Equal(0, await EngineLight.Settled(Bunch, 0), "the world is still lit by a dark bunch");
            Assert.False(be.TryIgnite(), "nothing is left to light");
        }

        [VsTest]
        public async Task NoneModeNeverBurns()
        {
            CandelaConfig.Current.BurnoutMode = BurnoutMode.None;
            var be = await PlaceBunch(3);

            await Burn(BeeswaxHours * 3);

            Assert.Equal(3 * BeeswaxHours, be.Fuel);
            Assert.Equal(9, Light()[2]);
        }

        [VsTest]
        public async Task UnloadedTimeIsNotBurnedByDefault()
        {
            var be = await PlaceBunch(1);
            await Reload(be, hoursAway: 20);
            Assert.Close(BeeswaxHours, be.Fuel, 0.1);
        }

        [VsTest]
        public async Task CappedCatchUpBurnsOnlyUpToTheCap()
        {
            CandelaConfig.Current.UnattendedMode = UnattendedMode.CappedCatchUp;
            CandelaConfig.Current.CatchUpCapHours = 5;
            var be = await PlaceBunch(2);

            await Reload(be, hoursAway: 20);

            Assert.Close(2 * BeeswaxHours - 2 * 5, be.Fuel, 0.2);
        }

        [VsTest]
        public async Task AlwaysCatchesUpEverything()
        {
            CandelaConfig.Current.UnattendedMode = UnattendedMode.Always;
            var be = await PlaceBunch(2);

            await Reload(be, hoursAway: 20);

            Assert.Close(2 * BeeswaxHours - 2 * 20, be.Fuel, 0.2);
        }

        [VsTest]
        public async Task PartBurnedCandlesComeBackAsStubsRoundedDown()
        {
            var be = await PlaceBunch(3);
            var block = (BlockCandelaCandles)World.GetBlock(Bunch);

            Assert.Equal("game:candle", block.CandleForHours(Sapi.World, BeeswaxHours)?.Collectible.Code.ToString());
            Assert.Equal("candela:candlestub-beeswax-75", block.CandleForHours(Sapi.World, BeeswaxHours * 0.99)?.Collectible.Code.ToString());
            Assert.Equal("candela:candlestub-beeswax-50", block.CandleForHours(Sapi.World, BeeswaxHours * 0.6)?.Collectible.Code.ToString());
            Assert.Equal("candela:candlestub-beeswax-25", block.CandleForHours(Sapi.World, BeeswaxHours * 0.3)?.Collectible.Code.ToString());
            Assert.Null(block.CandleForHours(Sapi.World, BeeswaxHours * 0.2));

            await Burn(BeeswaxHours * 0.4);
            ItemStack[] drops = block.GetDrops(Sapi.World, Bunch, null);
            Assert.Equal(1, drops.Length);
            Assert.Equal("candela:candlestub-beeswax-50", drops[0].Collectible.Code.ToString());
            Assert.Equal(3, drops[0].StackSize);
        }

        /// <summary>
        /// A bunch placed before Candela was installed has no block entity. It gets one,
        /// new and full, the first time it is needed.
        /// </summary>
        [VsTest]
        public async Task ABunchWithNoBlockEntityGetsOne()
        {
            await PlaceBunch(2);
            Sapi.World.BlockAccessor.RemoveBlockEntity(Bunch);
            Assert.Null(World.BEOrNull<BECandles>(Bunch));
            Assert.Equal(8, Light()[2], "without a block entity it shines as vanilla");

            var block = (BlockCandelaCandles)World.GetBlock(Bunch);
            BECandles be = block.EnsureBlockEntity(Sapi.World, Bunch);

            Assert.NotNull(be);
            Assert.Equal(2 * BeeswaxHours, be.Fuel);
        }

        // ----- with a player -----

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task PlacingAndAddingKeepsThePool()
        {
            World.SetBlock("game:air", Bunch);
            await Player.Hold("game:candle", 4);

            await ShiftUse(P(8, 0, 8));
            Assert.Equal("game:bunchocandles-1", World.BlockCode(Bunch));
            var be = World.BE<BECandles>(Bunch);
            Assert.Equal(BeeswaxHours, be.Fuel);

            await Burn(10);
            await ShiftUse(Bunch);

            Assert.Equal("game:bunchocandles-2", World.BlockCode(Bunch));
            Assert.True(ReferenceEquals(be, World.BE<BECandles>(Bunch)), "adding a candle replaced the block entity");
            Assert.Close(2 * BeeswaxHours - 10, be.Fuel, 0.5);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task TakingACandleGivesBackAStub()
        {
            var be = await PlaceBunch(2);
            await Burn(BeeswaxHours * 0.4);
            await EmptyHand();
            ClearInventoryExceptHand();

            await Interact.UseBlock(Bunch);
            await Ticks(4);

            Assert.Equal("game:bunchocandles-1", World.BlockCode(Bunch));
            Assert.True(ReferenceEquals(be, World.BE<BECandles>(Bunch)), "the block entity was replaced");
            Assert.Close(BeeswaxHours * 0.6, be.Fuel, 0.5);
            Assert.True(PlayerHas("candela:candlestub-beeswax-50"), "no half-burned stub came back");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ShiftClickSnuffsAndATorchRelights()
        {
            var be = await PlaceBunch(2);
            await EmptyHand();

            await ShiftUse(Bunch);
            Assert.True(be.Snuffed, "shift-click with a free hand should snuff");
            Assert.Equal("game:bunchocandles-2", World.BlockCode(Bunch), "snuffing took a candle");

            await Player.Hold("game:torch-basic-lit-up");
            await Interact.UseBlock(Bunch);
            await Ticks(4);
            Assert.False(be.Snuffed, "a lit torch should relight it");
        }

        /// <summary>
        /// The server's light was right and the client's was not: a snuffed bunch went
        /// on lighting the room for the player, which every server-side check passed.
        /// </summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ASnuffedBunchIsDarkOnTheClient()
        {
            var be = await PlaceBunch(2);
            Assert.Equal(8, await EngineLight.Settled(Bunch, 8), "a new bunch never lit the client");
            await EmptyHand();

            await ShiftUse(Bunch);
            Assert.True(be.Snuffed, "shift-click with a free hand should snuff");

            Assert.Equal(0, await EngineLight.Settled(Bunch, 0), "the client is still lit by a snuffed bunch");

            Assert.True(be.TryIgnite(), "a snuffed bunch with fuel should light");
            Assert.Equal(8, await EngineLight.Settled(Bunch, 8), "relighting did not light the client");
        }

        /// <summary>
        /// Not an assertion: a picture of bunches at each height, at night, to check by
        /// eye that the flames sit on the wicks in every rotation.
        /// </summary>
        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task ShrunkBunchesForTheEye()
        {
            await World.SetCalendarTo(500 * 24 + 22);
            float[] heights = { 1f, 0.75f, 0.5f, 0.25f };
            for (int i = 0; i < 8; i++)
            {
                var pos = P(4 + (i % 4) * 2, 1, 6 + (i / 4) * 3);
                World.SetBlock("game:bunchocandles-9", pos);
                await Ticks(2);
                var be = World.BE<BECandles>(pos);
                double full = 9 * BeeswaxHours;
                be.SetFuel(full * heights[i % 4] - 1);
            }
            await Ticks(20);
            await Player.Teleport(new Vec3d(P(7, 3, 2).X + 0.5, P(7, 3, 2).Y, P(7, 3, 2).Z + 0.5));
            await Interact.LookAt(P(7, 1, 7));
            await Frames.Wait(60);
            Log("shot: " + await Shot.Take("results/candles-heights.png"));

            // Close enough to see each flame against its wick: a full bunch and a half
            // one, which land on different rotations.
            var close = P(6, 1, 6);
            await Player.Teleport(new Vec3d(close.X + 1.0, close.Y + 0.2, close.Z - 0.6));
            await Interact.LookAt(P(5, 1, 6));
            await Frames.Wait(60);
            Log("shot: " + await Shot.Take("results/candles-close.png"));
        }

        // ----- helpers -----

        static async Task<BECandles> PlaceBunch(int quantity)
        {
            World.SetBlock("game:bunchocandles-" + quantity, Bunch);
            await Ticks(2);
            var be = World.BE<BECandles>(Bunch);
            Assert.NotNull(be, "the bunch has no block entity");
            return be;
        }

        static byte[] Light() => World.GetBlock(Bunch).GetLightHsv(Sapi.World.BlockAccessor, Bunch);

        /// <summary>Burn for game hours, the way the block entity's listener would.</summary>
        static async Task Burn(double hours)
        {
            await World.TickNow(Bunch);
            await Hours(hours);
            await World.TickNow(Bunch);
        }

        /// <summary>
        /// What a chunk unload and reload looks like to the block entity: saved, away
        /// for a while, loaded and initialised again.
        /// </summary>
        static async Task Reload(BECandles be, double hoursAway)
        {
            await World.TickNow(Bunch);
            var tree = new TreeAttribute();
            be.ToTreeAttributes(tree);
            tree.SetDouble("candela:lastUpdateHours", Sapi.World.Calendar.TotalHours - hoursAway);

            be.FromTreeAttributes(tree, Sapi.World);
            be.Initialize(Sapi);
            await World.TickNow(Bunch);
        }

        /// <summary>
        /// Player.Hold with air leaves an air stack in the hand, which is not a free
        /// hand to anything that checks for one.
        /// </summary>
        static async Task EmptyHand()
        {
            var slot = Player.Me.InventoryManager.ActiveHotbarSlot;
            slot.Itemstack = null;
            slot.MarkDirty();
            await Ticks(2);
        }

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

        static void ClearInventoryExceptHand()
        {
            foreach (var inv in Player.Me.InventoryManager.Inventories.Values)
            {
                if (inv.ClassName is not ("hotbar" or "backpack")) continue;
                foreach (var slot in inv)
                {
                    if (slot == Player.Me.InventoryManager.ActiveHotbarSlot) continue;
                    slot.Itemstack = null;
                    slot.MarkDirty();
                }
            }
        }

        static bool PlayerHas(string code)
        {
            foreach (var inv in Player.Me.InventoryManager.Inventories.Values)
            {
                foreach (var slot in inv)
                {
                    if (slot.Itemstack?.Collectible.Code.ToString() == code) return true;
                }
            }
            return false;
        }
    }
}
