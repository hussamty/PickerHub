"""Render desktop/Assets/icon.svg into the PNG, ICO and ICNS files the app and its packages use.

Needs ffmpeg built with librsvg, and Pillow. Run after editing the SVG; the outputs are committed.
"""
import subprocess
import tempfile
from pathlib import Path

from PIL import Image

ASSETS = Path(__file__).resolve().parents[1] / "desktop" / "Assets"
SOURCE = (ASSETS / "icon.svg").read_text(encoding="utf-8")
# At small sizes the neighbouring pixels in the lens would blur into noise: the mark is drawn
# larger, with the picked pixel alone in the lens.
SMALL = SOURCE.replace('<g id="detail"', '<g id="detail" display="none"', 1) \
              .replace(' fill="#FFFFFF" display="none"/>', ' fill="#FFFFFF"/>', 1) \
              .replace('transform="translate(-30 -12)"', 'transform="translate(512 512) scale(1.1) translate(-542 -522)"', 1)


def render(svg, size):
    with tempfile.TemporaryDirectory() as work:
        source, target = Path(work) / "icon.svg", Path(work) / "icon.png"
        source.write_text(svg, encoding="utf-8")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-width", "1024", "-height", "1024", "-i", source, "-frames:v", "1", target], check=True)
        return Image.open(target).convert("RGBA").resize((size, size), Image.LANCZOS)


def main():
    assert SMALL.count('display="none"') == 1 and "scale(1.1)" in SMALL, "the small variant no longer matches the SVG structure"
    images = {size: render(SMALL if size <= 64 else SOURCE, size) for size in (16, 24, 32, 48, 64, 128, 256, 512, 1024)}
    for size in (16, 24, 32, 48, 64, 128, 256, 512):
        images[size].save(ASSETS / f"icon-{size}.png", optimize=True)
    render(SMALL, 128).save(ASSETS / "icon-small-128.png", optimize=True)
    images[256].save(ASSETS / "app.ico", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)], append_images=[images[s] for s in (16, 24, 32, 48, 64, 128)])
    images[1024].save(ASSETS / "app.icns", append_images=[images[s] for s in (16, 32, 64, 128, 256, 512)])
    for path in sorted(ASSETS.iterdir()):
        print(f"{path.name:14} {path.stat().st_size:>8}")


if __name__ == "__main__":
    main()
