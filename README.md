# Candela

Candle-making beyond the cooking pot, and candles that burn down and need tending.

Vintage Story's only candle is beeswax and flax cooked in a pot, and once placed it
burns forever. Candela adds the candles people actually made and gives light a cost.

## Features

- **Dipping** - melt rendered fat into tallow in a cooking pot, keep it molten on a
  burning firepit, and build tapers up on a dipping rod one coat at a time
- **Burn-down** - vanilla's beeswax candles burn for 48 game hours each, shrinking
  in quarters as they go. A part-burned candle taken off a bunch comes back as a
  stub carrying the hours it has left (rounded down)
- **Snuffing** - shift-right-click a bunch with a free hand to put it out and save
  it; light it again with a lit torch or a firestarter. A lit candle can light a torch
- **Tallow candles** - dipped tallow candles place and burn like beeswax ones:
  paler, smokier, a little dimmer, and 24 hours a candle
- **Lanterns burn a candle** - a lantern starts with the candle it was crafted
  with (beeswax or tallow; there is a tallow version of every lantern recipe).
  Right-click it with a candle or stub to swap in a fresh one, and what was left of
  the old one comes back. Tallow soots the glass: two light levels less. Picking a
  lantern up keeps what is left of its candle. Shift-click snuffs it, a torch or
  firestarter relights it, and lanterns still hang from ceilings
- Candles and lanterns placed before Candela was installed carry on as vanilla
  until first touched (candles) or loaded (lanterns), when they start out new

Planned: clay candle molds, chandeliers.

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
