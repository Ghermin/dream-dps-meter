"""Draws about/icon.png (128x128) and about/preview.png (636x358) for Dream DPS Meter."""
import math
import random
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1]
FONTS = "C:/Windows/Fonts/"

BG_TOP = (14, 24, 52)
BG_BOTTOM = (5, 9, 22)
CYAN = (135, 221, 241)
PALE = (213, 248, 255)
GOLD = (242, 193, 78)
PURPLE = (204, 150, 255)
RED = (255, 123, 115)
GREEN = (126, 224, 150)


def background(w, h, seed=7):
    img = Image.new("RGB", (w, h))
    px = img.load()
    cx, cy = w * 0.3, h * 0.25
    rmax = math.hypot(w, h)
    for y in range(h):
        for x in range(w):
            t = min(1.0, math.hypot(x - cx, y - cy) / rmax * 1.6)
            px[x, y] = tuple(int(BG_TOP[i] * (1 - t) + BG_BOTTOM[i] * t) for i in range(3))
    # faint stars
    d = ImageDraw.Draw(img)
    rnd = random.Random(seed)
    for _ in range(int(w * h / (min(w, h) ** 2 / 90))):
        x, y = rnd.uniform(0, w), rnd.uniform(0, h * 0.75)
        r = rnd.choice([0.6, 0.8, 1.0, 1.4]) * min(w, h) / 512 * 2.0
        a = rnd.randint(70, 190)
        d.ellipse([x - r, y - r, x + r, y + r], fill=tuple(int(c * a / 255) + int(BG_TOP[i] * (1 - a / 255)) for i, c in enumerate(PALE)))
    return img


def glow(base, draw_fn, radius, strength=1.0):
    """Draws shapes once blurred (glow) then sharp on top."""
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    draw_fn(ImageDraw.Draw(layer))
    blurred = layer.filter(ImageFilter.GaussianBlur(radius))
    if strength != 1.0:
        a = blurred.getchannel("A").point(lambda v: min(255, int(v * strength)))
        blurred.putalpha(a)
    base.alpha_composite(blurred)
    base.alpha_composite(layer)


def moon(d, cx, cy, r, bg):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=PALE + (255,))
    o = r * 0.62
    d.ellipse([cx - r + o, cy - r - o * 0.35, cx + r + o, cy + r - o * 0.35], fill=(0, 0, 0, 0))  # cut out: transparent, shows the real sky


def bars(d, x0, base_y, bw, gap, heights, colors, radius):
    for i, (h, c) in enumerate(zip(heights, colors)):
        x = x0 + i * (bw + gap)
        d.rounded_rectangle([x, base_y - h, x + bw, base_y], radius=radius, fill=c + (255,))


def sparkle(d, cx, cy, r, color):
    pts = []
    for k in range(8):
        ang = k * math.pi / 4 - math.pi / 2
        rr = r if k % 2 == 0 else r * 0.28
        pts.append((cx + rr * math.cos(ang), cy + rr * math.sin(ang)))
    d.polygon(pts, fill=color + (255,))


def make_icon(size=128):
    S = 512
    img = background(S, S).convert("RGBA")
    bg_at_moon = img.getpixel((150, 140))[:3]

    def art(d):
        moon(d, 150, 140, 70, bg_at_moon)
        bars(d, 150, 430, 58, 26, [120, 190, 260, 330], [CYAN, PURPLE, CYAN, GOLD], 12)
        sparkle(d, 405, 95, 34, GOLD)
    glow(img, art, 14, 0.9)
    # ground line
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([110, 446, 470, 456], radius=5, fill=PALE + (200,))
    # frame
    d.rounded_rectangle([6, 6, S - 7, S - 7], radius=40, outline=CYAN + (150,), width=8)
    return img.resize((size, size), Image.LANCZOS).convert("RGB")


def make_preview(w=636, h=358):
    k = 2
    W, H = w * k, h * k
    img = background(W, H, seed=11).convert("RGBA")
    # icon art on the left
    icon = make_icon(512).resize((230 * k, 230 * k), Image.LANCZOS)
    img.alpha_composite(icon.convert("RGBA"), (44 * k, (H - icon.height) // 2))

    d = ImageDraw.Draw(img)
    title = ImageFont.truetype(FONTS + "segoeuib.ttf", 58 * k)
    sub = ImageFont.truetype(FONTS + "segoeui.ttf", 24 * k)
    small = ImageFont.truetype(FONTS + "segoeuisl.ttf", 19 * k)
    x = 300 * k

    def text_art(dd):
        dd.text((x, 70 * k), "Dream", font=title, fill=PALE + (255,))
        dd.text((x, 132 * k), "DPS Meter", font=title, fill=GOLD + (255,))
    glow(img, text_art, 10, 0.6)

    d.text((x + 2 * k, 214 * k), "Dégâts · DPS · Sources", font=sub, fill=CYAN + (255,))
    # element split bar, like in the HUD
    segs = [(0.34, CYAN), (0.22, PURPLE), (0.18, GOLD), (0.14, RED), (0.12, GREEN)]
    bx, by, bw, bh = x + 2 * k, 262 * k, 300 * k, 10 * k
    cur = bx
    for frac, c in segs:
        sw = bw * frac
        d.rectangle([cur, by, cur + sw - 2 * k, by + bh], fill=c + (255,))
        cur += sw
    d.text((x + 2 * k, 288 * k), "intégré au HUD · mise à jour auto", font=small, fill=(150, 175, 195, 255))
    return img.resize((w, h), Image.LANCZOS).convert("RGB")


make_icon().save(OUT + "/icon.png", optimize=True)
make_preview().save(OUT + "/preview.png", optimize=True)
print("ok")
