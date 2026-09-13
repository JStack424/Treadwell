#!/usr/bin/env python3
"""Render Treadwell's original deterministic 256px package icon."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "packages" / "Treadwell" / "icon.png"

img = Image.new("RGBA", (256, 256), (19, 42, 35, 255))
d = ImageDraw.Draw(img)

# Forged-brass border and inset forest field.
d.rounded_rectangle((8, 8, 247, 247), radius=30, fill=(31, 63, 49, 255), outline=(219, 174, 82, 255), width=8)
d.rounded_rectangle((20, 20, 235, 235), radius=23, outline=(92, 117, 77, 255), width=3)

# Sparse grass marks behind the road.
for x, y in ((42, 58), (72, 40), (201, 57), (217, 105), (45, 155), (204, 180), (56, 211)):
    d.line((x, y + 8, x, y), fill=(113, 143, 82, 255), width=3)
    d.line((x, y + 5, x - 5, y + 1), fill=(113, 143, 82, 255), width=3)
    d.line((x, y + 5, x + 5, y), fill=(113, 143, 82, 255), width=3)

# A road narrowing toward the horizon.
road = [(76, 232), (181, 232), (164, 174), (169, 119), (151, 55), (119, 55), (111, 119), (103, 174)]
d.polygon(road, fill=(139, 105, 67, 255), outline=(59, 45, 34, 255))
d.line(road + [road[0]], fill=(221, 185, 116, 255), width=4, joint="curve")

# Hand-laid paving stones, each with a small highlight.
stones = [
    (90, 204, 122, 226), (127, 204, 169, 226),
    (103, 178, 132, 198), (137, 178, 162, 198),
    (110, 151, 139, 171), (142, 151, 163, 171),
    (116, 128, 140, 145), (143, 127, 163, 145),
    (119, 105, 142, 121), (144, 104, 163, 121),
    (121, 84, 143, 99), (145, 83, 158, 99),
    (123, 64, 143, 78), (145, 64, 154, 78),
]
for box in stones:
    d.rounded_rectangle(box, radius=5, fill=(99, 100, 91, 255), outline=(49, 52, 49, 255), width=2)
    x0, y0, x1, _ = box
    d.line((x0 + 5, y0 + 4, x1 - 5, y0 + 4), fill=(159, 154, 132, 255), width=2)

# Two warm boot prints make the movement purpose legible at icon scale.
def boot(x: int, y: int, flip: bool = False) -> None:
    if flip:
        d.rounded_rectangle((x + 6, y, x + 17, y + 22), radius=4, fill=(239, 194, 87, 255), outline=(77, 54, 28, 255), width=2)
        d.ellipse((x, y + 15, x + 14, y + 30), fill=(239, 194, 87, 255), outline=(77, 54, 28, 255), width=2)
    else:
        d.rounded_rectangle((x, y, x + 11, y + 22), radius=4, fill=(239, 194, 87, 255), outline=(77, 54, 28, 255), width=2)
        d.ellipse((x + 3, y + 15, x + 17, y + 30), fill=(239, 194, 87, 255), outline=(77, 54, 28, 255), width=2)

boot(116, 178)
boot(137, 144, True)

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
img.save(OUTPUT, format="PNG", optimize=False, compress_level=9)
print(OUTPUT)
