using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
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
        /// Not an assertion: a pot of molten tallow on the fire, which should stand open
        /// with the tallow showing, and a freshly dipped rod in the hotbar, whose bar
        /// should fill as the coat sets and be gone once it has.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task TallowPotAndSettingRodForTheEye()
        {
            await World.SetCalendarTo(500 * 24 + 12);
            await Ticks(10);

            var firepit = await CandelaDipping.FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            // Close and high enough to see down into the pot.
            await Player.Teleport(new Vec3d(firepit.Pos.X + 0.5, firepit.Pos.Y + 0.6, firepit.Pos.Z - 0.6));
            await Interact.LookAt(firepit.Pos);
            await Frames.Wait(60);
            Log("shot: " + await Shot.Take("results/looks-tallow-pot.png"));

            var hotbar = Player.Me.InventoryManager.GetHotbarInventory();
            hotbar[0].Itemstack = ((ItemDippingRod)Sapi.World.GetItem(new AssetLocation("candela:dippingrod-0"))).WithAnotherLayer(Sapi.World);
            hotbar[0].MarkDirty();
            await OnClient();
            Capi.World.Player.InventoryManager.ActiveHotbarSlotNumber = 0;
            await OnServer();

            await Frames.Wait(30);
            Log("shot: " + await Shot.Take("results/looks-rod-just-dipped.png"));
            await Hours(ItemDippingRod.SetHours * 0.6);
            await Frames.Wait(30);
            Log("shot: " + await Shot.Take("results/looks-rod-setting.png"));
            await Hours(ItemDippingRod.SetHours);
            await Frames.Wait(30);
            Log("shot: " + await Shot.Take("results/looks-rod-set.png"));
        }

        /// <summary>
        /// The dip as the player sees it, in first person over a pot of tallow. Asserts
        /// only that the animation reached the player; the pictures are for the eye.
        ///
        /// The third-person side of it is pictured pose by pose on a stand-in, by
        /// DipPosesOnAStandIn: shots of the running animation on one all came out on the
        /// same frame, though the animation was cycling - watched live, it dips.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task DippingInFirstPerson()
        {
            await OnClient();
            var anims = Capi.World.Player.Entity.Properties.Client.AnimationsByMetaCode;
            bool tp = anims.ContainsKey("candela-dip"), fp = anims.ContainsKey("candela-dip-fp");
            await OnServer();
            Assert.True(tp, "the player has no candela-dip animation - the patch did not apply");
            Assert.True(fp, "the player has no candela-dip-fp animation");

            await World.SetCalendarTo(500 * 24 + 12);
            var firepit = await CandelaDipping.FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            await Player.Teleport(new Vec3d(firepit.Pos.X - 0.4, firepit.Pos.Y, firepit.Pos.Z - 0.6));
            await Player.Hold("candela:dippingrod-2");
            await Interact.Aim(firepit.Pos);

            // A fresh rod, so the first hold is a dip and not "the coat is setting".
            await Input.MouseDown(EnumMouseButton.Right);
            for (int i = 0; i < 3; i++)
            {
                await Frames.Wait(6);
                Log("shot: " + await Shot.Take($"results/dip-firstperson-{i}.png"));
            }
            await Input.MouseUp(EnumMouseButton.Right);
        }

        /// <summary>
        /// Each keyframe of the dip held still on the stand-in, to tune it by: run
        /// tools/dipanim.py --probe first, which adds them as candela-probe-N. Without
        /// that it has nothing to show and says so.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task DipPosesOnAStandIn()
        {
            string[] poses = await ProbePoses("candela-probe-");
            if (poses.Length == 0)
            {
                Log("no probe poses - generate the patch with tools/dipanim.py --probe");
                return;
            }

            var (_, bot) = await StandInAtThePot();
            foreach (string pose in poses)
            {
                await Play(bot, pose);
                await Frames.Wait(60);
                Log("shot: " + await Shot.Take($"results/dip-pose-{pose.Substring("candela-probe-".Length)}.png"));
            }
        }

        /// <summary>
        /// The first-person dip poses, played on the player and seen through their own
        /// eyes over the pot: tools/dipanim.py --probe adds them as candela-probe-fp-N.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task DipPosesFirstPerson()
        {
            string[] poses = await ProbePoses("candela-probe-fp-");
            if (poses.Length == 0)
            {
                Log("no first-person probe poses - generate the patch with tools/dipanim.py --probe");
                return;
            }

            await World.SetCalendarTo(500 * 24 + 12);
            var firepit = await CandelaDipping.FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            await Player.Teleport(new Vec3d(firepit.Pos.X - 0.4, firepit.Pos.Y, firepit.Pos.Z - 0.6));
            await Player.Hold("candela:dippingrod-2");
            await Interact.Aim(firepit.Pos);

            foreach (string pose in poses)
            {
                await Play(Player.Me.Entity, pose);
                await Frames.Wait(60);
                Log("shot: " + await Shot.Take($"results/dip-pose-fp-{pose.Substring("candela-probe-fp-".Length)}.png"));
            }
        }

        /// <summary>
        /// A pot of tallow on the fire, a playerbot with a rod standing at it facing
        /// in, and the camera close beside them both, side on.
        /// </summary>
        static async Task<(BlockEntityFirepit, Entity)> StandInAtThePot()
        {
            await World.SetCalendarTo(500 * 24 + 12);
            var firepit = await CandelaDipping.FirepitWithCookedTallow(fatPerSlot: 3, slots: 2);
            BlockPos pot = firepit.Pos;

            var bot = (EntityAgent)World.SpawnEntity("game:playerbot", pot.WestCopy());
            bot.ServerPos.SetPos(pot.X - 0.35, pot.Y, pot.Z + 0.5);
            bot.ServerPos.Yaw = GameMath.PIHALF;
            bot.Pos.SetFrom(bot.ServerPos);
            bot.BodyYaw = GameMath.PIHALF;
            bot.RightHandItemSlot.Itemstack = World.Stack("candela:dippingrod-3", 1);
            bot.RightHandItemSlot.MarkDirty();
            await Ticks(20);

            await Player.Teleport(new Vec3d(bot.Pos.X + 0.6, bot.Pos.Y + 0.1, bot.Pos.Z + 1.9));
            await Interact.LookAt(new Vec3d(bot.Pos.X + 0.3, bot.Pos.Y + 0.8, bot.Pos.Z));
            return (firepit, bot);
        }

        /// <summary>
        /// The probe poses tools/dipanim.py --probe added as <paramref name="prefix"/>N,
        /// in order. Only a number after the prefix: the first-person poses,
        /// <c>candela-probe-fp-N</c>, also start <c>candela-probe-</c>.
        /// </summary>
        static async Task<string[]> ProbePoses(string prefix)
        {
            await OnClient();
            string[] poses = Capi.World.Player.Entity.Properties.Client.AnimationsByMetaCode.Keys
                .Select(k => (code: k, n: k.StartsWith(prefix) && int.TryParse(k.Substring(prefix.Length), out int n) ? n : -1))
                .Where(p => p.n >= 0)
                .OrderBy(p => p.n)
                .Select(p => p.code)
                .ToArray();
            await OnServer();
            return poses;
        }

        /// <summary>Plays one of the player's animations, alone, on the client's copy of an entity.</summary>
        static async Task Play(Entity entity, string code)
        {
            long id = entity.EntityId;
            await OnClient();
            var shown = Capi.World.GetEntityById(id);
            foreach (string running in shown.AnimManager.ActiveAnimationsByAnimCode.Keys.ToArray()) shown.AnimManager.StopAnimation(running);
            shown.AnimManager.StartAnimation(Capi.World.Player.Entity.Properties.Client.AnimationsByMetaCode[code].Clone());
            await OnServer();
        }

        /// <summary>
        /// Not an assertion: the moulds in each state and the molten waxes, in the hotbar
        /// and on the ground, with a part-set mould in hand to show its bar.
        /// </summary>
        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task MouldsForTheEye()
        {
            await World.SetCalendarTo(500 * 24 + 12);
            string[] row = { "candela:candlemould-blue-raw", "candela:candlemould-fire-fired", "candela:candlemould-red-tallow",
                             "candela:candlemould-blue-beeswax", "candela:tallow-molten", "candela:beeswax-molten" };

            var hotbar = Player.Me.InventoryManager.GetHotbarInventory();
            for (int i = 0; i < 10; i++) { hotbar[i].Itemstack = null; hotbar[i].MarkDirty(); }
            for (int i = 0; i < row.Length; i++)
            {
                hotbar[i].Itemstack = World.Stack(row[i], 1);
                hotbar[i].MarkDirty();
            }
            var empty = (ItemCandleMould)Sapi.World.GetItem(new AssetLocation("candela:candlemould-blue-fired"));
            hotbar[6].Itemstack = empty.Worked(Sapi.World, World.Stack("candela:candlemould-blue-fired", 1), "tallow");
            hotbar[6].MarkDirty();
            await OnClient();
            Capi.World.Player.InventoryManager.ActiveHotbarSlotNumber = 6;
            await OnServer();

            for (int i = 0; i < 4; i++)
            {
                var item = Sapi.World.SpawnItemEntity(World.Stack(row[i], 1), new Vec3d(P(6 + i, 1, 9).X + 0.5, P(6, 1, 9).Y + 0.1, P(6, 1, 9).Z + 0.5));
                item.ServerPos.Motion.Set(0, 0, 0);
            }
            await Ticks(20);

            await Player.Teleport(new Vec3d(P(7, 1, 6).X + 1.0, P(7, 1, 6).Y, P(7, 1, 6).Z + 0.5));
            await Interact.LookAt(P(7, 0, 9));
            await Frames.Wait(30);
            Log("shot: " + await Shot.Take("results/looks-moulds.png"));
            await Hours(ItemCandleMould.SetHours * 0.5);
            await Frames.Wait(30);
            Log("shot: " + await Shot.Take("results/looks-moulds-setting.png"));
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
