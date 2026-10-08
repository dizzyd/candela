using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// Vanilla's torches keeping what is left of them: broken and placed again, and in
    /// and out of a torch holder, rounded down to the quarter as a candle stub is.
    /// </summary>
    public class CandelaTorches
    {
        static BlockPos Torch => P(8, 1, 8);
        static BlockPos Holder => P(8, 2, 8);

        const string LitTorch = "game:torch-basic-lit-up";
        const double TorchHours = 48;

        [BeforeEach, AfterEach]
        public void DefaultConfig() => CandelaConfig.Current.AssignFrom(new CandelaConfig());

        /// <summary>
        /// Empty pockets, since the harness keeps the player's inventory from one test to
        /// the next: a torch one test got back would otherwise be found by the next.
        /// Not the bag slots, which hold the backpacks themselves.
        /// </summary>
        [BeforeEach]
        public void EmptyPockets()
        {
            if (Player.Me == null) return;
            foreach (ItemSlot slot in Carried().Where(s => s is not ItemSlotBackpack && !s.Empty))
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        }

        [VsTest]
        public async Task ABrokenTorchComesBackWithWhatIsLeft()
        {
            await PlaceTorch();
            await Hours(TorchHours * 0.4);

            ItemStack[] drops = World.GetBlock(Torch).GetDrops(Sapi.World, Torch, null);

            Assert.Equal(1, drops.Length);
            Assert.Equal(LitTorch, drops[0].Collectible.Code.ToString());
            Assert.Equal(50, drops[0].Attributes.GetInt(TorchTime.Attr), "40% burned should come back as half");
        }

        [VsTest]
        public async Task ANewTorchComesBackNewAndStacksWithNewOnes()
        {
            await PlaceTorch();

            ItemStack dropped = World.GetBlock(Torch).GetDrops(Sapi.World, Torch, null).Single();

            Assert.False(dropped.Attributes.HasAttribute(TorchTime.Attr));
            Assert.Equal(1, dropped.Collectible.GetMergableQuantity(dropped, World.Stack(LitTorch, 1), EnumMergePriority.AutoMerge));
        }

        [VsTest]
        public async Task OnePickedStraightBackUpIsNew()
        {
            await PlaceTorch();
            await Hours(0.5);

            Assert.False(World.GetBlock(Torch).GetDrops(Sapi.World, Torch, null).Single().Attributes.HasAttribute(TorchTime.Attr),
                "a torch put down and picked up within the hour came back part-burned");
        }

        [VsTest]
        public async Task APartBurnedOnePickedStraightBackUpIsAsItWas()
        {
            await PlaceTorch(TorchTime.Stamp(World.Stack(LitTorch, 1), 0.5));
            await Hours(0.5);

            Assert.Equal(50, World.GetBlock(Torch).GetDrops(Sapi.World, Torch, null).Single().Attributes.GetInt(TorchTime.Attr),
                "a half torch put down and picked up within the hour lost a quarter");
        }

        [VsTest]
        public async Task LessThanAQuarterLeftComesBackAsNothing()
        {
            await PlaceTorch();
            await Hours(TorchHours * 0.8);

            Assert.Equal(0, World.GetBlock(Torch).GetDrops(Sapi.World, Torch, null).Length);
        }

        [VsTest]
        public void TorchesWithAsMuchLeftStackAndOthersDoNot()
        {
            ItemStack half = TorchTime.Stamp(World.Stack(LitTorch, 1), 0.6);
            ItemStack alsoHalf = TorchTime.Stamp(World.Stack(LitTorch, 1), 0.5);
            ItemStack quarter = TorchTime.Stamp(World.Stack(LitTorch, 1), 0.3);

            Assert.Equal(1, half.Collectible.GetMergableQuantity(half, alsoHalf, EnumMergePriority.AutoMerge));
            Assert.Equal(0, half.Collectible.GetMergableQuantity(half, quarter, EnumMergePriority.AutoMerge));
            Assert.Equal(0, half.Collectible.GetMergableQuantity(half, World.Stack(LitTorch, 1), EnumMergePriority.AutoMerge));
        }

        [VsTest]
        public async Task APlacedPartBurnedTorchBurnsOnlyWhatItHad()
        {
            var be = await PlaceTorch(TorchTime.Stamp(World.Stack(LitTorch, 1), 0.5));

            Assert.Close(TorchTime.FractionLeft(be), 0.5, 0.01);

            // And burns out when that is gone, not when a new one would.
            // Vanilla checks its timer on only three seconds in ten, so it is fired until it does.
            await Hours(TorchHours * 0.5 + 2);
            for (int i = 0; i < 60 && World.BlockCode(Torch) == LitTorch; i++) await World.TickNow(Torch);
            Assert.Equal("game:torch-basic-burnedout-up", World.BlockCode(Torch), "the half torch did not burn out after half a torch's hours");
        }

        [VsTest]
        public async Task TurnedOffEveryTorchComesBackNew()
        {
            CandelaConfig.Current.TorchesKeepTheirTime = false;
            await PlaceTorch(TorchTime.Stamp(World.Stack(LitTorch, 1), 0.5));
            Assert.Close(TorchTime.FractionLeft(World.BE<BlockEntityTorch>(Torch)), 1, 0.01, "the mark was honoured with the setting off");

            await Hours(TorchHours * 0.4);
            Assert.False(World.GetBlock(Torch).GetDrops(Sapi.World, Torch, null).Single().Attributes.HasAttribute(TorchTime.Attr));
        }

        [VsTest]
        public void APartBurnedTorchSaysSo()
        {
            var dsc = new StringBuilder();
            var slot = new DummySlot(TorchTime.Stamp(World.Stack(LitTorch, 1), 0.5));
            slot.Itemstack.Collectible.GetHeldItemInfo(slot, dsc, Sapi.World, false);
            Assert.True(dsc.ToString().Contains("24"), "no hours left in the tooltip: " + dsc);
        }

        [VsTest]
        public void EveryTorchHolderGetsTheBehaviour()
        {
            foreach (string code in new[] { "game:torchholder-brass-empty-north", "game:torchholder-aged-filled-east" })
            {
                Assert.True(World.Block(code).BlockEntityBehaviors.Any(b => b.Name == BEBehaviorTorchHolderTime.Name), code);
            }
        }

        // ----- with a player -----

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task PlacingOneByHandKeepsItsTime()
        {
            await HoldPartBurned(0.75);

            await Interact.UseBlock(Torch.DownCopy(), BlockFacing.UP);
            await Until(() => World.BlockCode(Torch) == LitTorch, 40, "the torch was not placed");

            Assert.Close(TorchTime.FractionLeft(World.BE<BlockEntityTorch>(Torch)), 0.75, 0.01);
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task AHolderGivesBackTheTorchItWasGiven()
        {
            await PlaceHolder();
            await HoldPartBurned(0.5);

            await Interact.UseBlock(Holder);
            await Until(() => World.BlockCode(Holder) == "game:torchholder-brass-filled-north", 40, "the torch did not go in");
            Assert.Close(HolderTime().Fraction, 0.5, 0.001);

            // Holders do not burn it.
            await Hours(TorchHours * 2);

            await EmptyHand();
            await Interact.UseBlock(Holder);
            await Until(() => World.BlockCode(Holder) == "game:torchholder-brass-empty-north", 40, "the torch did not come out");
            await Ticks(4);

            ItemStack got = Carried().Select(s => s.Itemstack)
                .FirstOrDefault(s => s?.Collectible.Code.ToString() == LitTorch);
            Assert.NotNull(got, "no torch came back");
            Assert.Equal(50, got.Attributes.GetInt(TorchTime.Attr), "the holder refilled the torch");
            Assert.Close(HolderTime().Fraction, 1, 0.001, "the empty holder still remembers a torch");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ANewTorchInAHolderComesBackNew()
        {
            await PlaceHolder();
            await Player.Hold(LitTorch);
            await Interact.UseBlock(Holder);
            await Until(() => World.BlockCode(Holder) == "game:torchholder-brass-filled-north", 40, "the torch did not go in");

            await EmptyHand();
            await Interact.UseBlock(Holder);
            await Until(() => World.BlockCode(Holder) == "game:torchholder-brass-empty-north", 40, "the torch did not come out");
            await Ticks(4);

            Assert.True(PlayerHas(LitTorch), "no torch came back");
            Assert.False(Carried().Any(s => s.Itemstack?.Attributes.HasAttribute(TorchTime.Attr) == true), "a new torch came back marked");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ABrokenHolderDropsItsTorchAsItWas()
        {
            await PlaceHolder();
            await HoldPartBurned(0.25);
            await Interact.UseBlock(Holder);
            await Until(() => World.BlockCode(Holder) == "game:torchholder-brass-filled-north", 40, "the torch did not go in");

            ItemStack[] drops = World.GetBlock(Holder).GetDrops(Sapi.World, Holder, null);
            ItemStack torch = drops.Single(d => d.Collectible.Code.ToString() == LitTorch);
            Assert.Equal(25, torch.Attributes.GetInt(TorchTime.Attr));
            Assert.True(drops.Any(d => d.Collectible.Code.Path.StartsWith("torchholder")), "the holder itself was not dropped");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task WithNoRoomForItTheTorchStaysPut()
        {
            await PlaceHolder();
            await HoldPartBurned(0.5);
            Assert.True(UseHolder(), "the torch did not go in");

            var filled = new List<ItemSlot>();
            try
            {
                // Put-out torches: they will not take a lit one onto their stack.
                ItemStack filler = World.Stack("game:torch-basic-extinct-up", 1);
                foreach (ItemSlot slot in Carried().Where(s => s.Empty && s.CanHold(new DummySlot(filler))))
                {
                    slot.Itemstack = filler.Clone();
                    filled.Add(slot);
                }

                Assert.False(UseHolder(), "a torch was taken out with nowhere to go");
                Assert.Equal("game:torchholder-brass-filled-north", World.BlockCode(Holder));
                Assert.Close(HolderTime().Fraction, 0.5, 0.001, "the holder forgot its torch");
            }
            finally
            {
                foreach (ItemSlot slot in filled) slot.Itemstack = null;
            }
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task TurningItOffAndOnNeverShrinksANewTorch()
        {
            await PlaceHolder();
            await HoldPartBurned(0.5);
            Assert.True(UseHolder(), "the torch did not go in");

            // Off: out comes a new torch, as in vanilla, and in goes another.
            CandelaConfig.Current.TorchesKeepTheirTime = false;
            Assert.True(UseHolder(), "the torch did not come out");
            Assert.False(TakeCarriedTorch().Attributes.HasAttribute(TorchTime.Attr), "the setting is off but the torch came back part-burned");
            await Player.Hold(LitTorch);
            Assert.True(UseHolder(), "the new torch did not go in");

            // On again: the new torch comes back new.
            CandelaConfig.Current.TorchesKeepTheirTime = true;
            Assert.True(UseHolder(), "the torch did not come out");
            Assert.False(TakeCarriedTorch().Attributes.HasAttribute(TorchTime.Attr), "the holder handed back the half torch it held before");
        }

        // ----- helpers -----

        /// <summary>The hotbar and backpack. Not every inventory: the creative one throws when walked.</summary>
        static IEnumerable<ItemSlot> Carried() => Player.Me.InventoryManager.Inventories.Values
            .Where(i => i.ClassName is "hotbar" or "backpack").SelectMany(i => i);

        /// <summary>The holder's torch time, once <see cref="PlaceHolder"/> has put one up.</summary>
        static BEBehaviorTorchHolderTime HolderTime() => World.BE<BlockEntity>(Holder).GetBehavior<BEBehaviorTorchHolderTime>();

        /// <summary>
        /// Right-clicks the holder on the server, as the player, with what they hold. No
        /// client in it: for what the holder does, not for the click reaching it.
        /// </summary>
        static bool UseHolder() =>
            World.GetBlock(Holder).OnBlockInteractStart(Sapi.World, Player.Me, new BlockSelection { Position = Holder.Copy(), Face = BlockFacing.SOUTH });

        /// <summary>A lit torch in hand with <paramref name="fraction"/> of it left, on the client too.</summary>
        static async Task HoldPartBurned(double fraction)
        {
            await Player.Hold(LitTorch);
            ItemSlot slot = Player.Me.InventoryManager.ActiveHotbarSlot;
            slot.Itemstack = TorchTime.Stamp(slot.Itemstack, fraction);
            slot.MarkDirty();
            await Ticks(4);
        }

        /// <summary>Takes the lit torch the player carries out of their inventory, and returns it.</summary>
        static ItemStack TakeCarriedTorch()
        {
            ItemSlot slot = Carried().FirstOrDefault(s => s.Itemstack?.Collectible.Code.ToString() == LitTorch);
            Assert.NotNull(slot, "no torch came back");
            return slot.TakeOutWhole();
        }

        static async Task<BlockEntityTorch> PlaceTorch(ItemStack from = null)
        {
            World.SetBlock(LitTorch, Torch);
            await Ticks(2);
            // SetBlock places with no stack; replay the placement with the torch it came from.
            var be = World.BE<BlockEntityTorch>(Torch);
            be.OnBlockPlaced(from ?? World.Stack(LitTorch, 1));
            return be;
        }

        /// <summary>
        /// A brass holder on a wall, at head height, with the player in front of it:
        /// StandNear alone can put them behind the wall, where no click reaches it.
        /// </summary>
        static async Task PlaceHolder()
        {
            World.SetBlock("game:planks-oak-ud", Holder.NorthCopy());
            World.SetBlock("game:torchholder-brass-empty-north", Holder);
            await Ticks(4);
            Assert.Equal("game:torchholder-brass-empty-north", World.BlockCode(Holder), "the holder fell off");
            await Player.Teleport(P(8, 1, 11));
            await Interact.LookAt(Holder);
        }
    }
}
