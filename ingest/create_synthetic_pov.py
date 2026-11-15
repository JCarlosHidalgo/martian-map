
from pathlib import Path
from PIL import Image, ImageDraw
import hashlib

path = Path("contracts/fixtures/zone/pov/synthetic_view.jpg")
path.parent.mkdir(parents=True, exist_ok=True)

img = Image.new("RGB", (800, 450), (185, 120, 95))
draw = ImageDraw.Draw(img)

draw.rectangle((0, 0, 800, 230), fill=(185, 120, 95))

draw.polygon(
    [(0, 260), (130, 190), (260, 245),
     (410, 175), (580, 240), (720, 185),
     (800, 230), (800, 450), (0, 450)],
    fill=(130, 75, 55)
)

draw.polygon(
    [(0, 330), (200, 285), (420, 350),
     (620, 290), (800, 335),
     (800, 450), (0, 450)],
    fill=(165, 90, 65)
)

draw.text((25, 25), "MARTIAN MAP - SYNTHETIC POV", fill="white")
draw.text((25, 55), "Illustrative test image - NOT NASA data", fill="white")

img.save(path, "JPEG", quality=90)

sha = hashlib.sha256(path.read_bytes()).hexdigest()

print("Imagem criada:", path)
print("SHA-256:", sha)
