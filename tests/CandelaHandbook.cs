using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// The handbook pages are there and their text resolves. A page whose lang key is
    /// mistyped shows the player the key itself, which is easy to miss in a build that
    /// otherwise works.
    /// </summary>
    public class CandelaHandbook
    {
        [VsTest]
        public void BothGuidePagesLoadWithTheirText()
        {
            foreach (var (file, code) in new[]
            {
                ("60-candela-dipping", "craftinginfo-candela-dipping"),
                ("61-candela-upkeep", "gamemechanicinfo-candela-upkeep"),
            })
            {
                IAsset asset = Sapi.Assets.TryGet(new AssetLocation("candela", "config/handbook/" + file + ".json"));
                Assert.NotNull(asset, file);
                Assert.True(asset.ToText().Contains(code), file + " does not declare page " + code);

                foreach (string part in new[] { "title", "text" })
                {
                    string key = "candela:" + code + "-" + part;
                    string text = Lang.Get(key);
                    Assert.True(text != key && text.Length > 0, key + " does not resolve");
                }
            }
        }
    }
}
