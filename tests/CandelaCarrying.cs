using System.Linq;
using System.Text;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// A pot of wax lifted off the fire keeps its wax, rather than spilling it on the
    /// ground as vanilla does with whatever is in a pot it takes away.
    ///
    /// The moves are the slot moves the firepit's dialog makes: a click with an empty
    /// cursor and a shift-click both come down to the pot slot's TryPutInto.
    /// </summary>
    public class CandelaCarrying
    {
        const string Tallow = "candela:tallow-molten";

        [VsTest]
        public async Task ALiftedPotTakesItsTallowWithIt()
        {
            var firepit = await PotOfTallowOnTheFire();

            var hand = new DummySlot();
            Assert.Equal(1, firepit.inputSlot.TryPutInto(Sapi.World, hand));
            await Ticks(2);

            Assert.True(((InventorySmelting)firepit.Inventory).Slots.All(s => s.Empty), "the tallow stayed behind in the firepit");
            Assert.Equal(0, Dropped(firepit.Pos), "the tallow was spilled on the ground");

            var contents = hand.Itemstack.Attributes[CarriedWax.Contents] as ITreeAttribute;
            Assert.NotNull(contents, "the pot carries nothing");
            ItemStack carried = contents.Values.OfType<ItemstackAttribute>().Single().value;
            Assert.Equal(Tallow, carried.Collectible.Code.ToString());
            Assert.Equal(12, carried.StackSize);

            var info = new StringBuilder();
            hand.Itemstack.Collectible.GetHeldItemInfo(hand, info, Sapi.World, false);
            Assert.True(info.ToString().Contains(Lang.Get("candela:pot-holds", "12x " + Lang.Get("candela:item-tallow-molten").ToLower())), "the tooltip does not say what it holds: " + info);
        }

        [VsTest]
        public async Task PutBackStraightAwayItIsStillMolten()
        {
            var firepit = await PotOfTallowOnTheFire();
            var hand = new DummySlot();
            firepit.inputSlot.TryPutInto(Sapi.World, hand);
            await Ticks(2);

            Assert.Equal(1, hand.TryPutInto(Sapi.World, firepit.inputSlot));
            await Ticks(2);

            ItemSlot tallow = ItemMoltenWax.FindIn(firepit);
            Assert.NotNull(tallow, "the tallow did not go back into the pot");
            Assert.Equal(12, tallow.StackSize);
            Assert.Null(firepit.inputSlot.Itemstack.Attributes[CarriedWax.Contents], "the pot still carries a copy");
        }

        /// <summary>
        /// Carried, it stays pourable for a few hours - long enough to walk it to the
        /// moulds - then sets in the pot, two portions to the lump, and is fat when it
        /// goes back on the fire.
        /// </summary>
        [VsTest]
        public async Task CarriedLongEnoughItSetsInThePot()
        {
            var firepit = await PotOfTallowOnTheFire();
            var hand = new DummySlot();
            firepit.inputSlot.TryPutInto(Sapi.World, hand);
            await Ticks(2);

            await Hours(1);
            ItemStack molten = CarriedWax.MoltenIn(Sapi.World, hand.Itemstack, out _);
            Assert.True(((ItemMoltenWax)molten.Collectible).IsWorkable(Sapi.World, molten), "an hour off the fire it should still pour");

            await Hours(CarriedWax.PourableHours);
            var info = new StringBuilder();
            hand.Itemstack.Collectible.GetHeldItemInfo(hand, info, Sapi.World, false);
            Assert.True(info.ToString().Contains("6x " + Lang.Get("item-fat-rendered").ToLower()), "a cold pot should say it has set: " + info);

            hand.TryPutInto(Sapi.World, firepit.inputSlot);
            await Ticks(2);

            ItemSlot fat = ((InventorySmelting)firepit.Inventory).Slots.First(s => !s.Empty);
            Assert.Equal("game:fat-rendered", fat.Itemstack.Collectible.Code.ToString());
            Assert.Equal(6, fat.StackSize);
        }

        /// <summary>
        /// Shift-clicked out into a stack of empty pots it would merge, and there would
        /// be no one pot to carry the tallow: marked, it does not stack.
        /// </summary>
        [VsTest]
        public async Task APotOfTallowDoesNotStackWithEmptyPots()
        {
            var firepit = await PotOfTallowOnTheFire();
            Assert.True(firepit.inputSlot.Itemstack.Attributes.GetBool(CarriedWax.HoldsWax), "the pot on the fire is not marked");

            var pots = new DummySlot(World.Stack("game:claypot-blue-fired", 2));
            Assert.Equal(0, firepit.inputSlot.TryPutInto(Sapi.World, pots));
            Assert.Equal(2, pots.StackSize);
            Assert.NotNull(ItemMoltenWax.FindIn(firepit), "the tallow went somewhere");
        }

        /// <summary>Emptied by dipping, the pot is ordinary again and stacks.</summary>
        [VsTest]
        public async Task AnEmptiedPotLosesItsMark()
        {
            var firepit = await PotOfTallowOnTheFire();
            ItemSlot tallow = ItemMoltenWax.FindIn(firepit);
            tallow.Itemstack = null;
            tallow.MarkDirty();
            await Ticks(5);

            Assert.False(firepit.inputSlot.Itemstack.Attributes.GetBool(CarriedWax.HoldsWax), "an empty pot is still marked");
        }

        static async Task<BlockEntityFirepit> PotOfTallowOnTheFire()
        {
            var firepit = await CandelaDipping.FirepitWithCookedTallow(fatPerSlot: 6, slots: 1);
            // The firepit marks the pot from its own tick.
            await Until(() => firepit.inputSlot.Itemstack?.Attributes.GetBool(CarriedWax.HoldsWax) == true, 40, "the pot was never marked");
            return firepit;
        }

        static int Dropped(BlockPos pos) =>
            Sapi.World.GetEntitiesAround(pos.ToVec3d().Add(0.5, 0.5, 0.5), 3, 3, e => e is EntityItem).Length;
    }
}
