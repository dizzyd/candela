using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using static Candela.Tests.CandelaDipping;
using static Candela.Tests.Hands;

namespace Candela.Tests
{
    /// <summary>
    /// Art of Growing's own firepit, <c>artofgrowing:firepit</c>: vanilla's class and
    /// behaviors under another code, and the one its dry grass and thatch build. A dip
    /// vat patched onto vanilla's firepit file alone never reached it, and a player's
    /// rod opened and closed the firepit's dialog instead. Each test does nothing and
    /// says so without the mod; to run them -
    ///
    ///   tests/fixtures/fetch.sh, then run.sh ... --mods $PWD/tests/fixtures/Mods --client
    /// </summary>
    public class CompatArtOfGrowing
    {
        const string AogFirepit = "artofgrowing:firepit-extinct";

        static BlockPos Ground => P(8, 0, 8);
        static BlockPos Above => P(8, 1, 8);

        [VsTest]
        public void ItsFirepitDipsBeforeItOpens()
        {
            if (!Loaded()) return;

            foreach (string state in new[] { "extinct", "lit", "cold" })
            {
                var behaviors = Sapi.World.GetBlock(new AssetLocation("artofgrowing:firepit-" + state)).BlockBehaviors.ToList();
                int dipVat = behaviors.FindIndex(b => b is BlockBehaviorDipVat);
                int container = behaviors.FindIndex(b => b is BlockBehaviorContainer);
                Assert.True(dipVat >= 0, $"artofgrowing:firepit-{state} has no CandelaDipVat");
                Assert.True(dipVat < container, $"artofgrowing:firepit-{state} runs Container before CandelaDipVat");
            }
        }

        /// <summary>
        /// The way a player gets one: shift-right-click the ground with dry grass. If
        /// this stops building Art of Growing's firepit, the dip below is testing a
        /// firepit nobody builds.
        /// </summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task DryGrassBuildsItsFirepit()
        {
            if (!Loaded()) return;
            await CompatServerMods.AnswerTheBasics();

            await Player.StandNear(Above);
            await Player.Hold("game:drygrass", 4);
            await ShiftUse(Ground, BlockFacing.UP);

            Assert.Equal("artofgrowing:firepit-construct1", World.BlockCode(Above));
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task DippingAtItsFirepitAddsACoat()
        {
            if (!Loaded()) return;
            await CompatServerMods.AnswerTheBasics();

            var firepit = await FirepitWithCookedTallow(fatPerSlot: 3, slots: 2, firepitCode: AogFirepit);
            await Player.Hold("candela:dippingrod-0");

            await Dip();

            Assert.Equal("candela:dippingrod-1", Player.Held?.Collectible.Code.ToString());
            Assert.Equal(11, TallowSlot(firepit).StackSize);
        }

        static bool Loaded()
        {
            if (Sapi.World.GetBlock(new AssetLocation(AogFirepit)) != null) return true;

            Log("artofgrowingpatch is not loaded - run tests/fixtures/fetch.sh and pass --mods");
            return false;
        }
    }
}
