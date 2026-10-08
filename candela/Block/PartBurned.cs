using System;

namespace candela;

/// <summary>
/// How much of a new light comes back when one is picked up: candles off a bunch,
/// chandelier or lantern, and torches. Rounded down to the quarter by each caller
/// after up to an hour's grace, so picking one up never makes more wax than that
/// grace, and a candle or a torch put down in the wrong place and picked straight back
/// up comes back as it went down rather than a quarter less.
/// </summary>
public static class PartBurned
{
    /// <summary>
    /// The fraction of a new light that <paramref name="hoursLeft"/> of
    /// <paramref name="fullHours"/> counts as, up to 1. The grace is an hour, but never
    /// more than a tenth of the burn time: a server that makes candles burn for an hour
    /// would otherwise see them never burn down when picked up.
    /// </summary>
    public static double Fraction(double hoursLeft, double fullHours) =>
        fullHours <= 0 ? 1 : Math.Min(1, (hoursLeft + Math.Min(1, fullHours / 10)) / fullHours);
}
