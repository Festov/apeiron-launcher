"""Build a fully opaque square app icon (no white corner fringes)."""
from __future__ import annotations

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Assets"
SRC = ASSETS / "icon.png"
if not SRC.exists():
    SRC = ASSETS / "icon.ico"
OUT_ICO = ASSETS / "icon.ico"
OUT_PNG = ASSETS / "icon.png"
BG = (11, 12, 16)
SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def load_rgba() -> Image.Image:
    img = Image.open(SRC)
    if getattr(img, "n_frames", 1) > 1 or SRC.suffix.lower() == ".ico":
        best = None
        i = 0
        while True:
            try:
                img.seek(i)
                frame = img.convert("RGBA")
                if best is None or frame.size[0] > best.size[0]:
                    best = frame.copy()
                i += 1
            except EOFError:
                break
        return best or img.convert("RGBA")
    return img.convert("RGBA")


def is_fringe(r: int, g: int, b: int) -> bool:
    mx, mn = max(r, g, b), min(r, g, b)
    if r > 170 and g > 170 and b > 170:
        return True
    if mx - mn < 40 and mn > 60:
        return True
    return False


def to_square(src: Image.Image) -> Image.Image:
    if src.size != (256, 256):
        src = src.resize((256, 256), Image.Resampling.LANCZOS)
    w, h = src.size
    out = Image.new("RGBA", (w, h), (*BG, 255))
    sp = src.load()
    op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, _ = sp[x, y]
            edge = min(x, y, w - 1 - x, h - 1 - y)
            if is_fringe(r, g, b):
                continue
            mx, mn = max(r, g, b), min(r, g, b)
            if edge <= 28 and mx - mn < 45 and mn > 35:
                continue
            op[x, y] = (r, g, b, 255)
    return out


def scrub(img: Image.Image) -> Image.Image:
    img = img.copy()
    px = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            r, g, b, _ = px[x, y]
            if is_fringe(r, g, b):
                px[x, y] = (*BG, 255)
            else:
                px[x, y] = (r, g, b, 255)
    return img


def main() -> None:
    square = to_square(load_rgba())
    for pos in ((0, 0), (255, 0), (0, 255), (255, 255), (8, 8)):
        print("pixel", pos, square.getpixel(pos))

    square.save(OUT_PNG, format="PNG")
    frames = [scrub(square.resize(size, Image.Resampling.LANCZOS)) for size in SIZES]
    frames[-1].save(
        OUT_ICO,
        format="ICO",
        sizes=[f.size for f in frames],
        append_images=frames[:-1],
    )
    print(f"wrote {OUT_ICO} ({OUT_ICO.stat().st_size} bytes)")
    print(f"wrote {OUT_PNG} ({OUT_PNG.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
