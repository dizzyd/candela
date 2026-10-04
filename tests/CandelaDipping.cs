using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Dipping tallow candles: rendered fat melted in a pot, kept molten on a firepit,
    /// and built up on a dipping rod one held right-click at a time.
    ///
    /// The server-side cases check the pieces a player never sees directly - the
    /// recipe, the vanilla cooking path it rides on, and the rule that tallow does not
    /// set while it is hot. The dipping itself needs block selection and a held use
    /// key, so those cases are [RequiresClient].
    /// </summary>
    public class CandelaDipping
    {
        static BlockPos Firepit => P(8, 1, 8);

        const string FirepitCode = "game:firepit-extinct";
        const string EmptyPot    = "game:claypot-blue-fired";
        const string RenderedFat = "game:fat-rendered";
        const string Tallow      = "candela:tallow-molten";

        // Well above the setting point, so a few game minutes of cooling during a
        // test cannot take it below - 90°C a game hour is vanilla's item cooling.
        const float HotTallow = 300f;

        [VsTest]
        public void ContentLoads()
        {
            Assert.NotNull(Sapi.World.GetItem(new AssetLocation(Tallow)));
            Assert.NotNull(Sapi.World.GetItem(new AssetLocation("candela:candle-tallow")));
            for (int i = 0; i <= ItemDippingRod.MaxLayers; i++)
            {
                Assert.NotNull(Sapi.World.GetItem(new AssetLocation("candela:dippingrod-" + i)));
            }

            // The patch adds the behavior through behaviorsByType "*", which every
            // finished firepit state takes.
            foreach (string state in new[] { "extinct", "lit", "cold" })
            {
                Block firepit = Sapi.World.GetBlock(new AssetLocation("game:firepit-" + state));
                Assert.True(firepit.BlockBehaviors.Any(b => b is BlockBehaviorDipVat), $"firepit-{state} has no CandelaDipVat");
            }
        }

        /// <summary>
        /// One malformed cooking recipe throws out of RecipeRegistrySystem.AssetsLoaded
        /// and takes every cooking recipe with it, vanilla's included - which a test
        /// only of candela's own recipe would report as just that recipe missing.
        /// </summary>
        [VsTest]
        public void VanillaCookingRecipesStillLoad()
        {
            var recipes = Sapi.ModLoader.GetModSystem<RecipeRegistrySystem>().CookingRecipes;
            Assert.True(recipes.Any(r => r.Code == "rendered fat1"), "vanilla's rendered fat recipe is missing");
            Assert.Equal(4, recipes.Count(r => r.CooksInto?.ResolvedItemstack?.Collectible.Code.ToString() == Tallow));
        }

        [VsTest]
        public void RenderedFatInAPotMatchesTheTallowRecipe()
        {
            var pot = (BlockCookingContainer)Sapi.World.GetBlock(new AssetLocation(EmptyPot));

            // Two slots of three: the two-slot recipe, three servings of it.
            var stacks = new[] { World.Stack(RenderedFat, 3), World.Stack(RenderedFat, 3) };
            CookingRecipe recipe = pot.GetMatchingCookingRecipe(Sapi.World, stacks, out int servings);

            Assert.NotNull(recipe);
            Assert.Equal(Tallow, recipe.CooksInto.ResolvedItemstack.Collectible.Code.ToString());
            Assert.Equal(3, servings);
        }

        [VsTest]
        public async Task CookingLeavesTheTallowInThePotOnTheFire()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);

            ItemSlot tallow = TallowSlot(firepit);
            Assert.NotNull(tallow, "no molten tallow in the pot's cooking slots after cooking");
            Assert.Equal(6, tallow.StackSize);

            // The cooked pot has nothing in its own contents, so vanilla reverts it to
            // the empty pot, and the tallow is left in that pot's visible slots.
            Assert.Equal(EmptyPot, firepit.inputSlot.Itemstack?.Collectible.Code.ToString());
            Assert.Equal(4, firepit.otherCookingSlots.Length);
        }

        /// <summary>
        /// The moment after cooking, before vanilla reverts the cooked pot: its slots
        /// are hidden, the firepit heats the pot, and the tallow is as hot as the pot.
        /// </summary>
        [VsTest]
        public async Task FreshlyCookedTallowIsAsHotAsThePot()
        {
            World.SetBlock(FirepitCode, Firepit);
            await Ticks(2);
            var firepit = World.BE<BlockEntityFirepit>(Firepit);
            Cook(firepit, fatPerSlot: 1, slots: 1);

            // No ticks, and nothing marked dirty, since either can trigger the revert.
            ItemStack cookedPot = firepit.inputSlot.Itemstack;
            Assert.Equal("game:claypot-blue-cooked", cookedPot.Collectible.Code.ToString());
            Assert.Equal(0, firepit.otherCookingSlots.Length);

            ItemSlot tallow = TallowSlot(firepit);
            cookedPot.Collectible.SetTemperature(Sapi.World, cookedPot, HotTallow);
            tallow.Itemstack.Collectible.SetTemperature(Sapi.World, tallow.Itemstack, 20f);
            Assert.Equal(0f, tallow.Itemstack.Collectible.GetTransitionRateMul(Sapi.World, tallow, EnumTransitionType.Harden));
        }

        /// <summary>
        /// Once the pot has reverted, the firepit heats the tallow itself and leaves
        /// the pot alone - so a cold pot says nothing about the tallow in it.
        /// </summary>
        [VsTest]
        public async Task TallowInARevertedPotIsJudgedByItsOwnHeat()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 1, slots: 1);
            ItemSlot tallow = TallowSlot(firepit);
            ItemStack pot = firepit.inputSlot.Itemstack;
            pot.Collectible.SetTemperature(Sapi.World, pot, 20f);

            tallow.Itemstack.Collectible.SetTemperature(Sapi.World, tallow.Itemstack, HotTallow);
            Assert.Equal(0f, tallow.Itemstack.Collectible.GetTransitionRateMul(Sapi.World, tallow, EnumTransitionType.Harden));

            tallow.Itemstack.Collectible.SetTemperature(Sapi.World, tallow.Itemstack, 20f);
            Assert.Greater(tallow.Itemstack.Collectible.GetTransitionRateMul(Sapi.World, tallow, EnumTransitionType.Harden), 0f);
        }

        [VsTest]
        public void HotTallowDoesNotSet()
        {
            var slot = new DummySlot(World.Stack(Tallow, 1));
            var tallow = slot.Itemstack.Collectible;

            tallow.SetTemperature(Sapi.World, slot.Itemstack, HotTallow);
            Assert.Equal(0f, tallow.GetTransitionRateMul(Sapi.World, slot, EnumTransitionType.Harden));

            tallow.SetTemperature(Sapi.World, slot.Itemstack, 20f);
            Assert.Greater(tallow.GetTransitionRateMul(Sapi.World, slot, EnumTransitionType.Harden), 0f);
        }

        [VsTest]
        public void FinishedRodsCutIntoCandlesAndGiveTheStickBack()
        {
            GridRecipe cut = Sapi.World.GridRecipes.Single(r =>
                r.Output.ResolvedItemStack?.Collectible.Code.ToString() == "candela:candle-tallow");

            Assert.Equal(4, cut.Output.ResolvedItemStack.StackSize);
            var rod = cut.ResolvedIngredients.Single(i => i?.Code?.ToString() == "candela:dippingrod-6");
            Assert.Equal("game:stick", rod.ReturnedStack?.ResolvedItemstack?.Collectible.Code.ToString());
        }

        /// <summary>
        /// The interaction help is asked for every 15 ms while a firepit is in view.
        /// It once returned null for a firepit with no tallow, and the HUD's .Length
        /// on that took the client down the moment one came into view.
        /// </summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task LookingAtAPlainFirepitIsSafe()
        {
            World.SetBlock(FirepitCode, Firepit);
            await Ticks(2);

            await Interact.Aim(Firepit);
            await Frames.Wait(30);

            // Still in a game to ask: a crash ends the session long before this. The
            // block itself may have gone from extinct to cold - vanilla does that alone.
            Assert.True(World.BlockCode(Firepit).StartsWith("game:firepit-"), "the firepit is gone");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task DippingAddsACoatAndUsesTallow()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            await Player.Hold("candela:dippingrod-0");

            await Dip();

            Assert.Equal("candela:dippingrod-1", Player.Held?.Collectible.Code.ToString());
            Assert.Equal(5, TallowSlot(firepit).StackSize);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ACoatMustSetBeforeTheNext()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            await Player.Hold("candela:dippingrod-0");

            await Dip();
            await Dip(expectChange: false);
            Assert.Equal("candela:dippingrod-1", Player.Held?.Collectible.Code.ToString());
            Assert.Equal(5, TallowSlot(firepit).StackSize);

            await Hours(ItemDippingRod.SetHours * 1.5);
            await Dip();
            Assert.Equal("candela:dippingrod-2", Player.Held?.Collectible.Code.ToString());
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ColdTallowCannotBeDipped()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 1, slots: 1, temperature: 20f);
            await Player.Hold("candela:dippingrod-0");

            await Dip(expectChange: false);

            Assert.Equal("candela:dippingrod-0", Player.Held?.Collectible.Code.ToString());
            Assert.Equal(1, TallowSlot(firepit).StackSize);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task AFinishedRodIsNotDippedFurther()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 1, slots: 1);
            await Player.Hold("candela:dippingrod-6");

            await Dip(expectChange: false);

            Assert.Equal("candela:dippingrod-6", Player.Held?.Collectible.Code.ToString());
            Assert.Equal(1, TallowSlot(firepit).StackSize);
        }

        /// <summary>
        /// A firepit with a pot of molten tallow in it, cooked through vanilla's own
        /// DoSmelt and left to settle into the state cooking really leaves it in.
        /// </summary>
        static async Task<BlockEntityFirepit> FirepitWithCookedTallow(int fatPerSlot, int slots, float temperature = HotTallow)
        {
            World.SetBlock(FirepitCode, Firepit);
            await Ticks(2);

            var firepit = World.BE<BlockEntityFirepit>(Firepit);
            Cook(firepit, fatPerSlot, slots);
            firepit.inputSlot.MarkDirty();
            await Ticks(2);

            ItemSlot tallow = TallowSlot(firepit);
            Assert.NotNull(tallow, "cooking produced no molten tallow");
            tallow.Itemstack.Collectible.SetTemperature(Sapi.World, tallow.Itemstack, temperature);
            tallow.MarkDirty();
            firepit.MarkDirty(true);

            // Long enough for the client's copy of the firepit to catch up.
            await Ticks(10);
            return firepit;
        }

        static void Cook(BlockEntityFirepit firepit, int fatPerSlot, int slots)
        {
            firepit.inputSlot.Itemstack = World.Stack(EmptyPot, 1);
            firepit.inputSlot.MarkDirty();
            for (int i = 0; i < slots; i++)
            {
                firepit.otherCookingSlots[i].Itemstack = World.Stack(RenderedFat, fatPerSlot);
            }

            var pot = (BlockCookingContainer)firepit.inputSlot.Itemstack.Collectible;
            pot.DoSmelt(Sapi.World, firepit.Inventory as ISlotProvider, firepit.inputSlot, firepit.outputSlot);
        }

        static ItemSlot TallowSlot(BlockEntityFirepit firepit) => ItemMoltenTallow.FindIn(firepit);

        /// <summary>
        /// Hold right-click on the firepit until the rod changes, or for long enough
        /// that it would have.
        /// </summary>
        static async Task Dip(bool expectChange = true)
        {
            string before = Player.Held?.Collectible.Code.ToString();
            await Interact.Aim(Firepit);

            await Input.MouseDown(EnumMouseButton.Right);
            try
            {
                // 0.8 s of hold; a throttled window renders slowly, so the ceiling is generous.
                await Until(() => Player.Held?.Collectible.Code.ToString() != before, 200);
            }
            catch (AssertionException) when (!expectChange)
            {
                // Timing out is the expected outcome.
            }
            finally
            {
                await Input.MouseUp(EnumMouseButton.Right);
            }

            await Ticks(4);
            if (expectChange) Assert.True(Player.Held?.Collectible.Code.ToString() != before, "the dip did not take");
        }
    }
}
