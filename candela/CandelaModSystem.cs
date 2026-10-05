using System;
using Vintagestory.API.Common;

namespace candela;

public class CandelaModSystem : ModSystem
{
    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);

        // Both sides: the server burns by it, and the client quotes its burn hours.
        CandelaConfig.Load(api);
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterItemClass("CandelaMoltenWax", typeof(ItemMoltenWax));
        api.RegisterItemClass("CandelaDippingRod", typeof(ItemDippingRod));
        api.RegisterItemClass("CandelaCandleMould", typeof(ItemCandleMould));
        api.RegisterBlockBehaviorClass("CandelaDipVat", typeof(BlockBehaviorDipVat));

        api.RegisterBlockClass("CandelaCandles", typeof(BlockCandelaCandles));
        api.RegisterBlockEntityClass("CandelaCandles", typeof(BECandles));
        api.RegisterItemClass("CandelaCandle", typeof(ItemCandelaCandle));
        api.RegisterItemClass("CandelaPlaceableCandle", typeof(ItemPlaceableCandle));

        api.RegisterBlockClass("CandelaChandelier", typeof(BlockCandelaChandelier));
        api.RegisterBlockClass("CandelaLantern", typeof(BlockCandelaLantern));
        api.RegisterBlockEntityBehaviorClass("CandelaLanternFuel", typeof(BEBehaviorLanternFuel));

        RegisterWithConfigKit(api);
        WaxPotPatch.Install(api);
    }

    public override void Dispose()
    {
        WaxPotPatch.Uninstall();
        base.Dispose();
    }

    private const string ConfigKitSystem = "ConfigKit.ConfigKitModSystem";
    private const string ConfigKitRegister = "RegisterManagedConfig";

    /// <summary>Whether ConfigKit is installed and took the config. Asserted in a test.</summary>
    public static bool ConfigKitBound { get; private set; }

    /// <summary>
    /// Hands <see cref="CandelaConfig.Current"/> to ConfigKit if it is installed: an
    /// in-game settings screen, and the server's values synced to every client - which
    /// this mod does not do on its own, and which keeps a client's tooltips quoting the
    /// server's burn hours rather than its own file's.
    ///
    /// Bound by reflection, as fornax and crucibulum do, so ConfigKit is optional to
    /// build against as well as to run with. The surface is one method, and it reflects
    /// over the config object - the BCL attributes on CandelaConfig are the schema.
    /// </summary>
    private void RegisterWithConfigKit(ICoreAPI api)
    {
        var system = api.ModLoader.GetModSystem(ConfigKitSystem);
        if (system == null) return;   // not installed, which is the ordinary case

        var register = system.GetType().GetMethod(ConfigKitRegister);
        if (register == null)
        {
            api.Logger.Warning("[candela] ConfigKit is installed but has no {0} - Candela's settings "
                + "will not appear in its screen and will not sync from the server.", ConfigKitRegister);
            return;
        }

        try
        {
            register.Invoke(system, new object[]
            {
                "candela",                  // domain
                CandelaConfig.Current,      // the object it reflects over, and assigns into
                CandelaConfig.FileName,     // the file this mod already writes, so there is one source
                null,                       // onSyncedFromServer - everything is read live
                null,                       // onSettingChanged
                null,                       // onConfigSaved
            });

            ConfigKitBound = true;
        }
        catch (Exception e)
        {
            // Reflection wraps whatever went wrong inside the call in a TargetInvocationException
            // whose own message says nothing, so unwrap it or this is unactionable.
            api.Logger.Warning("[candela] could not hand the config to ConfigKit: {0}",
                (e as System.Reflection.TargetInvocationException)?.InnerException ?? e);
        }
    }
}
