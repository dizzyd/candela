#!/usr/bin/env python3
"""
Generates Candela's item shapes, which are families of near-identical boxes
better described by numbers than drawn by hand:

  shapes/item/dippingrod-{0..6}.json   a stick with four wicks, thickening a coat at a time
  shapes/item/candlestub-{75,50,25}.json   vanilla's candle3, cut down

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
    """One wick and whatever wax it has gathered."""
    if coats == 0:
        # A bare wick, a little longer than a finished taper so it reads as cord.
        return [box(f"wick{i}", (cx - 0.15, ROD_Y - 9, Z - 0.15), (cx + 0.15, ROD_Y, Z + 0.15), "wick")]

    width = 0.5 + coats * 0.25          # 0.75 .. 2.0: a finished taper is vanilla's 2px candle
    length = 6 + coats * 0.5            # 6.5 .. 9
    top = ROD_Y - 1                     # a sliver of wick shows between rod and wax
    half = width / 2
    parts = [
        box(f"wick{i}", (cx - 0.15, top, Z - 0.15), (cx + 0.15, ROD_Y, Z + 0.15), "wick"),
        box(f"wax{i}", (cx - half, top - length, Z - half), (cx + half, top, Z + half), "wax", uv_origin=(1 + i, 2)),
    ]
    if coats >= 2:
        # Dipping leaves the bottom fattest: each coat drips and sets there.
        drip = half + 0.12 * (coats - 1)
        bottom = top - length
        parts.append(box(f"drip{i}", (cx - drip, bottom - 0.25, Z - drip), (cx + drip, bottom + 0.75, Z + drip), "wax", uv_origin=(8, 8)))
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
