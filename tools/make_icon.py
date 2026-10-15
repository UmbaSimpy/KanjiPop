"""Generate src/app.ico (cute kana badge). Needs Pillow + a Japanese font (Yu Gothic)."""
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

# gradient squircle (peach pink -> lavender)
grad = Image.new("RGBA", (S, S))
px = grad.load()
c1, c2 = (255, 154, 190), (150, 130, 255)
for y in range(S):
    for x in range(S):
        t = (x + y) / (2 * S)
        px[x, y] = tuple(int(c1[i] + (c2[i] - c1[i]) * t) for i in range(3)) + (255,)
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle((24, 24, S - 24, S - 24), radius=300, fill=255)
img.paste(grad, (0, 0), mask)

# soft highlight
hl = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(hl).ellipse((120, 60, 700, 420), fill=(255, 255, 255, 70))
hl = hl.filter(ImageFilter.GaussianBlur(40))
img = Image.alpha_composite(img, Image.composite(hl, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))

d = ImageDraw.Draw(img)
font = None
for f in ("C:/Windows/Fonts/YuGothB.ttc", "C:/Windows/Fonts/meiryob.ttc", "C:/Windows/Fonts/msgothic.ttc"):
    if os.path.exists(f):
        font = ImageFont.truetype(f, 640)
        break
# shadow + glyph
d.text((S // 2 + 14, S // 2 - 20 + 14), "あ", font=font, anchor="mm", fill=(120, 70, 170, 110))
d.text((S // 2, S // 2 - 20), "あ", font=font, anchor="mm", fill=(255, 255, 255, 255))
# blush cheeks
for cx in (205, S - 205):
    d.ellipse((cx - 75, 690 - 45, cx + 75, 690 + 45), fill=(255, 110, 150, 150))
# sparkle
d.polygon([(800, 150), (825, 215), (890, 240), (825, 265), (800, 330), (775, 265), (710, 240), (775, 215)],
          fill=(255, 255, 255, 235))

out = os.path.join(os.path.dirname(__file__), "..", "src", "app.ico")
img.resize((256, 256), Image.LANCZOS).save(out, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
img.resize((256, 256), Image.LANCZOS).save(os.path.join(os.path.dirname(__file__), "..", "icon-preview.png"))
print("wrote", out)
