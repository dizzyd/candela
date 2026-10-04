using Vintagestory.API.Common;

namespace candela;

public class CandelaModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterItemClass("CandelaMoltenTallow", typeof(ItemMoltenTallow));
        api.RegisterItemClass("CandelaDippingRod", typeof(ItemDippingRod));
        api.RegisterBlockBehaviorClass("CandelaDipVat", typeof(BlockBehaviorDipVat));

        // Burn-down is decided on the server; the client only ever sees the
        // resulting block variant, so it has no use for the config.
        if (api.Side != EnumAppSide.Server) return;

        CandelaConfig.Load(api);
    }
}
