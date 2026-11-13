"""
Draws PetFam's official habitat wallpapers and, from the very same shapes, their habitat maps.

Each scene is a list of layers: a shape, how it is painted, and what kind of place it is for a pet (floor, water,
mud, sand, sunny spot, shade, not floor, sky). The picture and the map come from one description, so the map is
exact. Output (in src/PetFam.App/Assets/Wallpapers):
    <id>.jpg           2560x1440 picture
    <id>.habitat.txt   128 columns x 72 rows, one letter per spot (see KINDS)

Run:  python -I build/wallpapers/make_wallpapers.py
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 2560, 1440
COLS, ROWS = 128, 72
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "src", "PetFam.App", "Assets", "Wallpapers")

# Letter per kind, as read by PetFam.Core.World.OfficialHabitat.
KINDS = {"ground": "g", "water": "w", "mud": "m", "sand": "s", "sun": "u", "shade": "h", "blocked": "x", "sky": "k"}
CODE = {k: i + 1 for i, k in enumerate(KINDS)}


def gradient(top, bottom, h=H, w=W):
    t = np.linspace(0, 1, h)[:, None, None]
    a = np.array(top, dtype=float)[None, None, :]
    b = np.array(bottom, dtype=float)[None, None, :]
    return Image.fromarray(np.repeat((a + (b - a) * t), w, axis=1).astype(np.uint8), "RGB")


class Scene:
    def __init__(self):
        self.img = Image.new("RGB", (W, H))
        self.kind = Image.new("L", (W, H), 0)

    def fill(self, img_or_color, mask, kind=None, blur=0, alpha=1.0):
        """Paints a colour or picture through a mask (L image); records the kind where the mask is solid."""
        if blur:
            mask = mask.filter(ImageFilter.GaussianBlur(blur))
        src = img_or_color if isinstance(img_or_color, Image.Image) else Image.new("RGB", (W, H), img_or_color)
        m = mask if alpha >= 1 else mask.point(lambda v: int(v * alpha))
        self.img.paste(src, (0, 0), m)
        if kind is not None:
            self.kind.paste(CODE[kind], (0, 0), mask.point(lambda v: 255 if v >= 128 else 0))

    @staticmethod
    def mask():
        return Image.new("L", (W, H), 0)

    def ellipse(self, cx, cy, rx, ry, paint, kind=None, blur=0, alpha=1.0):
        m = self.mask()
        ImageDraw.Draw(m).ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=255)
        self.fill(paint, m, kind, blur, alpha)

    def polygon(self, pts, paint, kind=None, blur=0, alpha=1.0):
        m = self.mask()
        ImageDraw.Draw(m).polygon(pts, fill=255)
        self.fill(paint, m, kind, blur, alpha)

    def rect(self, x0, y0, x1, y1, paint, kind=None, blur=0, alpha=1.0, radius=0):
        m = self.mask()
        ImageDraw.Draw(m).rounded_rectangle((x0, y0, x1, y1), radius=radius, fill=255)
        self.fill(paint, m, kind, blur, alpha)

    def save(self, name):
        os.makedirs(OUT, exist_ok=True)
        self.img.save(os.path.join(OUT, name + ".jpg"), quality=90, optimize=True)
        k = np.asarray(self.kind)
        letters = {v: KINDS[k_] for k_, v in CODE.items()}
        rows = []
        for r in range(ROWS):
            y = int((r + 0.5) * H / ROWS)
            row = ""
            for c in range(COLS):
                x = int((c + 0.5) * W / COLS)
                row += letters.get(int(k[y, x]), "x")
            rows.append(row)
        with open(os.path.join(OUT, name + ".habitat.txt"), "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(rows) + "\n")
        counts = {KINDS[k_]: sum(row.count(KINDS[k_]) for row in rows) for k_ in KINDS}
        print(name, counts)


def hills(points_y, base):
    pts = [(0, H)] + [(x, y) for x, y in points_y] + [(W, H)]
    return pts


def wavy(y0, amp, period, phase, x_step=40):
    return [(x, y0 + amp * math.sin(x / period + phase)) for x in range(0, W + x_step, x_step)]


def clouds(s, rng, n, y_range, scale=1.0):
    for _ in range(n):
        cx, cy = rng.uniform(0, W), rng.uniform(*y_range)
        for _ in range(6):
            s.ellipse(cx + rng.uniform(-160, 160) * scale, cy + rng.uniform(-30, 30) * scale,
                      rng.uniform(80, 170) * scale, rng.uniform(40, 70) * scale, (255, 255, 255), blur=18, alpha=0.75)


def flowers(s, rng, n, y_range, colors, avoid=lambda x, y: False):
    d = ImageDraw.Draw(s.img)
    for _ in range(n):
        x, y = rng.uniform(40, W - 40), rng.uniform(*y_range)
        if avoid(x, y):
            continue
        r = 4 + (y - y_range[0]) / (y_range[1] - y_range[0]) * 7
        col = rng.choice(colors)
        for a in range(5):
            ang = a / 5 * math.tau
            d.ellipse((x + math.cos(ang) * r - r * 0.7, y + math.sin(ang) * r - r * 0.7, x + math.cos(ang) * r + r * 0.7, y + math.sin(ang) * r + r * 0.7), fill=col)
        d.ellipse((x - r * 0.5, y - r * 0.5, x + r * 0.5, y + r * 0.5), fill=(255, 220, 90))


def grass_tufts(s, rng, n, y_range, color, avoid=lambda x, y: False):
    d = ImageDraw.Draw(s.img)
    for _ in range(n):
        x, y = rng.uniform(0, W), rng.uniform(*y_range)
        if avoid(x, y):
            continue
        k = 0.5 + (y - y_range[0]) / (y_range[1] - y_range[0])
        for i in range(3):
            dx = (i - 1) * 6 * k
            d.line((x + dx, y, x + dx * 2.2, y - 22 * k), fill=color, width=max(1, int(3 * k)))


def tree(s, cx, ground_y, size, kind_shade=True, canopy=((56, 128, 70), (78, 158, 84), (104, 182, 98))):
    # Shade on the grass first, then trunk and a round canopy (not floor).
    if kind_shade:
        s.ellipse(cx + size * 0.25, ground_y + size * 0.06, size * 1.15, size * 0.24, (60, 120, 66), "shade", blur=10, alpha=0.75)
    s.rect(cx - size * 0.11, ground_y - size * 1.2, cx + size * 0.11, ground_y + 6, (120, 84, 56), "blocked", radius=int(size * 0.08))
    for (dx, dy, r, c) in [(-0.45, -1.45, 0.62, 0), (0.45, -1.5, 0.6, 0), (0, -1.95, 0.7, 1), (-0.2, -1.6, 0.55, 1), (0.25, -2.15, 0.45, 2), (-0.35, -2.0, 0.35, 2)]:
        s.ellipse(cx + dx * size, ground_y + dy * size, r * size, r * size * 0.92, canopy[c], "blocked")


# ---------------------------------------------------------------- sunny meadow

def meadow():
    rng = random.Random(3)
    s = Scene()
    horizon = 600
    s.fill(gradient((120, 190, 240), (214, 238, 250)), Image.new("L", (W, H), 255), "sky")
    s.ellipse(2100, 210, 230, 230, (255, 250, 220), blur=60, alpha=0.8)
    clouds(s, rng, 7, (120, 380))
    s.polygon(hills(wavy(500, 40, 260, 0.4), H), (168, 196, 222), "blocked", blur=6)
    s.polygon(hills(wavy(560, 30, 190, 1.7), H), (128, 186, 128), "blocked", blur=3)
    for x in range(0, W + 60, 70):
        s.ellipse(x + rng.uniform(-20, 20), horizon - 10 + rng.uniform(-8, 8), rng.uniform(40, 70), rng.uniform(30, 48), (74, 140, 86), "blocked")
    meadow_mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(meadow_mask).polygon(hills(wavy(horizon, 10, 300, 0.2), H), fill=255)
    s.fill(gradient((150, 214, 120), (86, 176, 82)), meadow_mask, "ground")

    # Sunny patches, a sandy path, the pond with its sandy rim, a mud puddle, a big tree with its shade.
    s.ellipse(1250, 820, 280, 60, (200, 236, 140), "sun", blur=14, alpha=0.9)
    s.ellipse(420, 1240, 260, 70, (200, 236, 140), "sun", blur=14, alpha=0.9)
    path = [(980, H), (1180, 1300), (1120, 1150), (1260, 1010), (1200, 900), (1300, 780), (1260, 700),
            (1330, 700), (1380, 790), (1290, 905), (1360, 1015), (1215, 1150), (1290, 1300), (1150, H)]
    s.polygon(path, (232, 206, 150), "sand", blur=4)
    s.ellipse(1840, 1010, 470, 165, (232, 214, 160), "sand", blur=6)
    s.ellipse(1840, 1010, 420, 135, gradient((110, 196, 236), (50, 140, 210)), "water")
    for i in range(6):
        s.ellipse(1700 + i * 60, 960 + (i % 3) * 30, 70, 6, (220, 244, 255), alpha=0.7, blur=2)
    s.ellipse(980, 1300, 170, 48, (120, 86, 56), "mud", blur=5)
    s.ellipse(940, 1290, 60, 14, (150, 112, 80), alpha=0.8, blur=3)
    tree(s, 520, 920, 240)
    tree(s, 2380, 760, 150)
    avoid = lambda x, y: (x - 1840) ** 2 / 480 ** 2 + (y - 1010) ** 2 / 175 ** 2 < 1 or abs(x - 1200) < 140
    flowers(s, rng, 160, (720, 1420), [(255, 120, 150), (255, 255, 255), (180, 140, 255), (255, 200, 80)], avoid)
    grass_tufts(s, rng, 500, (700, 1430), (70, 150, 66), avoid)
    s.save("sunny-meadow")


# ---------------------------------------------------------------- seaside

def seaside():
    rng = random.Random(5)
    s = Scene()
    s.fill(gradient((96, 176, 236), (255, 226, 196)), Image.new("L", (W, H), 255), "sky")
    s.ellipse(700, 330, 200, 200, (255, 240, 200), blur=70, alpha=0.8)
    clouds(s, rng, 5, (100, 330), 0.9)
    sea = Image.new("L", (W, H), 0)
    ImageDraw.Draw(sea).polygon([(0, 520), (W, 520), (W, H), (0, H)], fill=255)
    s.fill(gradient((40, 120, 190), (80, 200, 210)), sea, "water")
    d = ImageDraw.Draw(s.img)
    for y in range(560, 900, 26):
        for x in range(int(rng.uniform(-80, 0)), W, 160):
            d.arc((x, y, x + 90, y + 14), 200, 340, fill=(200, 236, 245), width=2)
    beach = Image.new("L", (W, H), 0)
    ImageDraw.Draw(beach).polygon([(0, H)] + wavy(900, 22, 210, 0.5) + [(W, H)], fill=255)
    s.fill(gradient((236, 214, 168), (246, 226, 184)), beach, "sand")
    s.polygon([(0, H)] + wavy(905, 22, 210, 0.5) + [(W, H)], (214, 190, 146), alpha=0.35, blur=12)   # wet sand edge
    s.ellipse(1500, 1150, 360, 90, (255, 236, 186), "sun", blur=16, alpha=0.9)
    s.ellipse(720, 1170, 210, 64, (226, 204, 160), "sand", blur=4)
    s.ellipse(720, 1170, 180, 48, gradient((100, 200, 220), (60, 160, 200)), "water")
    for (x, y, r) in [(540, 1120, 46), (880, 1210, 34), (2280, 1300, 60), (2360, 1270, 40)]:
        s.ellipse(x, y, r, r * 0.7, (140, 132, 126), "blocked")
        s.ellipse(x - r * 0.25, y - r * 0.25, r * 0.5, r * 0.3, (176, 168, 160), alpha=0.8)
    dune = [(0, H), (0, 1060), (180, 1020), (420, 1090), (600, 1220), (700, 1340), (720, H)]
    s.polygon(dune, (128, 186, 106), "ground", blur=3)
    grass_tufts(s, rng, 220, (1050, 1430), (90, 150, 70), lambda x, y: x > 650 - (y - 1050) * 0.4)
    # Palm with its shade.
    s.ellipse(2050, 1080, 320, 66, (196, 170, 128), "shade", blur=12, alpha=0.8)
    trunk = [(1960, 1080), (1990, 1080), (2050, 760), (2110, 520), (2085, 515), (2020, 760)]
    s.polygon(trunk, (150, 110, 70), "blocked")
    for ang in [-160, -130, -95, -60, -25, 10]:
        a = math.radians(ang)
        tip = (2095 + math.cos(a) * 330, 520 + math.sin(a) * 140 + 120)
        mid = (2095 + math.cos(a) * 170, 520 + math.sin(a) * 120 + 20)
        s.polygon([(2095, 515), (mid[0], mid[1] - 36), tip, (mid[0], mid[1] + 30)], (60, 140, 76), "blocked")
    s.save("seaside")


# ---------------------------------------------------------------- cozy garden

def garden():
    rng = random.Random(9)
    s = Scene()
    s.fill(gradient((150, 206, 244), (230, 244, 252)), Image.new("L", (W, H), 255), "sky")
    clouds(s, rng, 5, (90, 300), 0.8)
    for x in range(-40, W + 80, 160):
        s.ellipse(x, 470, 120, 90, (96, 160, 100), "blocked")
    s.rect(0, 430, W, 640, (214, 168, 120), "blocked")
    d = ImageDraw.Draw(s.img)
    for x in range(0, W, 64):
        d.rectangle((x + 4, 420, x + 56, 640), fill=(226, 182, 134))
        d.polygon([(x + 4, 420), (x + 30, 396), (x + 56, 420)], fill=(226, 182, 134))
        d.line((x + 4, 640, x + 4, 420), fill=(180, 136, 92), width=3)
    d.rectangle((0, 470, W, 488), fill=(196, 150, 104))
    d.rectangle((0, 580, W, 598), fill=(196, 150, 104))
    lawn = Image.new("L", (W, H), 0)
    ImageDraw.Draw(lawn).rectangle((0, 640, W, H), fill=255)
    s.fill(gradient((140, 206, 112), (92, 176, 84), ), lawn, "ground")
    # Flower beds along the fence (not floor), the vegetable patch (mud), stepping stones.
    s.rect(0, 640, W, 720, (126, 92, 64), "blocked", radius=20)
    flowers(s, rng, 140, (650, 712), [(255, 110, 140), (255, 180, 60), (180, 120, 255), (255, 255, 255)])
    s.rect(140, 1030, 620, 1250, (122, 88, 58), "mud", radius=30)
    for row in range(3):
        for col in range(5):
            s.ellipse(200 + col * 90, 1080 + row * 70, 26, 14, (80, 150, 70))
    for i, (x, y) in enumerate([(1180, 1400), (1240, 1270), (1290, 1150), (1330, 1040), (1360, 940), (1380, 850)]):
        s.ellipse(x, y, 70 - i * 6, 26 - i * 2, (200, 196, 186), "ground")
    s.ellipse(1240, 860, 300, 60, (190, 232, 140), "sun", blur=14, alpha=0.9)
    s.ellipse(760, 1360, 260, 60, (190, 232, 140), "sun", blur=14, alpha=0.9)
    # Fountain.
    s.ellipse(1900, 1000, 330, 120, (190, 186, 180), "blocked")
    s.ellipse(1900, 990, 290, 96, gradient((110, 196, 236), (60, 150, 214)), "water")
    s.rect(1880, 860, 1920, 990, (180, 176, 170), "blocked", radius=10)
    s.ellipse(1900, 860, 70, 22, (200, 196, 190), "blocked")
    for i in range(5):
        s.ellipse(1820 + i * 40, 980 + (i % 2) * 20, 30, 4, (230, 248, 255), alpha=0.8)
    # Sandbox.
    s.rect(2200, 1180, 2520, 1400, (170, 120, 76), "blocked", radius=12)
    s.rect(2222, 1200, 2498, 1380, (238, 216, 160), "sand", radius=8)
    # Parasol and its shade.
    s.ellipse(980, 900, 230, 56, (80, 140, 80), "shade", blur=10, alpha=0.75)
    s.rect(972, 700, 988, 900, (140, 120, 100), "blocked")
    s.polygon([(760, 720), (980, 610), (1200, 720)], (240, 96, 96), "blocked")
    for k in range(4):
        x0 = 760 + k * 110
        s.polygon([(x0, 720), (980, 610), (x0 + 55, 720)], (255, 240, 236), alpha=0.9)
    grass_tufts(s, rng, 380, (740, 1430), (76, 150, 70),
                lambda x, y: (140 < x < 620 and 1030 < y < 1250) or (x - 1900) ** 2 / 340 ** 2 + (y - 1000) ** 2 / 130 ** 2 < 1 or x > 2190 and y > 1170)
    s.save("cozy-garden")


if __name__ == "__main__":
    meadow()
    seaside()
    garden()
