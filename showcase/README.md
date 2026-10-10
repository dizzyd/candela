# Showcase hall

`Showcase.cs` is not a test. It builds a closed, dark 48x48 hall showing every
flame, dye, wax, oil, burn stage and holder Candela has. Each row has a sign at its
west end. From the door at the south wall, the rows run:

| row (z) | what |
|---|---|
| 3, 6 | beeswax, then tallow: a bunch of three for each flame (plain, red, yellow, green, teal, blue, violet) |
| 9, 12 | beeswax, then tallow: a bunch of seven for each dye (undyed + vanilla's 11), one candle per flame |
| 15 | bunches of 1 to 9, flames and dyes mixed |
| 18 | burning down: full, ¾, ½, ¼, spent, snuffed. Beeswax, then tallow |
| 22 overhead | chandeliers, a beeswax and a tallow one for each flame |
| 26 overhead | chandeliers, one for each dye |
| 30 overhead | chandeliers holding 0 to 8 candles, mixed, then eight mixed tallow ones |
| 34 | lanterns, one for each flame: beeswax, then tallow (sooty, dimmer) |
| 37 | every glass over a blue flame. Quartz and plain show blue; coloured glass wins |
| 40 | lanterns, one for each dye |
| 43 | each metal: a small lantern on the floor and a large one hanging overhead |
| 46 | oil burners: olive, then linseed (sooty) - full, wick turned down, run dry - then an empty burner, and a beeswax and a tallow candle to compare |

## Making the world

Build it on the test box, headless, in a slot of its own; keep the session, save,
and bring the save back into the pack. `--filter BuildTheHall` and not `Showcase`,
which matches the photos too.

```bash
# on the Mac
cd ~/src/anego-1.22/vstestkit
bash scripts/sync-linux.sh dizzyd@vsclient.home --mod ../candela/candela --slot candela-showcase

# on the box, in ~/vstestkit-candela-showcase
bash scripts/run.sh mods/candela/showcase --mod mods/candela/candela --slot candela-showcase --keep --filter BuildTheHall
VSTK_SLOT=candela-showcase bash scripts/vstk cmd "/autosavenow"
VSTK_SLOT=candela-showcase bash scripts/stop.sh

# on the Mac, with the pack's game closed
scp dizzyd@vsclient.home:vstestkit-candela-showcase/run/candela-showcase/data/Saves/vstestkit.vcdbs \
    ~/.cairn/packs/candela/data/Saves/"Candela showcase.vcdbs"
```

Install the same build into the pack (`scripts/install-to-pack.sh`), or the world
opens against a Candela that does not match it.

The world spawns players just inside the door. It opens without vstestkit
installed: its `vstestkit-flat` playstyle is only a label once the world exists.

With `--client`, the run also takes a picture of each section and saves them to
`results/showcase-*.png`.

The candles burn as they would anywhere else (`LoadedOnly` by default), so a
world played long enough runs out. Make a new one with the steps above.

## Pictures for posting

`Photos.cs` builds small staged scenes instead: a dark, low room each (walnut floor,
granite walls), one subject, and the camera close at candle height. They need a
client, and look best at a real resolution with particles on. The default
vstestkit client template is 960x600 with particles off, so on the test box raise
`screenWidth`/`screenHeight`, `viewDistance`, `ssaa` and `renderParticles` in that
slot's `templates/clientsettings.json` first:

```bash
bash scripts/run.sh mods/candela/showcase --mod mods/candela/candela --client --filter Photos
# -> results/photo-*.png
```
