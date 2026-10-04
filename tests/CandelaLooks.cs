using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Not assertions: pictures of Candela's items, to judge by eye - in the hotbar,
    /// in hand, and lying on the ground. Each run writes them to results/.
    /// </summary>
    public class CandelaLooks
    {
        static string Describe(ModelTransform t) => t == null ? "null"
            : $"t({t.Translation.X},{t.Translation.Y},{t.Translation.Z}) r({t.Rotation.X},{t.Rotation.Y},{t.Rotation.Z}) o({t.Origin.X},{t.Origin.Y},{t.Origin.Z}) s{t.ScaleXYZ.X}";

        static readonly string[] Hotbar =
        {
            "candela:dippingrod-0", "candela:dippingrod-2", "candela:dippingrod-4", "candela:dippingrod-6",
            "candela:candlestub-beeswax-75", "candela:candlestub-beeswax-50", "candela:candlestub-beeswax-25",
            "candela:candlestub-tallow-75", "candela:candlestub-tallow-25", "candela:candle-tallow",
        };

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task ItemsForTheEye()
        {
            await World.SetCalendarTo(500 * 24 + 12);

            var hotbar = Player.Me.InventoryManager.GetHotbarInventory();
            for (int i = 0; i < Hotbar.Length; i++)
            {
                hotbar[i].Itemstack = World.Stack(Hotbar[i], 1);
                hotbar[i].MarkDirty();
            }
            // Selected on the client: the server setting it is not sent back.
            await OnClient();
            Capi.World.Player.InventoryManager.ActiveHotbarSlotNumber = 3;   // the finished rod in hand
            await OnServer();

            // A row on the ground in front of where the player will stand.
            string[] dropped = { "candela:dippingrod-0", "candela:dippingrod-3", "candela:dippingrod-6",
                                 "candela:candlestub-beeswax-75", "candela:candlestub-beeswax-25", "candela:candlestub-tallow-50" };
            for (int i = 0; i < dropped.Length; i++)
            {
                var at = P(5 + i, 1, 9).ToVec3d().Add(0.5, 0.1, 0.5);
                Sapi.World.SpawnItemEntity(World.Stack(dropped[i], 1), at, new Vec3d(0, 0, 0));
            }

            await Player.Teleport(new Vec3d(P(7, 1, 6).X + 1.0, P(7, 1, 6).Y, P(7, 1, 6).Z + 0.5));
            await Interact.LookAt(P(7, 1, 9));
            await Frames.Wait(90);
            Log("shot: " + await Shot.Take("results/looks-ground.png"));

            // Close up on the finished rod, which a hotbar icon is too small to judge.
            var finished = P(7, 1, 9);
            await Player.Teleport(new Vec3d(finished.X + 0.5, finished.Y, finished.Z - 1.2));
            await Interact.LookAt(finished);
            await Frames.Wait(30);
            Log("shot: " + await Shot.Take("results/looks-rod-close.png"));

            // The rod in hand, looking down a little as a player dipping would.
            await Interact.LookAt(P(7, 0, 8));
            await Frames.Wait(30);
            Log("shot: " + await Shot.Take("results/looks-held.png"));
        }

        /// <summary>
        /// The finished rod in hand, seen in third person, with two candidate
        /// third-person transforms swapped in on the client.
        ///
        /// First person is not pictured: in this headless client no held item shows in
        /// first person - vanilla's stick included, only blocks such as a torch do - so a
        /// picture of it here says nothing. The rod uses the stick's first-person defaults.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task HeldRodForTheEye()
        {
            await World.SetCalendarTo(500 * 24 + 12);
            var hotbar = Player.Me.InventoryManager.GetHotbarInventory();
            hotbar[0].Itemstack = World.Stack("candela:dippingrod-6", 1);
            hotbar[0].MarkDirty();
            await Player.Teleport(new Vec3d(P(7, 1, 6).X + 1.0, P(7, 1, 6).Y, P(7, 1, 6).Z + 0.5));
            await Interact.LookAt(P(7, 1, 12));

            await OnClient();
            Capi.World.Player.InventoryManager.ActiveHotbarSlotNumber = 0;
            var rod = Capi.World.GetItem(new AssetLocation("candela:dippingrod-6"));
            await Input.Press(GlKeys.F5);
            await Frames.Wait(90);
            Log("shot as shipped: " + await Shot.Take("results/held-third.png"));

            var sideOn = rod.TpHandTransform.Clone();
            sideOn.Rotation.Y = 0;
            rod.TpHandTransform = sideOn;
            await Frames.Wait(30);
            Log("shot side-on: " + await Shot.Take("results/held-third-sideon.png"));

            await Input.Press(GlKeys.F5);
            await Input.Press(GlKeys.F5);
            await OnServer();
        }

        /// <summary>
        /// The scene the mod icon is cut from: beeswax and tallow candles lit at night,
        /// close and low, with the HUD hidden. tools/icon.py crops and scales the shot.
        /// </summary>
        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task ModIconScene()
        {
            // Dusk: dark enough for the flames to glow, light enough to see the wax.
            await World.SetCalendarTo(500 * 24 + 19.6);
            World.SetBlock("game:bunchocandles-9", P(8, 1, 8));
            World.SetBlock("candela:tallowcandles-5", P(9, 1, 8));
            await Ticks(10);

            var at = P(8, 1, 8);
            await Player.Teleport(new Vec3d(at.X + 0.9, at.Y - 1.0, at.Z - 0.55));
            await Interact.LookAt(at);

            await Input.Hotkey("togglehud");   // F4: hide the HUD
            await Frames.Wait(90);
            Log("shot: " + await Shot.Take("results/icon-scene.png"));
            await Input.Hotkey("togglehud");
        }
    }
}
