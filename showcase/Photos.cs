using System.Linq;
using System.Threading.Tasks;
using candela;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using static Candela.Showcase.Showcase;

namespace Candela.Showcase
{
    /// <summary>
    /// Not tests: small staged scenes for posting, each in a dark, low room of its own
    /// with the camera close at candle height. Pictures land in results/photo-*.png.
    /// Run with --client --filter Photos.
    /// </summary>
    public class Photos
    {
        const int Roof = 5;

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task RainbowArc()
        {
            await Room();
            (int x, int z)[] arc = { (-3, 1), (-2, 2), (-1, 3), (0, 3), (1, 3), (2, 2), (3, 1) };
            for (int i = 0; i < Flames.Length; i++)
                await Bunch(Beeswax, P(8 + arc[i].x, 1, 7 + arc[i].z), Enumerable.Repeat(new CandleLook(Flames[i], null), 9));
            await Shoot("rainbow-arc", At(8.5, 1.6, 6.3), At(8.5, 1.2, 9.3));
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task DyedWax()
        {
            await Room();
            for (int i = 0; i < Dyes.Length; i++)
                await Bunch(Beeswax, P(3 + 2 * (i % 6), 1, i < 6 ? 8 : 10), Flames.Select(f => new CandleLook(f, Dyes[i])));
            await Shoot("dyed-wax", At(8.5, 2.0, 6.0), At(8.5, 1.1, 9.6));
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task WaxAndFlame()
        {
            await Room();
            (string dye, string flame)[] pairs = { ("black", "red"), ("white", "blue"), ("purple", "violet"), ("green", "teal"), ("yellow", "yellow") };
            for (int i = 0; i < pairs.Length; i++)
                await Bunch(Beeswax, P(4 + 2 * i, 1, 8), Enumerable.Repeat(new CandleLook(pairs[i].flame, pairs[i].dye), 9));
            await Shoot("wax-and-flame", At(8.5, 1.9, 4.4), At(8.5, 1.2, 8.6));
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task Chandeliers()
        {
            await Room();
            await Chandelier(P(8, Roof - 1, 8), Enumerable.Range(0, 8).Select(i => new CandleLook(Flames[1 + i % 6], Dyes[1 + i % 11])));
            await Chandelier(P(5, Roof - 1, 11), Enumerable.Repeat(new CandleLook("teal", null), 8));
            await Chandelier(P(11, Roof - 1, 11), Enumerable.Repeat(new CandleLook("violet", null), 8));
            await Shoot("chandeliers", At(8.5, 3.9, 5.4), At(8.5, 4.4, 8.6));
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task LanternGlass()
        {
            await Room();
            (int x, int z)[] arc = { (-4, 1), (-3, 2), (-3, 3), (-1, 4), (0, 4), (1, 4), (3, 3), (3, 2), (4, 1) };
            for (int i = 0; i < Glasses.Length; i++)
                await Lantern(P(8 + arc[i].x, 1, 6 + arc[i].z), "large-up", Metals[i], Glasses[i], Beeswax, new CandleLook("blue", null));
            await Shoot("lantern-glass", At(8.5, 1.65, 6.0), At(8.5, 1.35, 9.3));
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task HangingLanterns()
        {
            await Room();
            for (int i = 0; i < 6; i++)
            {
                var look = new CandleLook(Flames[1 + i], null);
                // The first hanging one would sit right over the camera.
                if (i > 0) await Lantern(P(8, Roof - 1, 4 + 2 * i), "large-down", Metals[i], "quartz", Beeswax, look);
                await Lantern(P(5, 1, 4 + 2 * i), "small-up", Metals[6 + i], "quartz", Tallow, look);
                await Lantern(P(11, 1, 4 + 2 * i), "small-up", Metals[6 + i], "quartz", Tallow, look);
            }
            await Shoot("hanging-lanterns", At(6.3, 2.0, 2.4), At(9.2, 2.6, 12));
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task BurningDown()
        {
            await Room();
            double[] left = { 1, 0.75, 0.5, 0.25, 0 };
            for (int i = 0; i < left.Length; i++)
            {
                var be = await Bunch(Tallow, P(4 + 2 * i, 1, 8), Enumerable.Repeat(new CandleLook(null, "red"), 4));
                be.SetFuel(left[i] * be.Quantity * be.FullHours, new CandleLook(null, "red"));
            }
            var snuffed = await Bunch(Tallow, P(14, 1, 8), Enumerable.Repeat(new CandleLook(null, "red"), 4));
            snuffed.Snuff();
            await Shoot("burning-down", At(5.0, 1.7, 6.4), At(10.5, 1.15, 8.5));
        }

        // ----- helpers -----

        /// <summary>A dark, low room filling the plot: walnut floor, granite walls, ebony ceiling.</summary>
        static async Task Room()
        {
            await World.SetCalendarTo(500 * 24 + 22);
            World.Fill(P(0, 0, 0), P(15, 0, 15), "game:planks-walnut-ud");
            World.Fill(P(0, 1, 0), P(15, Roof - 1, 15), "game:air");
            World.Fill(P(0, Roof, 0), P(15, Roof, 15), "game:planks-ebony-ud");
            World.Fill(P(0, 1, 0), P(15, Roof - 1, 0), "game:stonebricks-granite");
            World.Fill(P(0, 1, 15), P(15, Roof - 1, 15), "game:stonebricks-granite");
            World.Fill(P(0, 1, 0), P(0, Roof - 1, 15), "game:stonebricks-granite");
            World.Fill(P(15, 1, 0), P(15, Roof - 1, 15), "game:stonebricks-granite");
            await Ticks(5);
        }

        static Vec3d At(double x, double y, double z) => P(0, 0, 0).ToVec3d().Add(x, y, z);

        static async Task Shoot(string name, Vec3d eye, Vec3d at)
        {
            Player.Me.WorldData.FreeMove = true;
            Player.Me.WorldData.NoClip = true;
            ((IServerPlayer)Player.Me).BroadcastPlayerData();
            await Ticks(20);

            await Player.Teleport(eye.AddCopy(0, -Player.Me.Entity.LocalEyePos.Y, 0));
            await Interact.LookAt(at);
            await Input.Hotkey("togglehud");
            await Frames.Wait(150);
            Log("shot: " + await Shot.Take($"results/photo-{name}.png"));
            await Input.Hotkey("togglehud");
        }
    }
}
