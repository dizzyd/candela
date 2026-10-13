using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using static Candela.Tests.Hands;

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
        const double BeeswaxHours = 432;
        const double TallowHours = 216;

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

            Assert.Equal(BEBehaviorLanternFuel.DefaultBunchCode, fuel.BunchCode);
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

            Assert.Equal("candela:tallowcandles", fuel.BunchCode);
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

        /// <summary>
        /// Worldgen lights a placed structure - a trader camp's lanterns - before its
        /// block entities are initialised: created and given their behaviors, as
        /// SpawnBlockEntity leaves them, with no Api. That lit them dark.
        /// </summary>
        [VsTest]
        public void AnUninitialisedLanternLightsAsANewCandle()
        {
            Block block = Sapi.World.GetBlock(new AssetLocation(LanternCode));
            BlockEntity be = Sapi.ClassRegistry.CreateBlockEntity(block.EntityClass);
            be.CreateBehaviors(block, Sapi.World);
            be.Pos = Lantern.Copy();

            var fuel = be.GetBehavior<BEBehaviorLanternFuel>();
            Assert.NotNull(fuel);
            Assert.Equal(VanillaLight, fuel.LightHsv([7, 3, VanillaLight], Sapi.World)[2]);
        }

        /// <summary>
        /// A lantern the world was already lit dark around - a camp generated before the
        /// fix - comes back on when its chunk next loads.
        /// </summary>
        [VsTest]
        public async Task ALanternLitDarkIsHealedOnLoad()
        {
            await PlaceLantern();
            Assert.Equal(VanillaLight, await EngineLight.Settled(Lantern, VanillaLight));

            Sapi.World.BlockAccessor.RemoveBlockLight(Light(), Lantern);
            Assert.Equal(0, await EngineLight.Settled(Lantern, 0), "could not darken the world to start with");

            World.BE<BlockEntity>(Lantern).Initialize(Sapi);
            Assert.Equal(VanillaLight, await EngineLight.Settled(Lantern, VanillaLight), "the dark lantern was not healed");
        }

        /// <summary>
        /// A vanilla schematic pasted over a lantern says nothing about its candle, and
        /// leaves the one it has rather than burning it down.
        /// </summary>
        [VsTest]
        public async Task PastingAVanillaSchematicKeepsTheCandle()
        {
            var fuel = await PlaceLantern();
            await Burn(10);

            World.BE<BlockEntity>(Lantern).FromTreeAttributes(VanillaTree(), Sapi.World);
            await Ticks(2);

            Assert.False(fuel.Flame.Spent);
            Assert.Close(fuel.Flame.Fuel, BeeswaxHours - 10, 0.2);
            Assert.Equal(VanillaLight, Light()[2]);
        }

        /// <summary>A lantern worldgen hung - a trader camp's - keeps its candle new, lit and bright.</summary>
        [VsTest]
        public async Task AGeneratedLanternDoesNotBurnDown()
        {
            var fuel = await PlaceGeneratedLantern();
            Assert.True(fuel.Tended);
            Assert.Equal(BeeswaxHours, fuel.Flame.Fuel);

            await Burn(BeeswaxHours + 100);

            Assert.Equal(BeeswaxHours, fuel.Flame.Fuel);
            Assert.Equal(VanillaLight, Light()[2]);
            Assert.Equal(VanillaLight, await EngineLight.Settled(Lantern, VanillaLight), "the world is not lit by a tended lantern");
        }

        [VsTest]
        public async Task ATendedLanternStillSnuffsAndRelights()
        {
            var fuel = await PlaceGeneratedLantern();

            fuel.Snuff();
            Assert.Equal(0, Light()[2]);
            Assert.True(fuel.TryIgnite());
            Assert.Equal(VanillaLight, Light()[2]);
            Assert.True(fuel.Tended, "snuffing it was taken for a candle of the player's own");
        }

        /// <summary>Only lanterns worldgen made: one a player placed already has a candle of its own.</summary>
        [VsTest]
        public async Task APlacedLanternIsNotTended()
        {
            var fuel = await PlaceLantern();
            BEBehaviorLanternFuel.TendNew([World.BE<BlockEntity>(Lantern)]);
            Assert.False(fuel.Tended);
        }

        /// <summary>Taken down, it is an ordinary new candle - not one that never burns.</summary>
        [VsTest]
        public async Task PickingUpATendedLanternGivesAnOrdinaryCandle()
        {
            await PlaceGeneratedLantern();
            await Burn(100);

            ItemStack picked = World.GetBlock(Lantern).OnPickBlock(Sapi.World, Lantern);
            Assert.Equal(BeeswaxHours, LanternStack.Fuel(picked));
            Assert.False(picked.Attributes.HasAttribute("candela:tended"));

            var fuel = await PlaceLantern(picked);
            Assert.False(fuel.Tended);
            await Burn(10);
            Assert.Close(fuel.Flame.Fuel, BeeswaxHours - 10, 0.2);
        }

        /// <summary>A candle of the player's own burns as any other.</summary>
        [VsTest, RequiresClient]
        public async Task RefuellingATendedLanternEndsIt()
        {
            var fuel = await PlaceGeneratedLantern();

            ItemSlot hand = Player.Me.InventoryManager.ActiveHotbarSlot;
            hand.Itemstack = World.Stack("candela:candle-tallow", 1);
            Assert.True(fuel.TryRefuel(Player.Me, hand));
            Assert.False(fuel.Tended);
            Assert.True(PlayerHas("game:candle"), "the camp's candle did not come back new");

            await Burn(10);
            Assert.Close(fuel.Flame.Fuel, TallowHours - 10, 0.2);
        }

        /// <summary>Breaking and replacing a lantern must not refill it.</summary>
        [VsTest]
        public async Task PickingUpALanternKeepsWhatIsLeft()
        {
            await PlaceLantern(LanternWith("candela:tallowcandles", TallowHours));
            await Burn(10);

            ItemStack picked = World.GetBlock(Lantern).OnPickBlock(Sapi.World, Lantern);
            Assert.Close(LanternStack.Fuel(picked), TallowHours - 10, 0.2);
            Assert.Equal("candela:tallowcandles", LanternStack.BunchCode(picked));
            // Vanilla's own attributes ride along untouched.
            Assert.Equal("quartz", picked.Attributes.GetString("glass"));

            var fuel = await PlaceLantern(picked);
            Assert.Close(fuel.Flame.Fuel, TallowHours - 10, 0.2);
        }

        /// <summary>Picked up between two ticks, it is billed up to the moment it was taken.</summary>
        [VsTest]
        public async Task PickingUpBillsTheTimeSinceTheLastTick()
        {
            await PlaceLantern();
            await World.TickNow(Lantern);
            await Hours(10);

            ItemStack picked = World.GetBlock(Lantern).OnPickBlock(Sapi.World, Lantern);
            Assert.Close(LanternStack.Fuel(picked), BeeswaxHours - 10, 0.2);
        }

        /// <summary>
        /// The old candle comes out as burned as it is now, and the new one is not billed
        /// for the hours the old one burned.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task RefuellingBillsTheOldCandleNotTheNew()
        {
            var fuel = await PlaceLantern();
            await World.TickNow(Lantern);
            await Hours(BeeswaxHours * 0.6);

            ItemSlot hand = Player.Me.InventoryManager.ActiveHotbarSlot;
            hand.Itemstack = World.Stack("candela:candle-tallow", 1);
            Assert.True(fuel.TryRefuel(Player.Me, hand));
            await World.TickNow(Lantern);

            Assert.Close(fuel.Flame.Fuel, TallowHours, 0.2);
            Assert.True(PlayerHas("candela:candlestub-beeswax-25"), "the old candle did not come back burned down");
        }

        /// <summary>A chandelier once passed for a beeswax candle, and a lantern ate it.</summary>
        [VsTest, RequiresClient]
        public async Task ALanternDoesNotTakeAChandelier()
        {
            var fuel = await PlaceLantern();

            ItemSlot hand = Player.Me.InventoryManager.ActiveHotbarSlot;
            hand.Itemstack = World.Stack("game:chandelier-candle0", 1);
            try
            {
                Assert.False(fuel.TryRefuel(Player.Me, hand), "the lantern took a chandelier as its candle");
                Assert.Equal("game:chandelier-candle0", hand.Itemstack?.Collectible.Code.ToString(), "the chandelier left the hand");
            }
            finally
            {
                // The harness keeps the inventory: left in hand, the next test clicks with it.
                hand.Itemstack = null;
                hand.MarkDirty();
            }
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
                Assert.Equal(bunch, LanternStack.BunchCode(output.Itemstack), candle);
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

            Assert.Equal("candela:tallowcandles", fuel.BunchCode);
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

        /// <summary>
        /// The candle's line under vanilla's in the block info, as the client shows it.
        /// The server sends the fuel only when the light changes, so the client counts
        /// the hours down itself.
        /// </summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ThePlacedLanternSaysHowLongItsCandleHasLeft()
        {
            await PlaceLantern(LanternWith("game:bunchocandles", BeeswaxHours));
            await World.TickNow(Lantern);
            await Ticks(10);
            await Hours(10);
            await World.TickNow(Lantern);

            // The client hears of the jump in time with its next calendar packet.
            string want = Lang.Get("candela:candles-burning", (int)BeeswaxHours - 10, 1);
            string info = null;
            for (int i = 0; i < 200 && info?.Contains(want) != true; i++)
            {
                await Ticks(1);
                await OnClient();
                info = Capi.World.BlockAccessor.GetBlock(Lantern).GetPlacedBlockInfo(Capi.World, Lantern, Capi.World.Player);
                await OnServer();
            }

            Log(info);
            Assert.True(info.Contains(Lang.Get("lantern-materialwithpanels", Lang.Get("material-copper"), Lang.Get("block-glass-quartz"))), "vanilla's line is gone");
            Assert.True(info.Contains(want), "no candle line, or the wrong hours");
            Assert.True(info.Contains(LanternStack.CandleName(Sapi.World, "game:bunchocandles", CandleLook.Plain)), "the candle is not named");
        }

        /// <summary>A tended lantern names its candle and says it is kept, rather than quoting hours that never go down.</summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ATendedLanternSaysItIsKept()
        {
            await PlaceGeneratedLantern();

            string want = Lang.Get("candela:lantern-tended");
            string info = null;
            for (int i = 0; i < 200 && info?.Contains(want) != true; i++)
            {
                await Ticks(1);
                await OnClient();
                info = Capi.World.BlockAccessor.GetBlock(Lantern).GetPlacedBlockInfo(Capi.World, Lantern, Capi.World.Player);
                await OnServer();
            }

            Log(info);
            Assert.True(info.Contains(want), "no tended line");
            Assert.True(info.Contains(LanternStack.CandleName(Sapi.World, "game:bunchocandles", CandleLook.Plain)), "the candle is not named");
            Assert.False(info.Contains(Lang.Get("candela:candles-burning", (int)BeeswaxHours, 1)), "it still quotes hours");
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
            LanternStack.Write(stack, hours, candle, snuffed: false, CandleLook.Plain);
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

        /// <summary>
        /// A lantern as worldgen leaves one, through to its first load: made from a
        /// vanilla schematic's state with no candle and never initialised, tended at the
        /// end of generation, saved, then loaded over a placed lantern and initialised.
        /// </summary>
        static async Task<BEBehaviorLanternFuel> PlaceGeneratedLantern()
        {
            await PlaceLantern();
            var be = World.BE<BlockEntity>(Lantern);

            BlockEntity generated = Sapi.ClassRegistry.CreateBlockEntity(be.Block.EntityClass);
            generated.CreateBehaviors(be.Block, Sapi.World);
            generated.Pos = Lantern.Copy();
            generated.FromTreeAttributes(VanillaTree(), Sapi.World);

            BEBehaviorLanternFuel.TendNew([generated]);
            var saved = new TreeAttribute();
            generated.ToTreeAttributes(saved);
            Assert.False(saved.HasAttribute("candela:fuel"), "a lantern with no candle yet saved one as burned down");

            be.FromTreeAttributes(saved, Sapi.World);
            be.Initialize(Sapi);
            // Loaded for real, the client would get this state with the chunk.
            be.MarkDirty(true);
            await World.TickNow(Lantern);
            return be.GetBehavior<BEBehaviorLanternFuel>();
        }

        /// <summary>The placed lantern's state as a vanilla schematic holds it: no candle.</summary>
        static TreeAttribute VanillaTree()
        {
            var tree = new TreeAttribute();
            World.BE<BlockEntity>(Lantern).ToTreeAttributes(tree);
            foreach (string key in tree.Keys.Where(k => k.StartsWith("candela:")).ToList()) tree.RemoveAttribute(key);
            return tree;
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
