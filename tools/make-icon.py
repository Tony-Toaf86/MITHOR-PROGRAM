"""Genera assets/mithor.ico (y un PNG de vista previa) para MITHOR.

Uso:  python tools/make-icon.py
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 1024
HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.abspath(os.path.join(HERE, "..", "assets"))
os.makedirs(OUT_DIR, exist_ok=True)

BG = (16, 20, 27, 255)
BORDER = (45, 54, 70, 255)
ACCENT_HI = (255, 228, 154)
ACCENT_LO = (255, 138, 61)
RAY = (255, 176, 92, 255)


def rounded_mask(radius=230, inset=8):
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [inset, inset, S - inset, S - inset], radius=radius, fill=255
    )
    return mask


def draw_rays(layer, cx, cy):
    d = ImageDraw.Draw(layer)
    for k in range(8):
        ang = np.deg2rad(k * 45 + 22.5)
        r1, r2 = 268, 352
        x1, y1 = cx + np.cos(ang) * r1, cy + np.sin(ang) * r1
        x2, y2 = cx + np.cos(ang) * r2, cy + np.sin(ang) * r2
        d.line([(x1, y1), (x2, y2)], fill=RAY, width=30)
        for (px, py) in ((x1, y1), (x2, y2)):
            d.ellipse([px - 15, py - 15, px + 15, py + 15], fill=RAY)


def main():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    bd = ImageDraw.Draw(base)
    bd.rounded_rectangle(
        [8, 8, S - 8, S - 8], radius=230, fill=BG, outline=BORDER, width=10
    )
    img = Image.alpha_composite(img, base)

    cx, cy, R = S // 2, S // 2 - 8, 206

    # Halo cálido
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for i in range(34, 0, -1):
        alpha = int(92 * (i / 34.0) ** 2)
        r = R + 40 + i * 5
        gd.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 145, 55, alpha))
    img = Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(46)))

    # Rayos
    rays = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    draw_rays(rays, cx, cy)
    img = Image.alpha_composite(img, rays)

    # Disco solar con degradado radial
    sun = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sun)
    for i in range(R, 0, -1):
        t = i / float(R)
        col = (
            int(ACCENT_HI[0] + (ACCENT_LO[0] - ACCENT_HI[0]) * t),
            int(ACCENT_HI[1] + (ACCENT_LO[1] - ACCENT_HI[1]) * t),
            int(ACCENT_HI[2] + (ACCENT_LO[2] - ACCENT_HI[2]) * t),
            255,
        )
        sd.ellipse([cx - i, cy - i, cx + i, cy + i], fill=col)
    img = Image.alpha_composite(img, sun)

    # Sombra de atenuación diagonal (sugiere control de brillo)
    xs = np.linspace(0.0, 1.0, S)[None, :]
    ys = np.linspace(0.0, 1.0, S)[:, None]
    alpha = ((xs * 0.45 + ys * 0.55) * 150).astype(np.uint8)
    shade = Image.new("RGBA", (S, S), (6, 8, 12, 0))
    shade.putalpha(Image.fromarray(alpha))
    shade.putalpha(Image.composite(shade.getchannel("A"), Image.new("L", (S, S), 0), rounded_mask()))
    img = Image.alpha_composite(img, shade)

    ico = os.path.join(OUT_DIR, "mithor.ico")
    img.save(
        ico,
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    img.resize((256, 256), Image.LANCZOS).save(
        os.path.join(OUT_DIR, "mithor-256.png"), format="PNG"
    )
    print("OK ->", ico)


if __name__ == "__main__":
    main()
