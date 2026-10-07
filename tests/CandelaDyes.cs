using System.Linq;
using System.Text;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Dyed wax: vanilla's liquid dyes cooked in with the wax, carried by the molten wax
    /// into the rod's last coat and the mould's fill, and by the candles they make into
    /// bunches, chandeliers and lanterns and out again. Remelting gives undyed wax, which
    /// can be dyed again in the same cook.
    /// </summary>
    public class CandelaDyes
    {
        static BlockPos Firepit => P(8, 1, 8);
        static BlockPos Bunch => P(8, 1, 8);

        const string EmptyPot = "game:claypot-blue-fired";
        const double TallowHours = 216;

        [BeforeEach, AfterEach]
        public void DefaultConfig()
        {
            CandelaConfig.Current.AssignFrom(new CandelaConfig());
            CandelaConfig.Current.WeatherPutsOut = false;
        }

        /// <summary>Every recipe that melts wax takes a dye, and needs none.</summary>
        [VsTest]
        public void EveryWaxRecipeTakesAnOptionalDye()
        {
            var recipes = Sapi.ModLoader.GetModSystem<RecipeRegistrySystem>().CookingRecipes;
            var wax = recipes.Where(r => r.CooksInto?.ResolvedItemstack?.Collectible is ItemMoltenWax).ToList();
            Assert.True(wax.Count >= 16, $"only {wax.Count} wax recipes");
            foreach (CookingRecipe recipe in wax)
            {
                CookingRecipeIngredient dye = recipe.Ingredients.FirstOrDefault(i => i.Code == "dye");
                Assert.NotNull(dye, recipe.Code + " takes no dye");
                Assert.Equal(0, dye.MinQuantity, recipe.Code + "'s dye should be optional");
                Assert.True(dye.Matches(World.Stack("game:dye-black", 1)), recipe.Code + " does not take black dye");
            }
        }

        [VsTest]
        public void DyeCookedInDyesTheWax()
        {
            ItemStack molten = Melt(World.Stack("game:fat-rendered", 3), World.Stack("game:dye-black", 30));
            Assert.Equal("candela:tallow-molten", molten.Collectible.Code.ToString());
            Assert.Equal(6, molten.StackSize, "two portions a lump of fat; the dye adds none");
            Assert.Equal("black", WaxDyes.Of(molten));
        }

        /// <summary>Every one of vanilla's dyes is a dye here, and dyes the wax its own colour.</summary>
        [VsTest]
        public void EveryVanillaDyeDyes()
        {
            foreach (string dye in WaxDyes.All)
            {
                Assert.NotNull(Sapi.World.GetItem(new AssetLocation("game:dye-" + dye)), "vanilla has no dye-" + dye);
                Assert.Equal(dye, WaxDyes.Of(Melt(World.Stack("game:beeswax", 2), World.Stack("game:dye-" + dye, 20))));
            }
        }

        [VsTest]
        public void WaxCookedWithoutDyeIsUndyed()
        {
            Assert.Null(WaxDyes.Of(Melt(World.Stack("game:fat-rendered", 2))));
        }

        /// <summary>Stubs remelt undyed whatever they were, and the same cook can dye them again.</summary>
        [VsTest]
        public void StubsRemeltUndyedAndCanBeDyedAgain()
        {
            ItemStack DyedStubs(string dye) => WaxDyes.Stamp(World.Stack("candela:candlestub-tallow-50", 2), dye);

            Assert.Null(WaxDyes.Of(Melt(DyedStubs("red"))), "red stubs came out red");
            Assert.Null(WaxDyes.Of(Melt(DyedStubs("red"), DyedStubs("blue"))), "red and blue stubs came out dyed");
            Assert.Equal("green", WaxDyes.Of(Melt(DyedStubs("red"), World.Stack("game:dye-green", 20))));
        }

        /// <summary>
        /// The rod's dye is its last coat's: the outermost coat is the one that shows, so
        /// an undyed coat over a dyed one leaves it plain. The candles cut from it carry
        /// the dye along with the wicks' flame.
        /// </summary>
        [VsTest]
        public void TheRodTakesItsLastCoatsDye()
        {
            ItemStack rod = FlameColours.Stamp(World.Stack("candela:dippingrod-0", 1), "red");
            rod = Coat(rod, "blue");
            Assert.Equal("blue", WaxDyes.Of(rod));
            rod = Coat(rod, "black");
            Assert.Equal("black", WaxDyes.Of(rod), "the second coat's dye");
            rod = Coat(rod, null);
            Assert.Null(WaxDyes.Of(rod), "an undyed coat over a dyed one");
            while (!((ItemDippingRod)rod.Collectible).IsFinished) rod = Coat(rod, "black");
            Assert.Equal(new CandleLook("red", "black"), CandleLook.Of(rod), "the wicks' flame and the last coat's dye");

            var candles = new DummySlot(World.Stack("candela:candle-tallow", 4));
            IRecipeBase cut = Sapi.World.GridRecipes.First(r => r.Output.ResolvedItemStack?.Collectible.Code.ToString() == "candela:candle-tallow");
            candles.Itemstack.Collectible.OnCreatedByCrafting([new DummySlot(rod)], candles, cut);
            Assert.Equal(new CandleLook("red", "black"), CandleLook.Of(candles.Itemstack));
        }

        [VsTest]
        public void AFilledMouldCarriesItsWaxsDye()
        {
            var mould = (ItemCandleMould)Sapi.World.GetItem(new AssetLocation("candela:candlemould-blue-fired"));
            ItemStack molten = WaxDyes.Stamp(World.Stack("candela:tallow-molten", 6), "purple");
            ItemStack filled = mould.Filled(Sapi.World, World.Stack("candela:candlemould-blue-fired", 1), molten);
            Assert.Equal("purple", WaxDyes.Of(filled));

            ItemStack candles = ItemCandleMould.Candles(Sapi.World, "tallow", new CandleLook("teal", "purple"));
            Assert.Equal(new CandleLook("teal", "purple"), CandleLook.Of(candles));
        }

        [VsTest]
        public void DyedAndUndyedCandlesDoNotStackAndSayWhichIsWhich()
        {
            ItemStack plain = World.Stack("candela:candle-tallow", 1);
            ItemStack black = new CandleLook(null, "black").Stamp(World.Stack("candela:candle-tallow", 1));
            ItemStack both = new CandleLook("red", "black").Stamp(World.Stack("candela:candle-tallow", 1));

            Assert.Equal(0, plain.Collectible.GetMergableQuantity(plain, black, EnumMergePriority.AutoMerge), "dyed stacked with undyed");
            Assert.Equal(1, black.Collectible.GetMergableQuantity(black, new CandleLook(null, "black").Stamp(World.Stack("candela:candle-tallow", 1)), EnumMergePriority.AutoMerge));
            Assert.Equal("Tallow candle (black)", black.GetName());
            Assert.Equal("Tallow candle (black, red flame)", both.GetName());
        }

        /// <summary>A bunch keeps each candle's dye, gives each back, and drops a stack for each look.</summary>
        [VsTest]
        public async Task ABunchKeepsEachCandlesDye()
        {
            World.SetBlock("candela:tallowcandles-1", Bunch);
            await Ticks(2);
            var be = World.BE<BECandles>(Bunch);
            be.SetFuel(TallowHours, new CandleLook(null, "black"));
            be.AddCandle(TallowHours, new CandleLook("red", "black"));
            Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("candela:tallowcandles-2")).BlockId, Bunch);
            be.AddCandle(TallowHours, CandleLook.Plain);
            Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("candela:tallowcandles-3")).BlockId, Bunch);
            await Ticks(2);

            var info = new StringBuilder();
            CandleInfo.AppendLooks(info, be.Looks.ToList());
            Assert.True(info.ToString().Contains("Wax: 2 black, 1 undyed"), "info said: " + info);

            // Saved and loaded, as a chunk does.
            var tree = new TreeAttribute();
            be.ToTreeAttributes(tree);
            Assert.Equal("black,black,", tree.GetString("candela:dyes"));
            be.FromTreeAttributes(tree, Sapi.World);
            Assert.Equal(new CandleLook("red", "black"), be.LookOf(1));

            ItemStack[] drops = World.GetBlock(Bunch).GetDrops(Sapi.World, Bunch, null);
            Assert.Equal(3, drops.Length, "a stack for each look");
            Assert.Equal(1, drops.Count(d => CandleLook.Of(d) == new CandleLook("red", "black")));

            be.TakeCandle(out CandleLook last);
            Assert.Equal(CandleLook.Plain, last, "the last one on is the first off");
        }

        [VsTest]
        public async Task ALanternKeepsItsCandlesDye()
        {
            var lantern = (BlockCandelaLantern)Sapi.World.GetBlock(new AssetLocation("game:lantern-large-up"));
            var output = new DummySlot(World.Stack("game:lantern-large-up", 1));
            ItemStack candle = new CandleLook("blue", "white").Stamp(World.Stack("game:candle", 1));
            lantern.OnCreatedByCrafting([new DummySlot(World.Stack("game:clearquartz", 1)), new DummySlot(candle)], output, null);
            Assert.Equal(new CandleLook("blue", "white"), LanternStack.Look(output.Itemstack));

            World.SetBlock("game:lantern-large-up", Bunch);
            await Ticks(2);
            var be = World.BE<BlockEntity>(Bunch);
            foreach (var behavior in be.Behaviors) behavior.OnBlockPlaced(output.Itemstack);
            var fuel = be.GetBehavior<BEBehaviorLanternFuel>();
            Assert.Equal(new CandleLook("blue", "white"), fuel.Look);

            ItemStack picked = World.Stack("game:lantern-large-up", 1);
            fuel.WriteTo(picked);
            Assert.Equal(new CandleLook("blue", "white"), LanternStack.Look(picked), "picking it up lost the look");
        }

        /// <summary>A dip at the firepit, the real interaction, coats the rod in the pot's dye.</summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task DippingInDyedTallowDyesTheRod()
        {
            var firepit = await CandelaDipping.FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            ItemSlot tallow = CandelaDipping.TallowSlot(firepit);
            WaxDyes.Stamp(tallow.Itemstack, "green");
            tallow.MarkDirty();
            firepit.MarkDirty(true);
            await Ticks(10);

            await Player.Hold("candela:dippingrod-0");
            await CandelaDipping.Dip();

            Assert.Equal("candela:dippingrod-1", Player.Held?.Collectible.Code.ToString());
            Assert.Equal("green", WaxDyes.Of(Player.Held));
        }

        // ----- helpers -----

        /// <summary>
        /// Cooks <paramref name="stacks"/> in a pot through vanilla's own DoSmelt, and the
        /// molten wax that came out.
        /// </summary>
        static ItemStack Melt(params ItemStack[] stacks)
        {
            World.SetBlock("game:firepit-extinct", Firepit);
            var firepit = World.BE<BlockEntityFirepit>(Firepit);
            firepit.inputSlot.Itemstack = World.Stack(EmptyPot, 1);
            for (int i = 0; i < stacks.Length; i++) firepit.otherCookingSlots[i].Itemstack = stacks[i];

            var pot = (BlockCookingContainer)firepit.inputSlot.Itemstack.Collectible;
            Assert.NotNull(pot.GetMatchingCookingRecipe(Sapi.World, stacks, out _), "no recipe matches");
            pot.DoSmelt(Sapi.World, firepit.Inventory as ISlotProvider, firepit.inputSlot, firepit.outputSlot);

            ItemSlot molten = ItemMoltenWax.FindIn(firepit);
            Assert.NotNull(molten, "nothing molten came out");
            return molten.Itemstack;
        }

        /// <summary><paramref name="rod"/> with another coat of tallow dyed <paramref name="dye"/>.</summary>
        static ItemStack Coat(ItemStack rod, string dye) =>
            ((ItemDippingRod)rod.Collectible).WithAnotherLayer(Sapi.World, rod, WaxDyes.Stamp(World.Stack("candela:tallow-molten", 1), dye));
    }
}
