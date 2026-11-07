"""Builds about/icon.png (128x128) and about/preview.png (636x358) from an in-game capture of the mod.

Usage: python make_icons_from_capture.py <capture.png> <panel_left> <panel_top> <panel_right> <panel_bottom> <out_dir>
The panel box is the detail panel in the capture, in pixels.
"""
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONTS = "C:/Windows/Fonts/"
PALE = (213, 248, 255)
TITLE = (135, 221, 241)
GOLD = (242, 193, 78)

src = Image.open(sys.argv[1]).convert("RGB")
left, top, right, bottom = (int(v) for v in sys.argv[2:6])
out = sys.argv[6]

# ---- icon: the panel itself, squared on its own dark background
pw, ph = right - left, bottom - top
cx, cy = (left + right) // 2, (top + bottom) // 2
side = int(pw * 0.8)  # top-left of the panel: title, icons and bars stay readable at 128 px
box = (left + 2, top + 2, left + 2 + side, top + 2 + side)
icon = src.crop(box).resize((128, 128), Image.LANCZOS)
icon.save(out + "/icon.png", optimize=True)

# ---- preview: the scene around the panel (16:9), with the mod name
W, H = 636, 358
k = 2
crop_h = int((bottom - top) * 1.45)
crop_w = int(crop_h * W / H)
x0 = max(0, left - int(pw * 0.25))
y0 = max(0, cy - crop_h // 2)
x0 = min(x0, src.width - crop_w)
y0 = min(y0, src.height - crop_h)
scene = src.crop((x0, y0, x0 + crop_w, y0 + crop_h)).resize((W * k, H * k), Image.LANCZOS).convert("RGBA")

# darken the right side so the title reads well, like the game's own vignettes
shade = Image.new("L", scene.size, 0)
sd = ImageDraw.Draw(shade)
for x in range(scene.width):
    t = max(0.0, (x / scene.width - 0.45) / 0.55)
    sd.line([(x, 0), (x, scene.height)], fill=int(170 * t))
dark = Image.new("RGBA", scene.size, (5, 9, 22, 255))
scene = Image.composite(dark, scene, shade)

title_font = ImageFont.truetype(FONTS + "palab.ttf", 50 * k)
sub_font = ImageFont.truetype(FONTS + "palai.ttf", 19 * k)
layer = Image.new("RGBA", scene.size, (0, 0, 0, 0))
d = ImageDraw.Draw(layer)
tx = int(W * k * 0.53)
d.text((tx, 92 * k), "Dream", font=title_font, fill=PALE + (255,))
d.text((tx, 148 * k), "DPS Meter", font=title_font, fill=GOLD + (255,))
d.text((tx + 2 * k, 222 * k), "Dégâts par mémoire et essence", font=sub_font, fill=TITLE + (255,))
d.text((tx + 2 * k, 252 * k), "Ctrl : le détail par source", font=sub_font, fill=(170, 195, 210, 255))
glow = layer.filter(ImageFilter.GaussianBlur(8 * k))
scene.alpha_composite(glow)
scene.alpha_composite(layer)
scene.resize((W, H), Image.LANCZOS).convert("RGB").save(out + "/preview.png", optimize=True)
print("ok", box, (x0, y0, crop_w, crop_h))
