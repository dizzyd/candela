# Candela

Candle-making beyond the cooking pot, and candles that burn down and need tending.

Vintage Story's only candle is beeswax and flax cooked in a pot, and once placed it
burns forever. Candela adds the candles people actually made and gives light a cost.

## Features

- **Dipping** - melt rendered fat into tallow in a cooking pot, keep it molten on a
  burning firepit, and build tapers up on a dipping rod one coat at a time
- **Moulds** - shape a candle mould from clay and fire it, then fill it from the
  pot in one go: four candles from six portions of tallow, or twelve of molten
  beeswax. Let it set and knock them out; the mould wears, and cracks in the end
- **Melting down** - beeswax melts in a pot too, for moulds, and burned-down stubs
  melt back into their own wax, a portion a stub
- **Burn-down** - vanilla's beeswax candles burn for 432 game hours each (two months), shrinking
  in quarters as they go. A part-burned candle taken off a bunch comes back as a
  stub carrying the hours it has left (rounded down)
- **Snuffing** - shift-right-click a bunch with a free hand to put it out and save
  it; light it again with a lit torch or a firestarter. A lit candle can light a torch
- **Weather** - rain, and now and then a strong wind, puts out candles and
  chandeliers with the sky over them (snuffed, not spent). Lanterns are sheltered
- **Tallow candles** - dipped tallow candles place and burn like beeswax ones:
  paler, smokier, a little dimmer, and 216 hours a candle (one month)
- **Lanterns burn a candle** - a lantern starts with the candle it was crafted
  with (beeswax or tallow; there is a tallow version of every lantern recipe).
  Right-click it with a candle or stub to swap in a fresh one, and what was left of
  the old one comes back. Tallow soots the glass: two light levels less. Picking a
  lantern up keeps what is left of its candle. Shift-click snuffs it, a torch or
  firestarter relights it, and lanterns still hang from ceilings
- **Chandeliers burn too** - their candles are one pool like a bunch's, beeswax
  whole or stub as in vanilla, and can now be taken out again, which is how spent
  ones are cleared. One that falls lands with the candles it had
- Candles and lanterns placed before Candela was installed carry on as vanilla
  until first touched (candles) or loaded (lanterns), when they start out new

Oil lamps are left to [Immersive Lighting](https://mods.vintagestory.at/techyimmersivelighting),
and torches to vanilla.

## Configuration

`ModConfig/candela.json`. With [ConfigKit](https://github.com/dizzyd/configkit)
installed these are also in its in-game settings screen, and a server's values are
synced to its players; without it, each side reads its own file.

| setting | default | |
|---|---|---|
| `BeeswaxBurnHours` | `432` | game hours a new beeswax candle burns; stubs burn their share |
| `TallowBurnHours` | `216` | the same for tallow |
| `BurnoutMode` | `Dim` | `Dim` - a spent light gutters to a dim glow; `Dark` - it goes out; `None` - lights never burn down |
| `UnattendedMode` | `LoadedOnly` | `LoadedOnly` - burns only while its chunk is loaded; `CappedCatchUp` - catches up unloaded time, up to `CatchUpCapHours`; `Always` - catches up all of it |
| `CatchUpCapHours` | `24` | in-game hours |
| `WeatherPutsOut` | `true` | rain and strong wind put out candles and chandeliers open to the sky |

A changed burn time applies to candles placed and lanterns crafted after the
change; a candle already burning keeps the hours it was given.

## Building

```bash
export VINTAGE_STORY="$(ls -d ~/.cairn/games/1.22* | sort -V | tail -1)"
./build.sh
```

The zip lands in `Releases/`.
