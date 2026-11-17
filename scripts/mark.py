"""Write desktop/Assets/icon.svg: the PickerHub mark, an eyedropper written as one broad-nib stroke in the shape of a P.

The centreline leaves the bulb's neck, runs along the bottom of the bowl, round a superellipse to the
top, turns down into the stem and narrows into the dropper's tip. A broad-nib pen held at a slight angle
sets the width everywhere, so verticals are full, horizontals light and the change between them smooth.
Run it after changing the geometry, then scripts/icons.py to render every size.
"""
import math

NECK = (488, 618)            # where the stroke leaves the bulb
BOWL = (556, 415, 168, 203)  # superellipse centre and semi-axes: right edge 724, top 212, bottom 618
POWER = 2.7                  # 2 is an ellipse; higher is squarer
TOP_TURN = 104               # centreline radius where the top turns into the stem
STEM_X, TIP_Y, TAPER_Y = 310, 836, 666
PEN_THIN, PEN_FULL, PEN_ANGLE = 80, 100, math.radians(-14)

def centreline(step=1.5):
    pts = []
    def line(a, b):
        n = max(1, int(math.dist(a, b) / step))
        for i in range(n): pts.append((a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n))
    cx, cy, ax, by = BOWL
    line(NECK, (cx, cy + by))
    # the bowl: a superellipse from its bottom, round the right, to its top
    n = 900
    for i in range(n):
        th = math.pi / 2 - math.pi * i / n
        c, s = math.cos(th), math.sin(th)
        pts.append((cx + ax * math.copysign(abs(c) ** (2 / POWER), c), cy + by * math.copysign(abs(s) ** (2 / POWER), s)))
    top = cy - by
    line((cx, top), (STEM_X + TOP_TURN, top))
    # a quarter circle turns the top into the stem
    for i in range(int(TOP_TURN * math.pi / 2 / step)):
        a = -math.pi / 2 - (math.pi / 2) * i / int(TOP_TURN * math.pi / 2 / step)
        pts.append((STEM_X + TOP_TURN + TOP_TURN * math.cos(a), top + TOP_TURN + TOP_TURN * math.sin(a)))
    line((STEM_X, top + TOP_TURN), (STEM_X, TIP_Y))
    pts.append((STEM_X, TIP_Y))
    return pts

def smoothstep(t): t = min(1, max(0, t)); return t * t * (3 - 2 * t)

def outline():
    pts = centreline()
    lengths = [0.0]
    for a, b in zip(pts, pts[1:]): lengths.append(lengths[-1] + math.dist(a, b))
    left, right = [], []
    for i, p in enumerate(pts):
        q0, q1 = pts[max(0, i - 3)], pts[min(len(pts) - 1, i + 3)]
        phi = math.atan2(q1[1] - q0[1], q1[0] - q0[0])
        w = PEN_THIN + (PEN_FULL - PEN_THIN) * abs(math.sin(phi - PEN_ANGLE))
        # the neck: slim where it leaves the bulb, full by the time it reaches the bowl's bottom
        w = 58 + (w - 58) * smoothstep(lengths[i] / 70)
        if p[0] < STEM_X + 1 and p[1] > TAPER_Y:
            t = (p[1] - TAPER_Y) / (TIP_Y - TAPER_Y)
            w = w * (1 - t) ** 1.6 * (1 + 1.6 * t) + 3 * t      # flat where it starts, fine at the point
        nx, ny = -math.sin(phi), math.cos(phi)
        left.append((p[0] + nx * w / 2, p[1] + ny * w / 2)); right.append((p[0] - nx * w / 2, p[1] - ny * w / 2))
    return left, right

def bezier(points):
    out = [f"M{points[0][0]:.1f} {points[0][1]:.1f}"]
    for i in range(len(points) - 1):
        p0, p1, p2, p3 = points[max(0, i - 1)], points[i], points[i + 1], points[min(len(points) - 1, i + 2)]
        c1 = (p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6)
        c2 = (p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6)
        out.append(f"C{c1[0]:.1f} {c1[1]:.1f} {c2[0]:.1f} {c2[1]:.1f} {p2[0]:.1f} {p2[1]:.1f}")
    return out

def thin(points, spacing):
    keep = [points[0]]
    for p in points[1:-1]:
        if math.dist(p, keep[-1]) >= spacing: keep.append(p)
    keep.append(points[-1]); return keep

def body(spacing=14):
    left, right = outline()
    a, b = thin(left, spacing), thin(right, spacing)[::-1]
    d = bezier(a) + [f"A1.5 1.5 0 0 1 {b[0][0]:.1f} {b[0][1]:.1f}"] + bezier(b)[1:] + ["Z"]
    return " ".join(d)

def counter(spacing=14):
    """The lens: the inside edge of the bowl, closed straight across from the neck to the stem."""
    left, right = outline()
    pts = centreline()
    # the inside of the bowl is the left-hand offset while the stroke runs anticlockwise round it
    inner = [q for q in right if q[1] < 600]
    inner = thin(inner, spacing)
    return " ".join(bezier(inner) + ["Z"])


BULB = "M507 590 C480 590 471 561 432 561 A58 58 0 0 0 432 677 C471 677 480 648 507 648 Z"
DROP = "M310 852 C314 862 327 868 327 880 A17 17 0 0 1 293 880 C293 868 306 862 310 852 Z"
TONES = ["#2B2B28", "#3A3A36", "#2B2B28", "#3A3A36", "#FFFFFF", "#4A4A45", "#2B2B28", "#4A4A45", "#3A3A36"]


def icon():
    cells = "\n".join(f'        <rect x="{379 + (i % 3) * 96}" y="{277 + (i // 3) * 96}" width="84" height="84" rx="14" fill="{tone}"/>' for i, tone in enumerate(TONES))
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">
  <!-- PickerHub mark: an eyedropper written as one broad-nib stroke in the shape of a P. The rubber bulb
       grows out of a slim neck where the bowl meets the stem, the glass body sweeps round the lens, and the
       stem narrows into the dropper's tip, from which a drop of the picked colour falls. The lens shows the
       pixels under the eyedropper, the picked one white at its centre. Generated by scripts/mark.py. -->
  <defs>
    <linearGradient id="ground" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#232321"/>
      <stop offset="1" stop-color="#0C0C0C"/>
    </linearGradient>
    <linearGradient id="rim" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#FFFFFF" stop-opacity=".28"/>
      <stop offset=".5" stop-color="#FFFFFF" stop-opacity=".05"/>
      <stop offset="1" stop-color="#FFFFFF" stop-opacity=".10"/>
    </linearGradient>
    <clipPath id="tile"><rect width="1024" height="1024" rx="232"/></clipPath>
    <clipPath id="lens"><path d="{counter()}"/></clipPath>
  </defs>
  <g clip-path="url(#tile)">
    <rect width="1024" height="1024" fill="url(#ground)"/>
    <g id="mark">
      <!-- The pixels seen through the lens; dropped at small sizes, where the picked pixel stands alone. -->
      <g id="detail" clip-path="url(#lens)">
        <rect x="300" y="200" width="420" height="420" fill="#141413"/>
{cells}
      </g>
      <rect id="pixel" x="447" y="345" width="140" height="140" rx="30" fill="#FFFFFF" display="none"/>
      <path id="body" fill="#FFF000" d="{body()}"/>
      <path id="bulb" fill="#FFF000" d="{BULB}"/>
      <path id="drop" fill="#FFFFFF" d="{DROP}"/>
    </g>
  </g>
  <rect x="3" y="3" width="1018" height="1018" rx="229" fill="none" stroke="url(#rim)" stroke-width="6"/>
</svg>
'''


if __name__ == "__main__":
    from pathlib import Path
    target = Path(__file__).resolve().parents[1] / "desktop" / "Assets" / "icon.svg"
    target.write_text(icon(), encoding="utf-8")
    print(target, target.stat().st_size)
