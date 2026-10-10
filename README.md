# Candela

Candle-making beyond the cooking pot, and candles that burn down and need tending.

Vintage Story's only candle is beeswax and flax cooked in a pot, and once placed it
burns forever. Candela adds the candles people actually made and gives light a cost.

## Features

- **Dipping** - melt rendered fat into tallow in a cooking pot, keep it molten on a
  burning firepit, and build tapers up on a dipping rod one coat at a time
- **Moulds** - shape a candle mould from clay, fire it, and set it on the ground.
  Lift the pot of molten wax off the fire and pour: four candles from six portions
  of tallow, or twelve of molten beeswax. A carried pot keeps its wax, and stays
  pourable for a few hours. Let it set and take them out with two flax fibres for
  the wicks - half what dipping takes. The mould cracks after ten uses
- **Melting down** - beeswax melts in a pot too, for moulds, and burned-down stubs
  melt back into their own wax, a portion a stub
- **Burn-down** - vanilla's beeswax candles burn for 432 game hours each (two months), shrinking
  in quarters as they go. A part-burned candle taken off a bunch comes back as a
  stub carrying the hours it has left (rounded down). One picked straight back up -
  within the hour, or a tenth of its burn time if that is shorter - comes back as it
  went down, so a misplaced candle costs nothing
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
- **Oil burners** - two copper plates and a flax twine make an oil burner, which
  takes a lantern's candle's place: right-click a lantern with one and the candle
  comes out, and a candle put back returns the burner with its oil. Fill it in the
  lantern from a bowl, jug or bucket of olive or linseed oil - half a litre, about four
  months' burning, two beeswax candles' worth - and top it up whenever you like.
  Linseed smokes like tallow, two light levels less. Ctrl-right-click with a free hand
  turns the wick down: half the light for twice the time. An empty container takes
  the oil back out. A lantern picked up keeps its burner, oil and wick setting
- **Chandeliers burn too** - their candles are one pool like a bunch's, whole or
  stub, and can now be taken out again, which is how spent ones are cleared. They
  take tallow as well as vanilla's beeswax, one or the other: an empty chandelier's
  first candle decides. Tallow is a level dimmer, as its bunches are. One that falls
  lands with the candles it had
- **Coloured flames** - four flax fibres and a mineral powder make four treated
  wicks: cinnabar burns red, sulfur yellow, borax green, verdigris teal, lapis lazuli
  blue and sylvite violet. Verdigris is new: grind a malachite nugget in a quern.
  Dip a rod or fill a mould with treated wicks and the candles burn that colour -
  the flames, the tips of the candles, and the light they cast. A bunch or a
  chandelier can mix colours, a flame of its own to each candle; the room takes the
  colour most of them burn, and a tie lights it plain. Lanterns burn their candle's
  colour unless the glass is coloured, which wins. The colour is in the wick, so
  stubs melt back into plain wax
- **Dyed wax** - cook any of vanilla's dyes in with the wax, a tenth of a litre a
  lump, and the candles poured or dipped from it take its colour, black and white
  included; a dipped candle is the colour of its last coat. Dyed wax and treated
  wicks go together - black candles with red flames. Stubs melt back undyed, and can
  be dyed again in the same cook
- **Torches keep their time** - vanilla's torches already burn down once placed, but
  picking one up gave back a new one. Now a lit torch comes back with what was left
  of it, rounded down to the quarter like a stub - under a quarter, nothing - and
  places again to burn only that. One picked straight back up, within the hour, comes
  back as it went down, as a candle does. Torches with as much left stack together. A torch
  holder gives back the torch it was given rather than a new one; it still does not
  burn it. A torch in hand or on the ground does not burn, as in vanilla
- Candles and lanterns placed before Candela was installed carry on as vanilla
  until first touched (candles) or loaded (lanterns), when they start out new

Placed oil lamps are left to [Immersive Lighting](https://mods.vintagestory.at/techyimmersivelighting),
whose lamp is its own block and burns on its own scale. Candela marks lamp oil with an
attribute of its own rather than the `combustibleProps` Immersive Lighting gives the
same oils, so the two install together. Vanilla's clay oil lamp is left as it is.

## Configuration

`ModConfig/candela.json`. With [ConfigKit](https://github.com/dizzyd/configkit)
installed these are also in its in-game settings screen, and a server's values are
synced to its players; without it, each side reads its own file.

| setting | default | |
|---|---|---|
| `BeeswaxBurnHours` | `432` | game hours a new beeswax candle burns; stubs burn their share |
| `TallowBurnHours` | `216` | the same for tallow |
| `OilBurnHoursPerLitre` | `1728` | game hours a litre of lamp oil burns in an oil burner, wick up; a burner holds half a litre, and turned down burns half as fast |
| `BurnoutMode` | `Dim` | `Dim` - a spent light gutters to a dim glow; `Dark` - it goes out; `None` - lights never burn down |
| `UnattendedMode` | `LoadedOnly` | `LoadedOnly` - burns only while its chunk is loaded; `CappedCatchUp` - catches up unloaded time, up to `CatchUpCapHours`; `Always` - catches up all of it |
| `CatchUpCapHours` | `24` | in-game hours |
| `WeatherPutsOut` | `true` | rain and strong wind put out candles and chandeliers open to the sky |
| `FlameLightSaturation` | `4` | how strongly a coloured flame colours the light, `0`-`7`: `4` matches vanilla's coloured lantern glass, `0` lights the room plain (the flame stays coloured) |
| `TorchesKeepTheirTime` | `true` | a lit torch picked up, or taken out of a holder, keeps what was left of it; `false` - every one comes back new, as in vanilla |

A changed burn time applies to candles placed and lanterns crafted after the
change; a candle already burning keeps the hours it was given. A changed
`FlameLightSaturation` reaches a light already burning the next time it changes -
lit, snuffed, or a candle added or taken.

## Building

```bash
export VINTAGE_STORY="$(ls -d ~/.cairn/games/1.22* | sort -V | tail -1)"
./build.sh
```

The zip lands in `Releases/`.
