using System.Collections.Generic;
using Vintagestory.API.Common;

namespace candela;

/// <summary>
/// The colour a candle's wax was dyed: vanilla's liquid dyes, cooked in with the wax as
/// an optional ingredient of the melting recipes and stamped on the molten wax as
/// <see cref="Attr"/> (<see cref="WaxPotPatch"/>). It goes with the wax from there - the
/// rod's last coat, the mould's fill, the candles they make - and is lost when the wax
/// is melted again: stubs remelt plain, and can be dyed again in the same cook.
///
/// Unlike <see cref="FlameColours"/> it changes nothing about the light; only how the
/// candle looks.
/// </summary>
public static class WaxDyes
{
    public const string Attr = "candela:dye";

    /// <summary>Vanilla's dyes, as its <c>game:dye-*</c> items name them.</summary>
    public static readonly IReadOnlyList<string> All =
        ["red", "orange", "yellow", "green", "blue", "woad", "purple", "pink", "white", "gray", "black"];

    private static readonly HashSet<string> known = [.. All];

    /// <summary><paramref name="dye"/> if it is one of vanilla's dyes, otherwise null.</summary>
    public static string Get(string dye) => dye != null && known.Contains(dye) ? dye : null;

    /// <summary>The dye <paramref name="stack"/>'s wax carries, or null for undyed.</summary>
    public static string Of(ItemStack stack) => Get(stack?.Attributes.GetString(Attr));

    /// <summary>
    /// Stamps <paramref name="stack"/>, in place, as dyed <paramref name="dye"/> - null, or
    /// a dye not in the list, makes it undyed. Returns it.
    /// </summary>
    public static ItemStack Stamp(ItemStack stack, string dye)
    {
        if (stack == null) return null;
        if (Get(dye) != null) stack.Attributes.SetString(Attr, dye);
        else stack.Attributes.RemoveAttribute(Attr);
        return stack;
    }

    /// <summary>The dye a stack of vanilla's liquid dye is, or null if it is not one.</summary>
    public static string OfLiquid(ItemStack stack) =>
        stack?.Collectible?.Code is AssetLocation code && code.Domain == "game" && code.Path.StartsWith("dye-") ? Get(code.Path.Substring(4)) : null;
}
