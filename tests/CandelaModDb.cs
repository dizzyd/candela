using System.Threading.Tasks;
using candela;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Not assertions: the scenes the ModDB page's screenshots are taken from. A small
    /// plastered room at night lit only by candles - bunches of both waxes on tables,
    /// one part-burned, a chandelier, a lantern - shot from a few places with the HUD
    /// hidden. ModDB wants them 480x480: shot square and larger, and scaled down -
    ///
    ///   VSTK_SCREEN_WIDTH=1080 VSTK_SCREEN_HEIGHT=1080, and screenWidth/screenHeight
    ///   1080 in templates/clientsettings.json to match.
    /// </summary>
    [RequiresClient]
    public class CandelaModDb
    {
        // The room's inside runs 3..12 on x and z, floor at y 0, ceiling at y 5.
        const int Lo = 3, Hi = 12, Ceiling = 5;

        [VsTest(TimeoutMs = 240000)]
        public async Task CandlelitRoom()
        {
            await World.SetCalendarTo(500 * 24 + 22);   // well into the night
            await BuildRoom();
            await Furnish();

            var lantern = World.BE<BlockEntity>(P(11, 2, 4)).GetBehavior<BEBehaviorLanternFuel>();
            BECandles beeswax = World.BE<BECandles>(P(6, 2, 7)), tallow = World.BE<BECandles>(P(7, 2, 7)), chandelier = World.BE<BECandles>(P(7, Ceiling - 1, 7));

            // Every light out: whatever is still lit is not the candles.
            lantern.Snuff(); beeswax.Snuff(); tallow.Snuff(); chandelier.Snuff();
            await Ticks(60);
            await Shoot("moddb-dark", feet: new Vec3d(11.5, 1, 11.5), look: new Vec3d(6.5, 2.4, 7.0));

            // The candles, the lantern still out: it gives so much more light than a
            // candle that it floods the room evenly, and the candles' pools are the point.
            beeswax.TryIgnite(); tallow.TryIgnite(); chandelier.TryIgnite();
            await Ticks(60);
            await Shoot("moddb-room-wide", feet: new Vec3d(11.5, 1, 11.5), look: new Vec3d(6.5, 2.4, 7.0));
            await Shoot("moddb-room-table", feet: new Vec3d(8.4, 1, 9.0), look: new Vec3d(6.9, 2.3, 7.4));
            await Shoot("moddb-room-chandelier", feet: new Vec3d(8.8, 1, 8.6), look: new Vec3d(7.5, 4.0, 7.5));

            // An alternative: a mould just poured, setting and steaming, on a third table
            // at the end of the long one. The client takes it up for particles on its
            // rescan, every 20 seconds (SystemClientTickingBlocks), so the wait.
            World.SetBlock("game:table-normal", P(8, 1, 7));
            World.SetBlock("game:air", P(8, 2, 7));
            var mould = (ItemCandleMould)Sapi.World.GetItem(new AssetLocation("candela:candlemould-red-fired"));
            ItemSlot hand = Player.Me.InventoryManager.ActiveHotbarSlot;
            hand.Itemstack = mould.Filled(Sapi.World, World.Stack("candela:candlemould-red-fired", 1), "tallow");
            hand.MarkDirty();
            BlockPos o = P(0, 0, 0);
            await Player.Teleport(new Vec3d(o.X + 9.5, o.Y + 1, o.Z + 8.5));
            await Ticks(4);
            await Hands.ShiftUse(P(8, 1, 7), BlockFacing.UP);
            await Hands.EmptyHand();
            await Ticks(25 * 33);
            // Steam comes in wisps: a few frames, to pick the one that caught some.
            for (int i = 0; i < 3; i++) await Shoot($"moddb-room-mould-{i}", feet: new Vec3d(9.7, 1, 10.1), look: new Vec3d(7.8, 2.2, 7.3), frames: 30 + 30 * i);

            // The lantern alone.
            lantern.TryIgnite(); beeswax.Snuff(); tallow.Snuff(); chandelier.Snuff();
            await Ticks(60);
            await Shoot("moddb-room-lantern", feet: new Vec3d(9.2, 1, 6.4), look: new Vec3d(11.5, 2.4, 4.5));
        }

        static async Task BuildRoom()
        {
            int lo = Lo - 1, hi = Hi + 1;

            // Clear the space, then floor and ceiling of oak, walls of dark walnut - pale
            // plaster lit evenly and lost the pools of candlelight - with an oak post at
            // each corner.
            for (int x = lo; x <= hi; x++)
            for (int z = lo; z <= hi; z++)
            {
                World.SetBlock("game:planks-oak-ud", P(x, 0, z));
                World.SetBlock("game:planks-oak-ud", P(x, Ceiling, z));
                for (int y = 1; y < Ceiling; y++)
                {
                    bool wall = x == lo || x == hi || z == lo || z == hi;
                    bool corner = (x == lo || x == hi) && (z == lo || z == hi);
                    World.SetBlock(corner ? "game:log-placed-oak-ud" : wall ? "game:planks-walnut-ns" : "game:air", P(x, y, z));
                }
            }

            // A leaded window in the far wall, and a beam across under the ceiling.
            World.SetBlock("game:glasspane-leaded-oak-ew", P(7, 2, lo));
            World.SetBlock("game:glasspane-leaded-oak-ew", P(8, 2, lo));
            for (int x = Lo; x <= Hi; x++) World.SetBlock("game:log-placed-oak-we", P(x, Ceiling - 1, 5));
            await Ticks(5);
        }

        static async Task Furnish()
        {
            // The long table: beeswax, and tallow burned half down. Few candles to a holder:
            // a full bunch gives light 15, a full chandelier 24 - near daylight - and
            // in a room this size either lights every wall alike.
            World.SetBlock("game:table-normal", P(6, 1, 7));
            World.SetBlock("game:table-normal", P(7, 1, 7));
            World.SetBlock("game:bunchocandles-5", P(6, 2, 7));
            World.SetBlock("candela:tallowcandles-3", P(7, 2, 7));
            for (int x = 5; x <= 8; x++)
            for (int z = 6; z <= 8; z++)
                if (World.BlockCode(P(x, 1, z)) == "game:air") World.SetBlock("game:rug-blue-diamond-center", P(x, 1, z));

            // Overhead, a chandelier of three, over the table.
            World.SetBlock("game:chandelier-candle3", P(7, Ceiling - 1, 7));

            // A side table in the corner with a lantern burning tallow.
            World.SetBlock("game:table-normal", P(11, 1, 4));
            World.SetBlock("game:lantern-large-up", P(11, 2, 4));

            // Something to light: books and a barrel against the walls.
            World.SetBlock("game:bookshelf", P(Lo, 1, 9));
            World.SetBlock("game:bookshelf", P(Lo, 1, 10));
            World.SetBlock("game:barrel", P(Hi, 1, Lo + 3));
            await Ticks(5);

            World.BE<BECandles>(P(7, 2, 7))?.SetFuel(3 * 216 * 0.45);

            ItemStack lantern = World.Stack("game:lantern-large-up", 1);
            lantern.Attributes.SetString("material", "copper");
            lantern.Attributes.SetString("lining", "plain");
            lantern.Attributes.SetString("glass", "quartz");
            LanternStack.Write(lantern, 216, "candela:tallowcandles", snuffed: false);
            World.BE<BlockEntity>(P(11, 2, 4)).OnBlockPlaced(lantern);
            World.BE<BlockEntity>(P(11, 2, 4)).MarkDirty(true);
            await Ticks(10);
        }

        /// <summary>
        /// The player standing at <paramref name="feet"/> (plot coordinates; the floor's
        /// top is y 1) looking at <paramref name="look"/>, HUD hidden.
        /// </summary>
        static async Task Shoot(string name, Vec3d feet, Vec3d look, int frames = 90)
        {
            BlockPos o = P(0, 0, 0);
            await Player.Teleport(new Vec3d(o.X + feet.X, o.Y + feet.Y, o.Z + feet.Z));
            await Interact.LookAt(new Vec3d(o.X + look.X, o.Y + look.Y, o.Z + look.Z));
            await Input.Hotkey("togglehud");
            await Frames.Wait(frames);
            Log("shot: " + await Shot.Take($"results/{name}.png"));
            await Input.Hotkey("togglehud");
        }
    }
}
