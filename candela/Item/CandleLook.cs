using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace candela;

/// <summary>
/// How a candle looks: the colour its wick burns (<see cref="FlameColours"/>) and the
/// colour its wax was dyed (<see cref="WaxDyes"/>), each null for plain. Carried
/// together, from a stack into a bunch, a chandelier or a lantern and back out, so the
/// two cannot come apart on the way.
/// </summary>
public readonly record struct CandleLook(string Flame, string Dye)
{
    public static readonly CandleLook Plain = default;

    public bool IsPlain => Flame == null && Dye == null;

    /// <summary>How the candle <paramref name="stack"/> looks.</summary>
    public static CandleLook Of(ItemStack stack) => new(FlameColours.Of(stack), WaxDyes.Of(stack));

    /// <summary>Stamps <paramref name="stack"/>, in place, with this look - plain clears it. Returns it.</summary>
    public ItemStack Stamp(ItemStack stack) => WaxDyes.Stamp(FlameColours.Stamp(stack, Flame), Dye);

    /// <summary>
    /// The look among crafting inputs: a flame from a treated wick or a stack already
    /// carrying one, a dye from a stack carrying one - a rod's last coat.
    /// </summary>
    public static CandleLook FromInputs(ItemSlot[] slots) => new(
        FlameColours.FromInputs(slots),
        slots.Select(s => WaxDyes.Of(s.Itemstack)).FirstOrDefault(d => d != null));

    /// <summary>"Tallow candle (black, red flame)", from the name without.</summary>
    public static string Name(ItemStack stack, string name)
    {
        CandleLook look = Of(stack);
        var parts = new List<string>();
        if (look.Dye != null) parts.Add(Lang.Get("candela:dye-" + look.Dye));
        if (look.Flame != null) parts.Add(Lang.Get("candela:flame-" + look.Flame));
        return parts.Count == 0 ? name : Lang.Get("candela:with-look", name, CandleInfo.List(parts));
    }
}
