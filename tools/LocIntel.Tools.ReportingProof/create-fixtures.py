"""Rebuild synthetic image fixtures; Pillow is needed only to regenerate assets.

These are test illustrations, not a map renderer or actual site photographs.
"""
from pathlib import Path
from PIL import Image, ImageDraw

assets = Path(__file__).parent / "Assets"
image = Image.new("RGB", (1200, 550), "#eaf0e7")
draw = ImageDraw.Draw(image)
draw.polygon([(0, 350), (400, 270), (850, 440), (1200, 330), (1200, 470), (800, 550), (400, 390), (0, 450)], fill="#a3ceda")
for x in range(100, 1200, 200):
    draw.line([(x, 0), (x, 550)], fill="white", width=22)
for y in (100, 240):
    draw.line([(0, y), (1200, y)], fill="white", width=22)
draw.polygon([(320, 120), (650, 140), (730, 320), (430, 280)], fill="#c9a9e0", outline="#663c83", width=5)
draw.polygon([(450, 175), (560, 180), (590, 235), (470, 225)], fill="#eaf0e7", outline="#663c83", width=4)
for x, y in [(200, 150), (780, 160), (920, 410)]:
    draw.ellipse((x-12, y-12, x+12, y+12), fill="#b52d36", outline="white", width=3)
image.save(assets / "map.png")

image = Image.new("RGB", (1200, 700), "#b8d7e3")
draw = ImageDraw.Draw(image)
draw.rectangle((0, 510, 1200, 700), fill="#829582")
draw.rectangle((180, 170, 1000, 570), fill="#c7bcaa")
draw.polygon([(160, 170), (580, 70), (1030, 170)], fill="#485565")
for x in range(240, 1000, 150):
    for y in (240, 370):
        draw.rectangle((x, y, x+80, y+85), fill="#526c7a", outline="#f4f0e7", width=8)
draw.rectangle((550, 430, 640, 570), fill="#4d5057")
draw.polygon([(550, 570), (640, 570), (840, 700), (420, 700)], fill="#d1d0c8")
image.save(assets / "photo.jpg", quality=90)
