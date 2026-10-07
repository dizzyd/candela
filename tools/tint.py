#!/usr/bin/env python3
"""
Makes the treated-wick and verdigris textures by tinting vanilla's own:

  python3 tools/tint.py "$VINTAGE_STORY"

Wicks are vanilla's flax fibres stained part way towards the colour they burn;
verdigris is vanilla's crushed lime, coloured as the game's own Verdigris pigment
(nugget.json's pigment for malachite). Flames are vanilla's candle texture turned
to each flame's hue: the flames of chandeliers, lanterns and bunches' tips are
cubes in the model that sample its orange corner, and FlameMeshes points them at
these instead. Dyed candles are the beeswax and tallow candle textures in each of
vanilla's dyes, the flame's orange corner kept, and the tallow texture itself gets
that corner back. Plain Python - no imaging library - like icon.py: vanilla's
sources here are 8-bit RGB or RGBA, not interlaced.
"""
import colorsys, os, struct, sys, zlib

def read(path):
    data = open(path, 'rb').read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', "not a PNG"
    pos, idat = 8, b''
    while pos < len(data):
        n, kind = struct.unpack('>I4s', data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + n]
        pos += 12 + n
        if kind == b'IHDR':
            w, h, depth, ctype, _, _, interlace = struct.unpack('>IIBBBBB', body)
            assert depth == 8 and ctype in (2, 6) and interlace == 0, (path, depth, ctype, interlace)
        elif kind == b'IDAT':
            idat += body
    bpp = 3 if ctype == 2 else 4
    raw, stride, rows, prev, i = zlib.decompress(idat), w * bpp, [], bytearray(w * bpp), 0
    for _ in range(h):
        f = raw[i]; line = bytearray(raw[i + 1:i + 1 + stride]); i += 1 + stride
        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = prev[x]
            c = prev[x - bpp] if x >= bpp else 0
            if f == 1: line[x] = (line[x] + a) & 255
            elif f == 2: line[x] = (line[x] + b) & 255
            elif f == 3: line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c; pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append([tuple(line[x * bpp:x * bpp + bpp]) + ((255,) if bpp == 3 else ()) for x in range(w)])
        prev = line
    return w, h, rows

def write(path, w, h, rows):
    raw = b''.join(b'\x00' + bytes(v for p in row for v in p) for row in rows)
    def chunk(kind, body): return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body) & 0xffffffff)
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
                + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))

def tint(src, dest, colour, strength):
    """Each pixel's brightness carried over in `colour`, mixed `strength` of the way in."""
    w, h, rows = read(src)
    out = []
    for row in rows:
        line = []
        for r, g, b, a in row:
            lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255
            stained = [min(255, int(c * (0.4 + 0.9 * lum))) for c in colour]
            line.append(tuple(int(o + (s - o) * strength) for o, s in zip((r, g, b), stained)) + (a,))
        out.append(line)
    write(dest, w, h, out)
    print("wrote", os.path.normpath(dest))

def turn(src, dest, hue):
    """Each pixel's hue set to `hue` (0-1), its saturation and brightness kept."""
    w, h, rows = read(src)
    out = []
    for row in rows:
        line = []
        for r, g, b, a in row:
            _, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            line.append(tuple(int(c * 255 + 0.5) for c in colorsys.hsv_to_rgb(hue, s, v)) + (a,))
        out.append(line)
    write(dest, w, h, out)
    print("wrote", os.path.normpath(dest))

# The flame colours of FlameColours.cs, as a stain on the fibres.
WICKS = {
    "red":    (200, 40, 30),
    "yellow": (220, 195, 40),
    "green":  (60, 190, 50),
    "teal":   (40, 170, 160),
    "blue":   (50, 80, 210),
    "violet": (150, 70, 200),
}

# block/candle's flame: the corner the flame cubes in every candle model sample.
FLAME_CORNER = (4, 5)  # columns, rows

def in_corner(x, y):
    return x < FLAME_CORNER[0] and y < FLAME_CORNER[1]

def with_corner(rows, flame_rows):
    """`rows` with the flame corner taken from `flame_rows`."""
    return [[flame_rows[y][x] if in_corner(x, y) else p for x, p in enumerate(row)] for y, row in enumerate(rows)]

def dye(rows, colour):
    """Each wax pixel as `colour`, lighter or darker as it was than the wax's average,
    so the grain shows at any colour - white and black included. The corner is left."""
    lum = lambda p: 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]
    body = [lum(p) for y, row in enumerate(rows) for x, p in enumerate(row) if not in_corner(x, y) and p[3] > 0]
    mean = sum(body) / len(body)
    out = []
    for y, row in enumerate(rows):
        line = []
        for x, p in enumerate(row):
            if in_corner(x, y) or p[3] == 0:
                line.append(p)
                continue
            d = (lum(p) - mean) * 0.9
            line.append(tuple(max(0, min(255, int(c + d))) for c in colour) + (p[3],))
        out.append(line)
    return out

# Vanilla's dyes: the average of each block/liquid/dye texture, gray and white set by
# hand (theirs are palette PNGs, which read() does not take). Woad is blue's twin there.
DYES = {
    "red":    (181, 38, 66),
    "orange": (181, 66, 38),
    "yellow": (181, 172, 38),
    "green":  (66, 136, 56),
    "blue":   (56, 76, 136),
    "woad":   (56, 76, 136),
    "purple": (96, 56, 136),
    "pink":   (181, 38, 119),
    "white":  (228, 226, 220),
    "gray":   (122, 122, 122),
    "black":  (28, 28, 28),
}

# FlameColours.cs's particle hues, 0-255, so the model's flames match the particles'.
FLAMES = {
    "red":    4,
    "yellow": 44,
    "green":  80,
    "teal":   120,
    "blue":   165,
    "violet": 195,
}

def main():
    game = os.path.join(sys.argv[1], 'assets', 'survival', 'textures')
    out = os.path.join(os.path.dirname(__file__), '..', 'candela', 'assets', 'candela', 'textures', 'item')
    os.makedirs(out, exist_ok=True)

    for name, colour in WICKS.items():
        tint(os.path.join(game, 'item', 'resource', 'fibers.png'), os.path.join(out, f'wick-{name}.png'), colour, 0.6)
    tint(os.path.join(game, 'item', 'resource', 'crushed', 'lime.png'), os.path.join(out, 'powder-verdigris.png'), (112, 154, 108), 0.9)

    blocks = os.path.join(os.path.dirname(__file__), '..', 'candela', 'assets', 'candela', 'textures', 'block')
    os.makedirs(blocks, exist_ok=True)
    for name, hue in FLAMES.items():
        turn(os.path.join(game, 'block', 'candle.png'), os.path.join(blocks, f'flame-{name}.png'), hue / 256)

    # The tallow candle, drawn by hand from vanilla's, lost the flame corner: its tips
    # were pale wax. Put it back, then dye both.
    w, h, beeswax = read(os.path.join(game, 'block', 'candle.png'))
    tallow_path = os.path.join(blocks, 'candle-tallow.png')
    _, _, tallow = read(tallow_path)
    tallow = with_corner(tallow, beeswax)
    write(tallow_path, w, h, tallow)
    print("wrote", os.path.normpath(tallow_path))
    for name, colour in DYES.items():
        # Tallow is the paler, more opaque wax, and takes a dye paler.
        pale = tuple(int(c * 0.85 + 255 * 0.15) for c in colour)
        for wax, rows, c in (("beeswax", beeswax, colour), ("tallow", tallow, pale)):
            dest = os.path.join(blocks, f'candle-{wax}-{name}.png')
            write(dest, w, h, dye(rows, c))
            print("wrote", os.path.normpath(dest))

main()
