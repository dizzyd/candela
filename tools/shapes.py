#!/usr/bin/env python3
"""
Generates Candela's item shapes, which are families of near-identical boxes
better described by numbers than drawn by hand:

  shapes/item/dippingrod-{0..6}.json   a stick with four wicks, thickening a coat at a time
  shapes/item/candlestub-{75,50,25}.json   vanilla's candle3, cut down
  shapes/item/candlemould-{empty,filled}.json   a clay block with four wells, and
                                                with wax standing in them

Run from the repo root after changing a proportion here:

  python3 tools/shapes.py

Units are sixteenths of a block, as in every VS shape.
"""
import json, os

OUT = os.path.join(os.path.dirname(__file__), '..', 'candela', 'assets', 'candela', 'shapes', 'item')

def box(name, frm, to, tex, uv_origin=(0, 0), glow=None, children=None):
    """A box whose faces each take a texture area the size of that face."""
    w, h, d = (to[i] - frm[i] for i in range(3))
    u, v = uv_origin
    def face(fw, fh):
        f = {"texture": "#" + tex, "uv": [u, v, round(u + fw, 3), round(v + fh, 3)]}
        if glow: f["glow"] = glow
        return f
    el = {
        "name": name,
        "from": [round(c, 3) for c in frm],
        "to": [round(c, 3) for c in to],
        "faces": {
            "north": face(w, h), "south": face(w, h),
            "east": face(d, h), "west": face(d, h),
            "up": face(w, d), "down": face(w, d),
        },
    }
    if children: el["children"] = children
    return el

def shape(textures, elements):
    return {"textureWidth": 16, "textureHeight": 16, "textures": textures, "elements": elements}

def write(name, data):
    with open(os.path.join(OUT, name + '.json'), 'w') as f:
        json.dump(data, f, indent='\t')
        f.write('\n')

# ----- dipping rod -----

ROD_Y = 13          # underside of the rod; the tapers hang from here
WICKS_X = [3, 6.5, 10, 13.5]
Z = 8               # everything is centred on the rod's line

def taper(i, cx, coats):
    """One wick and whatever wax it has gathered.

    A dipped taper hangs from its wick, so the end at the rod is the candle's top.
    Each dip runs down before it sets, so the wax is thinnest at the rod and widest
    towards the bottom, finishing in a rounded drip rather than a flat foot - the
    foot is only made later, when the drip is trimmed off.
    """
    if coats == 0:
        # A bare wick, a little longer than a finished taper so it reads as cord.
        return [box(f"wick{i}", (cx - 0.15, ROD_Y - 9, Z - 0.15), (cx + 0.15, ROD_Y, Z + 0.15), "wick")]

    widest = 0.5 + coats * 0.25         # 0.75 .. 2.0: a finished taper is vanilla's 2px candle
    length = 6 + coats * 0.5            # 6.5 .. 9
    top = ROD_Y - 0.4                   # only a sliver of wick between rod and wax
    parts = [box(f"wick{i}", (cx - 0.15, top, Z - 0.15), (cx + 0.15, ROD_Y, Z + 0.15), "wick")]

    # Stacked segments, narrow at the top and widening down; the first coat is
    # still a straight thread of wax.
    segments = 1 if coats == 1 else 4
    narrowest = widest if coats == 1 else widest * 0.7
    body = length - (0 if coats == 1 else 0.6)
    y = top
    for k in range(segments):
        w = narrowest + (widest - narrowest) * (k / max(1, segments - 1))
        h = body / segments
        # One continuous strip of texture down the taper, so the steps between
        # segments read as a taper rather than as bands.
        parts.append(box(f"wax{i}-{k}", (cx - w / 2, y - h, Z - w / 2), (cx + w / 2, y, Z + w / 2), "wax", uv_origin=(1 + 2 * i, 2 + (top - y))))
        y -= h

    if coats >= 2:
        # The drip: what runs off the bottom of each coat and sets there.
        tip = widest * 0.55
        parts.append(box(f"drip{i}", (cx - tip / 2, y - 0.6, Z - tip / 2), (cx + tip / 2, y, Z + tip / 2), "wax", uv_origin=(1 + 2 * i, 2 + (top - y))))
    return parts

for coats in range(7):
    elements = [box("rod", (0, ROD_Y, Z - 0.5), (16, ROD_Y + 1, Z + 0.5), "oak", uv_origin=(0, 3.5))]
    for i, cx in enumerate(WICKS_X):
        elements += taper(i, cx, coats)
    write(f"dippingrod-{coats}", shape(
        {"oak": "game:block/wood/debarked/oak", "wick": "game:block/linen", "wax": "candela:block/candle-tallow"},
        elements))

# ----- candle stubs -----

for left in (75, 50, 25):
    height = 4 * left / 100             # vanilla's candle3 is 4px of wax
    wick = box("top", (0.8, height, 0.8), (1.2, height + 0.5, 1.2), "candle", glow=196)
    body = box("candle", (7, 0, 7), (9, height, 9), "candle", uv_origin=(0, 16 - height), children=[wick])
    # The wick is a child, so its coordinates are relative to the body's corner.
    write(f"candlestub-{left}", shape({"candle": "game:block/candle"}, [body]))

print("wrote", sorted(os.listdir(OUT)))

# ----- candle mould -----
#
# An 8x8 clay block with four 2x2 wells, the footprint of its clayforming pattern
# (recipes/clayforming/candlemould.json) drawn taller: the wells are candle-length.

M0, M1 = 4, 12      # footprint, both axes
MH = 8              # height
WELLS = [(5, 7), (9, 11)]   # well spans, both axes

def mould(filled):
    els = [box("floor", [M0, 0, M0], [M1, 1, M1], "clay")]
    # Walls: three full-depth strips across x, and between them short strips that
    # leave the wells open.
    for i, (x0, x1) in enumerate([(4, 5), (7, 9), (11, 12)]):
        els.append(box(f"wallx{i}", [x0, 1, M0], [x1, MH, M1], "clay"))
    for wx0, wx1 in WELLS:
        for j, (z0, z1) in enumerate([(4, 5), (7, 9), (11, 12)]):
            els.append(box(f"wallz{wx0}{j}", [wx0, 1, z0], [wx1, MH, z1], "clay"))
    if filled:
        for wx0, wx1 in WELLS:
            for wz0, wz1 in WELLS:
                els.append(box(f"wax{wx0}{wz0}", [wx0, 1, wz0], [wx1, MH - 0.5, wz1], "wax"))
    return els

write("candlemould-empty", shape({"clay": "game:block/clay/hardened/blue"}, mould(False)))
write("candlemould-filled", shape({"clay": "game:block/clay/hardened/blue", "wax": "game:block/candle"}, mould(True)))

