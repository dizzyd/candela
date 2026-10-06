using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using candela;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using static Candela.Tests.Hands;

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
        // test cannot take it below - tallow cools at ItemMoltenWax.CooldownSpeed.
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

            // Added in AssetsFinalize to every finished state of every firepit, vanilla's
            // and any other mod's - ahead of Container, which opens the GUI and stops the
            // chain, so behind it a dip never runs.
            foreach (string state in new[] { "extinct", "lit", "cold" })
            {
                Assert.NotNull(Sapi.World.GetBlock(new AssetLocation("game:firepit-" + state)), $"no game:firepit-{state}");
            }
            foreach (Block firepit in Sapi.World.Blocks.Where(b => b is BlockFirepit))
            {
                var behaviors = firepit.BlockBehaviors.ToList();
                int container = behaviors.FindIndex(b => b is BlockBehaviorContainer);
                if (container < 0) continue;   // a construct stage
                int dipVat = behaviors.FindIndex(b => b is BlockBehaviorDipVat);
                Assert.True(dipVat >= 0, $"{firepit.Code} has no CandelaDipVat");
                Assert.True(dipVat < container, $"{firepit.Code} runs Container before CandelaDipVat");
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
            // Four from rendered fat, four from tallow stubs.
            Assert.Equal(8, recipes.Count(r => r.CooksInto?.ResolvedItemstack?.Collectible.Code.ToString() == Tallow));
            Assert.Equal(8, recipes.Count(r => r.CooksInto?.ResolvedItemstack?.Collectible.Code.ToString() == "candela:beeswax-molten"));
        }

        /// <summary>
        /// A pot full of fat makes twelve portions, more than the pot's six servings -
        /// which vanilla's preview held against it ("too small to make 12x molten
        /// tallow") while the cook, counting servings, went ahead.
        /// </summary>
        [VsTest]
        public void APotFullOfFatSaysItWillMakeTallow()
        {
            World.SetBlock(FirepitCode, Firepit);
            var firepit = World.BE<BlockEntityFirepit>(Firepit);
            firepit.inputSlot.Itemstack = World.Stack(EmptyPot, 1);
            firepit.otherCookingSlots[0].Itemstack = World.Stack(RenderedFat, 6);

            var pot = (BlockCookingContainer)firepit.inputSlot.Itemstack.Collectible;
            string text = pot.GetOutputText(Sapi.World, firepit.Inventory as ISlotProvider, firepit.inputSlot);
            Assert.Equal(Lang.Get("mealcreation-nonfood", 12, Lang.Get("candela:item-tallow-molten").ToLower()), text);
            Assert.True(pot.CanSmelt(Sapi.World, firepit.Inventory as ISlotProvider, firepit.inputSlot.Itemstack, null), "the pot would not cook it");
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

            // Two portions of tallow a lump of fat: six lumps, twelve portions.
            Assert.Equal(12, recipe.CooksInto.Quantity * servings);
        }

        [VsTest]
        public async Task CookingLeavesTheTallowInThePotOnTheFire()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);

            ItemSlot tallow = TallowSlot(firepit);
            Assert.NotNull(tallow, "no molten tallow in the pot's cooking slots after cooking");
            Assert.Equal(12, tallow.StackSize);

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

        /// <summary>
        /// Vanilla's DoSmelt gives the pot the ingredients' heat and the cooksInto stack
        /// none, so tallow cooked from fat at 170°C came out at 20°C: "Cold" in the
        /// firepit dialog, and already setting. Every other test here sets the tallow's
        /// temperature by hand after cooking, which is how that went unseen.
        /// </summary>
        [VsTest]
        public async Task CookedTallowKeepsTheFatsHeat()
        {
            World.SetBlock(FirepitCode, Firepit);
            await Ticks(2);
            var firepit = World.BE<BlockEntityFirepit>(Firepit);

            Cook(firepit, fatPerSlot: 3, slots: 2, fatTemperature: 170f);

            ItemSlot tallow = TallowSlot(firepit);
            Assert.Greater(tallow.Itemstack.Collectible.GetTemperature(Sapi.World, tallow.Itemstack), 160f);
        }

        /// <summary>
        /// The dialog's output line asks what the pot's contents would cook into, which
        /// for tallow is nothing - "No matching recipe found" under a pot just finished.
        /// </summary>
        [VsTest]
        public async Task ThePotSaysWhetherItsTallowIsReady()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 1, slots: 1);
            var inventory = (InventorySmelting)firepit.Inventory;
            Assert.Equal(Lang.Get("candela:firepit-molten-tallow"), inventory.GetOutputText());

            ItemSlot tallow = TallowSlot(firepit);
            tallow.Itemstack.Collectible.SetTemperature(Sapi.World, tallow.Itemstack, 20f);
            Assert.Equal(Lang.Get("candela:firepit-setting-tallow"), inventory.GetOutputText());
        }

        /// <summary>
        /// The pot patches go on both sides, and singleplayer's two sides share one
        /// process: registered per side, each postfix would run twice.
        /// </summary>
        [VsTest, RequiresClient]
        public void PotPatchesRegisterOnce()
        {
            foreach (string method in new[] { "DoSmelt", "GetOutputText" })
            {
                var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(BlockCookingContainer), method));
                Assert.Equal(1, info.Postfixes.Count(p => p.owner == WaxPotPatch.HarmonyId));
            }
        }

        /// <summary>
        /// Off the fire, fresh tallow should set within about half a game hour. It took
        /// two: vanilla cooling, a half-hour hold at heat after every warming, and a
        /// half-hour harden on top.
        /// </summary>
        [VsTest]
        public async Task TallowOffTheFireSetsWithinHalfAnHour()
        {
            var slot = new DummySlot(World.Stack(Tallow, 6));
            slot.Itemstack.Collectible.SetTemperature(Sapi.World, slot.Itemstack, 170f);

            await Hours(0.1);
            slot.Itemstack.Collectible.UpdateAndGetTransitionStates(Sapi.World, slot);
            Assert.Equal(Tallow, slot.Itemstack.Collectible.Code.ToString(), "set before it could have cooled");
            Assert.Greater(ItemMoltenWax.Temperature(Sapi.World, slot), ((ItemMoltenWax)slot.Itemstack.Collectible).SetsBelow);

            await Hours(0.45);
            slot.Itemstack.Collectible.UpdateAndGetTransitionStates(Sapi.World, slot);
            Assert.Equal(RenderedFat, slot.Itemstack.Collectible.Code.ToString(), "still molten half an hour off the fire");
            // Six portions, three lumps: it melted two to the lump.
            Assert.Equal(3, slot.Itemstack.StackSize);
        }

        /// <summary>
        /// A burning fire keeps heating a pot of tallow once it has cooked. The firepit
        /// heats its input only while it can still cook something, and tallow matches
        /// no recipe, so the heat stopped with the cook - the tallow cooled on a fire
        /// that was still going, and the fire let itself go out at the end of the log.
        /// </summary>
        [VsTest]
        public async Task ABurningFireKeepsTheTallowHot()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 3, slots: 2, temperature: 60f);
            firepit.fuelSlot.Itemstack = World.Stack("game:firewood", 8);
            firepit.igniteWithFuel(firepit.fuelSlot.Itemstack);
            firepit.furnaceTemperature = 400f;

            Assert.True(firepit.canHeatInput(), "the firepit will not heat a pot of tallow");

            await Ticks(60);
            Assert.Greater(ItemMoltenWax.Temperature(Sapi.World, TallowSlot(firepit)), 80f, "the fire did not warm the tallow");
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

        /// <summary>The wicks are flax fibres, as vanilla's own candle takes - not twine.</summary>
        [VsTest]
        public void ARodIsAStickAndFourFlaxFibres()
        {
            GridRecipe rod = Sapi.World.GridRecipes.Single(r =>
                r.Output.ResolvedItemStack?.Collectible.Code.ToString() == "candela:dippingrod-0");

            var wicks = rod.ResolvedIngredients.Single(i => i?.Code?.ToString() == "game:flaxfibers");
            Assert.Equal(4, wicks.Quantity);
            Assert.True(rod.ResolvedIngredients.Any(i => i?.Code?.ToString() == "game:stick"), "the rod takes no stick");
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

        /// <summary>
        /// A real cook, with the firepit's dialog open as a player has it: the pot should
        /// stand open with its tallow once done. With the dialog open the client hears of
        /// each slot on its own, and the pot's change arrived before the tallow's - so
        /// the renderer was chosen for a pot with nothing in it, kept its lid on, and was
        /// never asked again. FirepitWithCookedTallow sets the firepit all at once and
        /// could not show this.
        /// </summary>
        [VsTest(TimeoutMs = 240000), RequiresClient]
        public async Task ACookedPotStandsOpenWithTheDialogOpen()
        {
            World.SetBlock(FirepitCode, Firepit);
            await Ticks(2);
            var firepit = World.BE<BlockEntityFirepit>(Firepit);
            firepit.inputSlot.Itemstack = World.Stack(EmptyPot, 1);
            ItemStack fat = World.Stack(RenderedFat, 6);
            fat.Collectible.SetTemperature(Sapi.World, fat, 150f);
            firepit.otherCookingSlots[0].Itemstack = fat;
            firepit.fuelSlot.Itemstack = World.Stack("game:firewood", 32);
            firepit.igniteWithFuel(firepit.fuelSlot.Itemstack);
            firepit.MarkDirty(true);
            await Ticks(10);

            await EmptyHand();
            await Interact.UseBlock(Firepit);
            await Gui.WaitFor<GuiDialogBlockEntityFirepit>();

            await Until(() => TallowSlot(firepit) != null, 4000);
            await Ticks(20);

            await OnClient();
            var clientFirepit = (BlockEntityFirepit)Capi.World.BlockAccessor.GetBlockEntity(Firepit);
            bool clientHasTallow = TallowSlot(clientFirepit) != null;
            string renderer = ContentRenderer(clientFirepit)?.GetType().Name;
            await OnServer();

            Assert.True(clientHasTallow, "the client never saw the tallow");
            Assert.Equal(nameof(WaxPotRenderer), renderer, "the pot on the fire is drawn by the wrong renderer");
        }

        static object ContentRenderer(BlockEntityFirepit firepit)
        {
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            object renderer = typeof(BlockEntityFirepit).GetField("renderer", Any)?.GetValue(firepit);
            return renderer?.GetType().GetField("contentStackRenderer", Any)?.GetValue(renderer);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task DippingAddsACoatAndUsesTallow()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            await Player.Hold("candela:dippingrod-0");

            await Dip();

            Assert.Equal("candela:dippingrod-1", Player.Held?.Collectible.Code.ToString());
            Assert.Equal(11, TallowSlot(firepit).StackSize);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ACoatMustSetBeforeTheNext()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            await Player.Hold("candela:dippingrod-0");

            await Dip();
            await Dip(expectChange: false);
            Assert.Equal("candela:dippingrod-1", Player.Held?.Collectible.Code.ToString());
            Assert.Equal(11, TallowSlot(firepit).StackSize);

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
            Assert.Equal(2, TallowSlot(firepit).StackSize);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task AFinishedRodIsNotDippedFurther()
        {
            var firepit = await FirepitWithCookedTallow(fatPerSlot: 1, slots: 1);
            await Player.Hold("candela:dippingrod-6");

            await Dip(expectChange: false);

            Assert.Equal("candela:dippingrod-6", Player.Held?.Collectible.Code.ToString());
            Assert.Equal(2, TallowSlot(firepit).StackSize);
        }

        /// <summary>
        /// A firepit with a pot of molten tallow in it, cooked through vanilla's own
        /// DoSmelt and left to settle into the state cooking really leaves it in.
        /// </summary>
        internal static async Task<BlockEntityFirepit> FirepitWithCookedTallow(int fatPerSlot, int slots, float temperature = HotTallow, string firepitCode = FirepitCode)
        {
            World.SetBlock(firepitCode, Firepit);
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

        static void Cook(BlockEntityFirepit firepit, int fatPerSlot, int slots, float fatTemperature = 20f)
        {
            firepit.inputSlot.Itemstack = World.Stack(EmptyPot, 1);
            firepit.inputSlot.MarkDirty();
            for (int i = 0; i < slots; i++)
            {
                ItemStack fat = World.Stack(RenderedFat, fatPerSlot);
                fat.Collectible.SetTemperature(Sapi.World, fat, fatTemperature);
                firepit.otherCookingSlots[i].Itemstack = fat;
            }

            var pot = (BlockCookingContainer)firepit.inputSlot.Itemstack.Collectible;
            pot.DoSmelt(Sapi.World, firepit.Inventory as ISlotProvider, firepit.inputSlot, firepit.outputSlot);
        }

        internal static ItemSlot TallowSlot(BlockEntityFirepit firepit) => ItemMoltenWax.FindIn(firepit);

        /// <summary>
        /// Hold right-click on the firepit until the rod changes, or for long enough
        /// that it would have.
        /// </summary>
        internal static async Task Dip(bool expectChange = true)
        {
            string before = Player.Held?.Collectible.Code.ToString();
            await Interact.Aim(Firepit);

            await Input.MouseDown(EnumMouseButton.Right);
            try
            {
                // Checked mid-hold, not after: with the firepit's Container ahead of the
                // dip vat the dialog opened on the first frame, the injected hold carried
                // on behind it - a player's ends at the dialog - and the finished dip
                // closed it again, so the end state looked like a clean dip.
                await Frames.Wait(5);
                Assert.False((await Gui.OpenDialogs()).Contains("GuiDialogBlockEntityFirepit"),
                    "dipping opened the firepit's dialog");

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
