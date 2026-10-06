using System.Linq;
using System.Reflection;
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
    /// A pot of wax on Stone Bake Oven's cooking top (StoneBakeOvenCompat): lifted off
    /// with its wax, kept hot, drawn open. Each does nothing and says so without the
    /// mod; to run them, hand it in -
    ///
    ///   run.sh ... --mods DIR    with stonebakeoven_*.zip in DIR (ModDB: stonebakeoven)
    /// </summary>
    public class CompatStoneBakeOven
    {
        const string CookingTop = "stonebakeoven:ovencookingtopplain-granite";
        const string Pot = "game:claypot-blue-fired";

        static BlockPos Top => P(8, 1, 8);

        /// <summary>
        /// Lifted straight away, before the oven's tick has marked the pot: the wax goes
        /// with it, not onto the ground.
        /// </summary>
        [VsTest]
        public async Task LiftingThePotOffKeepsItsWax()
        {
            if (!await PotOfTallow(tick: false)) return;
            var inventory = Inventory();

            var hand = new DummySlot();
            Assert.Equal(1, inventory[1].TryPutInto(Sapi.World, hand));
            await Ticks(2);

            Assert.True(inventory.Slots.All(s => s.Empty), "the tallow stayed behind on the oven");
            Assert.Equal(0, Sapi.World.GetEntitiesAround(Top.ToVec3d().Add(0.5, 0.5, 0.5), 3, 3, e => e is EntityItem).Length, "the tallow was spilled");
            Assert.Equal(12, CarriedWax.MoltenIn(Sapi.World, hand.Itemstack, out _)?.StackSize ?? 0, "the pot does not carry the tallow");
        }

        /// <summary>Shift-clicked out into a stack of empty pots, it does not merge into it.</summary>
        [VsTest]
        public async Task APotOfTallowDoesNotStackWithEmptyPots()
        {
            if (!await PotOfTallow(tick: false)) return;

            var pots = new DummySlot(World.Stack(Pot, 2));
            Assert.Equal(0, Inventory()[1].TryPutInto(Sapi.World, pots));
            Assert.Equal(2, pots.StackSize);
        }

        /// <summary>The cooking top heats a pot of molten wax as it would a pot that is cooking.</summary>
        [VsTest]
        public async Task TheCookingTopKeepsWaxHot()
        {
            if (!await PotOfTallow(tick: true)) return;
            var be = World.BE<BlockEntity>(Top);
            bool heats = (bool)be.GetType().GetMethod("canHeatInput").Invoke(be, null);
            Assert.True(heats, "the cooking top would let the wax go cold");
        }

        /// <summary>On the client, the pot is drawn by Candela's open-pot renderer.</summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ThePotIsDrawnOpenWithItsWax()
        {
            if (!await PotOfTallow(tick: true)) return;
            await Ticks(20);

            await OnClient();
            var be = Capi.World.BlockAccessor.GetBlockEntity(Top);
            var renderer = be?.GetType().GetField("renderer", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(be) as FirepitContentsRenderer;
            string drawnBy = renderer?.contentStackRenderer?.GetType().Name;
            await OnServer();

            Assert.Equal(nameof(WaxPotRenderer), drawnBy, "the pot is drawn by the wrong renderer");
        }

        /// <summary>
        /// A cooking top with a pot of hot tallow in it, or false - and a note - if the
        /// mod is not loaded. <paramref name="tick"/>: let the oven tick first.
        /// </summary>
        static async Task<bool> PotOfTallow(bool tick)
        {
            if (Sapi.World.GetBlock(new AssetLocation(CookingTop)) == null)
            {
                Log("stonebakeoven is not loaded - pass it with --mods");
                return false;
            }

            World.SetBlock(CookingTop, Top);
            await Ticks(2);
            var inventory = Inventory();
            inventory[1].Itemstack = World.Stack(Pot, 1);
            inventory[1].MarkDirty();

            ItemStack tallow = World.Stack("candela:tallow-molten", 12);
            tallow.Collectible.SetTemperature(Sapi.World, tallow, 300f);
            inventory.Slots[0].Itemstack = tallow;
            if (tick)
            {
                inventory.Slots[0].MarkDirty();
                World.BE<BlockEntity>(Top).MarkDirty(true);
                await Ticks(10);
            }
            return true;
        }

        static InventorySmelting Inventory() => (InventorySmelting)((BlockEntityContainer)World.BE<BlockEntity>(Top)).Inventory;
    }
}
