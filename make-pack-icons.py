"""Copies the soft-minimal ICO pack into icons-new/ and makes the electric-blue Stats variant (hue shift on every frame)."""
import colorsys, io, struct, sys, os
from PIL import Image

SRC = sys.argv[1]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icons-new")


def frames(path):
    im = Image.open(path)
    return [(s[0], im.ico.getimage(s).convert("RGBA")) for s in sorted(im.ico.sizes())]


def write_ico(fr, path):
    blobs = []
    for n, im in fr:
        b = io.BytesIO(); im.save(b, "PNG"); blobs.append((n, b.getvalue()))
    off = 6 + 16 * len(blobs)
    data = struct.pack("<HHH", 0, 1, len(blobs))
    for n, b in blobs:
        data += struct.pack("<BBBBHHII", n if n < 256 else 0, n if n < 256 else 0, 0, 0, 1, 32, len(b), off); off += len(b)
    open(path, "wb").write(data + b"".join(b for _, b in blobs))


def blue(im):
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a == 0: continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if s > 0.08:  # keep the white pictogram white
                h = (h + 0.158) % 1.0   # mint (~160 deg) to electric blue (~222 deg)
                s = min(1, s * 1.1 + 0.05); v = min(1, v * 1.22 + 0.04)
            r2, g2, b2 = colorsys.hsv_to_rgb(h, s, v)
            px[x, y] = (int(r2 * 255), int(g2 * 255), int(b2 * 255), a)
    return im


os.makedirs(OUT, exist_ok=True)
for app in ("files", "hub", "notes", "stats", "weather"):
    fr = frames(os.path.join(SRC, "kinetik-%s.ico" % app))
    if app == "stats":
        fr = [(n, blue(im.copy())) for n, im in fr]
    write_ico(fr, os.path.join(OUT, "Kinetik%s.ico" % app.capitalize()))
    dict(fr)[max(n for n, _ in fr)].save(os.path.join(OUT, "Kinetik%s.png" % app.capitalize()))
    print(app, [n for n, _ in fr])
