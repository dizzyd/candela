using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace candela;

/// <summary>
/// Rain and strong wind put out an open flame that has the sky over it: bunches and
/// chandeliers, not lanterns, whose glass is the point of them. Put out means snuffed
/// - the fuel is kept, and a torch or a firestarter lights it again.
/// </summary>
public static class Weather
{
    /// <summary>Precipitation, 0-1 as the weather system gives it, that puts a flame out.</summary>
    public const float RainPutsOut = 0.05f;

    /// <summary>Wind speed - vanilla's dust starts blowing at 0.5 - from which a flame may go out.</summary>
    public const double StrongWind = 0.7;

    /// <summary>
    /// The chance, each burn tick (four real seconds), that strong wind takes a flame:
    /// a lit candle in a gale lasts half a minute or so.
    /// </summary>
    public const double WindChancePerTick = 0.15;

    /// <summary>
    /// Whether the weather puts out a flame - the decision alone, from what the world
    /// reports, so it can be tested without weather. <paramref name="roll"/> is 0-1.
    /// </summary>
    public static bool PutsOut(bool exposed, float precipitation, double wind, double roll)
    {
        if (!exposed) return false;
        if (precipitation >= RainPutsOut) return true;
        return wind >= StrongWind && roll < WindChancePerTick;
    }

    /// <summary>Whether the weather puts out a flame at <paramref name="pos"/> now. Server side.</summary>
    public static bool PutsOutAt(ICoreAPI api, BlockPos pos)
    {
        if (!CandelaConfig.Current.WeatherPutsOut) return false;

        // At or above the rain map's top is open to the sky; under any roof, not.
        bool exposed = api.World.BlockAccessor.GetRainMapHeightAt(pos) <= pos.Y;
        if (!exposed) return false;

        var weather = api.ModLoader.GetModSystem<WeatherSystemBase>();
        float precipitation = weather?.GetPrecipitation(pos.ToVec3d().Add(0.5, 0.5, 0.5)) ?? 0;
        double wind = api.World.BlockAccessor.GetWindSpeedAt(pos).Length();
        return PutsOut(exposed, precipitation, wind, api.World.Rand.NextDouble());
    }
}
