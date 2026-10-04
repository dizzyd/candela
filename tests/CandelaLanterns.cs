using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Lanterns burning a candle: the light from each kind of candle and in each state,
    /// the candle surviving the lantern being picked up and crafted, refuelling, and -
    /// the thing CandleStory broke - lanterns still hanging from ceilings.
    /// </summary>
    public class CandelaLanterns
    {
        static BlockPos Lantern => P(8, 1, 8);

        const string LanternCode = "game:lantern-large-up";

        // Vanilla's large lantern: [7, 3, 20] with quartz glass and a plain lining.
        const int VanillaLight = 20;
        const double BeeswaxHours = 96;
        const double TallowHours = 48;

        // Assigned into rather than replaced: ConfigKit, when it is installed, holds
        // this object and would otherwise go on editing one nobody reads.
        [BeforeEach, AfterEach]
        public void DefaultConfig() => CandelaConfig.Current.AssignFrom(new CandelaConfig());

        [VsTest]
        public async Task LanternsArePatchedAndKeepTheirBlockEntity()
        {
            Assert.IsType<BlockCandelaLantern>(Sapi.World.GetBlock(new AssetLocation(LanternCode)));

            var fuel = await PlaceLantern();
            Assert.IsType<BELantern>(World.BE<BlockEntity>(Lantern));
            Assert.NotNull(fuel);
        }

        [VsTest]
        public async Task ANewLanternBurnsABeeswaxCandle()
        {
            var fuel = await PlaceLantern();

            Assert.Equal(BEBehaviorLanternFuel.DefaultCandle, fuel.Candle);
            Assert.Equal(BeeswaxHours, fuel.Flame.Fuel);
            Assert.Equal(VanillaLight, Light()[2]);
        }

        [VsTest]
        public async Task ASpentLanternGutters()
        {
            var fuel = await PlaceLantern();

            await Burn(BeeswaxHours + 1);

            Assert.True(fuel.Flame.Spent);
            Assert.Equal(VanillaLight / 3, Light()[2]);
            Assert.Equal(VanillaLight / 3, await EngineLight.Settled(Lantern, VanillaLight / 3), "the world is still lit as by a new candle");
        }

        [VsTest]
        public async Task ASpentLanternGoesDarkInDarkMode()
        {
            CandelaConfig.Current.BurnoutMode = BurnoutMode.Dark;
            await PlaceLantern();

            await Burn(BeeswaxHours + 1);

            Assert.Equal(0, Light()[2]);
            Assert.Equal(0, await EngineLight.Settled(Lantern, 0), "the world is still lit by a dark lantern");
        }

        [VsTest]
        public async Task TallowSootsTheGlass()
        {
            var fuel = await PlaceLantern(LanternWith("candela:tallowcandles", TallowHours));

            Assert.Equal("candela:tallowcandles", fuel.Candle);
            Assert.Equal(TallowHours, fuel.Flame.Fuel);
            Assert.Equal(VanillaLight - 2, Light()[2]);
            Assert.Equal(VanillaLight - 2, await EngineLight.Settled(Lantern, VanillaLight - 2), "placing a tallow lantern lit the world as beeswax");

            await Burn(TallowHours + 1);
            Assert.Equal((VanillaLight - 2) / 3, Light()[2]);
        }

        [VsTest]
        public async Task ASnuffedLanternGivesNoLightUntilLit()
        {
            var fuel = await PlaceLantern();

            fuel.Snuff();
            await Burn(10);
            Assert.Equal(0, Light()[2]);
            Assert.Equal(0, await EngineLight.Settled(Lantern, 0), "the world is still lit by a snuffed lantern");
            Assert.Equal(BeeswaxHours, fuel.Flame.Fuel);

            Assert.True(fuel.TryIgnite());
            Assert.Equal(VanillaLight, Light()[2]);
            Assert.Equal(VanillaLight, await EngineLight.Settled(Lantern, VanillaLight), "relighting did not light the world");
        }

        /// <summary>Breaking and replacing a lantern must not refill it.</summary>
        [VsTest]
        public async Task PickingUpALanternKeepsWhatIsLeft()
        {
            await PlaceLantern(LanternWith("candela:tallowcandles", TallowHours));
            await Burn(10);

            ItemStack picked = World.GetBlock(Lantern).OnPickBlock(Sapi.World, Lantern);
            Assert.Close(TallowHours - 10, LanternStack.Fuel(picked), 0.2);
            Assert.Equal("candela:tallowcandles", LanternStack.Candle(picked));
            // Vanilla's own attributes ride along untouched.
            Assert.Equal("quartz", picked.Attributes.GetString("glass"));

            var fuel = await PlaceLantern(picked);
            Assert.Close(TallowHours - 10, fuel.Flame.Fuel, 0.2);
        }

        [VsTest]
        public void ACraftedLanternBurnsTheCandleItWasMadeWith()
        {
            var lantern = (BlockCandelaLantern)Sapi.World.GetBlock(new AssetLocation(LanternCode));

            foreach (var (candle, bunch, hours) in new[] { ("game:candle", "game:bunchocandles", BeeswaxHours), ("candela:candle-tallow", "candela:tallowcandles", TallowHours) })
            {
                var inputs = new ItemSlot[] { new DummySlot(World.Stack("game:clearquartz", 1)), new DummySlot(World.Stack(candle, 1)) };
                var output = new DummySlot(World.Stack(LanternCode, 1));

                lantern.OnCreatedByCrafting(inputs, output, null);

                Assert.Equal(hours, LanternStack.Fuel(output.Itemstack), candle);
                Assert.Equal(bunch, LanternStack.Candle(output.Itemstack), candle);
            }
        }

        [VsTest]
        public void TallowHasALanternRecipeForEveryBeeswaxOne()
        {
            int Count(string candle) => Sapi.World.GridRecipes.Count(r =>
                r.Output.ResolvedItemStack?.Collectible.FirstCodePart() == "lantern" &&
                r.ResolvedIngredients.Any(i => i?.ResolvedItemStack?.Collectible.Code.ToString() == candle));

            int beeswax = Count("game:candle");
            Assert.Greater(beeswax, 0);
            Assert.Equal(beeswax, Count("candela:candle-tallow"));
        }

        // ----- with a player -----

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task AFreshCandleSwapsOutWhatIsLeft()
        {
            var fuel = await PlaceLantern();
            await Burn(BeeswaxHours * 0.6);
            await Player.Hold("candela:candle-tallow", 2);

            await Interact.UseBlock(Lantern);
            await Ticks(4);

            Assert.Equal("candela:tallowcandles", fuel.Candle);
            Assert.Equal(TallowHours, fuel.Flame.Fuel);
            Assert.Equal(1, Player.Held?.StackSize ?? 0, "the candle was not used");
            Assert.True(PlayerHas("candela:candlestub-beeswax-25"), "the old candle did not come back as a stub");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ShiftClickSnuffsAndATorchRelights()
        {
            var fuel = await PlaceLantern();
            await EmptyHand();

            await ShiftUse(Lantern);
            Assert.True(fuel.Flame.Snuffed, "shift-click with a free hand should snuff");
            Assert.Equal(LanternCode, World.BlockCode(Lantern), "snuffing picked the lantern up");

            await Player.Hold("game:torch-basic-lit-up");
            await Interact.UseBlock(Lantern);
            await Ticks(4);
            Assert.False(fuel.Flame.Snuffed, "a lit torch should relight it");
        }

        /// <summary>CandleStory replaced the lantern's class and lost this.</summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task LanternsStillHangFromCeilings()
        {
            var ceiling = P(8, 4, 8);
            World.SetBlock("game:planks-oak-ud", ceiling);
            World.SetBlock("game:air", P(8, 3, 8));
            await Player.Teleport(new Vec3d(P(8, 1, 6).X + 0.5, P(8, 1, 6).Y, P(8, 1, 6).Z + 0.5));
            // A lantern as crafted, with vanilla's material, lining and glass: one
            // without them places, but vanilla then draws it with a missing texture.
            var slot = Player.Me.InventoryManager.ActiveHotbarSlot;
            slot.Itemstack = LanternWith("game:bunchocandles", BeeswaxHours);
            slot.MarkDirty();
            await Ticks(2);

            await Interact.UseBlock(ceiling, BlockFacing.DOWN);
            await Ticks(4);

            Assert.Equal("game:lantern-large-down", World.BlockCode(P(8, 3, 8)));
            Assert.NotNull(World.BE<BlockEntity>(P(8, 3, 8)).GetBehavior<BEBehaviorLanternFuel>());
        }

        // ----- helpers -----

        static ItemStack LanternWith(string candle, double hours)
        {
            ItemStack stack = World.Stack(LanternCode, 1);
            stack.Attributes.SetString("material", "copper");
            stack.Attributes.SetString("lining", "plain");
            stack.Attributes.SetString("glass", "quartz");
            LanternStack.Write(stack, hours, candle, snuffed: false);
            return stack;
        }

        /// <summary>
        /// A lantern as a player places one: the block, then the placed-from-item hook
        /// with the stack it came from.
        /// </summary>
        static async Task<BEBehaviorLanternFuel> PlaceLantern(ItemStack from = null)
        {
            World.SetBlock(LanternCode, Lantern);
            await Ticks(2);
            var be = World.BE<BlockEntity>(Lantern);
            if (from != null)
            {
                foreach (var behavior in be.Behaviors) behavior.OnBlockPlaced(from);
                await Ticks(2);
            }
            return be.GetBehavior<BEBehaviorLanternFuel>();
        }

        static byte[] Light() => World.GetBlock(Lantern).GetLightHsv(Sapi.World.BlockAccessor, Lantern);

        static async Task Burn(double hours)
        {
            await World.TickNow(Lantern);
            await Hours(hours);
            await World.TickNow(Lantern);
        }

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

        static bool PlayerHas(string code) =>
            Player.Me.InventoryManager.Inventories.Values.Any(inv => inv.Any(s => s.Itemstack?.Collectible.Code.ToString() == code));
    }
}
