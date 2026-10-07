using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace candela;

/// <summary>
/// The colour a candle burns, from a wick treated with a mineral salt - the
/// <c>candela:wick-*</c> items, whose <c>candela.flame</c> attribute names it. Candles,
/// stubs and dipping rods carry it on the stack as <see cref="Attr"/>; a stack without
/// it burns as any candle does.
///
/// It lives in the wick rather than the wax. Wax moves through pot slots, carried
/// pots and moulds, and would need a rule for every mix; a wick enters a candle in
/// exactly two places - the dipping rod and the mould - and burns away with it, so
/// stubs melted down give plain wax again.
/// </summary>
public static class FlameColours
{
    public const string Attr = "candela:flame";

    /// <summary>
    /// Light saturation for a coloured flame: vanilla's coloured lantern glass's, so a
    /// green candle and green glass light a room alike.
    /// </summary>
    public const byte LightSaturation = 4;

    /// <param name="LightHue">On the engine's 0-63 light scale; red, green, blue and violet are BELantern.setLightColor's glass hues.</param>
    /// <param name="ParticleHue">On the 0-255 scale particle colours use, matching the light's.</param>
    public record Colour(string Code, byte LightHue, byte ParticleHue);

    private static readonly Dictionary<string, Colour> byCode = new Colour[]
    {
        new("red", 0, 4),
        new("green", 20, 80),
        new("teal", 30, 120),
        new("blue", 42, 165),
        new("violet", 48, 195),
    }.ToDictionary(c => c.Code);

    public static Colour Get(string code) => code != null && byCode.TryGetValue(code, out Colour c) ? c : null;

    /// <summary>The flame <paramref name="stack"/> burns, or null for a plain one.</summary>
    public static string Of(ItemStack stack) => Get(stack?.Attributes.GetString(Attr))?.Code;

    /// <summary>
    /// Stamps <paramref name="stack"/>, in place, as burning <paramref name="flame"/> -
    /// null, or a colour there is no such, makes it plain. Returns it.
    /// </summary>
    public static ItemStack Stamp(ItemStack stack, string flame)
    {
        if (stack == null) return null;
        if (Get(flame) != null) stack.Attributes.SetString(Attr, flame);
        else stack.Attributes.RemoveAttribute(Attr);
        return stack;
    }

    /// <summary>The flame a treated wick gives, or null if <paramref name="collectible"/> is not one.</summary>
    public static string OfWick(CollectibleObject collectible) => Get(collectible?.Attributes?["candela"]["flame"].AsString())?.Code;

    public static bool IsTreatedWick(CollectibleObject collectible) => OfWick(collectible) != null;

    /// <summary>The flame among crafting inputs - a treated wick, or something already carrying one.</summary>
    public static string FromInputs(ItemSlot[] slots) =>
        slots.Select(s => Of(s.Itemstack) ?? OfWick(s.Itemstack?.Collectible)).FirstOrDefault(f => f != null);

    /// <summary>"Tallow candle (green flame)", from the name without.</summary>
    public static string Name(ItemStack stack, string name) =>
        Of(stack) is string flame ? Lang.Get("candela:with-flame", name, Lang.Get("candela:flame-" + flame)) : name;

    /// <summary>
    /// The flame a group of candles lights a room with: the commonest, plain ones
    /// counted as a colour of their own, and plain on a tie - one light cannot be two
    /// colours, and a red and a blue together should not pick one at random.
    /// </summary>
    public static string Prevailing(IEnumerable<string> flames)
    {
        var counts = flames.GroupBy(f => f ?? "").Select(g => (flame: g.Key, n: g.Count())).OrderByDescending(c => c.n).ToList();
        if (counts.Count == 0 || (counts.Count > 1 && counts[0].n == counts[1].n)) return null;
        return Get(counts[0].flame)?.Code;
    }

    /// <summary>
    /// <paramref name="light"/> in <paramref name="flame"/>'s colour, its brightness kept,
    /// as a new array - or <paramref name="light"/> itself, when it is plain or dark.
    /// That is often a block's own LightHsv, so never change what comes back.
    /// </summary>
    public static byte[] Tint(byte[] light, string flame)
    {
        if (Get(flame) is not Colour colour || light[2] == 0) return light;
        return [colour.LightHue, LightSaturation, light[2]];
    }
}
