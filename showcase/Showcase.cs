using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Showcase
{
    /// <summary>
    /// Not a test: builds a dark hall showing every flame, dye, wax, oil, burn stage
    /// and holder Candela has, to walk around in. Run it with --keep, save, and copy the
    /// save into a pack - see README.md beside this.
    ///
    /// Rows run along x and step back along z from the door at the south wall. Floor
    /// rows are candles and lanterns; the chandelier rows hang from the ceiling.
    /// </summary>
    public class Showcase
    {
        const int Size = 48, Ceiling = 8;

        internal static readonly string[] Flames = { null, "red", "yellow", "green", "teal", "blue", "violet" };
        internal static readonly string[] Dyes = new string[] { null }.Concat(WaxDyes.All).ToArray();
        internal static readonly string[] Glasses = { "quartz", "plain", "red", "yellow", "green", "blue", "violet", "pink", "brown" };
        internal static readonly string[] Metals = { "copper", "brass", "blackbronze", "bismuth", "tinbronze", "bismuthbronze", "iron",
                                            "molybdochalkos", "silver", "gold", "meteoriciron", "steel", "electrum" };

        internal const string Beeswax = "game:bunchocandles", Tallow = "candela:tallowcandles";

        [VsTest(TimeoutMs = 600000), PlotSize(Size, 16)]
        public async Task BuildTheHall()
        {
            await World.SetCalendarTo(500 * 24 + 21);
            Hall();
            await Ticks(5);

            // ---- candles on the floor ----
            Sign(3, "Beeswax\neach flame");
            for (int i = 0; i < Flames.Length; i++) await Bunch(Beeswax, P(6 + 4 * i, 1, 3), Enumerable.Repeat(new CandleLook(Flames[i], null), 3));

            Sign(6, "Tallow\neach flame");
            for (int i = 0; i < Flames.Length; i++) await Bunch(Tallow, P(6 + 4 * i, 1, 6), Enumerable.Repeat(new CandleLook(Flames[i], null), 3));

            Sign(9, "Beeswax\neach dye,\nevery flame");
            for (int i = 0; i < Dyes.Length; i++) await Bunch(Beeswax, P(6 + 3 * i, 1, 9), Flames.Select(f => new CandleLook(f, Dyes[i])));

            Sign(12, "Tallow\neach dye,\nevery flame");
            for (int i = 0; i < Dyes.Length; i++) await Bunch(Tallow, P(6 + 3 * i, 1, 12), Flames.Select(f => new CandleLook(f, Dyes[i])));

            Sign(15, "1 to 9\nmixed");
            for (int n = 1; n <= 9; n++) await Bunch(Beeswax, P(6 + 4 * n - 4, 1, 15), Mixed(n));

            Sign(18, "Burning down\nbeeswax, tallow");
            await BurnStages(Beeswax, 6);
            await BurnStages(Tallow, 27);

            // ---- chandeliers overhead ----
            Sign(22, "Chandeliers\neach flame,\nbeeswax, tallow\n(overhead)");
            for (int i = 0; i < Flames.Length; i++)
            {
                await Chandelier(P(6 + 5 * i, Ceiling - 1, 22), Enumerable.Repeat(new CandleLook(Flames[i], null), 8));
                await Chandelier(P(8 + 5 * i, Ceiling - 1, 22), Enumerable.Repeat(new CandleLook(Flames[i], null), 8), Tallow);
            }

            Sign(26, "Chandeliers\neach dye\n(overhead)");
            for (int i = 0; i < Dyes.Length; i++) await Chandelier(P(6 + 3 * i, Ceiling - 1, 26), Enumerable.Repeat(new CandleLook(null, Dyes[i]), 8));

            Sign(30, "Chandeliers\n0 to 8, mixed,\nthen tallow\n(overhead)");
            for (int n = 0; n <= 8; n++) await Chandelier(P(6 + 4 * n, Ceiling - 1, 30), Mixed(n));
            await Chandelier(P(42, Ceiling - 1, 30), Mixed(8), Tallow);

            // ---- lanterns ----
            Sign(34, "Lanterns\nbeeswax, then\ntallow (sooty)");
            for (int i = 0; i < Flames.Length; i++)
            {
                await Lantern(P(6 + 3 * i, 1, 34), "large-up", "copper", "quartz", Beeswax, new CandleLook(Flames[i], null));
                await Lantern(P(27 + 3 * i, 1, 34), "large-up", "copper", "quartz", Tallow, new CandleLook(Flames[i], null));
            }

            Sign(37, "Glass over\na blue flame:\ncoloured wins");
            for (int i = 0; i < Glasses.Length; i++) await Lantern(P(6 + 3 * i, 1, 37), "large-up", "brass", Glasses[i], Beeswax, new CandleLook("blue", null));

            Sign(40, "Lanterns\neach dye");
            for (int i = 0; i < Dyes.Length; i++) await Lantern(P(6 + 3 * i, 1, 40), "large-up", "iron", "quartz", Beeswax, new CandleLook(null, Dyes[i]));

            Sign(43, "Metals, small\nhanging large\noverhead");
            for (int i = 0; i < Metals.Length; i++)
            {
                var look = new CandleLook(Flames[i % Flames.Length], null);
                await Lantern(P(6 + 3 * i, 1, 43), "small-up", Metals[i], "quartz", Beeswax, look);
                await Lantern(P(6 + 3 * i, Ceiling - 1, 43), "large-down", Metals[i], "quartz", Beeswax, look);
            }

            Sign(46, "Oil burners\nolive, linseed\n(sooty)");
            string[] oils = { "game:oilportion-olive", "game:oilportion-flax" };
            for (int i = 0; i < oils.Length; i++)
            {
                int x = 6 + 12 * i;
                await Burner(P(x, 1, 46), oils[i], LampOil.BurnerLitres, wickLow: false);
                await Burner(P(x + 3, 1, 46), oils[i], LampOil.BurnerLitres, wickLow: true);
                await Burner(P(x + 6, 1, 46), oils[i], 0, wickLow: false);
            }
            await Burner(P(30, 1, 46), null, 0, wickLow: false);
            await Lantern(P(36, 1, 46), "large-up", "copper", "quartz", Beeswax, CandleLook.Plain);
            await Lantern(P(39, 1, 46), "large-up", "copper", "quartz", Tallow, CandleLook.Plain);

            await Ticks(20);

            // Spawn inside the door, facing in.
            var spawn = P(Size / 2, 1, 1);
            Sapi.WorldManager.SaveGame.DefaultSpawn = new PlayerSpawnPos(spawn.X, spawn.Y, spawn.Z);
            Log($"hall built; spawn {spawn}");

            if (Capi != null) await Tour();
        }

        /// <summary>
        /// With a client attached, a picture of each part of the hall, for posting: the
        /// HUD hidden, and the player flying free so the camera can sit at candle height
        /// or up among the chandeliers. Each stop is an eye position and a point to look
        /// at, in plot coordinates.
        /// </summary>
        static async Task Tour()
        {
            Player.Me.WorldData.FreeMove = true;
            Player.Me.WorldData.NoClip = true;
            ((IServerPlayer)Player.Me).BroadcastPlayerData();
            await Input.Hotkey("togglehud");

            (string name, Vec3d eye, Vec3d at)[] stops =
            {
                ("hall", At(2, 6.5, 2), At(26, 1, 24)),
                ("hall-back", At(45, 6.5, 45.5), At(20, 1.5, 20)),
                ("dyes-row", At(4, 1.75, 10.6), At(16, 1.2, 9.5)),
                ("dyes-black-end", At(41.3, 1.55, 8.4), At(31, 1.15, 9.5)),
                ("dyes-tallow-end", At(41.3, 1.55, 11.4), At(31, 1.15, 12.5)),
                ("dye-bunch-close", At(32.2, 1.7, 7.9), At(33.5, 1.2, 9.5)),
                ("flames-rows", At(3.5, 1.7, 1.8), At(22, 1.1, 4.5)),
                ("bunch-sizes", At(3.5, 1.8, 13.8), At(22, 1.1, 15.5)),
                ("burning-down", At(4.5, 1.6, 17.0), At(12, 1.15, 18.5)),
                ("chandeliers-flames", At(4, 3.5, 19.5), At(20, 7.0, 23)),
                ("chandeliers-flames-close", At(4.0, 5.2, 20.6), At(20, 7.1, 22.5)),
                ("chandeliers-dyes", At(4, 4.5, 24.5), At(20, 7.0, 26.5)),
                ("chandelier-mixed", At(38.5, 6.9, 28.3), At(38.5, 7.2, 30.5)),
                ("chandelier-tallow-mixed", At(42.5, 6.9, 28.3), At(42.5, 7.2, 30.5)),
                ("lanterns-flames", At(4, 1.9, 32.6), At(18, 1.3, 34.5)),
                ("lanterns-glass", At(7.5, 1.9, 35.6), At(16, 1.3, 37.5)),
                ("lanterns-glass-close", At(13.6, 1.6, 35.9), At(19, 1.35, 37.5)),
                ("lanterns-dyes", At(4, 1.9, 38.6), At(18, 1.3, 40.5)),
                ("lanterns-metals", At(4, 3.0, 41.0), At(20, 3.5, 43.5)),
                ("oil-burners", At(4, 1.9, 44.6), At(20, 1.3, 46.5)),
            };
            double eyeHeight = Player.Me.Entity.LocalEyePos.Y;
            foreach (var (name, eye, at) in stops)
            {
                await Player.Teleport(eye.AddCopy(0, -eyeHeight, 0));
                await Interact.LookAt(at);
                await Frames.Wait(120);
                Log("shot: " + await Shot.Take($"results/showcase-{name}.png"));
            }
            await Input.Hotkey("togglehud");
        }

        /// <summary>A point in the plot, from its corner.</summary>
        static Vec3d At(double x, double y, double z) => P(0, 0, 0).ToVec3d().Add(x, y, z);

        /// <summary>Candle i of n: the flames in turn, and the dyes in turn.</summary>
        static CandleLook[] Mixed(int n) =>
            Enumerable.Range(0, n).Select(i => new CandleLook(Flames[i % Flames.Length], Dyes[i % Dyes.Length])).ToArray();

        static void Hall()
        {
            const string wall = "game:planks-oak-ud";
            World.Fill(P(0, 0, 0), P(Size - 1, 0, Size - 1), "game:rock-chalk");
            World.Fill(P(0, 1, 0), P(Size - 1, Ceiling, Size - 1), "game:air");
            World.Fill(P(0, Ceiling, 0), P(Size - 1, Ceiling, Size - 1), wall);
            World.Fill(P(0, 1, 0), P(Size - 1, Ceiling - 1, 0), wall);
            World.Fill(P(0, 1, Size - 1), P(Size - 1, Ceiling - 1, Size - 1), wall);
            World.Fill(P(0, 1, 0), P(0, Ceiling - 1, Size - 1), wall);
            World.Fill(P(Size - 1, 1, 0), P(Size - 1, Ceiling - 1, Size - 1), wall);
        }

        static void Sign(int z, string text)
        {
            var at = P(2, 1, z);
            World.SetBlock("game:sign-ground-north", at);
            // A ground sign faces by its block entity's angle, not the block's variant:
            // half a turn so the writing faces the door.
            var sign = World.BE<BlockEntitySign>(at);
            sign.MeshAngleRad = GameMath.PI;
            sign.SetText(text);
        }

        internal static async Task<BECandles> Bunch(string kind, BlockPos at, System.Collections.Generic.IEnumerable<CandleLook> looks)
        {
            CandleLook[] all = looks.ToArray();
            World.SetBlock(kind + "-" + all.Length, at);
            await Ticks(2);
            var be = World.BE<BECandles>(at);
            SetLooks(be, all);
            return be;
        }

        /// <summary>A chandelier of new candles of <paramref name="kind"/>, in <paramref name="looks"/>.</summary>
        internal static async Task Chandelier(BlockPos at, System.Collections.Generic.IEnumerable<CandleLook> looks, string kind = Beeswax)
        {
            CandleLook[] all = looks.ToArray();
            World.SetBlock("game:chandelier-candle" + all.Length, at);
            await Ticks(2);
            var be = World.BE<BECandles>(at);
            if (kind != Beeswax)
            {
                be.SetKind(kind);
                be.SetFuel(all.Length * be.FullHours, CandleLook.Plain);
            }
            SetLooks(be, all);
        }

        /// <summary>Full, three quarters, half, a quarter, spent, and snuffed: a bunch of four each.</summary>
        static async Task BurnStages(string kind, int x)
        {
            double[] left = { 1, 0.75, 0.5, 0.25, 0 };
            for (int i = 0; i < left.Length; i++)
            {
                var be = await Bunch(kind, P(x + 3 * i, 1, 18), Enumerable.Repeat(CandleLook.Plain, 4));
                be.SetFuel(left[i] * be.Quantity * be.FullHours, CandleLook.Plain);
            }
            var snuffed = await Bunch(kind, P(x + 3 * left.Length, 1, 18), Enumerable.Repeat(CandleLook.Plain, 4));
            snuffed.Snuff();
        }

        /// <summary>Every candle's look at once, through the block entity's own saved form.</summary>
        internal static void SetLooks(BECandles be, CandleLook[] looks)
        {
            var tree = new TreeAttribute();
            be.ToTreeAttributes(tree);
            tree.SetString("candela:flames", string.Join(",", looks.Select(l => l.Flame ?? "")));
            tree.SetString("candela:dyes", string.Join(",", looks.Select(l => l.Dye ?? "")));
            be.FromTreeAttributes(tree, Sapi.World);
            be.MarkDirty(true);
        }

        /// <summary>
        /// A copper lantern with an oil burner in: <paramref name="oil"/> null for an
        /// empty one, which is out; no litres for one run dry, which gutters.
        /// </summary>
        static async Task Burner(BlockPos at, string oil, double litres, bool wickLow)
        {
            const string code = "game:lantern-large-up";
            World.SetBlock(code, at);
            await Ticks(2);
            var be = World.BE<BlockEntity>(at);
            ((BELantern)be).DidPlace("copper", "plain", "quartz");

            ItemStack from = World.Stack(code, 1);
            LanternStack.WriteBurner(from, litres, oil, snuffed: oil == null, wickLow);
            foreach (var behavior in be.Behaviors) behavior.OnBlockPlaced(from);
            be.MarkDirty(true);
        }

        internal static async Task Lantern(BlockPos at, string variant, string metal, string glass, string kind, CandleLook look)
        {
            string code = "game:lantern-" + variant;
            World.SetBlock(code, at);
            await Ticks(2);
            var be = World.BE<BlockEntity>(at);
            ((BELantern)be).DidPlace(metal, "plain", glass);

            ItemStack from = World.Stack(code, 1);
            double hours = kind == Tallow ? CandelaConfig.Current.TallowBurnHours : CandelaConfig.Current.BeeswaxBurnHours;
            LanternStack.Write(from, hours, kind, snuffed: false, look);
            foreach (var behavior in be.Behaviors) behavior.OnBlockPlaced(from);
            be.MarkDirty(true);
        }
    }
}
