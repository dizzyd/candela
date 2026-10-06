using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>The player's hand, for the tests that click with one.</summary>
    static class Hands
    {
        /// <summary>
        /// Nothing in hand. Not Player.Hold with air, which leaves an air stack in the
        /// slot - not a free hand to anything that checks for one. Waits for the client's
        /// copy of the slot to catch up, since the click starts there.
        /// </summary>
        public static async Task EmptyHand()
        {
            var slot = Player.Me.InventoryManager.ActiveHotbarSlot;
            slot.Itemstack = null;
            slot.MarkDirty();
            await Ticks(4);
        }

        /// <summary>
        /// Shift-right-click on <paramref name="pos"/> - the middle of its
        /// <paramref name="face"/>, if given. Shift is let go whatever happens: left
        /// down, it would turn every later test's right-click into a shift-click.
        /// </summary>
        public static async Task ShiftUse(BlockPos pos, BlockFacing face = null)
        {
            await Input.KeyDown(GlKeys.ShiftLeft, shift: true);
            try
            {
                await Interact.UseBlock(pos, face);
            }
            finally
            {
                await Input.KeyUp(GlKeys.ShiftLeft);
            }
            await Ticks(4);
        }

        /// <summary>Whether <paramref name="code"/> is anywhere in the player's inventories.</summary>
        public static bool PlayerHas(string code) =>
            Player.Me.InventoryManager.Inventories.Values.Any(inv => inv.Any(s => s.Itemstack?.Collectible.Code.ToString() == code));
    }
}
