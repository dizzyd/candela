using System.Linq;
using System.Threading.Tasks;
using candela;
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
    /// Candle moulds: clay, fired, set down on the ground and poured full from a pot of
    /// molten wax lifted off the fire, left to set, and knocked out four candles at a
    /// time.
    /// </summary>
    public class CandelaMoulds
    {
        const string Empty = "candela:candlemould-blue-fired";

        [VsTest]
        public void MouldsAreFormedAndFired()
        {
            Assert.True(Sapi.GetClayformingRecipes().Any(r => r.Output.ResolvedItemStack?.Collectible.Code.ToString() == "candela:candlemould-blue-raw"),
                "no clayforming recipe makes a raw mould");

            var raw = Sapi.World.GetItem(new AssetLocation("candela:candlemould-red-raw"));
            var fired = raw.CombustibleProps?.SmeltedStack?.ResolvedItemstack;
            Assert.Equal("candela:candlemould-red-fired", fired?.Collectible.Code.ToString());
            Assert.Equal(10, Sapi.World.GetItem(new AssetLocation(Empty)).GetMaxDurability(World.Stack(Empty, 1)));
        }

        /// <summary>
        /// The raw mould is the shape its clayforming pattern forms, voxel for voxel:
        /// what you shape on the grid is what you get.
        /// </summary>
        [VsTest]
        public void TheMouldIsWhatTheClayFormingMakes()
        {
            var recipe = Sapi.GetClayformingRecipes().Single(r => r.Output.ResolvedItemStack?.Collectible.Code.ToString() == "candela:candlemould-blue-raw");
            var shape = Shape.TryGet(Sapi, "candela:shapes/item/candlemould-empty.json");

            var model = new bool[16, 16, 16];
            foreach (var el in shape.Elements)
            {
                for (int x = (int)el.From[0]; x < (int)el.To[0]; x++)
                for (int y = (int)el.From[1]; y < (int)el.To[1]; y++)
                for (int z = (int)el.From[2]; z < (int)el.To[2]; z++)
                    model[x, y, z] = true;
            }

            int layers = recipe.Voxels.GetLength(1);
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
            {
                bool formed = y < layers && recipe.Voxels[x, y, z];
                Assert.Equal(formed, model[x, y, z], $"voxel {x},{y},{z}");
            }
        }

        // ----- poured on the ground -----

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task TallowPoursIntoAMouldOnTheGround()
        {
            await SetDown(Empty);
            ItemSlot hand = await HoldPotOf("candela:tallow-molten", 10);

            await Pour();

            Assert.Equal("candela:candlemould-blue-tallow", Stored()?.Collectible.Code.ToString());
            Assert.Equal(4, CarriedWax.MoltenIn(Sapi.World, hand.Itemstack, out _)?.StackSize ?? 0, "the pot did not give six portions");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task BeeswaxPoursIntoAMouldOnTheGround()
        {
            await SetDown(Empty);
            ItemSlot hand = await HoldPotOf("candela:beeswax-molten", 12);

            await Pour();

            Assert.Equal("candela:candlemould-blue-beeswax", Stored()?.Collectible.Code.ToString());
            Assert.Null(hand.Itemstack.Attributes[CarriedWax.Contents], "twelve portions should have emptied the pot");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task TooLittleWaxPoursNothing()
        {
            await SetDown(Empty);
            ItemSlot hand = await HoldPotOf("candela:tallow-molten", 5);

            await Pour(expectChange: false);

            Assert.Equal(Empty, Stored()?.Collectible.Code.ToString(), "the mould was filled, or picked up");
            Assert.Equal(5, CarriedWax.MoltenIn(Sapi.World, hand.Itemstack, out _).StackSize);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task SetWaxDoesNotPour()
        {
            await SetDown(Empty);
            await HoldPotOf("candela:tallow-molten", 10, temperature: 20f);

            await Pour(expectChange: false);

            Assert.Equal(Empty, Stored()?.Collectible.Code.ToString());
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ARawMouldIsNotPouredInto()
        {
            await SetDown("candela:candlemould-blue-raw");
            await HoldPotOf("candela:tallow-molten", 10);

            await Pour(expectChange: false);

            Assert.Equal("candela:candlemould-blue-raw", Stored()?.Collectible.Code.ToString());
        }

        /// <summary>
        /// Pouring plays the crucible's pour, not the pot's own use animation - two
        /// hands setting a block down - which held over a pour rocked the pot back and
        /// forth. With screenshots of it mid-pour, for the eye.
        /// </summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task PouringLooksLikePouring()
        {
            await World.SetCalendarTo(500 * 24 + 12);
            await SetDown(Empty);
            await HoldPotOf("candela:tallow-molten", 10);
            await Interact.Aim(Ground);

            string[] playing;
            await Input.MouseDown(EnumMouseButton.Right);
            try
            {
                await Frames.Wait(20);
                await OnClient();
                playing = Capi.World.Player.Entity.AnimManager.ActiveAnimationsByAnimCode.Keys.ToArray();
                await OnServer();
                Log("playing: " + string.Join(", ", playing));
                Log("shot: " + await Shot.Take("results/pour-firstperson.png"));
            }
            finally
            {
                await Input.MouseUp(EnumMouseButton.Right);
            }

            // In first person the player plays each animation's -fp twin.
            Assert.True(playing.Any(a => a is "pour" or "pour-fp"), "not pouring: " + string.Join(", ", playing));
            Assert.False(playing.Any(a => a.StartsWith("twohandplaceblock")), "still rocking the pot");
        }

        /// <summary>A mould steams on the ground while its candles set, and not before or after.</summary>
        [VsTest]
        public async Task AMouldSteamsWhileItSets()
        {
            var mould = (ItemCandleMould)Sapi.World.GetItem(new AssetLocation("candela:candlemould-blue-tallow"));
            ItemStack poured = mould.Filled(Sapi.World, World.Stack(Empty, 1), "tallow");
            int Puffs(ItemStack stack) => Enumerable.Range(0, 2000).Count(_ => ((IGroundStoredParticleEmitter)stack.Collectible).ShouldSpawnGSParticles(Sapi.World, stack));

            int fresh = Puffs(poured);
            await Hours(ItemCandleMould.SetHours * 0.9);
            int nearlySet = Puffs(poured);
            await Hours(ItemCandleMould.SetHours);
            int set = Puffs(poured);

            Log($"puffs in 2000 ticks: just poured {fresh}, nearly set {nearlySet}, set {set}");
            Assert.Greater(fresh, nearlySet);
            Assert.Greater(nearlySet, 0);
            Assert.Equal(0, set);
            Assert.Equal(0, Puffs(World.Stack(Empty, 1)));
        }

        /// <summary>Not an assertion: a mould just poured, steaming, beside one that has set.</summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task SteamForTheEye()
        {
            await World.SetCalendarTo(500 * 24 + 12);
            await SetDown("candela:candlemould-blue-tallow", hoursAgo: 0);
            World.SetBlock("game:air", P(9, 1, 8));
            ItemSlot hand = Player.Me.InventoryManager.ActiveHotbarSlot;
            var mould = (ItemCandleMould)Sapi.World.GetItem(new AssetLocation("candela:candlemould-blue-tallow"));
            hand.Itemstack = mould.Filled(Sapi.World, World.Stack(Empty, 1), "tallow");
            hand.Itemstack.Attributes.SetDouble("candela:filledHours", Sapi.World.Calendar.TotalHours - 1);
            hand.MarkDirty();
            await Ticks(4);
            await Hands.ShiftUse(P(9, 0, 8), BlockFacing.UP);
            await Hands.EmptyHand();

            await Player.Teleport(new Vec3d(P(8, 1, 6).X + 1.0, P(8, 1, 6).Y + 0.2, P(8, 1, 6).Z + 0.3));
            await Interact.LookAt(new Vec3d(P(8, 1, 8).X + 1.0, P(8, 1, 8).Y + 0.4, P(8, 1, 8).Z + 0.5));
            // The client takes up a block for particle ticks when it changes - here
            // before the mould has arrived in the ground storage - or on its rescan,
            // every 20 seconds (SystemClientTickingBlocks).
            await Ticks(25 * 33);
            await Frames.Wait(30);
            Log("shot: " + await Shot.Take("results/mould-steam.png"));
        }

        /// <summary>As a vanilla tool mould, right-click the cooled cast - holding the flax for the wicks.</summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ASetMouldOnTheGroundGivesItsCandles()
        {
            await SetDown("candela:candlemould-blue-tallow", hoursAgo: ItemCandleMould.SetHours * 2);
            EnumGameMode mode = Player.Me.WorldData.CurrentGameMode;
            // Set directly: vstestkit's Player.SetGameMode runs /gamemode, which throws
            // looking the player up by name. The server is what checks it.
            Player.Me.WorldData.CurrentGameMode = EnumGameMode.Survival;
            try
            {
                await Player.Hold("game:flaxfibers", 3);
                int before = CountOf("candela:candle-tallow");

                await Interact.UseBlock(Ground);
                await Ticks(4);

                Assert.Equal(before + 4, CountOf("candela:candle-tallow"));
                Assert.Equal(Empty, Stored()?.Collectible.Code.ToString(), "the mould should stay down, empty");
                Assert.Equal(1, Player.Held?.StackSize ?? 0, "two fibres should have gone into the wicks");
            }
            finally
            {
                Player.Me.WorldData.CurrentGameMode = mode;
            }
        }

        /// <summary>A free hand on a set mould gets nothing, and does not walk off with it full.</summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task NoCandlesWithoutWicks()
        {
            await SetDown("candela:candlemould-blue-tallow", hoursAgo: ItemCandleMould.SetHours * 2);
            await Hands.EmptyHand();
            int before = CountOf("candela:candle-tallow");

            await Interact.UseBlock(Ground);
            await Ticks(4);

            Assert.Equal(before, CountOf("candela:candle-tallow"));
            Assert.Equal("candela:candlemould-blue-tallow", Stored()?.Collectible.Code.ToString(), "the mould was emptied or picked up");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task AMouldStillSettingStaysDown()
        {
            await SetDown("candela:candlemould-blue-tallow", hoursAgo: 0);
            await Hands.EmptyHand();
            int before = CountOf("candela:candle-tallow");

            await Interact.UseBlock(Ground);
            await Ticks(4);

            Assert.Equal(before, CountOf("candela:candle-tallow"));
            Assert.Equal("candela:candlemould-blue-tallow", Stored()?.Collectible.Code.ToString(), "a setting mould was picked up or emptied");
        }

        /// <summary>Pouring is the one way in: a mould held to the pot on the fire opens the firepit, as anything else does.</summary>
        [VsTest]
        public void AMouldDoesNotDipAtTheFirepit()
        {
            Assert.False(Sapi.World.GetItem(new AssetLocation(Empty)) is IWaxWorker);
        }

        [VsTest]
        public async Task ASetMouldGivesFourCandlesAndWears()
        {
            ItemSlot hand = await HoldFilled("tallow", hoursAgo: ItemCandleMould.SetHours * 2);
            int before = CountOf("candela:candle-tallow");

            KnockOut(hand);

            Assert.Equal(Empty, hand.Itemstack?.Collectible.Code.ToString());
            Assert.Equal(before + 4, CountOf("candela:candle-tallow"));
            Assert.Equal(9, hand.Itemstack.Collectible.GetRemainingDurability(hand.Itemstack));
        }

        [VsTest]
        public async Task BeeswaxMouldsGiveBeeswaxCandles()
        {
            ItemSlot hand = await HoldFilled("beeswax", hoursAgo: ItemCandleMould.SetHours * 2);
            int before = CountOf("game:candle");

            KnockOut(hand);

            Assert.Equal(before + 4, CountOf("game:candle"));
        }

        [VsTest]
        public async Task CandlesThatHaveNotSetStayIn()
        {
            ItemSlot hand = await HoldFilled("tallow", hoursAgo: 0);
            int before = CountOf("candela:candle-tallow");

            KnockOut(hand);

            Assert.Equal("candela:candlemould-blue-tallow", hand.Itemstack?.Collectible.Code.ToString());
            Assert.Equal(before, CountOf("candela:candle-tallow"));
        }

        /// <summary>Two flax fibres in the other hand for the wicks, taken; without them, nothing comes out.</summary>
        [VsTest]
        public async Task TheCandlesNeedWicks()
        {
            EnumGameMode mode = Player.Me.WorldData.CurrentGameMode;
            // Set directly: vstestkit's Player.SetGameMode runs /gamemode, which throws
            // looking the player up by name. The server is what checks it.
            Player.Me.WorldData.CurrentGameMode = EnumGameMode.Survival;
            try
            {
                ItemSlot hand = await HoldFilled("tallow", hoursAgo: ItemCandleMould.SetHours * 2);
                int before = CountOf("candela:candle-tallow");

                KnockOut(hand, wicks: 1);
                Assert.Equal("candela:candlemould-blue-tallow", hand.Itemstack?.Collectible.Code.ToString(), "one fibre should not do");
                Assert.Equal(before, CountOf("candela:candle-tallow"));

                KnockOut(hand, wicks: 3);
                Assert.Equal(before + 4, CountOf("candela:candle-tallow"));
                Assert.Equal(1, Player.Me.Entity.LeftHandItemSlot.StackSize, "two fibres should have gone into the wicks");
            }
            finally
            {
                Player.Me.WorldData.CurrentGameMode = mode;
            }
        }

        /// <summary>The last use cracks it: the candles come out, the mould does not.</summary>
        [VsTest]
        public async Task AWornMouldCracksOnItsLastUse()
        {
            ItemSlot hand = await HoldFilled("tallow", hoursAgo: ItemCandleMould.SetHours * 2);
            hand.Itemstack.Attributes.SetInt("durability", 1);
            int before = CountOf("candela:candle-tallow");

            KnockOut(hand);

            Assert.True(hand.Empty, "a mould on its last use should crack");
            Assert.Equal(before + 4, CountOf("candela:candle-tallow"));
        }

        /// <summary>
        /// Shift-right-click on the ground sets a full mould down, as it does an empty
        /// one, rather than knocking its candles out.
        /// </summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task AFullMouldSetsDownOnTheGround()
        {
            World.SetBlock("game:air", P(8, 1, 8));
            await HoldFilled("tallow", hoursAgo: ItemCandleMould.SetHours * 2);
            int before = CountOf("candela:candle-tallow");

            await ShiftUse(P(8, 0, 8));

            Assert.Equal("game:groundstorage", World.BlockCode(P(8, 1, 8)), "the mould was not set down");
            var stored = World.BE<BlockEntityGroundStorage>(P(8, 1, 8)).Inventory.FirstNonEmptySlot?.Itemstack;
            Assert.Equal("candela:candlemould-blue-tallow", stored?.Collectible.Code.ToString());
            Assert.Equal(before, CountOf("candela:candle-tallow"), "setting it down knocked the candles out");
        }

        /// <summary>A filled mould in hand, poured <paramref name="hoursAgo"/>.</summary>
        static async Task<ItemSlot> HoldFilled(string wax, double hoursAgo)
        {
            var empty = (ItemCandleMould)Sapi.World.GetItem(new AssetLocation(Empty));
            await Hours(hoursAgo);   // nothing to wait for if zero
            ItemStack filled = empty.Filled(Sapi.World, World.Stack(Empty, 1), wax);
            filled.Attributes.SetDouble("candela:filledHours", Sapi.World.Calendar.TotalHours - hoursAgo);

            ItemSlot hand = Player.Me.InventoryManager.ActiveHotbarSlot;
            hand.Itemstack = filled;
            hand.MarkDirty();
            await Ticks(2);
            return hand;
        }

        /// <summary>Right-click with the mould in hand, <paramref name="wicks"/> flax fibres in the other.</summary>
        static void KnockOut(ItemSlot hand, int wicks = ItemCandleMould.WicksPerFill)
        {
            ItemSlot offhand = Player.Me.Entity.LeftHandItemSlot;
            offhand.Itemstack = wicks > 0 ? World.Stack("game:flaxfibers", wicks) : null;
            offhand.MarkDirty();

            EnumHandHandling handling = EnumHandHandling.NotHandled;
            hand.Itemstack.Collectible.OnHeldInteractStart(hand, Player.Me.Entity, null, null, true, ref handling);
        }

        static int CountOf(string code)
        {
            int n = 0;
            foreach (var inv in new[] { Player.Me.InventoryManager.GetHotbarInventory(), Player.Me.InventoryManager.GetOwnInventory("backpack") })
            {
                if (inv == null) continue;
                foreach (var slot in inv) if (slot.Itemstack?.Collectible.Code.ToString() == code) n += slot.StackSize;
            }
            return n;
        }

        static BlockPos Ground => P(8, 1, 8);

        /// <summary>
        /// <paramref name="code"/> set down on the ground the way a player does it -
        /// shift-right-click - filled <paramref name="hoursAgo"/> if it is a full one.
        /// </summary>
        static async Task SetDown(string code, double hoursAgo = 0)
        {
            World.SetBlock("game:air", Ground);
            ItemStack stack = World.Stack(code, 1);
            if (stack.Collectible is ItemCandleMould { IsFilled: true } mould)
            {
                stack = mould.Filled(Sapi.World, stack, mould.State);
                stack.Attributes.SetDouble("candela:filledHours", Sapi.World.Calendar.TotalHours - hoursAgo);
            }
            ItemSlot hand = Player.Me.InventoryManager.ActiveHotbarSlot;
            hand.Itemstack = stack;
            hand.MarkDirty();
            await Ticks(4);

            // On the middle of the ground's top: ground storage then keeps it in the
            // middle, and a click anywhere on it reaches it. Off-centre it goes in the
            // quadrant under the cursor, and a click elsewhere finds an empty one.
            await Hands.ShiftUse(P(8, 0, 8), BlockFacing.UP);
            Assert.Equal(code, Stored()?.Collectible.Code.ToString(), "the mould was not set down");
        }

        /// <summary>The mould on the ground.</summary>
        static ItemStack Stored() =>
            World.BEOrNull<BlockEntityGroundStorage>(Ground)?.Inventory.FirstOrDefault(s => !s.Empty)?.Itemstack;

        /// <summary>A pot in hand as one lifted off the fire, carrying <paramref name="portions"/> of <paramref name="molten"/>.</summary>
        static async Task<ItemSlot> HoldPotOf(string molten, int portions, float temperature = 300f)
        {
            ItemStack wax = World.Stack(molten, portions);
            wax.Collectible.SetTemperature(Sapi.World, wax, temperature);
            var contents = new TreeAttribute();
            contents.SetItemstack("0", wax);

            ItemStack pot = World.Stack("game:claypot-blue-fired", 1);
            pot.Attributes[CarriedWax.Contents] = contents;

            ItemSlot hand = Player.Me.InventoryManager.ActiveHotbarSlot;
            hand.Itemstack = pot;
            hand.MarkDirty();
            await Ticks(4);
            return hand;
        }

        /// <summary>Hold right-click on the mould until it fills, or for long enough that it would have.</summary>
        static async Task Pour(bool expectChange = true)
        {
            string before = Stored()?.Collectible.Code.ToString();
            await Interact.Aim(Ground);
            await Input.MouseDown(EnumMouseButton.Right);
            try
            {
                await Until(() => Stored()?.Collectible.Code.ToString() != before, 200);
            }
            catch (AssertionException) when (!expectChange)
            {
            }
            finally
            {
                await Input.MouseUp(EnumMouseButton.Right);
            }
            await Ticks(4);
            if (expectChange) Assert.True(Stored()?.Collectible.Code.ToString() != before, "the mould was not poured into");
        }
    }
}
