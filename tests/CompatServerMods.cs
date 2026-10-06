using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using static Candela.Tests.CandelaDipping;

namespace Candela.Tests
{
    /// <summary>
    /// Dipping on a server running The BASICs, Newfies' Server Essentials Suite and
    /// Anti-Cheat Client, and Immersive Firewood - a player's, where holding right-click
    /// on the firepit with a rod opened and closed its dialog instead. The BASICs puts a
    /// finalizer on the client's block selection, the Suite hooks CanUseBlock and guards
    /// interaction packets, and Immersive Firewood patches the firepit's burn tick and
    /// its fuel. Each test does nothing and says so without them; to run them -
    ///
    ///   tests/fixtures/fetch.sh, then run.sh ... --mods $PWD/tests/fixtures/Mods --client
    /// </summary>
    public class CompatServerMods
    {
        static readonly string[] ModIds = { "thebasics", "newfiesserveressentialssuite", "newfiesanticheatclient", "vsfirewood" };

        /// <summary>
        /// The client's firepit, not the server's: the client rebuilds each block's
        /// behaviors from the server's packet, and the click starts there.
        /// </summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task TheClientsFirepitDipsBeforeItOpens()
        {
            if (!await Ready()) return;

            await OnClient();
            var behaviors = Capi.World.GetBlock(new AssetLocation("game:firepit-extinct")).BlockBehaviors.ToList();
            await OnServer();

            int dipVat = behaviors.FindIndex(b => b is BlockBehaviorDipVat);
            int container = behaviors.FindIndex(b => b is BlockBehaviorContainer);
            string order = string.Join(", ", behaviors.Select(b => b.GetType().Name));
            Assert.True(dipVat >= 0, $"the client's firepit has no CandelaDipVat: {order}");
            Assert.True(dipVat < container, $"the client's firepit runs Container before CandelaDipVat: {order}");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task DippingAddsACoat()
        {
            if (!await Ready()) return;

            var firepit = await FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            await Player.Hold("candela:dippingrod-0");

            await Dip();

            Assert.Equal("candela:dippingrod-1", Player.Held?.Collectible.Code.ToString());
            Assert.Equal(11, TallowSlot(firepit).StackSize);
        }

        /// <summary>Two coats, a set apart - a hold that repeats, as a player's does.</summary>
        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task ASecondCoatTakesOnceTheFirstHasSet()
        {
            if (!await Ready()) return;

            await FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            await Player.Hold("candela:dippingrod-0");

            await Dip();
            await Hours(ItemDippingRod.SetHours * 1.5);
            await Dip();

            Assert.Equal("candela:dippingrod-2", Player.Held?.Collectible.Code.ToString());
        }

        /// <summary>Whether the mods are loaded, and if so, The BASICs' prompt answered.</summary>
        static async Task<bool> Ready()
        {
            string[] missing = ModIds.Where(id => !Sapi.ModLoader.IsModEnabled(id)).ToArray();
            if (missing.Length > 0)
            {
                Log($"{string.Join(", ", missing)} not loaded - run tests/fixtures/fetch.sh and pass --mods");
                return false;
            }

            await AnswerTheBasics();
            return true;
        }

        /// <summary>
        /// Answers The BASICs' analytics prompt, if it is loaded. It asks an admin once, a
        /// few seconds into their first session, and its dialog takes the mouse - which
        /// cancels whatever right-click is held at the time. Answering "off" also keeps a
        /// test run from sending telemetry. Every fixture shares one --mods directory, so
        /// any dipping test may find it loaded.
        /// </summary>
        internal static async Task AnswerTheBasics()
        {
            if (!Sapi.ModLoader.IsModEnabled("thebasics")) return;

            var answer = await Cmd("/basicsanalytics off", asPlayer: Player.Me.PlayerName);
            Assert.Equal(EnumCommandStatus.Success, answer.Status, $"could not answer The BASICs' analytics prompt: {answer.StatusMessage}");
            await Gui.CloseDialogs();
        }
    }
}
