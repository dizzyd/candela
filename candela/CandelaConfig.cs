using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
/// Player-editable settings, read from and written back to <c>ModConfig/candela.json</c>.
///
/// Loaded on both sides. The server is the authority on everything that burns; the
/// client needs the burn hours too, for the hours a candle's tooltip quotes. With
/// ConfigKit installed the server's values are synced to every client and there is
/// an in-game settings screen; without it each side reads its own file, which in
/// multiplayer can leave a client's tooltips quoting its own numbers.
///
/// Public fields with BCL attributes only, so that this is ConfigKit's whole schema
/// and nothing here needs ConfigKit to compile or run.
/// </summary>
public class CandelaConfig
{
    public const string FileName = "candela.json";

    /// <summary>Game hours one new beeswax candle burns for. Stubs burn their share of it.</summary>
    [Category("Burning")]
    [Description("Game hours one new beeswax candle burns for. Part-burned stubs burn their share of it.")]
    [Range(1, 10000)]
    public double BeeswaxBurnHours = DefaultBeeswaxBurnHours;

    /// <summary>Game hours one new tallow candle burns for. Stubs burn their share of it.</summary>
    [Category("Burning")]
    [Description("Game hours one new tallow candle burns for. Part-burned stubs burn their share of it.")]
    [Range(1, 10000)]
    public double TallowBurnHours = DefaultTallowBurnHours;

    [Category("Upkeep")]
    [Description("What a light that has burned down does: Dim gutters to a faint glow, Dark goes out, None never burns down at all.")]
    public BurnoutMode BurnoutMode = BurnoutMode.Dim;

    [Category("Upkeep")]
    [Description("How lights burn while no one is near: LoadedOnly burns only while the chunk is loaded, CappedCatchUp catches up unloaded time up to the cap, Always catches up all of it.")]
    public UnattendedMode UnattendedMode = UnattendedMode.LoadedOnly;

    /// <summary>
    /// The most in-game hours a light catches up on reload. Read only when
    /// <see cref="UnattendedMode"/> is <see cref="candela.UnattendedMode.CappedCatchUp"/>.
    /// </summary>
    [Category("Upkeep")]
    [Description("With CappedCatchUp, the most game hours a light burns through for the time its chunk was unloaded.")]
    [Range(0, 10000)]
    public double CatchUpCapHours = DefaultCatchUpCapHours;

    // Two months and one of a world with vanilla's nine-day months - in hours, so they
    // stay put on a world whose months are longer.
    private const double DefaultBeeswaxBurnHours = 432;
    private const double DefaultTallowBurnHours = 216;
    private const double DefaultCatchUpCapHours = 24;

    /// <summary>
    /// What the rest of the mod reads. Starts as the defaults, so anything running
    /// before <see cref="Load"/> gets those rather than a null. Loading assigns into
    /// it rather than replacing it - see <see cref="AssignFrom"/>. Settable so the
    /// in-game suite can swap in a config of its own.
    /// </summary>
    public static CandelaConfig Current { get; set; } = new CandelaConfig();

    /// <summary>
    /// Hours one new candle of <paramref name="wax"/> burns for, or null for a wax
    /// this config does not know.
    /// </summary>
    public double? HoursFor(string wax) => wax switch
    {
        "beeswax" => BeeswaxBurnHours,
        "tallow" => TallowBurnHours,
        _ => null,
    };

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
            Current.AssignFrom(new CandelaConfig());
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

        api.Logger.Notification("[candela] Beeswax {0}h, tallow {1}h, BurnoutMode {2}, UnattendedMode {3}, CatchUpCapHours {4}",
            config.BeeswaxBurnHours, config.TallowBurnHours, config.BurnoutMode, config.UnattendedMode, config.CatchUpCapHours);

        Current.AssignFrom(config);
    }

    /// <summary>
    /// Copies every setting into this object, which stays the one everything holds.
    ///
    /// Load runs once per side, and in singleplayer both sides share this assembly and
    /// so this static. Replacing it would leave ConfigKit, handed the object by
    /// whichever side registered first, editing one nobody reads any more.
    /// </summary>
    public void AssignFrom(CandelaConfig other)
    {
        foreach (var field in typeof(CandelaConfig).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            field.SetValue(this, field.GetValue(other));
        }
    }

    /// <summary>
    /// Brings hand-edited values back into range. ConfigKit enforces the [Range]s on
    /// its own screen; this is for the file edited directly, with or without it.
    /// </summary>
    private void Sanitise(ICoreAPI api)
    {
        BeeswaxBurnHours = Positive(api, nameof(BeeswaxBurnHours), BeeswaxBurnHours, DefaultBeeswaxBurnHours);
        TallowBurnHours = Positive(api, nameof(TallowBurnHours), TallowBurnHours, DefaultTallowBurnHours);

        // Zero is a legitimate cap here: CappedCatchUp with nothing to catch up.
        if (double.IsNaN(CatchUpCapHours) || double.IsInfinity(CatchUpCapHours) || CatchUpCapHours < 0)
        {
            api.Logger.Warning("[candela] CatchUpCapHours {0} is not usable, using the default {1}", CatchUpCapHours, DefaultCatchUpCapHours);
            CatchUpCapHours = DefaultCatchUpCapHours;
        }
    }

    // NaN and Infinity both survive a naive comparison; Newtonsoft reads 1e400 as Infinity.
    // A candle that burns for zero hours would be spent the moment it was placed.
    private static double Positive(ICoreAPI api, string name, double value, double fallback)
    {
        if (!double.IsNaN(value) && !double.IsInfinity(value) && value > 0) return value;

        api.Logger.Warning("[candela] {0} {1} is not a usable number of hours, using the default {2}", name, value, fallback);
        return fallback;
    }
}

/// <summary>
/// What kind of candle a collectible is, from its <c>candela</c> attributes:
/// <c>wax</c> names the kind, <c>fraction</c> how much of a new one is left (a stub;
/// 1 if absent), and <c>bunch</c> the bunch block it places as. Its hours come from
/// the config, so stubs keep in step with whatever a new candle is set to.
/// </summary>
public static class CandleWax
{
    public static bool IsCandle(CollectibleObject collectible) =>
        collectible?.Attributes?["candela"]["wax"].Exists == true && collectible.Attributes["candela"]["bunch"].Exists;

    /// <summary>Hours a collectible of this kind burns for, or null if it is not a candle Candela knows.</summary>
    public static double? HoursOf(CollectibleObject collectible)
    {
        if (!IsCandle(collectible)) return null;

        var attrs = collectible.Attributes["candela"];
        double? full = CandelaConfig.Current.HoursFor(attrs["wax"].AsString());
        return full * attrs["fraction"].AsDouble(1);
    }

    public static string BunchOf(CollectibleObject collectible) => collectible.Attributes["candela"]["bunch"].AsString();
}
