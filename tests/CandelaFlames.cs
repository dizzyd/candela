using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using static Candela.Tests.Hands;

namespace Candela.Tests
{
    /// <summary>
    /// Coloured flames: wicks treated with a mineral salt, carried by the dipping rod
    /// and the mould into the candles, by the candles into bunches, chandeliers and
    /// lanterns - one colour a candle, so they mix - and out again as items.
    /// </summary>
    public class CandelaFlames
    {
        static BlockPos Bunch => P(8, 1, 8);
        static BlockPos Lantern => P(8, 1, 8);
        static BlockPos Chandelier => P(8, 3, 8);

        const double BeeswaxHours = 432;
        const string LanternCode = "game:lantern-large-up";

        [BeforeEach, AfterEach]
        public void DefaultConfig()
        {
            CandelaConfig.Current.AssignFrom(new CandelaConfig());
            CandelaConfig.Current.WeatherPutsOut = false;
        }

        [VsTest]
        public void WicksAndVerdigrisLoad()
        {
            foreach (string flame in FlameColours.All.Select(c => c.Code))
            {
                var wick = Sapi.World.GetItem(new AssetLocation("candela:wick-" + flame));
                Assert.NotNull(wick, "wick-" + flame);
                Assert.Equal(flame, FlameColours.OfWick(wick));
                Assert.True(Sapi.World.GridRecipes.Any(r => r.Output.ResolvedItemStack?.Collectible == wick), "no recipe for wick-" + flame);
            }
            Assert.NotNull(Sapi.World.GetItem(new AssetLocation("candela:powder-verdigris")));
        }

        /// <summary>
        /// Every block that draws flames in its model declares every colour's flame
        /// texture: a block missing one would draw that colour's flame untextured. The
        /// list of places is in FlameColours. On the client: the server keeps no
        /// textures.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task EveryFlameBlockHasEveryColoursTexture()
        {
            await OnClient();
            string[] blocks = { "game:bunchocandles-*", "game:candle", "candela:tallowcandles-*", "candela:tallowcandle", "game:chandelier-*", "game:lantern-*" };
            foreach (string pattern in blocks)
            {
                Block[] found = Capi.World.SearchBlocks(new AssetLocation(pattern));
                Assert.True(found.Length > 0, "no blocks for " + pattern);
                foreach (Block block in found)
                foreach (FlameColours.Colour colour in FlameColours.All)
                {
                    Assert.True(block.Textures?.ContainsKey(FlameMeshes.TextureCode(colour.Code)) == true, $"{block.Code} does not declare the {colour.Code} flame");
                }
            }
            await OnServer();
        }

        [VsTest]
        public void EveryColourHasItsTextures()
        {
            foreach (FlameColours.Colour colour in FlameColours.All)
            {
                Assert.True(Sapi.Assets.Exists(new AssetLocation($"candela:textures/block/flame-{colour.Code}.png")), $"no flame-{colour.Code} texture");
                Assert.True(Sapi.Assets.Exists(new AssetLocation($"candela:textures/item/wick-{colour.Code}.png")), $"no wick-{colour.Code} texture");
            }
        }

        [VsTest]
        public void MalachiteGrindsToVerdigris()
        {
            var nugget = Sapi.World.GetItem(new AssetLocation("game:nugget-malachite"));
            Assert.NotNull(nugget);
            Assert.Equal("candela:powder-verdigris", nugget.GrindingProps?.GroundStack?.ResolvedItemstack?.Collectible.Code.ToString(), "the quern patch did not apply");

            // And only malachite: the patch is keyed by type.
            var galena = Sapi.World.GetItem(new AssetLocation("game:nugget-galena"));
            Assert.Null(galena.GrindingProps?.GroundStack?.ResolvedItemstack, "galena grinds to something now");
        }

        /// <summary>From wicks to rod, through every dip, to the tapers cut from it.</summary>
        [VsTest]
        public void ADippingRodCarriesItsWicksColourToItsCandles()
        {
            var rod0 = (ItemDippingRod)Sapi.World.GetItem(new AssetLocation("candela:dippingrod-0"));
            var made = new DummySlot(World.Stack("candela:dippingrod-0", 1));
            rod0.OnCreatedByCrafting([new DummySlot(World.Stack("game:stick", 1)), new DummySlot(World.Stack("candela:wick-green", 4))], made, Recipe("candela:dippingrod-0", "candela:wick-*"));
            Assert.Equal("green", FlameColours.Of(made.Itemstack));

            ItemStack rod = made.Itemstack;
            for (int i = 0; i < ItemDippingRod.MaxLayers; i++) rod = ((ItemDippingRod)rod.Collectible).WithAnotherLayer(Sapi.World, rod, null);
            Assert.Equal("candela:dippingrod-6", rod.Collectible.Code.ToString());
            Assert.Equal("green", FlameColours.Of(rod), "a dip lost the colour");

            var candles = new DummySlot(World.Stack("candela:candle-tallow", 4));
            candles.Itemstack.Collectible.OnCreatedByCrafting([new DummySlot(rod)], candles, Recipe("candela:candle-tallow", "candela:dippingrod-6"));
            Assert.Equal("green", FlameColours.Of(candles.Itemstack));
            Assert.True(candles.Itemstack.GetName().Contains("green flame"), "named " + candles.Itemstack.GetName());
        }

        /// <summary>Plain fibres still make plain rods and plain candles.</summary>
        [VsTest]
        public void PlainFibresStayPlain()
        {
            var rod0 = (ItemDippingRod)Sapi.World.GetItem(new AssetLocation("candela:dippingrod-0"));
            var made = new DummySlot(World.Stack("candela:dippingrod-0", 1));
            rod0.OnCreatedByCrafting([new DummySlot(World.Stack("game:stick", 1)), new DummySlot(World.Stack("game:flaxfibers", 4))], made, Recipe("candela:dippingrod-0", "game:flaxfibers"));
            Assert.Null(FlameColours.Of(made.Itemstack));
            Assert.Equal("Tallow candle", World.Stack("candela:candle-tallow", 1).GetName());
        }

        [VsTest]
        public void ColouredAndPlainCandlesDoNotStack()
        {
            ItemStack plain = World.Stack("game:candle", 1), red = FlameColours.Stamp(World.Stack("game:candle", 1), "red");
            Assert.Equal(0, plain.Collectible.GetMergableQuantity(plain, red, EnumMergePriority.AutoMerge));
            Assert.Equal(1, red.Collectible.GetMergableQuantity(red, FlameColours.Stamp(World.Stack("game:candle", 1), "red"), EnumMergePriority.AutoMerge));
        }

        // ----- bunches -----

        [VsTest]
        public async Task ABunchLightsWithItsCandlesColour()
        {
            var be = await PlaceBunch("blue");
            Assert.Equal(42, BunchLight()[0], "blue's hue");
            Assert.Equal(CandelaConfig.Current.FlameLightSaturation, BunchLight()[1]);
            Assert.Equal(7, BunchLight()[2], "brightness is the bunch's own");
        }

        /// <summary>
        /// One light for the bunch: the colour most of them burn, plain on a tie - a red
        /// and a blue do not pick one at random.
        /// </summary>
        /// <summary>How strongly the light is coloured is a setting: 7 the most, 0 lights plain.</summary>
        [VsTest]
        public async Task TheLightsSaturationIsASetting()
        {
            await PlaceBunch("blue");
            byte[] plain = Sapi.World.GetBlock(new AssetLocation("game:bunchocandles-1")).LightHsv;

            CandelaConfig.Current.FlameLightSaturation = 7;
            Assert.Equal(7, BunchLight()[1]);
            Assert.Equal(42, BunchLight()[0], "still blue");

            CandelaConfig.Current.FlameLightSaturation = 0;
            Assert.Equal(plain[0], BunchLight()[0], "0 lights plain: the hue");
            Assert.Equal(plain[1], BunchLight()[1], "0 lights plain: the saturation");
        }

        [VsTest]
        public async Task MixedBunchesTakeTheCommonestColour()
        {
            var be = await PlaceBunch("red");
            await AddCandle(be, "blue");
            Assert.Equal("red,blue", Seq(be.FlameColoursOfCandles));
            Assert.Equal(7, BunchLight()[0], "a tie should light plain");

            await AddCandle(be, "blue");
            Assert.Equal(42, BunchLight()[0], "two blue to one red should light blue");

            await AddCandle(be, null);
            await AddCandle(be, null);
            Assert.Equal("red,blue,blue,-,-", Seq(be.FlameColoursOfCandles));
            Assert.Equal(7, BunchLight()[0], "two plain and two blue tie");
        }

        /// <summary>
        /// The colour reaches the light the engine has stored, not only what GetLightHsv
        /// says. Read on the client: the server keeps light levels but no hue table to
        /// turn them into colours (WorldMap.GetLightRGBSVec4f throws there).
        /// </summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task TheWorldIsLitInTheColour()
        {
            var be = await PlaceBunch(null);
            await AddCandle(be, "blue");
            await AddCandle(be, "blue");
            BlockPos above = Bunch.UpCopy();

            Assert.True(await ClientLightTurns(above, blue: true), "the room did not turn blue");

            // And back: the blue ones taken off again.
            await TakeCandle(be);
            await TakeCandle(be);
            Assert.True(await ClientLightTurns(above, blue: false), "the blue did not leave the room");
        }

        /// <summary>
        /// Every chandelier shape gives each of its candles a flame of its own, in the order
        /// the candles go in - the six-candle one too, whose sixth tip vanilla left unlit -
        /// and the shape vanilla draws plain chandeliers from is left as it was.
        /// </summary>
        [VsTest]
        public void EachChandelierCandleHasItsOwnFlame()
        {
            string[] rainbow = FlameColours.All.Select(c => c.Code).ToArray();
            for (int n = 1; n <= 8; n++)
            {
                Shape shape = Shape.TryGet(Sapi, $"game:shapes/block/metal/chandelier/candle{n}.json");
                Assert.NotNull(shape, $"no shape for {n} candles");
                Shape coloured = FlameMeshes.Recoloured(shape, i => rainbow[i % rainbow.Length]);

                string expected = string.Join(",", Enumerable.Range(0, n).Select(i => rainbow[i % rainbow.Length]));
                Assert.Equal(expected, string.Join(",", PaintedFlames(coloured)), $"{n} candles");
                Assert.Equal(0, PaintedFlames(shape).Count(), $"{n} candles: the original shape was changed");
            }
        }

        /// <summary>The bunch's tips, which do not glow, in the order of the particle flames' wicks; and the single candle's.</summary>
        [VsTest]
        public void EveryBunchCandleHasItsOwnTip()
        {
            string[] rainbow = FlameColours.All.Select(c => c.Code).ToArray();
            Shape bunch = Shape.TryGet(Sapi, "game:shapes/block/wax/bunch.json");
            string expected = string.Join(",", Enumerable.Range(0, 9).Select(i => rainbow[i % rainbow.Length]));
            Assert.Equal(expected, string.Join(",", PaintedFlames(FlameMeshes.Recoloured(bunch, i => rainbow[i % rainbow.Length]))));

            Shape single = Shape.TryGet(Sapi, "game:shapes/block/wax/candle3.json");
            Assert.Equal("violet", string.Join(",", PaintedFlames(FlameMeshes.Recoloured(single, _ => "violet"))));
        }

        [VsTest]
        public void EveryLanternShapeHasOneFlame()
        {
            foreach (string size in new[] { "large", "small" })
            foreach (string hang in new[] { "ground", "wall", "ceiling" })
            {
                Shape shape = Shape.TryGet(Sapi, $"game:shapes/block/metal/lantern/{size}/{hang}.json");
                Assert.NotNull(shape, $"no {size} {hang} lantern");
                Assert.Equal("teal", string.Join(",", PaintedFlames(FlameMeshes.Recoloured(shape, _ => "teal"))), $"{size} {hang}");
            }
        }

        /// <summary>The colour of each element whose faces FlameMeshes repainted, in shape order.</summary>
        static System.Collections.Generic.IEnumerable<string> PaintedFlames(Shape shape)
        {
            System.Collections.Generic.IEnumerable<string> Walk(ShapeElement element)
            {
                string painted = element.FacesResolved?.FirstOrDefault(f => f?.Texture?.StartsWith("candela-flame-") == true)?.Texture;
                if (painted != null) yield return painted.Substring("candela-flame-".Length);
                foreach (ShapeElement child in element.Children ?? []) foreach (string c in Walk(child)) yield return c;
            }
            return shape.Elements.SelectMany(Walk);
        }

        /// <summary>
        /// The server's own light follows a candle on or off that changes the colour. The
        /// client relights itself (Relight.Synced), but the server's light is what a
        /// client gets with the chunk next time it loads.
        /// </summary>
        [VsTest]
        public async Task TheServersLightFollowsTheColour()
        {
            var be = await PlaceBunch("red");
            await ServerLightIs(Bunch, "red", "a red bunch");
            await AddCandle(be, "blue");
            await ServerLightIs(Bunch, null, "red and blue tie, so plain");
            await AddCandle(be, "blue");
            await ServerLightIs(Bunch, "blue", "two blue to one red");
            await TakeCandle(be);
            await ServerLightIs(Bunch, null, "one blue taken off, a tie again");
            await TakeCandle(be);
            await ServerLightIs(Bunch, "red", "the red one left");
        }

        /// <summary>
        /// Coloured state restored onto a block entity already running - a chandelier
        /// that fell and landed, a schematic pasted - relights in its colour, though it
        /// is no more or less lit than before.
        /// </summary>
        [VsTest]
        public async Task RestoredStateRelightsInItsColour()
        {
            var be = await PlaceBunch(null);
            var tree = new TreeAttribute();
            be.ToTreeAttributes(tree);
            tree.SetString("candela:flames", "blue");
            be.FromTreeAttributes(tree, Sapi.World);
            await ServerLightIs(Bunch, "blue", "a bunch restored blue");
        }

        [VsTest]
        public async Task RestoredLanternStateRelightsInItsColour()
        {
            await PlaceLantern(null, glass: null);
            var be = World.BE<BlockEntity>(Lantern);
            var tree = new TreeAttribute();
            be.ToTreeAttributes(tree);
            tree.SetString(FlameColours.Attr, "violet");
            be.FromTreeAttributes(tree, Sapi.World);
            await ServerLightIs(Lantern, "violet", "a lantern restored violet");
        }

        /// <summary>
        /// Waits for the server's stored light at <paramref name="pos"/> to be
        /// <paramref name="flame"/>'s hue and saturation. The hue to within a step: where
        /// the engine recomputes a block's light it blends the sources through RGB and
        /// back (ChunkIlluminator.RecalcBlockLightAtPos), and a lone blue comes out 41.
        /// </summary>
        static async Task ServerLightIs(BlockPos pos, string flame, string when)
        {
            byte[] plain = World.GetBlock(pos).LightHsv;
            FlameColours.Colour colour = FlameColours.Get(flame);
            int hue = colour?.LightHue ?? plain[0], sat = colour != null ? CandelaConfig.Current.FlameLightSaturation : plain[1];

            (int hue, int sat) seen = default;
            for (int i = 0; i < 40; i++)
            {
                IWorldChunk chunk = Sapi.World.BlockAccessor.GetChunkAtBlockPos(pos);
                ushort light = chunk.Unpack_AndReadLight(MapUtil.Index3d(pos.X & 31, pos.Y & 31, pos.Z & 31, 32, 32), out int lightSat);
                seen = (light >> 10, lightSat);
                if (System.Math.Abs(seen.hue - hue) <= 1 && seen.sat == sat) return;
                await Ticks(1);
            }
            Assert.Fail($"{when}: the server's light is hue {seen.hue} sat {seen.sat}, not {flame ?? "plain"}'s {hue} {sat}");
        }

        static async Task<bool> ClientLightTurns(BlockPos pos, bool blue)
        {
            for (int i = 0; i < 100; i++)
            {
                await OnClient();
                Vec4f rgb = Capi.World.BlockAccessor.GetLightRGBs(pos);
                await OnServer();
                if ((rgb.B > rgb.R * 1.2f) == blue) return true;
                await Ticks(1);
            }
            return false;
        }

        [VsTest]
        public async Task TheLastCandleOnIsTheFirstOff()
        {
            var be = await PlaceBunch("red");
            await AddCandle(be, "violet");

            double hours = be.TakeCandle(out CandleLook look);
            Assert.Equal("violet", look.Flame);
            Assert.Close(hours, BeeswaxHours, 0.5);
            Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("game:bunchocandles-1")).BlockId, Bunch);
            await Ticks(2);
            Assert.Equal("red", Seq(be.FlameColoursOfCandles));
        }

        [VsTest]
        public async Task ABrokenBunchDropsAStackForEachColour()
        {
            var be = await PlaceBunch("red");
            await AddCandle(be, null);
            await AddCandle(be, "red");

            ItemStack[] drops = World.GetBlock(Bunch).GetDrops(Sapi.World, Bunch, null);

            Assert.Equal(2, drops.Length);
            Assert.Equal("red", FlameColours.Of(drops[0]));
            Assert.Equal(2, drops[0].StackSize);
            Assert.Null(FlameColours.Of(drops[1]));
            Assert.Equal(1, drops[1].StackSize);
        }

        /// <summary>
        /// Each wick's flame in its own candle's colour, and the smoke left alone. The
        /// flame quads are too small and brief to judge from a screenshot - vanilla's
        /// barely show in one either - so this records what the particle tick spawns.
        /// </summary>
        [VsTest]
        public async Task EachWickFlamesInItsCandlesColour()
        {
            var be = await PlaceBunch("red");
            await AddCandle(be, "blue");
            await AddCandle(be, null);

            var block = (BlockCandelaCandles)World.GetBlock(Bunch);
            var recorder = new ParticleRecorder(Sapi.World.BlockAccessor);
            block.OnAsyncClientParticleTick(recorder, Bunch, 0, 0);

            var fire = block.ParticleProperties[0];
            Assert.Equal($"4,165,{fire.HsvaColor[0].avg}", string.Join(",", recorder.Hues.Take(3)), "the fire quads, wick by wick");
            Assert.True(recorder.Saturations.Skip(3).All(sat => sat == 0), "the smoke was coloured");
            Assert.Equal(20f, block.ParticleProperties[0].HsvaColor[0].avg, "the block's own flames were recoloured");
        }

        class ParticleRecorder : Vintagestory.API.Client.IAsyncParticleManager
        {
            public readonly System.Collections.Generic.List<float> Hues = new(), Saturations = new();
            public ParticleRecorder(IBlockAccessor access) => BlockAccess = access;
            public IBlockAccessor BlockAccess { get; }
            public int ParticlesAlive(EnumParticleModel model) => 0;
            public int Spawn(IParticlePropertiesProvider props)
            {
                var hsva = ((AdvancedParticleProperties)props).HsvaColor;
                Hues.Add(hsva[0].avg);
                Saturations.Add(hsva[1].avg);
                return 1;
            }
        }

        /// <summary>Saved and loaded - and a bunch saved before colours came in loads plain.</summary>
        [VsTest]
        public async Task ColoursAreSavedAndOldSavesArePlain()
        {
            var be = await PlaceBunch("teal");
            await AddCandle(be, null);
            await AddCandle(be, "green");

            var saved = new TreeAttribute();
            be.ToTreeAttributes(saved);
            Assert.Equal("teal,,green", saved.GetString("candela:flames"));

            saved.RemoveAttribute("candela:flames");
            be.FromTreeAttributes(saved, Sapi.World);
            Assert.True(be.FlameColoursOfCandles.All(f => f == null), "an old save came back coloured");
        }

        // ----- chandeliers -----

        [VsTest]
        public async Task ARainbowChandelierDropsEachColour()
        {
            World.SetBlock("game:planks-oak-ud", Chandelier.UpCopy());
            World.SetBlock("game:chandelier-candle0", Chandelier);
            await Ticks(2);
            var be = CandleHolders.EnsureBlockEntity(Sapi.World, Chandelier, "CandelaCandles");
            string[] rainbow = FlameColours.All.Select(c => c.Code).ToArray();
            for (int i = 0; i < rainbow.Length; i++)
            {
                be.AddCandle(BeeswaxHours, Flame(rainbow[i]));
                Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("game:chandelier-candle" + (i + 1))).BlockId, Chandelier);
                await Ticks(1);
            }
            Assert.Equal(string.Join(",", rainbow), Seq(be.FlameColoursOfCandles));
            Assert.Equal(7, World.GetBlock(Chandelier).GetLightHsv(Sapi.World.BlockAccessor, Chandelier)[0], "one candle of each colour - a tie, so plain");

            ItemStack[] drops = World.GetBlock(Chandelier).GetDrops(Sapi.World, Chandelier, null);
            Assert.Equal(string.Join(",", rainbow), Seq(drops.Skip(1).Select(FlameColours.Of)));
        }

        // ----- lanterns -----

        [VsTest]
        public async Task ALanternBurnsItsCandlesColour()
        {
            var fuel = await PlaceLantern("red", glass: null);
            Assert.Equal("red", fuel.Look.Flame);
            Assert.Equal(0, LanternLight()[0]);
            Assert.Equal(CandelaConfig.Current.FlameLightSaturation, LanternLight()[1]);
        }

        /// <summary>Coloured glass colours the light whatever the candle burns.</summary>
        [VsTest]
        public async Task ColouredGlassWins()
        {
            await PlaceLantern("red", glass: "green");
            Assert.Equal(20, LanternLight()[0], "green glass's hue");
        }

        [VsTest]
        public void ACraftedLanternKeepsItsCandlesColour()
        {
            var lantern = (BlockCandelaLantern)Sapi.World.GetBlock(new AssetLocation(LanternCode));
            var inputs = new ItemSlot[] { new DummySlot(World.Stack("game:clearquartz", 1)), new DummySlot(FlameColours.Stamp(World.Stack("game:candle", 1), "violet")) };
            var output = new DummySlot(World.Stack(LanternCode, 1));

            lantern.OnCreatedByCrafting(inputs, output, null);

            Assert.Equal("violet", LanternStack.Look(output.Itemstack).Flame);
        }

        [VsTest]
        public async Task PickingUpALanternKeepsItsColour()
        {
            var fuel = await PlaceLantern("teal", glass: null);
            ItemStack picked = World.GetBlock(Lantern).OnPickBlock(Sapi.World, Lantern);
            Assert.Equal("teal", LanternStack.Look(picked).Flame);
        }

        // ----- with a player -----

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task PlacingAColouredCandleAddsItsColour()
        {
            var be = await PlaceBunch("red");
            await Player.Teleport(new Vec3d(Bunch.X + 0.5, Bunch.Y, Bunch.Z - 1.5));
            await Player.Hold("game:candle");
            Player.Me.InventoryManager.ActiveHotbarSlot.Itemstack = FlameColours.Stamp(World.Stack("game:candle", 1), "green");
            Player.Me.InventoryManager.ActiveHotbarSlot.MarkDirty();
            await Ticks(4);

            await ShiftUse(Bunch);
            Assert.Equal("game:bunchocandles-2", World.BlockCode(Bunch));
            Assert.Equal("red,green", Seq(be.FlameColoursOfCandles));

            await EmptyHand();
            await Interact.UseBlock(Bunch);
            await Ticks(4);
            Assert.Equal("red", Seq(be.FlameColoursOfCandles));
            var taken = Player.Me.InventoryManager.Inventories.Values.SelectMany(inv => inv)
                .Select(s => s.Itemstack).FirstOrDefault(s => s?.Collectible.Code.ToString() == "game:candle");
            Assert.Equal("green", FlameColours.Of(taken), "the candle taken off lost its colour");
        }

        /// <summary>Not an assertion: a bunch of every colour, and one of them all, at night - for the eye.</summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ColouredFlamesForTheEye()
        {
            await DarkRoom();
            string[] rainbow = FlameColours.All.Select(c => c.Code).ToArray();
            for (int i = 0; i < rainbow.Length; i++)
            {
                BlockPos at = P(3 + 2 * i, 1, 8);
                World.SetBlock("game:bunchocandles-3", at);
                await Ticks(2);
                World.BE<BECandles>(at).SetFuel(3 * BeeswaxHours, Flame(rainbow[i]));
            }
            var mixed = P(8, 1, 11);
            World.SetBlock("game:bunchocandles-1", mixed);
            await Ticks(2);
            var be = World.BE<BECandles>(mixed);
            be.SetFuel(BeeswaxHours, Flame("red"));
            int n = 1;
            foreach (string flame in new[] { "yellow", "green", "teal", "blue", "violet", null })
            {
                be.AddCandle(BeeswaxHours, Flame(flame));
                Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("game:bunchocandles-" + ++n)).BlockId, mixed);
            }
            // The client takes new blocks up for particles - the flames - on its rescan,
            // every 20 seconds (SystemClientTickingBlocks), and only around the player - so
            // stand in the room for it.
            await Player.Teleport(P(8, 1, 3).ToVec3d().Add(0.5, 0, 0.5));
            await Ticks(25 * 33);

            // The testkit's client settings turn particles off, and the renderer reads
            // that once at startup - so turn its own copy on for these shots. And it
            // stops the clock, which stops particles too: a pool spawns Quantity times
            // the speed of time. The command, not World.SetTimeSpeed, because it tells
            // the client.
            await OnClient();
            var particles = ParticleRenderer();
            bool wasOn = (bool)particles.field.GetValue(particles.system);
            particles.field.SetValue(particles.system, true);
            await OnServer();
            await Cmd("/time speed 60");
            try
            {
                await Player.Teleport(P(8, 1, 3).ToVec3d().Add(0.5, 0, 0.5));
                await Interact.LookAt(P(8, 1, 9));
                await Input.Hotkey("togglehud");
                await Frames.Wait(90);
                Log("shot: " + await Shot.Take("results/flames-rainbow.png"));
                await Input.Hotkey("togglehud");

                await Player.Teleport(P(8, 1, 13).ToVec3d().Add(0.5, 0.2, 0.2));
                await Interact.LookAt(mixed);
                await Frames.Wait(60);
                Log("shot: " + await Shot.Take("results/flames-mixed.png"));

                // Close enough to see the flame quads themselves, a few frames apart: they
                // flicker, and a still catches some candles between flames.
                for (int i = 0; i < rainbow.Length; i++)
                {
                    BlockPos at = P(3 + 2 * i, 1, 8);
                    await Player.Teleport(at.ToVec3d().Add(0.5, -0.6, -0.7));
                    await Interact.LookAt(at);
                    for (int f = 0; f < 2; f++)
                    {
                        await Frames.Wait(20);
                        Log("shot: " + await Shot.Take($"results/flames-close-{rainbow[i]}-{f}.png"));
                    }
                }
            }
            finally
            {
                await OnClient();
                particles.field.SetValue(particles.system, wasOn);
                await OnServer();
                await Cmd("/time speed 0");
            }
        }

        /// <summary>
        /// A chandelier's and a lantern's flames are drawn from the coloured flame
        /// textures - each candle's own on a chandelier - and an all-plain chandelier is
        /// left to vanilla's mesh.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task ChandeliersAndLanternsAreDrawnInTheirColours()
        {
            BlockPos chandelier = P(6, 1, 8), lantern = P(10, 1, 8);
            World.SetBlock("game:chandelier-candle0", chandelier);
            await Ticks(2);
            var be = World.BE<BECandles>(chandelier);
            be.AddCandle(BeeswaxHours, CandleLook.Plain);
            Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("game:chandelier-candle1")).BlockId, chandelier);
            await Ticks(4);

            await OnClient();
            Assert.Null(Drawn(chandelier), "an all-plain chandelier should be vanilla's mesh");
            await OnServer();

            be.AddCandle(BeeswaxHours, Flame("red"));
            Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("game:chandelier-candle2")).BlockId, chandelier);
            await PlaceLantern(lantern, "blue", glass: null);

            for (int i = 0; i < 100; i++)
            {
                await OnClient();
                bool synced = Capi.World.BlockAccessor.GetBlockEntity(chandelier) is BECandles client && Seq(client.FlameColoursOfCandles) == "-,red"
                    && Capi.World.BlockAccessor.GetBlockEntity(lantern)?.GetBehavior<BEBehaviorLanternFuel>()?.Look.Flame == "blue";
                await OnServer();
                if (synced) break;
                await Ticks(1);
            }

            await OnClient();
            MeshData chandelierMesh = Drawn(chandelier), lanternMesh = Drawn(lantern);
            Assert.NotNull(chandelierMesh, "a chandelier with a red candle drew nothing of its own");
            Assert.True(Samples(chandelierMesh, "red"), "the red candle's flame is not red");
            Assert.False(Samples(chandelierMesh, "blue"), "a flame took a colour no candle burns");
            Assert.NotNull(lanternMesh, "the lantern drew nothing");
            Assert.True(Samples(lanternMesh, "blue"), "the blue candle's flame is not blue");
            ShapeLeftAlone(Capi.World.BlockAccessor.GetBlock(chandelier));
            ShapeLeftAlone(Capi.World.BlockAccessor.GetBlock(lantern));
            await OnServer();
        }

        /// <summary>
        /// A bunch's tips are drawn in their candles' colours, beeswax or tallow, its
        /// candles still from their own wax's texture; a plain bunch's tips stay orange.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task BunchTipsAreDrawnInTheirColours()
        {
            BlockPos beeswax = P(6, 1, 8), tallow = P(10, 1, 8), plain = P(8, 1, 10);
            foreach ((BlockPos at, string code) in new[] { (beeswax, "game:bunchocandles-2"), (tallow, "candela:tallowcandles-2"), (plain, "game:bunchocandles-2") })
            {
                World.SetBlock(code, at);
                await Ticks(2);
                World.BE<BECandles>(at).SetFuel(2 * BeeswaxHours, Flame(at == plain ? null : "green"));
            }

            for (int i = 0; i < 100; i++)
            {
                await OnClient();
                bool synced = Capi.World.BlockAccessor.GetBlockEntity(beeswax) is BECandles b && Seq(b.FlameColoursOfCandles) == "green,green"
                    && Capi.World.BlockAccessor.GetBlockEntity(tallow) is BECandles t && Seq(t.FlameColoursOfCandles) == "green,green";
                await OnServer();
                if (synced) break;
                await Ticks(1);
            }

            await OnClient();
            MeshData beeswaxMesh = Drawn(beeswax), tallowMesh = Drawn(tallow), plainMesh = Drawn(plain);
            Assert.True(Samples(beeswaxMesh, "green"), "a green beeswax bunch's tips are not green");
            Assert.True(SamplesTexture(beeswaxMesh, "game:block/candle"), "a green beeswax bunch's candles lost their texture");
            Assert.True(Samples(tallowMesh, "green"), "a green tallow bunch's tips are not green");
            Assert.True(SamplesTexture(tallowMesh, "candela:block/candle-tallow"), "a green tallow bunch's candles are not tallow");
            Assert.False(Samples(plainMesh, "green"), "a plain bunch's tips were coloured");
            ShapeLeftAlone(Capi.World.BlockAccessor.GetBlock(beeswax));
            await OnServer();
        }

        /// <summary>
        /// A lantern off its block - on a shelf or in a display case, or in hand - shows
        /// its candle's colour, and a blue one is not given a plain one's cached mesh.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task ALanternOffItsBlockShowsItsColour()
        {
            await OnClient();
            var lantern = (BlockCandelaLantern)Capi.World.GetBlock(new AssetLocation(LanternCode));
            ItemStack blue = new(lantern), plain = new(lantern);
            foreach (ItemStack stack in new[] { blue, plain })
            {
                stack.Attributes.SetString("material", "copper");
                stack.Attributes.SetString("lining", "plain");
                stack.Attributes.SetString("glass", "quartz");
            }
            LanternStack.Write(blue, BeeswaxHours, "game:bunchocandles", snuffed: false, Flame("blue"));

            var source = (IContainedMeshSource)lantern;
            Assert.True(Samples(source.GenMesh(new DummySlot(blue), Capi.BlockTextureAtlas, null), "blue"), "a blue lantern on a shelf is not blue");
            Assert.False(Samples(source.GenMesh(new DummySlot(plain), Capi.BlockTextureAtlas, null), "blue"), "a plain lantern on a shelf is blue");
            Assert.True(source.GetMeshCacheKey(new DummySlot(blue)) != source.GetMeshCacheKey(new DummySlot(plain)), "a blue and a plain lantern share a shelf mesh");

            ItemRenderInfo blueInfo = new(), plainInfo = new();
            lantern.OnBeforeRender(Capi, blue, EnumItemRenderTarget.HandFp, ref blueInfo);
            lantern.OnBeforeRender(Capi, plain, EnumItemRenderTarget.HandFp, ref plainInfo);
            Assert.NotNull(blueInfo.ModelRef, "a blue lantern in hand has no model");
            Assert.True(blueInfo.ModelRef != plainInfo.ModelRef, "a blue lantern in hand is drawn as a plain one");
            await OnServer();
        }

        /// <summary>
        /// The block's own shape location is as its JSON gave it. Making the coloured copy
        /// once turned it into "shapes/....json" in place, and vanilla, looking the shape up
        /// by it, drew the block invisible.
        /// </summary>
        static void ShapeLeftAlone(Block block)
        {
            string path = block.Shape.Base.Path;
            Assert.False(path.StartsWith("shapes/") || path.EndsWith(".json"), $"{block.Code}'s shape location was changed to {path}");
        }

        /// <summary>What the client's block entity at <paramref name="pos"/> draws of its own, or null if it leaves it to the block.</summary>
        static MeshData Drawn(BlockPos pos)
        {
            var pool = new MeshRecorder();
            return Capi.World.BlockAccessor.GetBlockEntity(pos).OnTesselation(pool, Capi.Tesselator) ? pool.Mesh : null;
        }

        /// <summary>Whether any of <paramref name="mesh"/>'s texture coordinates fall in <paramref name="flame"/>'s flame texture.</summary>
        static bool Samples(MeshData mesh, string flame) => SamplesTexture(mesh, "candela:block/flame-" + flame);

        static bool SamplesTexture(MeshData mesh, string texture)
        {
            TextureAtlasPosition at = Capi.BlockTextureAtlas[new AssetLocation(texture)];
            Assert.NotNull(at, $"{texture} is not in the block atlas");
            for (int i = 0; i + 1 < mesh.UvCount; i += 2)
            {
                float u = mesh.Uv[i], v = mesh.Uv[i + 1];
                if (u >= at.x1 && u <= at.x2 && v >= at.y1 && v <= at.y2) return true;
            }
            return false;
        }

        private class MeshRecorder : ITerrainMeshPool
        {
            public MeshData Mesh;
            public void AddMeshData(MeshData data, int lodLevel = 1) => Mesh = data;
            public void AddMeshData(MeshData data, float[] tfMatrix, int lodLevel = 1) => Mesh = data;
            public void AddMeshData(MeshData data, ColorMapData colorMapData, int lodLevel = 1) => Mesh = data;
        }

        /// <summary>
        /// Not an assertion: chandeliers and lanterns, whose flames are in their models, in
        /// every colour at night - for the eye.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ColouredModelFlamesForTheEye()
        {
            await DarkRoom();
            string[] rainbow = FlameColours.All.Select(c => c.Code).ToArray();

            // Stood on the floor, where its flames can be seen at eye level: every candle a
            // different colour, the last two plain.
            BlockPos chandelier = P(4, 1, 8);
            World.SetBlock("game:chandelier-candle0", chandelier);
            await Ticks(2);
            var be = World.BE<BECandles>(chandelier);
            string[] candles = [.. rainbow, null, null];
            for (int i = 0; i < candles.Length; i++)
            {
                be.AddCandle(BeeswaxHours, Flame(candles[i]));
                Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("game:chandelier-candle" + (i + 1))).BlockId, chandelier);
            }

            for (int i = 0; i < rainbow.Length; i++) await PlaceLantern(P(8 + i, 1, 8), rainbow[i], glass: null);
            // Long enough for the client to have the night, and the blocks.
            await Ticks(60);

            await Player.Teleport(P(8, 1, 3).ToVec3d().Add(0.5, 0, 0.5));
            await Interact.LookAt(P(7, 1, 8));
            await Input.Hotkey("togglehud");
            await Frames.Wait(90);
            Log("shot: " + await Shot.Take("results/flames-model-room.png"));

            // Close, from a little above, where all the tips show.
            await Player.Teleport(chandelier.ToVec3d().Add(0.5, -0.4, -1.3));
            await Interact.LookAt(chandelier.ToVec3d().Add(0.5, 0.6, 0.5));
            await Frames.Wait(60);
            Log("shot: " + await Shot.Take("results/flames-model-chandelier.png"));

            // Each lantern close, at its own height.
            for (int i = 0; i < rainbow.Length; i++)
            {
                BlockPos at = P(8 + i, 1, 8);
                await Player.Teleport(at.ToVec3d().Add(0.5, -0.6, -0.9));
                await Interact.LookAt(at.ToVec3d().Add(0.5, 0.4, 0.5));
                await Frames.Wait(40);
                Log("shot: " + await Shot.Take($"results/flames-model-lantern-{rainbow[i]}.png"));
            }
            await Input.Hotkey("togglehud");
        }

        /// <summary>
        /// A dark box over the plot at night: under the open sky the moon lights the
        /// ground brighter than candles do, and their colour is lost in it. A pale floor,
        /// so the light's colour shows - grass greens everything.
        /// </summary>
        static async Task DarkRoom()
        {
            await World.SetCalendarTo(500 * 24 + 22);
            World.Fill(P(1, 4, 1), P(14, 4, 14), "game:planks-oak-ud");
            World.Fill(P(1, 1, 1), P(14, 3, 1), "game:planks-oak-ud");
            World.Fill(P(1, 1, 14), P(14, 3, 14), "game:planks-oak-ud");
            World.Fill(P(1, 1, 1), P(1, 3, 14), "game:planks-oak-ud");
            World.Fill(P(14, 1, 1), P(14, 3, 14), "game:planks-oak-ud");
            World.Fill(P(1, 0, 1), P(14, 0, 14), "game:rock-chalk");
        }

        /// <summary>SystemRenderParticles and its private renderParticles flag, reached through ClientMain.particleManager.</summary>
        static (object system, System.Reflection.FieldInfo field) ParticleRenderer()
        {
            const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            object manager = Capi.World.GetType().GetField("particleManager", Any).GetValue(Capi.World);
            object system = manager.GetType().GetField("particleSystem", Any).GetValue(manager);
            return (system, system.GetType().GetField("renderParticles", Any));
        }

        // ----- helpers -----

        static async Task<BECandles> PlaceBunch(string flame)
        {
            World.SetBlock("game:bunchocandles-1", Bunch);
            await Ticks(2);
            var be = World.BE<BECandles>(Bunch);
            Assert.NotNull(be, "the bunch has no block entity");
            be.SetFuel(BeeswaxHours, Flame(flame));
            await Ticks(2);
            return be;
        }

        /// <summary>As CandlePlacement adds one: the candle into the pool, then the block exchanged for one more.</summary>
        static async Task AddCandle(BECandles be, string flame)
        {
            be.AddCandle(BeeswaxHours, Flame(flame));
            Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("game:bunchocandles-" + (be.Quantity + 1))).BlockId, Bunch);
            await Ticks(2);
        }

        static async Task TakeCandle(BECandles be)
        {
            be.TakeCandle(out _);
            Sapi.World.BlockAccessor.ExchangeBlock(Sapi.World.GetBlock(new AssetLocation("game:bunchocandles-" + (be.Quantity - 1))).BlockId, Bunch);
            await Ticks(2);
        }

        /// <summary>The grid recipe making <paramref name="output"/> from <paramref name="ingredient"/>.</summary>
        static IRecipeBase Recipe(string output, string ingredient) => Sapi.World.GridRecipes.First(r =>
            r.Output.ResolvedItemStack?.Collectible.Code.ToString() == output && (r.ResolvedIngredients ?? []).Any(i => i?.Code?.ToString() == ingredient));

        /// <summary>A candle's look with only a flame colour.</summary>
        static CandleLook Flame(string flame) => new(flame, null);

        /// <summary>"red,-,blue": the testkit's Equal compares sequences by reference.</summary>
        static string Seq(System.Collections.Generic.IEnumerable<string> colours) => string.Join(",", colours.Select(c => c ?? "-"));

        static byte[] BunchLight() => World.GetBlock(Bunch).GetLightHsv(Sapi.World.BlockAccessor, Bunch);

        static Task<BEBehaviorLanternFuel> PlaceLantern(string flame, string glass) => PlaceLantern(Lantern, flame, glass);

        static async Task<BEBehaviorLanternFuel> PlaceLantern(BlockPos at, string flame, string glass)
        {
            World.SetBlock(LanternCode, at);
            await Ticks(2);
            var be = World.BE<BlockEntity>(at);
            if (glass != null) ((BELantern)be).DidPlace("copper", "plain", glass);

            ItemStack from = World.Stack(LanternCode, 1);
            LanternStack.Write(from, BeeswaxHours, "game:bunchocandles", snuffed: false, Flame(flame));
            foreach (var behavior in be.Behaviors) behavior.OnBlockPlaced(from);
            await Ticks(2);
            return be.GetBehavior<BEBehaviorLanternFuel>();
        }

        static byte[] LanternLight() => World.GetBlock(Lantern).GetLightHsv(Sapi.World.BlockAccessor, Lantern);
    }
}
