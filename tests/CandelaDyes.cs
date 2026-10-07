using System.Linq;
using System.Text;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Client;
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

        /// <summary>
        /// Every candle body in the bunch's, a chandelier's and a lantern's shapes takes its
        /// own dye, beside its flame, in the order the candles go in.
        /// </summary>
        [VsTest]
        public void EveryCandleInAShapeTakesItsOwnDye()
        {
            string[] dyes = WaxDyes.All.ToArray();
            CandleLook LookOf(int i) => new(null, dyes[i % dyes.Length]);

            Shape bunch = Shape.TryGet(Sapi, "game:shapes/block/wax/bunch.json");
            Assert.Equal(string.Join(",", Enumerable.Range(0, 9).Select(i => dyes[i % dyes.Length])), string.Join(",", PaintedDyes(CandleMeshes.Recoloured(bunch, LookOf))));

            Shape chandelier = Shape.TryGet(Sapi, "game:shapes/block/metal/chandelier/candle8.json");
            Assert.Equal(string.Join(",", Enumerable.Range(0, 8).Select(i => dyes[i % dyes.Length])), string.Join(",", PaintedDyes(CandleMeshes.Recoloured(chandelier, LookOf))));

            Shape lantern = Shape.TryGet(Sapi, "game:shapes/block/metal/lantern/small/ground.json");
            Assert.Equal("black", string.Join(",", PaintedDyes(CandleMeshes.Recoloured(lantern, _ => new CandleLook("red", "black")))));
        }

        /// <summary>
        /// Placed, a dyed candle is drawn from its own wax's dyed texture - tallow's for a
        /// tallow bunch, beeswax's for a chandelier and a lantern - and an undyed one is not.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task PlacedCandlesAreDrawnDyed()
        {
            BlockPos tallow = P(5, 1, 8), chandelier = P(8, 1, 8), lantern = P(11, 1, 8), plain = P(8, 1, 11);
            World.SetBlock("candela:tallowcandles-2", tallow);
            World.SetBlock("game:chandelier-candle0", chandelier);
            World.SetBlock("game:bunchocandles-2", plain);
            await Ticks(2);
            World.BE<BECandles>(tallow).SetFuel(2 * TallowHours, new CandleLook("red", "black"));
            World.BE<BECandles>(plain).SetFuel(2 * 432, new CandleLook("red", null));
            var be = World.BE<BECandles>(chandelier);
            be.AddCandle(432, new CandleLook(null, "white"));
            Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("game:chandelier-candle1")).BlockId, chandelier);

            World.SetBlock("game:lantern-large-up", lantern);
            await Ticks(2);
            ItemStack from = World.Stack("game:lantern-large-up", 1);
            LanternStack.Write(from, 432, "game:bunchocandles", snuffed: false, new CandleLook(null, "purple"));
            foreach (var behavior in World.BE<BlockEntity>(lantern).Behaviors) behavior.OnBlockPlaced(from);

            for (int i = 0; i < 100; i++)
            {
                await OnClient();
                bool synced = Capi.World.BlockAccessor.GetBlockEntity(tallow) is BECandles t && t.LookOf(0).Dye == "black"
                    && Capi.World.BlockAccessor.GetBlockEntity(chandelier) is BECandles c && c.LookOf(0).Dye == "white"
                    && Capi.World.BlockAccessor.GetBlockEntity(lantern)?.GetBehavior<BEBehaviorLanternFuel>()?.Look.Dye == "purple";
                await OnServer();
                if (synced) break;
                await Ticks(1);
            }

            await OnClient();
            Assert.True(CandelaFlames.SamplesTexture(CandelaFlames.Drawn(tallow), "candela:block/candle-tallow-black"), "a black tallow bunch is not black tallow");
            Assert.True(CandelaFlames.SamplesTexture(CandelaFlames.Drawn(tallow), "candela:block/flame-red"), "its red flames lost their colour");
            Assert.True(CandelaFlames.SamplesTexture(CandelaFlames.Drawn(chandelier), "candela:block/candle-beeswax-white"), "a white candle on a chandelier is not white");
            Assert.True(CandelaFlames.SamplesTexture(CandelaFlames.Drawn(lantern), "candela:block/candle-beeswax-purple"), "a purple lantern candle is not purple");
            MeshData plainMesh = CandelaFlames.Drawn(plain);
            Assert.False(WaxDyes.All.Any(d => CandelaFlames.SamplesTexture(plainMesh, "candela:block/candle-beeswax-" + d)), "an undyed bunch was drawn dyed");
            await OnServer();
        }

        /// <summary>
        /// Off the block: candles, stubs, the rod and the mould are drawn dyed in hand and
        /// on the ground or a shelf, from their own wax's texture; undyed they are left to
        /// draw themselves.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task DyedItemsAreDrawnDyed()
        {
            await OnClient();
            var cases = new (string code, string wax)[]
            {
                ("game:candle", "beeswax"),
                ("candela:candle-tallow", "tallow"),
                ("candela:candlestub-beeswax-50", "beeswax"),
                ("candela:candlestub-tallow-25", "tallow"),
                ("candela:dippingrod-6", "tallow"),
                ("candela:candlemould-blue-tallow", "tallow"),
            };
            foreach (var (code, wax) in cases)
            {
                // On the client's own items: World.Stack is the server's.
                ItemStack dyed = WaxDyes.Stamp(new ItemStack(Capi.World.GetItem(new AssetLocation(code))), "green");
                ItemStack plain = new ItemStack(Capi.World.GetItem(new AssetLocation(code)));
                var source = (IContainedMeshSource)dyed.Collectible;

                MeshData mesh = source.GenMesh(new DummySlot(dyed), Capi.ItemTextureAtlas, null);
                Assert.NotNull(mesh, code + " dyed has no mesh");
                Assert.True(CandelaFlames.SamplesTexture(mesh, $"candela:block/candle-{wax}-green", Capi.ItemTextureAtlas), code + " on a shelf is not green");
                Assert.Null(source.GenMesh(new DummySlot(plain), Capi.ItemTextureAtlas, null), code + " undyed should be left to draw itself");
                Assert.True(source.GetMeshCacheKey(new DummySlot(dyed)) != source.GetMeshCacheKey(new DummySlot(plain)), code + " dyed and undyed share a shelf mesh");

                ItemRenderInfo dyedInfo = new(), plainInfo = new();
                dyed.Collectible.OnBeforeRender(Capi, dyed, EnumItemRenderTarget.Gui, ref dyedInfo);
                plain.Collectible.OnBeforeRender(Capi, plain, EnumItemRenderTarget.Gui, ref plainInfo);
                Assert.NotNull(dyedInfo.ModelRef, code + " in hand has no dyed model");
                Assert.True(dyedInfo.ModelRef != plainInfo.ModelRef, code + " in hand is drawn undyed");
            }
            await OnServer();
        }

        /// <summary>Molten wax dyed shows as vanilla's liquid dye, in a pot on the fire or carried.</summary>
        [VsTest, RequiresClient]
        public async Task EveryDyeHasAMoltenSurface()
        {
            await OnClient();
            foreach (string dye in WaxDyes.All) Assert.NotNull(CollectibleBehaviorPotOfWax.DyeSurface(dye), $"no surface for {dye} wax");
            Assert.Null(CollectibleBehaviorPotOfWax.DyeSurface(null));
            await OnServer();
        }

        /// <summary>
        /// Not an assertion: black candles with red flames, as the player asked, and a
        /// candle of every dye, a chandelier and a lantern, at night - for the eye.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task DyedWaxForTheEye()
        {
            await CandelaFlames.DarkRoom();

            // Black tallow, red flames, front and centre.
            BlockPos black = P(8, 1, 6);
            World.SetBlock("candela:tallowcandles-5", black);
            await Ticks(2);
            World.BE<BECandles>(black).SetFuel(5 * TallowHours, new CandleLook("red", "black"));

            // A candle of every dye, along the back.
            string[] dyes = WaxDyes.All.ToArray();
            for (int i = 0; i < dyes.Length; i++)
            {
                BlockPos at = P(2 + i, 1, 11);
                World.SetBlock("candela:tallowcandles-1", at);
                await Ticks(1);
                World.BE<BECandles>(at).SetFuel(TallowHours, new CandleLook(null, dyes[i]));
            }

            // A chandelier of white and black beeswax, and a purple lantern candle.
            BlockPos chandelier = P(4, 1, 7);
            World.SetBlock("game:chandelier-candle0", chandelier);
            await Ticks(2);
            var be = World.BE<BECandles>(chandelier);
            for (int i = 0; i < 8; i++)
            {
                be.AddCandle(432, new CandleLook(i % 2 == 0 ? "red" : null, i % 2 == 0 ? "black" : "white"));
                Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("game:chandelier-candle" + (i + 1))).BlockId, chandelier);
            }
            BlockPos lantern = P(12, 1, 7);
            World.SetBlock("game:lantern-large-up", lantern);
            await Ticks(2);
            ItemStack from = World.Stack("game:lantern-large-up", 1);
            LanternStack.Write(from, 432, "game:bunchocandles", snuffed: false, new CandleLook("violet", "purple"));
            foreach (var behavior in World.BE<BlockEntity>(lantern).Behaviors) behavior.OnBlockPlaced(from);

            // Particles for the flames: see CandelaFlames.ColouredFlamesForTheEye.
            await Player.Teleport(P(8, 1, 2).ToVec3d().Add(0.5, 0, 0.5));
            await Ticks(25 * 33);
            await OnClient();
            var particles = CandelaFlames.ParticleRenderer();
            bool wasOn = (bool)particles.field.GetValue(particles.system);
            particles.field.SetValue(particles.system, true);
            await OnServer();
            await Cmd("/time speed 60");
            try
            {
                await Input.Hotkey("togglehud");
                await Player.Teleport(P(8, 1, 2).ToVec3d().Add(0.5, 0, 0.5));
                await Interact.LookAt(P(8, 1, 9));
                await Frames.Wait(90);
                Log("shot: " + await Shot.Take("results/dyes-room.png"));

                await Player.Teleport(black.ToVec3d().Add(0.5, -0.5, -1.1));
                await Interact.LookAt(black.ToVec3d().Add(0.5, 0.3, 0.5));
                await Frames.Wait(40);
                Log("shot: " + await Shot.Take("results/dyes-black-red.png"));

                await Player.Teleport(P(7, 1, 9).ToVec3d().Add(0.5, -0.3, 0.2));
                await Interact.LookAt(P(7, 1, 11).ToVec3d().Add(0.5, 0.2, 0.5));
                await Frames.Wait(40);
                Log("shot: " + await Shot.Take("results/dyes-every.png"));

                await Player.Teleport(chandelier.ToVec3d().Add(0.5, -0.4, -1.3));
                await Interact.LookAt(chandelier.ToVec3d().Add(0.5, 0.6, 0.5));
                await Frames.Wait(40);
                Log("shot: " + await Shot.Take("results/dyes-chandelier.png"));
                await Input.Hotkey("togglehud");
            }
            finally
            {
                await OnClient();
                particles.field.SetValue(particles.system, wasOn);
                await OnServer();
                await Cmd("/time speed 0");
            }
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

        /// <summary>The dye of each element whose faces CandleMeshes painted with one, in shape order.</summary>
        static System.Collections.Generic.IEnumerable<string> PaintedDyes(Shape shape)
        {
            System.Collections.Generic.IEnumerable<string> Walk(ShapeElement element)
            {
                string painted = element.FacesResolved?.FirstOrDefault(f => f?.Texture?.StartsWith("candela-dye-") == true)?.Texture;
                if (painted != null) yield return painted.Substring("candela-dye-".Length);
                foreach (ShapeElement child in element.Children ?? []) foreach (string d in Walk(child)) yield return d;
            }
            return shape.Elements.SelectMany(Walk);
        }

        /// <summary><paramref name="rod"/> with another coat of tallow dyed <paramref name="dye"/>.</summary>
        static ItemStack Coat(ItemStack rod, string dye) =>
            ((ItemDippingRod)rod.Collectible).WithAnotherLayer(Sapi.World, rod, WaxDyes.Stamp(World.Stack("candela:tallow-molten", 1), dye));
    }
}
