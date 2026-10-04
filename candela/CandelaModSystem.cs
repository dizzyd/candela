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

        api.RegisterBlockClass("CandelaCandles", typeof(BlockCandelaCandles));
        api.RegisterBlockEntityClass("CandelaCandles", typeof(BECandles));
        api.RegisterItemClass("CandelaCandle", typeof(ItemCandelaCandle));
        api.RegisterItemClass("CandelaCandleStub", typeof(ItemCandleStub));

        // Burn-down is decided on the server. The client gets the burnout mode with
        // each bunch's state, so it has no use for the config file.
        if (api.Side != EnumAppSide.Server) return;

        CandelaConfig.Load(api);
    }
}
