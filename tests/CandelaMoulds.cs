using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using static Candela.Tests.Hands;

namespace Candela.Tests
{
    /// <summary>
    /// Candle moulds: clay, fired, filled from a pot of molten wax on the fire in one
    /// held right-click, left to set, and knocked out four candles at a time.
    /// </summary>
    public class CandelaMoulds
    {
        static BlockPos Firepit => P(8, 1, 8);

        const string Empty = "candela:candlemould-blue-fired";

        [VsTest]
        public void MouldsAreFormedAndFired()
        {
            Assert.True(Sapi.GetClayformingRecipes().Any(r => r.Output.ResolvedItemStack?.Collectible.Code.ToString() == "candela:candlemould-blue-raw"),
                "no clayforming recipe makes a raw mould");

            var raw = Sapi.World.GetItem(new AssetLocation("candela:candlemould-red-raw"));
            var fired = raw.CombustibleProps?.SmeltedStack?.ResolvedItemstack;
            Assert.Equal("candela:candlemould-red-fired", fired?.Collectible.Code.ToString());
            Assert.Equal(20, Sapi.World.GetItem(new AssetLocation(Empty)).GetMaxDurability(World.Stack(Empty, 1)));
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task AMouldFillsFromAPotOfTallow()
        {
            var firepit = await PotOf("candela:tallow-molten", 10);
            await Player.Hold(Empty);

            await Fill();

            Assert.Equal("candela:candlemould-blue-tallow", Player.Held?.Collectible.Code.ToString());
            Assert.Equal(4, ItemMoltenWax.FindIn(firepit).StackSize);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task AMouldFillsFromAPotOfBeeswax()
        {
            var firepit = await PotOf("candela:beeswax-molten", 12);
            await Player.Hold(Empty);

            await Fill();

            Assert.Equal("candela:candlemould-blue-beeswax", Player.Held?.Collectible.Code.ToString());
            Assert.Null(ItemMoltenWax.FindIn(firepit), "twelve portions should have emptied the pot");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task TooLittleWaxFillsNothing()
        {
            var firepit = await PotOf("candela:tallow-molten", 5);
            await Player.Hold(Empty);

            await Fill(expectChange: false);

            Assert.Equal(Empty, Player.Held?.Collectible.Code.ToString());
            Assert.Equal(5, ItemMoltenWax.FindIn(firepit).StackSize);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ARawMouldIsNotFilled()
        {
            await PotOf("candela:tallow-molten", 10);
            await Player.Hold("candela:candlemould-blue-raw");

            await Fill(expectChange: false);

            Assert.Equal("candela:candlemould-blue-raw", Player.Held?.Collectible.Code.ToString());
        }

        [VsTest]
        public async Task ASetMouldGivesFourCandlesAndWears()
        {
            ItemSlot hand = await HoldFilled("tallow", hoursAgo: ItemCandleMould.SetHours * 2);
            int before = CountOf("candela:candle-tallow");

            KnockOut(hand);

            Assert.Equal(Empty, hand.Itemstack?.Collectible.Code.ToString());
            Assert.Equal(before + 4, CountOf("candela:candle-tallow"));
            Assert.Equal(19, hand.Itemstack.Collectible.GetRemainingDurability(hand.Itemstack));
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

        static async Task<BlockEntityFirepit> PotOf(string molten, int portions)
        {
            World.SetBlock("game:firepit-extinct", Firepit);
            await Ticks(2);
            var firepit = World.BE<BlockEntityFirepit>(Firepit);
            firepit.inputSlot.Itemstack = World.Stack("game:claypot-blue-fired", 1);
            ItemStack stack = World.Stack(molten, portions);
            stack.Collectible.SetTemperature(Sapi.World, stack, 300f);
            firepit.otherCookingSlots[0].Itemstack = stack;
            firepit.MarkDirty(true);
            await Ticks(10);
            return firepit;
        }

        /// <summary>A filled mould in hand, poured <paramref name="hoursAgo"/>.</summary>
        static async Task<ItemSlot> HoldFilled(string wax, double hoursAgo)
        {
            var empty = (ItemCandleMould)Sapi.World.GetItem(new AssetLocation(Empty));
            await Hours(hoursAgo);   // nothing to wait for if zero
            ItemStack filled = empty.Worked(Sapi.World, World.Stack(Empty, 1), wax);
            filled.Attributes.SetDouble("candela:filledHours", Sapi.World.Calendar.TotalHours - hoursAgo);

            ItemSlot hand = Player.Me.InventoryManager.ActiveHotbarSlot;
            hand.Itemstack = filled;
            hand.MarkDirty();
            await Ticks(2);
            return hand;
        }

        static void KnockOut(ItemSlot hand)
        {
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

        static async Task Fill(bool expectChange = true)
        {
            string before = Player.Held?.Collectible.Code.ToString();
            await Interact.Aim(Firepit);
            await Input.MouseDown(EnumMouseButton.Right);
            try
            {
                await Until(() => Player.Held?.Collectible.Code.ToString() != before, 200);
            }
            catch (AssertionException) when (!expectChange)
            {
            }
            finally
            {
                await Input.MouseUp(EnumMouseButton.Right);
            }
            await Ticks(4);
            if (expectChange) Assert.True(Player.Held?.Collectible.Code.ToString() != before, "the mould did not fill");
        }
    }
}
