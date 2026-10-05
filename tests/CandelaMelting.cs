using System.Threading.Tasks;
using candela;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Wax melted in a pot other than from rendered fat: beeswax, for moulds, and the
    /// stubs candles burn down to, back into the wax they came from.
    /// </summary>
    public class CandelaMelting
    {
        static BlockPos Firepit => P(8, 1, 8);

        const string EmptyPot = "game:claypot-blue-fired";
        const string MoltenTallow = "candela:tallow-molten";
        const string MoltenBeeswax = "candela:beeswax-molten";

        [VsTest]
        public void BeeswaxMeltsAPortionALump()
        {
            Assert.Equal(6, Melt(MoltenBeeswax, World.Stack("game:beeswax", 3), World.Stack("game:beeswax", 3)));
        }

        /// <summary>One portion a stub of its own wax, whatever is left of it.</summary>
        [VsTest]
        public void StubsMeltAPortionAStub()
        {
            Assert.Equal(6, Melt(MoltenTallow, World.Stack("candela:candlestub-tallow-75", 3), World.Stack("candela:candlestub-tallow-25", 3)));
            Assert.Equal(4, Melt(MoltenBeeswax, World.Stack("candela:candlestub-beeswax-50", 2), World.Stack("candela:candlestub-beeswax-25", 2)));
        }

        /// <summary>Tallow and beeswax stubs are different waxes and do not melt together.</summary>
        [VsTest]
        public void MixedStubsDoNotMelt()
        {
            var pot = (BlockCookingContainer)Sapi.World.GetBlock(new AssetLocation(EmptyPot));
            var stacks = new[] { World.Stack("candela:candlestub-tallow-50", 2), World.Stack("candela:candlestub-beeswax-50", 2) };
            Assert.Null(pot.GetMatchingCookingRecipe(Sapi.World, stacks, out _));
        }

        [VsTest]
        public async Task MoltenBeeswaxSetsIntoBeeswax()
        {
            var slot = new DummySlot(World.Stack(MoltenBeeswax, 4));
            var wax = (ItemMoltenWax)slot.Itemstack.Collectible;
            Assert.Equal("beeswax", wax.Wax);

            wax.SetTemperature(Sapi.World, slot.Itemstack, 100f);
            Assert.Equal(0f, wax.GetTransitionRateMul(Sapi.World, slot, EnumTransitionType.Harden));

            // Beeswax sets higher than tallow: at 55°C tallow is molten, beeswax not.
            wax.SetTemperature(Sapi.World, slot.Itemstack, 55f);
            Assert.Greater(wax.GetTransitionRateMul(Sapi.World, slot, EnumTransitionType.Harden), 0f);

            // The transition counts from the first time it is asked about.
            slot.Itemstack.Collectible.UpdateAndGetTransitionStates(Sapi.World, slot);

            await Hours(0.5);
            slot.Itemstack.Collectible.UpdateAndGetTransitionStates(Sapi.World, slot);
            Assert.Equal("game:beeswax", slot.Itemstack.Collectible.Code.ToString());
            Assert.Equal(4, slot.StackSize);
        }

        [VsTest]
        public async Task ThePotSaysItHoldsBeeswax()
        {
            var firepit = await PotOf(MoltenBeeswax, 6, temperature: 150f);
            var inventory = (InventorySmelting)firepit.Inventory;
            Assert.Equal(Lang.Get("candela:firepit-molten-beeswax"), inventory.GetOutputText());
        }

        /// <summary>Only tallow dips: beeswax is for moulds.</summary>
        [VsTest]
        public async Task TheRodFindsNoTallowInABeeswaxPot()
        {
            var firepit = await PotOf(MoltenBeeswax, 6, temperature: 150f);
            Assert.Null(ItemMoltenWax.FindIn(firepit, "tallow"));
            Assert.NotNull(ItemMoltenWax.FindIn(firepit, "beeswax"));
        }

        /// <summary>
        /// Cooks <paramref name="stacks"/> in a pot through vanilla's own DoSmelt and
        /// returns how much of <paramref name="expect"/> came out.
        /// </summary>
        static int Melt(string expect, params ItemStack[] stacks)
        {
            var firepit = Fresh();
            for (int i = 0; i < stacks.Length; i++) firepit.otherCookingSlots[i].Itemstack = stacks[i];

            var pot = (BlockCookingContainer)firepit.inputSlot.Itemstack.Collectible;
            Assert.NotNull(pot.GetMatchingCookingRecipe(Sapi.World, stacks, out _), "no recipe matches");
            pot.DoSmelt(Sapi.World, firepit.Inventory as ISlotProvider, firepit.inputSlot, firepit.outputSlot);

            ItemSlot molten = ItemMoltenWax.FindIn(firepit);
            Assert.NotNull(molten, "nothing molten came out");
            Assert.Equal(expect, molten.Itemstack.Collectible.Code.ToString());
            return molten.StackSize;
        }

        static async Task<BlockEntityFirepit> PotOf(string molten, int portions, float temperature)
        {
            var firepit = Fresh();
            ItemStack stack = World.Stack(molten, portions);
            stack.Collectible.SetTemperature(Sapi.World, stack, temperature);
            firepit.otherCookingSlots[0].Itemstack = stack;
            firepit.MarkDirty(true);
            await Ticks(2);
            return firepit;
        }

        static BlockEntityFirepit Fresh()
        {
            World.SetBlock("game:firepit-extinct", Firepit);
            var firepit = World.BE<BlockEntityFirepit>(Firepit);
            firepit.inputSlot.Itemstack = World.Stack(EmptyPot, 1);
            firepit.inputSlot.MarkDirty();
            return firepit;
        }
    }
}
