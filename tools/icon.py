#!/usr/bin/env python3
"""
Cuts candela/modicon.png out of a screenshot of the icon scene.

The scene is CandelaLooks.ModIconScene in the in-game suite, which writes
results/icon-scene.png; copy that here and run:

  python3 tools/icon.py icon-scene.png [left top size]

The crop is a square of `size` pixels from (left, top), area-averaged down to
256x256. Plain Python - no imaging library - since the only PNGs involved are the
game's own screenshots: 8-bit, RGB or RGBA, not interlaced.
"""
import struct, sys, zlib, os

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
            assert depth == 8 and ctype in (2, 6) and interlace == 0, (depth, ctype, interlace)
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
        rows.append([tuple(line[x * bpp:x * bpp + 3]) for x in range(w)])
        prev = line
    return w, h, rows

def write(path, size, rows):
    raw = b''.join(b'\x00' + bytes(v for p in row for v in p) for row in rows)
    def chunk(kind, body): return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body) & 0xffffffff)
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 2, 0, 0, 0))
                + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))

def main():
    src = sys.argv[1]
    left, top, size = (int(a) for a in sys.argv[2:5]) if len(sys.argv) >= 5 else (290, 180, 420)
    out_size = 256
    w, h, rows = read(src)
    assert left + size <= w and top + size <= h, f"crop {left},{top} +{size} is outside {w}x{h}"

    scale = size / out_size
    out = []
    for oy in range(out_size):
        y0, y1 = top + int(oy * scale), top + max(int(oy * scale) + 1, int((oy + 1) * scale))
        line = []
        for ox in range(out_size):
            x0, x1 = left + int(ox * scale), left + max(int(ox * scale) + 1, int((ox + 1) * scale))
            px = [rows[y][x] for y in range(y0, y1) for x in range(x0, x1)]
            line.append(tuple(sum(p[c] for p in px) // len(px) for c in range(3)))
        out.append(line)

    dest = os.path.join(os.path.dirname(__file__), '..', 'candela', 'modicon.png')
    write(dest, out_size, out)
    print("wrote", os.path.normpath(dest))

main()
