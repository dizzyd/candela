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
    /// Oil burners in lanterns: swapped for the candle and back, filled from and emptied
    /// into liquid containers, burning at the configured rate, the wick turned down, the
    /// oil kept through being picked up, and the burner drawn in the lantern.
    /// </summary>
    public class CandelaOil
    {
        static BlockPos Lantern => P(8, 1, 8);

        const string LanternCode = "game:lantern-large-up";
        const string Olive = "game:oilportion-olive";
        const string Linseed = "game:oilportion-flax";

        // Vanilla's large lantern with quartz glass and a plain lining.
        const int VanillaLight = 20;
        const double HoursPerLitre = 1728;
        const double Fount = 0.5;

        // One hook: the harness runs only the first [BeforeEach] a class has.
        [BeforeEach]
        public void BeforeEach()
        {
            DefaultConfig();
            EmptyPockets();
        }

        [AfterEach]
        public void DefaultConfig() => CandelaConfig.Current.AssignFrom(new CandelaConfig());

        [VsTest]
        public void OliveAndLinseedAreLampOilAndOnlyLinseedSoots()
        {
            Assert.True(LampOil.Is(Sapi.World.GetItem(new AssetLocation(Olive))), "olive oil is not lamp oil");
            Assert.True(LampOil.Is(Sapi.World.GetItem(new AssetLocation(Linseed))), "linseed oil is not lamp oil");
            Assert.False(LampOil.Is(Sapi.World.GetItem(new AssetLocation("game:waterportion"))), "water is lamp oil");
            Assert.Equal(0, LampOil.Dim(Sapi.World, Olive));
            Assert.Equal(2, LampOil.Dim(Sapi.World, Linseed));
        }

        [VsTest]
        public void TwoCopperPlatesAndFlaxTwineMakeABurner()
        {
            var recipe = Sapi.World.GridRecipes.FirstOrDefault(r => r.Output.ResolvedItemStack?.Collectible.Code.ToString() == BurnerStack.Code);
            Assert.NotNull(recipe, "no burner recipe");
            var codes = recipe.ResolvedIngredients.Where(i => i != null).Select(i => i.ResolvedItemStack.Collectible.Code.ToString()).ToList();
            Assert.Equal(2, codes.Count(c => c == "game:metalplate-copper"));
            Assert.Equal(1, codes.Count(c => c == "game:flaxtwine"));
        }

        /// <summary>Filling stops at the fount's half litre, and the rest stays in the bucket.</summary>
        [VsTest]
        public async Task FillingTakesHalfALitreAndLeavesTheRest()
        {
            var fuel = await PlaceLantern(WithBurner(null, 0));
            var hand = new DummySlot(Bucket(Olive, 1));

            Assert.True(fuel.TryFill(null, hand));

            Assert.Close(fuel.Flame.Fuel, Fount, 0.001);
            Assert.Equal(Olive, fuel.Oil);
            Assert.Close(BucketLitres(hand.Itemstack), 0.5, 0.001);
            Assert.True(fuel.TryFill(null, hand), "a full fount should take the click and move nothing");
            Assert.Close(BucketLitres(hand.Itemstack), 0.5, 0.001);
        }

        /// <summary>An empty burner is out, and filling it does not light it: a flame does.</summary>
        [VsTest]
        public async Task AnEmptyBurnerIsDarkAndWillNotLightUntilFilled()
        {
            var fuel = await PlaceLantern(WithBurner(null, 0));

            Assert.Equal(0, Light()[2]);
            Assert.False(fuel.TryIgnite(), "an empty burner lit");

            fuel.TryFill(null, new DummySlot(Bucket(Olive, 1)));
            Assert.Equal(0, Light()[2]);

            Assert.True(fuel.TryIgnite());
            Assert.Equal(VanillaLight, Light()[2]);
            Assert.Equal(VanillaLight, await EngineLight.Settled(Lantern, VanillaLight), "lighting the burner did not light the world");
        }

        [VsTest]
        public async Task EmptyingPoursTheOilBackAndPutsItOut()
        {
            var fuel = await PlaceLantern(WithBurner(Olive, 0.3));
            var hand = new DummySlot(World.Stack("game:woodbucket", 1));

            Assert.True(fuel.TryEmpty(null, hand));

            Assert.Close(BucketLitres(hand.Itemstack), 0.3, 0.011);
            Assert.Equal(Olive, BucketContent(hand.Itemstack));
            Assert.Null(fuel.Oil, "the burner still has oil");
            Assert.Equal(0, Light()[2]);
            Assert.Equal(0, await EngineLight.Settled(Lantern, 0), "the world is still lit by an emptied burner");
        }

        [VsTest]
        public async Task LinseedOilSootsTheGlass()
        {
            await PlaceLantern(WithBurner(Linseed, Fount));
            Assert.Equal(VanillaLight - 2, Light()[2]);
        }

        /// <summary>Poured in together, the sootier oil is what the glass gets.</summary>
        [VsTest]
        public async Task LinseedToppingUpOliveSootsIt()
        {
            var fuel = await PlaceLantern(WithBurner(Olive, 0.25));
            fuel.TryFill(null, new DummySlot(Bucket(Linseed, 1)));

            Assert.Equal(Linseed, fuel.Oil);
            Assert.Close(fuel.Flame.Fuel, Fount, 0.001);
            Assert.Equal(VanillaLight - 2, Light()[2]);
        }

        [VsTest]
        public async Task AFullFountBurnsForHalfTheHoursOfALitre()
        {
            var fuel = await PlaceLantern(WithBurner(Olive, Fount));

            await Burn(HoursPerLitre * Fount - 10);
            Assert.False(fuel.Flame.Spent, "burned out early");
            Assert.Close(fuel.Flame.Fuel, 10 / HoursPerLitre, 0.0005);

            await Burn(20);
            Assert.True(fuel.Flame.Spent);
            Assert.Equal(VanillaLight / 3, Light()[2]);
        }

        [VsTest]
        public async Task TheWickTurnedDownGivesHalfTheLightForTwiceAsLong()
        {
            var fuel = await PlaceLantern(WithBurner(Olive, Fount));

            Assert.True(fuel.TryTurnWick());
            Assert.True(fuel.WickLow);
            Assert.Equal((VanillaLight + 1) / 2, Light()[2]);
            Assert.Equal((VanillaLight + 1) / 2, await EngineLight.Settled(Lantern, (VanillaLight + 1) / 2), "turning the wick down did not dim the world");

            await Burn(HoursPerLitre * Fount);
            Assert.Close(fuel.Flame.Fuel, Fount / 2, 0.001);

            Assert.True(fuel.TryTurnWick());
            Assert.Equal(VanillaLight, Light()[2]);
        }

        /// <summary>A server that changes the burn time changes oil already poured, which is kept in litres.</summary>
        [VsTest]
        public async Task TheBurnTimeIsTheConfigs()
        {
            CandelaConfig.Current.OilBurnHoursPerLitre = 100;
            var fuel = await PlaceLantern(WithBurner(Olive, Fount));

            await Burn(25);
            Assert.Close(fuel.Flame.Fuel, 0.25, 0.001);
        }

        [VsTest]
        public async Task PickingUpALanternKeepsItsBurnerAndOil()
        {
            var fuel = await PlaceLantern(WithBurner(Linseed, Fount));
            fuel.TryTurnWick();
            await Burn(100);

            ItemStack picked = World.GetBlock(Lantern).OnPickBlock(Sapi.World, Lantern);
            Assert.True(LanternStack.HasBurner(picked));
            Assert.Equal(Linseed, BurnerStack.Oil(picked));
            Assert.True(BurnerStack.WickLow(picked));
            double left = Fount - 100 * 0.5 / HoursPerLitre;
            Assert.Close(LanternStack.Fuel(picked), left, 0.0005);

            fuel = await PlaceLantern(picked);
            Assert.True(fuel.HasBurner);
            Assert.Equal(Linseed, fuel.Oil);
            Assert.True(fuel.WickLow);
            Assert.Close(fuel.Flame.Fuel, left, 0.0005);
        }

        [VsTest]
        public void ABurnerSaysWhatIsInIt()
        {
            ItemStack burner = BurnerStack.Make(Sapi.World, Olive, 0.25, wickLow: false);
            var dsc = new System.Text.StringBuilder();
            burner.Collectible.GetHeldItemInfo(new DummySlot(burner), dsc, Sapi.World, false);

            Log(dsc.ToString());
            Assert.True(dsc.ToString().Contains("0.25 L"), "no litres");
            Assert.True(dsc.ToString().Contains(((int)(0.25 * HoursPerLitre)).ToString()), "no hours");
        }

        // ----- with a player -----

        /// <summary>The burner takes the candle's place, and the candle swapped back returns the burner with its oil.</summary>
        [VsTest, RequiresClient]
        public async Task ABurnerAndACandleSwapForEachOther()
        {
            var fuel = await PlaceLantern();
            await Burn(432 * 0.6);
            EnumGameMode mode = Player.Me.WorldData.CurrentGameMode;
            await Player.SetGameMode(EnumGameMode.Survival);
            try
            {
                ItemSlot hand = Player.Me.InventoryManager.ActiveHotbarSlot;
                hand.Itemstack = BurnerStack.Make(Sapi.World, Olive, 0.4, wickLow: false);
                Assert.True(fuel.TryRefuel(Player.Me, hand));

                Assert.True(fuel.HasBurner);
                Assert.Close(fuel.Flame.Fuel, 0.4, 0.001);
                Assert.False(fuel.Flame.Snuffed, "a filled burner put in is lit, as a candle is");
                Assert.True(PlayerHas("candela:candlestub-beeswax-25"), "the candle did not come back burned down");
                Assert.Null(hand.Itemstack, "the burner was not used");

                hand.Itemstack = World.Stack("game:candle", 1);
                Assert.True(fuel.TryRefuel(Player.Me, hand));

                Assert.False(fuel.HasBurner);
                Assert.Equal(BEBehaviorLanternFuel.DefaultBunchCode, fuel.BunchCode);
                ItemStack back = Carried().Select(slot => slot.Itemstack).FirstOrDefault(BurnerStack.Is);
                Assert.NotNull(back, "the burner did not come back");
                Assert.Equal(Olive, BurnerStack.Oil(back));
                Assert.Close(BurnerStack.Litres(back), 0.4, 0.001);
            }
            finally
            {
                await Player.SetGameMode(mode);
            }
        }

        /// <summary>Filled and emptied by clicking, as a player does, with the client's own copy of the bucket.</summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ClickingWithABucketFillsAndEmptiesIt()
        {
            var fuel = await PlaceLantern(WithBurner(null, 0));
            await Player.Teleport(new Vec3d(P(8, 1, 6).X + 0.5, P(8, 1, 6).Y, P(8, 1, 6).Z + 0.5));
            await Hold(Bucket(Olive, 1));

            await Interact.UseBlock(Lantern);
            await Ticks(4);
            Assert.Close(fuel.Flame.Fuel, Fount, 0.001);
            Assert.Close(BucketLitres(Player.Held), 0.5, 0.001);

            await Hold(World.Stack("game:woodbucket", 1));
            await Interact.UseBlock(Lantern);
            await Ticks(4);
            Assert.Null(fuel.Oil, "clicking with an empty bucket did not empty it");
            Assert.Close(BucketLitres(Player.Held), Fount, 0.001);
            Assert.Equal(LanternCode, World.BlockCode(Lantern), "the click picked the lantern up");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task CtrlClickTurnsTheWick()
        {
            var fuel = await PlaceLantern(WithBurner(Olive, Fount));
            await Player.Teleport(new Vec3d(P(8, 1, 6).X + 0.5, P(8, 1, 6).Y, P(8, 1, 6).Z + 0.5));
            await EmptyHand();

            await CtrlUse(Lantern);
            Assert.True(fuel.WickLow, "ctrl-click with a free hand should turn the wick down");
            Assert.Equal(LanternCode, World.BlockCode(Lantern), "turning the wick picked the lantern up");

            await CtrlUse(Lantern);
            Assert.False(fuel.WickLow, "a second ctrl-click should turn it back up");
        }

        /// <summary>The burner reshapes each of vanilla's lanterns' candles; one it missed would draw a candle still.</summary>
        [VsTest, RequiresClient]
        public async Task EveryLanternDrawsTheBurner()
        {
            await OnClient();
            foreach (string code in new[] { "game:lantern-large-up", "game:lantern-large-down", "game:lantern-large-north", "game:lantern-small-up", "game:lantern-small-down", "game:lantern-small-north" })
            {
                var block = (BlockCandelaLantern)Capi.World.GetBlock(new AssetLocation(code));
                MeshData burner = block.ColouredMesh(Capi, Capi.Tesselator, "copper", "plain", "quartz", CandleLook.Plain, burner: true);
                Assert.NotNull(burner, code);

                Shape shape = Shape.TryGet(Capi, block.Shape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json"));
                Assert.True(CandleBody(shape.Elements), code + " has no candle to reshape");
                Assert.False(CandleBody(CandleMeshes.AsBurner(shape).Elements), code + " still draws a candle");
            }
            await OnServer();
        }

        // ----- helpers -----

        /// <summary>Whether a candle body is still textured with the candle: an element that is, with a child that is - its flame.</summary>
        static bool CandleBody(ShapeElement[] elements) => elements.Any(e =>
            (UsesCandle(e) && e.Children?.Any(UsesCandle) == true) || (e.Children != null && CandleBody(e.Children)));

        static bool UsesCandle(ShapeElement e) => e.FacesResolved?.Any(f => f?.Texture == "candle") == true;

        static ItemStack WithBurner(string oil, double litres)
        {
            ItemStack stack = World.Stack(LanternCode, 1);
            stack.Attributes.SetString("material", "copper");
            stack.Attributes.SetString("lining", "plain");
            stack.Attributes.SetString("glass", "quartz");
            LanternStack.WriteBurner(stack, litres, oil, snuffed: oil == null, wickLow: false);
            return stack;
        }

        static ItemStack Bucket(string oil, double litres)
        {
            var bucket = (BlockLiquidContainerBase)Sapi.World.GetBlock(new AssetLocation("game:woodbucket"));
            var stack = new ItemStack(bucket);
            bucket.SetContent(stack, World.Stack(oil, (int)System.Math.Round(litres * 100)));
            return stack;
        }

        static double BucketLitres(ItemStack bucket) => ((BlockLiquidContainerBase)bucket.Collectible).GetCurrentLitres(bucket);

        static string BucketContent(ItemStack bucket) => ((BlockLiquidContainerBase)bucket.Collectible).GetContent(bucket)?.Collectible.Code.ToString();

        static async Task Hold(ItemStack stack)
        {
            var slot = Player.Me.InventoryManager.ActiveHotbarSlot;
            slot.Itemstack = stack;
            slot.MarkDirty();
            await Ticks(4);
        }

        static async Task CtrlUse(BlockPos pos)
        {
            await Input.KeyDown(GlKeys.ControlLeft, ctrl: true);
            try
            {
                await Interact.UseBlock(pos);
            }
            finally
            {
                await Input.KeyUp(GlKeys.ControlLeft);
            }
            await Ticks(4);
        }

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
    }
}
