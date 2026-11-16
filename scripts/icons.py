"""Render desktop/Assets/icon.svg into the PNG, ICO and ICNS files the app and its packages use.

Needs ffmpeg built with librsvg, and Pillow. Run after editing the SVG; the outputs are committed.
"""
import subprocess
import tempfile
from pathlib import Path

from PIL import Image

ASSETS = Path(__file__).resolve().parents[1] / "desktop" / "Assets"
SOURCE = (ASSETS / "icon.svg").read_text(encoding="utf-8")
# At small sizes the mascot is drawn larger inside the tile, so its eyes stay readable.
TIGHT = SOURCE.replace('<g clip-path="url(#tile)">', '<g clip-path="url(#tile)"><g transform="translate(512 540) scale(1.24) translate(-512 -540)">', 1) \
              .replace('  </g>\n  <rect x="3"', '  </g></g>\n  <rect x="3"', 1) \
              .replace('<rect width="1024" height="1024" fill="url(#ground)"/>', '<rect x="-200" y="-200" width="1424" height="1424" fill="url(#ground)"/>', 1)


def render(svg, size):
    with tempfile.TemporaryDirectory() as work:
        source, target = Path(work) / "icon.svg", Path(work) / "icon.png"
        source.write_text(svg, encoding="utf-8")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-width", "1024", "-height", "1024", "-i", source, "-frames:v", "1", target], check=True)
        return Image.open(target).convert("RGBA").resize((size, size), Image.LANCZOS)


def main():
    assert TIGHT != SOURCE and TIGHT.count("</g></g>") == 1, "the tight variant no longer matches the SVG structure"
    images = {size: render(TIGHT if size <= 48 else SOURCE, size) for size in (16, 24, 32, 48, 64, 128, 256, 512, 1024)}
    for size in (16, 24, 32, 48, 64, 128, 256, 512):
        images[size].save(ASSETS / f"icon-{size}.png", optimize=True)
    render(TIGHT, 128).save(ASSETS / "icon-small-128.png", optimize=True)
    images[256].save(ASSETS / "app.ico", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)], append_images=[images[s] for s in (16, 24, 32, 48, 64, 128)])
    images[1024].save(ASSETS / "app.icns", append_images=[images[s] for s in (16, 32, 64, 128, 256, 512)])
    for path in sorted(ASSETS.iterdir()):
        print(f"{path.name:14} {path.stat().st_size:>8}")


if __name__ == "__main__":
    main()
