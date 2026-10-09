using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Every page the handbook would show for Candela's items, and for the vanilla ones
    /// it patches, composes without throwing - built as the handbook builds them, against
    /// the same list of every stack.
    /// </summary>
    public class CandelaHandbookPages
    {
        private static readonly string[] Patched = { "game:candle", "game:bunchocandles-*", "game:chandelier-*", "game:lantern-*", "game:torch-*", "game:torchholder-*", "game:oilportion-*" };

        [VsTest(TimeoutMs = 600000)]
        [RequiresClient]
        public async Task EveryCandelaPageComposes()
        {
            await OnClient();
            ICoreClientAPI capi = Capi;

            var all = new List<ItemStack>();
            foreach (CollectibleObject obj in capi.World.Collectibles)
            {
                if (!obj.HasBehavior<CollectibleBehaviorHandbookTextAndExtraInfo>())
                {
                    var bh = new CollectibleBehaviorHandbookTextAndExtraInfo(obj);
                    bh.OnLoaded(capi);
                    obj.CollectibleBehaviors = obj.CollectibleBehaviors.Append(bh);
                }
                List<ItemStack> stacks = obj.GetHandBookStacks(capi);
                if (stacks != null) all.AddRange(stacks);
            }
            ItemStack[] allStacks = all.ToArray();

            // A drop the handbook cannot resolve breaks every page, not just its own.
            var unresolved = new List<string>();
            foreach (ItemStack stack in allStacks)
            {
                BlockDropItemStack[] drops = stack.Block?.GetDropsForHandbook(stack, capi.World.Player);
                if (drops == null) continue;
                foreach (BlockDropItemStack drop in drops)
                {
                    if (drop?.ResolvedItemstack == null) unresolved.Add(stack.Collectible.Code + " drops " + drop?.Code);
                }
            }
            Log("unresolved drops: " + unresolved.Count + "\n  " + string.Join("\n  ", unresolved.Take(40)));

            var failures = new List<string>();
            int pages = 0;
            foreach (ItemStack stack in allStacks)
            {
                AssetLocation code = stack.Collectible.Code;
                if (code.Domain != "candela" && !Patched.Any(p => WildcardUtil.Match(new AssetLocation(p), code))) continue;
                pages++;
                try
                {
                    stack.Collectible.GetBehavior<CollectibleBehaviorHandbookTextAndExtraInfo>()
                        .GetHandbookInfo(new DummySlot(stack), capi, allStacks, _ => true);
                }
                catch (System.Exception e)
                {
                    failures.Add(code + ": " + e);
                }
            }
            Log(pages + " pages, " + failures.Count + " failed\n" + string.Join("\n\n", failures.Take(5)));

            Assert.Equal(0, unresolved.Count, string.Join("; ", unresolved.Take(10)));
            Assert.Equal(0, failures.Count, string.Join("\n\n", failures.Take(3)));
        }
    }
}
