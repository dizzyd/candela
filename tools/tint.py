#!/usr/bin/env python3
"""
Makes the treated-wick and verdigris textures by tinting vanilla's own:

  python3 tools/tint.py "$VINTAGE_STORY"

Wicks are vanilla's flax fibres stained part way towards the colour they burn;
verdigris is vanilla's crushed lime, coloured as the game's own Verdigris pigment
(nugget.json's pigment for malachite). Plain Python - no imaging library - like
icon.py: vanilla's sources here are 8-bit RGB or RGBA, not interlaced.
"""
import os, struct, sys, zlib

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

# The flame colours of FlameColours.cs, as a stain on the fibres.
WICKS = {
    "red":    (200, 40, 30),
    "green":  (60, 190, 50),
    "teal":   (40, 170, 160),
    "blue":   (50, 80, 210),
    "violet": (150, 70, 200),
}

def main():
    game = os.path.join(sys.argv[1], 'assets', 'survival', 'textures')
    out = os.path.join(os.path.dirname(__file__), '..', 'candela', 'assets', 'candela', 'textures', 'item')
    os.makedirs(out, exist_ok=True)

    for name, colour in WICKS.items():
        tint(os.path.join(game, 'item', 'resource', 'fibers.png'), os.path.join(out, f'wick-{name}.png'), colour, 0.6)
    tint(os.path.join(game, 'item', 'resource', 'crushed', 'lime.png'), os.path.join(out, 'powder-verdigris.png'), (112, 154, 108), 0.9)

main()
