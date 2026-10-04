# Candela

Candle-making beyond the cooking pot, and candles that burn down and need tending.

Vintage Story's only candle is beeswax and flax cooked in a pot, and once placed it
burns forever. Candela adds the candles people actually made and gives light a cost.

## Planned for 0.1

- **Tallow** from vanilla's rendered fat - the everyday, cheaper candle
- **Wicks** twisted from flax fibre
- **Dipping** - build tapers up a layer at a time from a vat of melted tallow or wax
- **Burn-down** - candles shorten in stages (full, ¾, ½, stub), and keep their
  remaining burn when picked up
- **Snuffing** - put a candle out to save it, relight it later
- **Lantern fuel** - lanterns burn a candle, and need a fresh one when it is spent

Later: clay candle molds, chandeliers.

Oil lamps are left to [Immersive Lighting](https://mods.vintagestory.at/techyimmersivelighting),
and torches to vanilla.

## Configuration

`ModConfig/candela.json`, server side:

| setting | default | options |
|---|---|---|
| `BurnoutMode` | `Dim` | `Dim` - a spent light gutters to a dim glow; `Dark` - it goes out; `None` - lights never burn down |
| `UnattendedMode` | `LoadedOnly` | `LoadedOnly` - burns only while its chunk is loaded; `CappedCatchUp` - catches up unloaded time, up to `CatchUpCapHours`; `Always` - catches up all of it |
| `CatchUpCapHours` | `24` | in-game hours |

## Building

```bash
export VINTAGE_STORY="$(ls -d ~/.cairn/games/1.22* | sort -V | tail -1)"
./build.sh
```

The zip lands in `Releases/`.
