"""Cut the interface fonts into fixed-weight instances.

Every platform then matches weights by OS/2 class, without depending on variable-font support or synthetic bold.
Download the upstream variable fonts from github.com/google/fonts (ofl/plusjakartasans and ofl/notokufiarabic)
into one folder, then run:

    uv run --with fonttools python scripts/fonts.py <folder> desktop/Fonts
"""
import sys
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

source, out = Path(sys.argv[1]), Path(sys.argv[2])
WEIGHTS = {400: "Regular", 500: "Medium", 600: "SemiBold", 700: "Bold"}
for file, family, stem in [("PlusJakartaSans[wght].ttf", "Plus Jakarta Sans", "PlusJakartaSans"), ("NotoKufiArabic[wght].ttf", "Noto Kufi Arabic", "NotoKufiArabic")]:
    for weight, style in WEIGHTS.items():
        font = instancer.instantiateVariableFont(TTFont(source / file), {"wght": weight})
        name = font["name"]
        # One family name on every platform: Windows, macOS and FreeType all group the four files together.
        for nid in (16, 17, 21, 22, 25):
            name.removeNames(nameID=nid)
        records = {1: family, 2: style, 4: f"{family} {style}" if style != "Regular" else family, 6: f"{stem}-{style}"}
        for nid, text in records.items():
            name.removeNames(nameID=nid)
            name.setName(text, nid, 3, 1, 0x409)
            name.setName(text, nid, 1, 0, 0)
        name.setName(f"{family} {style}", 3, 3, 1, 0x409)
        os2 = font["OS/2"]
        os2.usWeightClass = weight
        bits = os2.fsSelection & ~((1 << 0) | (1 << 5) | (1 << 6))
        os2.fsSelection = bits | ((1 << 5) if weight == 700 else (1 << 6) if weight == 400 else 0)
        font["head"].macStyle = 1 if weight == 700 else 0
        for table in ("STAT", "fvar", "avar", "gvar", "HVAR", "MVAR", "cvar"):
            if table in font: del font[table]
        if stem == "NotoKufiArabic":
            # The stock line box leaves 1.28 em above the baseline for Quranic marks the interface never uses,
            # so Arabic sat low in every pill. Letters, hamza and tanween reach 1.04 em; tails and dots -0.47 em.
            # A 1.08/0.52 box holds them all and centres the letter bodies the way Latin caps are centred.
            hhea = font["hhea"]; hhea.ascent, hhea.descent, hhea.lineGap = 1080, -520, 0
            os2.sTypoAscender, os2.sTypoDescender, os2.sTypoLineGap = 1080, -520, 0
        target = out / f"{stem}-{style}.ttf"
        font.save(target)
        check = TTFont(target)
        print(f"{target.name:32} {target.stat().st_size:>8}  {check['name'].getDebugName(1)} / {check['name'].getDebugName(2)} / weight {check['OS/2'].usWeightClass}")
for file in ("PlusJakartaSans-Regular.ttf", "NotoKufiArabic-Regular.ttf"):
    f = TTFont(out / file); h, o = f["hhea"], f["OS/2"]
    print(file, "upm", f["head"].unitsPerEm, "hhea", h.ascent, h.descent, h.lineGap, "typo", o.sTypoAscender, o.sTypoDescender, o.sTypoLineGap, "win", o.usWinAscent, o.usWinDescent, "x", getattr(o, "sxHeight", None), "cap", getattr(o, "sCapHeight", None), "useTypo", bool(o.fsSelection & 128))
