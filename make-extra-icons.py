"""Screen, Calendar, Clock and Feeds icons in the soft-minimal pack's style (unplated gradient shape, white details).
Each ICO frame is drawn on its own: the 16-32 px ones use thicker, simpler artwork. Output: icons-new/Kinetik<App>.ico/.png"""
import io, math, os, struct
from PIL import Image, ImageDraw, ImageFilter, ImageChops

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icons-new")
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
W = (255, 255, 255, 255)


def hx(h, a=255):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


def mixc(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(4))


PAL = {  # light, mid, dark
    "Screen": ("#8CF0CF", "#2FC79A", "#13816E"),
    "Calendar": ("#A6F3E3", "#2CC4B0", "#0E7C86"),
    "Clock": ("#FFB9E3", "#F25BB5", "#A5217F"),
    "Feeds": ("#D2F59A", "#86D140", "#3F8E22"),
}


class Canvas:
    def __init__(self, size):
        self.s = size
        self.px = size * max(4, 1024 // size)  # supersampled
        self.u = self.px / 128.0
        self.img = Image.new("RGBA", (self.px, self.px), (0, 0, 0, 0))

    def mask(self, fn):
        m = Image.new("L", (self.px, self.px), 0)
        fn(ImageDraw.Draw(m), self.u)
        return m

    def grad(self, mask, pal, angle=60):
        c0, c1, c2 = (hx(p) for p in pal)
        n = self.px
        strip = Image.new("RGBA", (n * 2, 1))
        p = strip.load()
        for i in range(n * 2):
            t = i / (n * 2 - 1)
            p[i, 0] = mixc(c0, c1, t / 0.5) if t < 0.5 else mixc(c1, c2, (t - 0.5) / 0.5)
        big = strip.resize((n * 2, n * 2), Image.NEAREST).rotate(-angle, resample=Image.BICUBIC)
        g = big.crop((n // 2, n // 2, n // 2 + n, n // 2 + n))
        layer = Image.new("RGBA", (n, n), (0, 0, 0, 0))
        layer.paste(g, (0, 0), mask)
        self.img.alpha_composite(layer)

    def shadow(self, mask, color, dy=3, blur=3, a=0.0):
        m = ImageChops.offset(mask, 0, int(dy * self.u)).filter(ImageFilter.GaussianBlur(blur * self.u)).point(lambda v: int(v * a))
        sh = Image.new("RGBA", (self.px, self.px), hx(color)[:3] + (0,))
        sh.putalpha(m)
        self.img.alpha_composite(sh)

    def gloss(self, mask):
        gm = Image.linear_gradient("L").rotate(180).resize((self.px, self.px // 2)).point(lambda v: int(v * 0.20))
        full = Image.new("L", (self.px, self.px), 0)
        full.paste(gm, (0, 0))
        g = Image.new("RGBA", (self.px, self.px), (255, 255, 255, 0))
        g.putalpha(ImageChops.multiply(full, mask))
        self.img.alpha_composite(g)

    def white(self, fn, alpha=255):
        m = self.mask(fn)
        if alpha < 255:
            m = m.point(lambda v: int(v * alpha / 255))
        l = Image.new("RGBA", (self.px, self.px), W[:3] + (0,))
        l.putalpha(m)
        self.img.alpha_composite(l)

    def out(self):
        return self.img.resize((self.s, self.s), Image.LANCZOS)


def line(d, pts, w, u):
    d.line([(x * u, y * u) for x, y in pts], fill=255, width=max(1, int(w * u)), joint="curve")
    r = w * u / 2
    for x, y in (pts[0], pts[-1]):
        d.ellipse((x * u - r, y * u - r, x * u + r, y * u + r), fill=255)


def screen(c, small):
    pal = PAL["Screen"]
    body = c.mask(lambda d, u: d.rounded_rectangle((8 * u, 16 * u, 120 * u, 90 * u), 15 * u, fill=255))

    def stand_fn(d, u):
        d.rectangle((52 * u, 88 * u, 76 * u, 104 * u), fill=255)
        d.rounded_rectangle((34 * u, 100 * u, 94 * u, 112 * u), 6 * u, fill=255)
    allm = ImageChops.lighter(body, c.mask(stand_fn))
    c.shadow(allm, pal[2])
    c.grad(allm, pal)
    c.gloss(body)
    inner = c.mask(lambda d, u: d.rounded_rectangle((18 * u, 26 * u, 110 * u, 80 * u), 9 * u, fill=255))
    c.grad(inner, (pal[2], pal[2], pal[2]), 60)
    if small:
        c.white(lambda d, u: line(d, [(26, 54), (46, 54), (56, 36), (70, 70), (80, 54), (102, 54)], 11, u))
    else:
        c.white(lambda d, u: line(d, [(28, 54), (46, 54), (55, 38), (68, 68), (77, 54), (100, 54)], 7, u))


def calendar(c, small):
    pal = PAL["Calendar"]
    page = c.mask(lambda d, u: d.rounded_rectangle((14 * u, 20 * u, 114 * u, 116 * u), 20 * u, fill=255))
    c.shadow(page, pal[2])
    c.grad(page, pal)
    c.gloss(page)
    band = ImageChops.multiply(page, c.mask(lambda d, u: d.rectangle((0, 0, 128 * u, 48 * u), fill=255)))
    c.grad(band, (pal[1], pal[2], pal[2]), 70)
    for x in (38, 78):
        c.white(lambda d, u, x=x: d.rounded_rectangle((x * u, 8 * u, (x + 12) * u, 34 * u), 6 * u, fill=255))
    cells = [(0, 0), (1, 0), (0, 1), (1, 1)] if small else [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
    for cx, cy in cells:
        if small:
            x, y, w, h = 28 + cx * 38, 64 + cy * 24, 28, 16
        else:
            x, y, w, h = 27 + cx * 28, 66 + cy * 24, 20, 16
        hot = (cx, cy) == ((1, 1) if small else (2, 1))
        c.white(lambda d, u, x=x, y=y, w=w, h=h: d.rounded_rectangle((x * u, y * u, (x + w) * u, (y + h) * u), 5 * u, fill=255), 255 if hot else 150)


def clock(c, small):
    pal = PAL["Clock"]
    face = c.mask(lambda d, u: d.ellipse((10 * u, 10 * u, 118 * u, 118 * u), fill=255))
    c.shadow(face, pal[2])
    c.grad(face, pal)
    c.gloss(face)
    if not small:
        for ang in (0, 90, 180, 270):
            a = math.radians(ang - 90)
            c.white(lambda d, u, a=a: line(d, [(64 + math.cos(a) * 42, 64 + math.sin(a) * 42), (64 + math.cos(a) * 49, 64 + math.sin(a) * 49)], 5, u), 170)
    wd = 12 if small else 8

    def hands(d, u):
        line(d, [(64, 64), (64, 32)], wd, u)
        line(d, [(64, 64), (88, 76)], wd, u)
        d.ellipse((55 * u, 55 * u, 73 * u, 73 * u), fill=255)
    c.white(hands)


def feeds(c, small):
    pal = PAL["Feeds"]
    body = c.mask(lambda d, u: d.rounded_rectangle((12 * u, 12 * u, 116 * u, 116 * u), 30 * u, fill=255))
    c.shadow(body, pal[2])
    c.grad(body, pal)
    c.gloss(body)
    w = 13 if small else 10

    def rss(d, u):
        d.ellipse((24 * u, 84 * u, 44 * u, 104 * u), fill=255) if small else d.ellipse((22 * u, 90 * u, 38 * u, 106 * u), fill=255)
        for r in ((34,) if small else (30, 58)):
            ox, oy = 32, 96
            d.arc(((ox - r) * u, (oy - r) * u, (ox + r) * u, (oy + r) * u), 270, 360, fill=255, width=int(w * u))
            for ang in (270, 360):
                a = math.radians(ang)
                ex, ey = (ox + math.cos(a) * (r - w / 2)) * u, (oy + math.sin(a) * (r - w / 2)) * u
                d.ellipse((ex - w * u / 2, ey - w * u / 2, ex + w * u / 2, ey + w * u / 2), fill=255)
    c.white(rss)


def draw(app, size):
    c = Canvas(size)
    {"Screen": screen, "Calendar": calendar, "Clock": clock, "Feeds": feeds}[app](c, size <= 32)
    return c.out()


def write_ico(fr, path):
    blobs = []
    for n, im in fr:
        b = io.BytesIO()
        im.save(b, "PNG")
        blobs.append((n, b.getvalue()))
    off = 6 + 16 * len(blobs)
    data = struct.pack("<HHH", 0, 1, len(blobs))
    for n, b in blobs:
        data += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(b), off)
        off += len(b)
    open(path, "wb").write(data + b"".join(b for _, b in blobs))


if __name__ == "__main__":
    sheet = Image.new("RGBA", (4 * 270, 380), (24, 24, 28, 255))
    for i, app in enumerate(["Screen"]):  # the others come from the Codex extended pack
        fr = [(n, draw(app, n)) for n in SIZES]
        write_ico(fr, os.path.join(OUT, "Kinetik%s.ico" % app))
        fr[-1][1].save(os.path.join(OUT, "Kinetik%s.png" % app))
        sheet.alpha_composite(fr[-1][1], (i * 270 + 7, 8))
        x = i * 270 + 20
        for n in (16, 24, 32, 48):
            sheet.alpha_composite(dict(fr)[n].resize((n * 2, n * 2), Image.NEAREST), (x, 290))
            x += n * 2 + 6
    sheet.save(os.path.join(OUT, "_sheet-extra.png"))
