using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Vintagestory.API.Common;

namespace candela;

/// <summary>
/// What happens to a light that has burned all the way down.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum BurnoutMode
{
    /// <summary>Gutters to a dim glow rather than going out. Upkeep buys brightness, not light itself.</summary>
    Dim,

    /// <summary>Goes fully dark.</summary>
    Dark,

    /// <summary>Never burns down at all - candles and lanterns behave as in vanilla.</summary>
    None,
}

/// <summary>
/// How a light burns while no player is around to see it.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum UnattendedMode
{
    /// <summary>
    /// Burns only while its chunk is loaded, and catches up nothing on reload, so a
    /// base left alone - or a server player who has not logged in for a week - comes
    /// back to the lights as they were left.
    /// </summary>
    LoadedOnly,

    /// <summary>Catches up the time it was unloaded on reload, but no more than <see cref="CandelaConfig.CatchUpCapHours"/>.</summary>
    CappedCatchUp,

    /// <summary>Catches up all of it, as vanilla torches do.</summary>
    Always,
}

/// <summary>
/// Player-editable settings, read from and written back to
/// <c>ModConfig/candela.json</c> in the game's data directory.
///
/// Server-side only: burn-down runs on the server, and the client sees only the
/// block variant that results.
/// </summary>
public class CandelaConfig
{
    public const string FileName = "candela.json";

    public BurnoutMode BurnoutMode { get; set; } = BurnoutMode.Dim;

    public UnattendedMode UnattendedMode { get; set; } = UnattendedMode.LoadedOnly;

    /// <summary>
    /// The most in-game hours a light catches up on reload. Read only when
    /// <see cref="UnattendedMode"/> is <see cref="candela.UnattendedMode.CappedCatchUp"/>.
    /// </summary>
    public float CatchUpCapHours { get; set; } = DefaultCatchUpCapHours;

    private const float DefaultCatchUpCapHours = 24f;

    /// <summary>
    /// What the rest of the mod reads. Starts as the defaults, so anything running
    /// before <see cref="Load"/> - or on the client, which never loads it - gets those
    /// rather than a null.
    /// </summary>
    public static CandelaConfig Current { get; set; } = new CandelaConfig();

    public static void Load(ICoreAPI api)
    {
        CandelaConfig config;

        try
        {
            config = api.LoadModConfig<CandelaConfig>(FileName);
        }
        catch (Exception e)
        {
            // Malformed JSON throws rather than returning null. Fall back to defaults,
            // and do not write over what the player was trying to edit.
            api.Logger.Error("[candela] Could not read ModConfig/{0}, using defaults: {1}", FileName, e.Message);
            Current = new CandelaConfig();
            return;
        }

        bool isNew = config == null;
        config ??= new CandelaConfig();
        config.Sanitise(api);

        // Written back every load: creates the file on first run, adds keys a new
        // version introduces, and records what is actually in effect after clamping.
        try
        {
            api.StoreModConfig(config, FileName);
        }
        catch (Exception e)
        {
            api.Logger.Warning("[candela] Could not write ModConfig/{0}: {1}", FileName, e.Message);
        }

        if (isNew) api.Logger.Notification("[candela] Wrote default config to ModConfig/{0}", FileName);

        api.Logger.Notification("[candela] BurnoutMode {0}, UnattendedMode {1}, CatchUpCapHours {2}",
            config.BurnoutMode, config.UnattendedMode, config.CatchUpCapHours);

        Current = config;
    }

    private void Sanitise(ICoreAPI api)
    {
        // NaN and Infinity both survive a naive comparison; Newtonsoft reads 1e100 as Infinity.
        if (float.IsNaN(CatchUpCapHours) || float.IsInfinity(CatchUpCapHours) || CatchUpCapHours < 0f)
        {
            api.Logger.Warning("[candela] CatchUpCapHours {0} is not usable, using the default {1}",
                CatchUpCapHours, DefaultCatchUpCapHours);
            CatchUpCapHours = DefaultCatchUpCapHours;
        }
    }
}
