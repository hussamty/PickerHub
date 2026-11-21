<script lang="ts" module>
  export interface Card { hex: string; ink: string; label: string; code: string; name: string }

  /** A value pulled toward a target by a damped spring. All motion on the stage is made of these: a
   *  cue only sets a target or gives a push, and the spring supplies the ease and the bounce. */
  class Spring {
    x: number; v = 0; to: number;
    constructor(at = 0) { this.x = this.to = at; }
    step(dt: number, stiffness: number, damping: number) { this.v += (-stiffness * (this.x - this.to) - damping * this.v) * dt; this.x += this.v * dt; }
    still(within: number) { return Math.abs(this.x - this.to) < within && Math.abs(this.v) < within * 10; }
    snap(value: number) { this.x = this.to = value; this.v = 0; }
  }
  interface Motion { x: Spring; y: Spring; pitch: Spring; turn: Spring; roll: Spring; size: Spring; stiff: number; damp: number; shown: boolean }
  interface Piece { x: number; y: number; vx: number; vy: number; depth: number; size: number; angle: number; spin: number; flip: number; tumble: number; sway: number; age: number; life: number; shape: number; tone: string }

  const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));
  const smooth = (t: number) => t * t * (3 - 2 * t);
  /** An ARGB word as a CSS colour. */
  const argb = (u: number) => `rgba(${(u >>> 16) & 255},${(u >>> 8) & 255},${u & 255},${(((u >>> 24) & 255) / 255).toFixed(3)})`;
  function mix(a: number, b: number, t: number) {
    t = clamp(t, 0, 1);
    const c = (s: number) => Math.round(((a >>> s) & 255) + (((b >>> s) & 255) - ((a >>> s) & 255)) * t);
    return `rgba(${c(16)},${c(8)},${c(0)},${(c(24) / 255).toFixed(3)})`;
  }
  /** A small deterministic generator, so the confetti falls the same way each time (1.x seeds with 7). */
  function seeded(seed: number) {
    return () => { seed = (seed + 0x6d2b79f5) | 0; let r = Math.imul(seed ^ (seed >>> 15), 1 | seed); r = (r + Math.imul(r ^ (r >>> 7), 61 | r)) ^ r; return ((r ^ (r >>> 14)) >>> 0) / 4294967296; };
  }
</script>

<script lang="ts">
  // The palette-of-the-day scene (desktop/PackStage.cs): a sealed envelope that opens, deals its cards
  // and falls away, and afterwards the same cards at rest in a row. The envelope is drawn in two
  // layers so the cards sit between its back and its front pocket; its flap turns about the top edge,
  // seen through the stage's lens: closed it lies over the pocket (front layer), past upright it
  // stands behind the cards and shows its lining (back layer). Confetti flies in two depths: far
  // pieces small, slow and soft, a few near ones large and out of focus over everything. Every motion
  // is a spring, stepped at 240 Hz so the stiff ones stay stable at any frame rate.
  import { onMount, tick } from "svelte";
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";
  import markSource from "../assets/icon.svg?raw";

  let { cards = [], sealed, serial = 1, onopen, oncopy, ondone }: {
    cards?: Card[]; sealed: boolean; serial?: number; onopen?: () => Promise<Card[] | null>; oncopy?: (c: Card) => void; ondone?: () => void;
  } = $props();

  // ---- Geometry (desktop/PackStage.cs) ----
  const CW = 150, CH = 210, TALL = 400, SHORT = 300, TOP = 44, LENS = 900;
  const W = 188, H = 244, HEAD = 120, RIM = 62, CORNER = 10, MOUTH = 9, APEX = 128, TIP = 16, SEAL_R = 19, SEAL_Y = 92;
  // Paint reaches past the envelope for its shadow; the flap's own canvas keeps a small margin.
  const RX = 48, RY = 24, RW = W + 96, RH = H + HEAD + 72, FM = 6, FW = W + 2 * FM, FH = APEX + 2 * FM;
  const ENV_Y = TALL - 30 - H - HEAD;
  const TONES = ["#FF5FA2", "#8B5CF6", "#3B9DFF", "#FFF000", "#34D399", "#FF8A3D"];
  // A shape drawn this far to the left of the canvas leaves only its blurred shadow in view.
  const OFF = 4000;

  let stage: HTMLElement;
  let width = $state(900);
  // svelte-ignore state_referenced_locally
  let phase = $state<"sealed" | "opening" | "resting">(sealed ? "sealed" : "resting");
  // svelte-ignore state_referenced_locally
  let shown = $state<Card[]>(sealed ? [] : cards);
  let hovered = $state(-1);
  let cardEls: HTMLElement[] = $state([]);
  let shineEls: HTMLElement[] = $state([]);
  let backLayer = $state<HTMLElement>(), frontLayer = $state<HTMLElement>();
  let backCanvas = $state<HTMLCanvasElement>(), liningCanvas = $state<HTMLCanvasElement>(), frontCanvas = $state<HTMLCanvasElement>(), flapCanvas = $state<HTMLCanvasElement>();
  let farCanvas: HTMLCanvasElement, nearCanvas: HTMLCanvasElement, lightEl: HTMLElement, floorEl = $state<HTMLElement>();

  // Plain values: the frame loop reads and writes them and poses the elements directly.
  let motions: Motion[] = [];
  const pitch = new Spring(), turn = new Spring(), flap = new Spring(), push = new Spring();
  let clock = 0, idle = 0, dropAt = -1, pressed = false, busy = false, placed = false;
  let cues: [number, () => void][] = [];
  let far: Piece[] = [], near: Piece[] = [];
  const random = seeded(7);
  let raf = 0, last = 0;
  let painted = { angle: -1, light: -9, key: "" };
  let lightKey = "";

  const dark = $derived(app.theme === "dark");
  const height = $derived(phase === "resting" ? SHORT : TALL);
  const n = () => shown.length;
  const mid = () => (n() - 1) / 2;
  // The first role sits where reading starts: on the right in Arabic, on the left in English.
  const place = (i: number) => (app.language === "ar" ? n() - 1 - i : i);
  const step = () => (n() < 2 ? 0 : Math.min(CW + 14, (width - 24 - CW) / (n() - 1)));
  /** A card's resting place: a shallow arc, the outer cards a little lower and leaning a little outward. */
  function slot(i: number) {
    const s = step(), total = s * (n() - 1) + CW, p = place(i);
    return { x: (width - total) / 2 + p * s, y: TOP + Math.pow(Math.abs(p - mid()), 2) * 1.5, w: CW, h: CH };
  }
  const restRoll = (i: number) => (place(i) - mid()) * 1.3;
  const envX = () => (width - W) / 2;
  const mouth = () => ({ x: width / 2, y: ENV_Y + HEAD + RIM });
  const bodyHit = (x: number, y: number) => x >= envX() - 18 && x <= envX() + W + 18 && y >= ENV_Y + HEAD - 18 && y <= ENV_Y + HEAD + H + 18;

  // ---- The envelope's outlines: one radius for all outer corners, a V-shaped mouth with a filleted
  // point, and straight flap edges that meet in a filleted tip. ----
  function build(draw: (p: Path2D) => void) { const p = new Path2D(); draw(p); return p; }
  const bodyShape = build((p) => { p.moveTo(CORNER, 0); p.arcTo(W, 0, W, H, CORNER); p.arcTo(W, H, 0, H, CORNER); p.arcTo(0, H, 0, 0, CORNER); p.arcTo(0, 0, W, 0, CORNER); p.closePath(); });
  const pocketShape = build((p) => { p.moveTo(0, CORNER); p.arcTo(W / 2, RIM, W, CORNER, MOUTH); p.lineTo(W, CORNER); p.arcTo(W, H, 0, H, CORNER); p.arcTo(0, H, 0, 0, CORNER); p.closePath(); });
  const lipLine = build((p) => { p.moveTo(0, CORNER); p.arcTo(W / 2, RIM, W, CORNER, MOUTH); p.lineTo(W, CORNER); });
  const pocketRim = build((p) => { p.moveTo(W, CORNER); p.arcTo(W, H, 0, H, CORNER); p.arcTo(0, H, 0, 0, CORNER); p.lineTo(0, CORNER); });
  // The inside of the back, seen through the mouth; it runs 1.5 px under the pocket so the two leave no seam.
  const inside = (c: number) => build((p) => { p.moveTo(0, CORNER + 1.5); p.arcTo(0, 0, W, 0, c); p.arcTo(W, 0, W, H, c); p.lineTo(W, CORNER + 1.5); p.arcTo(W / 2, RIM + 1.5, 0, CORNER + 1.5, MOUTH); p.closePath(); });
  const insideShape = inside(CORNER);
  const flapShape = (c: number) => build((p) => { p.moveTo(0, c); p.arcTo(0, 0, W, 0, c); p.arcTo(W, 0, W, H, c); p.arcTo(W / 2, APEX, 0, c, TIP); p.closePath(); });
  const flapEdge = (c: number) => build((p) => { p.moveTo(c, 0); p.arcTo(0, 0, 0, H, c); p.arcTo(W / 2, APEX, W, c, TIP); p.lineTo(W, c); p.arcTo(W, 0, 0, 0, c); });
  const closedFlap = flapShape(CORNER), closedRim = flapEdge(CORNER);
  // Closed, the top corners are the flap's and the back's together, rounded as one. Opened, two rounded
  // corners would pinch the fold into ears, so both square off while the flap stands past upright.
  const fold = (deg: number) => CORNER * (1 - smooth(clamp((deg - 90) / 60, 0, 1)));

  // The PickerHub mark, read from the app icon's own source so the seal and the icon never drift apart.
  const mark: [Path2D, string][] = (() => {
    const doc = new DOMParser().parseFromString(markSource, "image/svg+xml");
    const out: [Path2D, string][] = [];
    for (const id of ["body", "bulb", "drop"]) {
      const e = doc.getElementById(id);
      if (e) out.push([new Path2D(e.getAttribute("d")!), e.getAttribute("fill")!]);
    }
    const r = doc.getElementById("pixel");
    if (r) {
      const f = (k: string) => parseFloat(r.getAttribute(k)!);
      out.push([build((p) => { const [x, y, w, h, rx] = [f("x"), f("y"), f("width"), f("height"), f("rx")]; p.moveTo(x + rx, y); p.arcTo(x + w, y, x + w, y + h, rx); p.arcTo(x + w, y + h, x, y + h, rx); p.arcTo(x, y + h, x, y, rx); p.arcTo(x, y, x + w, y, rx); p.closePath(); }), r.getAttribute("fill")!]);
    }
    return out;
  })();

  // ---- Painting ----
  /** Sizes a canvas for painting. The envelope's canvases lean and turn in 3D on the GPU, which
   *  stretches their texture at the near side and softens edges and type; painting them oversampled
   *  (2x the CSS size on a 1x screen, 3x on a 2x screen) means the GPU only ever shrinks them. */
  function prepare(canvas: HTMLCanvasElement | undefined, w: number, h: number, oversample = false) {
    if (!canvas) return null;
    const screen = devicePixelRatio || 1;
    const dpr = oversample ? Math.min(4, Math.max(2, screen * 1.5)) : screen;
    if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) { canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr); }
    const ctx = canvas.getContext("2d")!;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    return { ctx, dpr };
  }
  /** Draws only the blurred shadow of what `draw` paints: a true Gaussian of the given sigma. */
  function blurred(ctx: CanvasRenderingContext2D, dpr: number, sigma: number, color: string, draw: () => void, dx = 0, dy = 0) {
    ctx.save();
    const m = ctx.getTransform();
    ctx.setTransform(m.a, m.b, m.c, m.d, m.e - OFF * dpr, m.f);
    ctx.shadowColor = color;
    ctx.shadowBlur = sigma * 2 * dpr;
    ctx.shadowOffsetX = (OFF + dx) * dpr;
    ctx.shadowOffsetY = dy * dpr;
    ctx.fillStyle = ctx.strokeStyle = "#000";
    draw();
    ctx.restore();
  }
  // Brand-yellow paper: lighter where it faces the light, with a soft specular band that follows the
  // pointer and a faint pearl tint at the band's edges.
  function paper(ctx: CanvasRenderingContext2D, shape: Path2D, top: number, bottom: number, lift: number, light: number) {
    const fill = ctx.createLinearGradient(0, top, 0, bottom);
    fill.addColorStop(0, mix(0xfffff45e, 0xfffff9a6, lift));
    fill.addColorStop(0.5, mix(0xfffff000, 0xfffff462, lift));
    fill.addColorStop(1, mix(0xfff5dd00, 0xfffbeb3a, lift));
    ctx.fillStyle = fill;
    ctx.fill(shape);
    const at = 0.5 + clamp(light, -1, 1) * 0.22;
    ctx.save();
    ctx.clip(shape);
    const band = ctx.createLinearGradient(-60, top, W + 60, bottom);
    for (const [c, s] of [[0x00ffffff, 0], [0x00ffffff, at - 0.2], [0x24ffe4f1, at - 0.08], [0x5cffffff, at], [0x24d8f1ff, at + 0.08], [0x00ffffff, at + 0.2], [0x00ffffff, 1]]) band.addColorStop(s, argb(c));
    ctx.fillStyle = band;
    ctx.fillRect(0, top, W, bottom - top);
    ctx.restore();
  }
  // A line along an outline, clipped inside the shape: half a stroke wide, and it never doubles the outer edge.
  function edge(ctx: CanvasRenderingContext2D, shape: Path2D, line: Path2D, color: number, w: number) {
    ctx.save();
    ctx.clip(shape);
    ctx.lineWidth = w; ctx.lineJoin = "round"; ctx.lineCap = "round"; ctx.strokeStyle = argb(color);
    ctx.stroke(line);
    ctx.restore();
  }
  // The lining: deep charcoal with a fine yellow pinstripe, as good stationery is lined.
  function lining(ctx: CanvasRenderingContext2D, shape: Path2D, top: number, bottom: number) {
    const fill = ctx.createLinearGradient(0, top, 0, bottom);
    fill.addColorStop(0, "#2A2A27"); fill.addColorStop(1, "#161615");
    ctx.fillStyle = fill;
    ctx.fill(shape);
    ctx.save();
    ctx.clip(shape);
    ctx.strokeStyle = argb(0x14fff000); ctx.lineWidth = 1;
    ctx.beginPath();
    const h = bottom - top;
    for (let k = -h; k < W; k += 6) { ctx.moveTo(k, bottom); ctx.lineTo(k + h, top); }
    ctx.stroke();
    ctx.restore();
  }
  // The seal: a disc of brand black with a fine yellow ring and the PickerHub mark.
  function seal(ctx: CanvasRenderingContext2D, dpr: number, x: number, y: number) {
    blurred(ctx, dpr, 2.2, "rgba(0,0,0,.4)", () => { ctx.beginPath(); ctx.arc(x, y + 1.5, SEAL_R, 0, Math.PI * 2); ctx.fill(); });
    const disc = ctx.createRadialGradient(x - SEAL_R * 0.35, y - SEAL_R * 0.45, 0, x - SEAL_R * 0.35, y - SEAL_R * 0.45, SEAL_R * 1.7);
    disc.addColorStop(0, "#41413C"); disc.addColorStop(0.5, "#161615"); disc.addColorStop(1, "#0A0A0A");
    ctx.fillStyle = disc;
    ctx.beginPath(); ctx.arc(x, y, SEAL_R, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = argb(0x99fff000); ctx.lineWidth = 1;
    ctx.beginPath(); ctx.arc(x, y, SEAL_R - 3.5, 0, Math.PI * 2); ctx.stroke();
    ctx.save();
    // The mark is 726 units tall, centred on (518, 534).
    ctx.translate(x, y); ctx.scale((SEAL_R * 1.1) / 726, (SEAL_R * 1.1) / 726); ctx.translate(-518, -534);
    for (const [shape, fill] of mark) { ctx.fillStyle = fill; ctx.fill(shape); }
    ctx.restore();
  }

  function paintBack(deg: number) {
    const s = prepare(backCanvas, RW, RH, true);
    if (!s) return;
    const { ctx, dpr } = s;
    ctx.translate(RX, RY + HEAD);
    blurred(ctx, dpr, 9, `rgba(0,0,0,${dark ? 0.45 : 0.16})`, () => ctx.fill(bodyShape), 0, 7);
    // Closed, the flap and pocket cover the inside exactly; drawing it would only darken their edges.
    if (deg < 0.5) return;
    const c = fold(deg);
    lining(ctx, c < CORNER ? inside(c) : insideShape, 0, RIM + 2);
  }
  // Opened past upright, the flap stands behind the cards and shows its lining. Its element is turned
  // about the hinge, so its back face shows: the lining is painted in the flap's own frame.
  function paintLining(deg: number) {
    const s = prepare(liningCanvas, FW, FH, true);
    if (!s || deg <= 90) return;
    const { ctx } = s;
    ctx.translate(FM, FM);
    const c = fold(deg), face = flapShape(c);
    // The flap reaches 1.5 px back across the fold, so the fold leaves no seam either.
    const whole = new Path2D(face); whole.rect(c, -1.5, W - 2 * c, 2);
    lining(ctx, whole, -2, APEX);
    edge(ctx, face, flapEdge(c), 0xf2fff000, 1.8);
  }
  function paintFront(deg: number, light: number) {
    const s = prepare(frontCanvas, RW, RH, true);
    if (!s) return;
    const { ctx, dpr } = s;
    ctx.translate(RX, RY);
    ctx.save();
    ctx.translate(0, HEAD);
    if (deg > 2) {
      // The pocket's lip shades whatever stands just inside it.
      ctx.save(); ctx.clip(insideShape); ctx.translate(0, -3);
      blurred(ctx, dpr, 5, argb(0x6b000000), () => { ctx.lineWidth = 12; ctx.stroke(lipLine); });
      ctx.restore();
    }
    paper(ctx, pocketShape, 0, H, 0, light);
    edge(ctx, pocketShape, pocketRim, dark ? 0x1f000000 : 0x33000000, 1.4);
    edge(ctx, pocketShape, lipLine, 0xc2ffffff, 1.8);
    if (deg < 88) {
      // The closed flap lifts off the pocket a little, and its shadow lengthens as it rises.
      const k = Math.cos((deg * Math.PI) / 180);
      ctx.save(); ctx.clip(pocketShape); ctx.translate(0, 2.2 + 5 * (1 - k)); ctx.scale(1, Math.max(0.001, k));
      blurred(ctx, dpr, 3.2 + 4 * (1 - k), `rgba(0,0,0,${((78 * k) / 255).toFixed(3)})`, () => ctx.fill(closedFlap));
      ctx.restore();
    }
    ctx.restore();
    // The printed face: the name of the pack, debossed, and a stamp line with the maker and the number.
    const family = getComputedStyle(stage).fontFamily;
    const name = t("باقة اليوم");
    let size = 21;
    ctx.font = `600 ${size}px ${family}`;
    while (size > 15 && ctx.measureText(name).width > W - 36) { size -= 0.5; ctx.font = `600 ${size}px ${family}`; }
    ctx.textAlign = "center"; ctx.textBaseline = "middle";
    ctx.direction = app.language === "ar" ? "rtl" : "ltr";
    const y = HEAD + H * 0.655;
    ctx.fillStyle = argb(0x66ffffff); ctx.fillText(name, W / 2, y + 0.9);
    ctx.fillStyle = "#111111"; ctx.fillText(name, W / 2, y);
    ctx.direction = "ltr";
    const line = HEAD + H - 21;
    ctx.font = `700 10.5px ${family}`; ctx.textAlign = "left"; ctx.fillText("PickerHub", 18, line);
    ctx.font = `600 10.5px ${family}`; ctx.textAlign = "right"; ctx.fillText(`No. ${String(serial).padStart(2, "0")}`, W - 18, line);
    ctx.strokeStyle = argb(0x2e111111); ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(18, line - 14.5); ctx.lineTo(W - 18, line - 14.5); ctx.stroke();
  }
  // Closed, the flap lies over the pocket; it catches more light as it lifts toward the viewer.
  function paintFlap(deg: number, light: number) {
    const s = prepare(flapCanvas, FW, FH, true);
    if (!s || deg >= 90) return;
    const { ctx, dpr } = s;
    ctx.translate(FM, FM);
    paper(ctx, closedFlap, 0, APEX, Math.sin((deg * Math.PI) / 180) * 0.8, light + 0.18);
    edge(ctx, closedFlap, closedRim, 0x9effffff, 1.8);
    seal(ctx, dpr, W / 2, SEAL_Y);
  }
  function paintEnvelope(deg: number, light: number) {
    const key = `${dark}|${app.language}|${serial}`;
    const turned = Math.abs(deg - painted.angle) > 0.05, lit = Math.abs(light - painted.light) > 0.004, other = key !== painted.key;
    if (!turned && !lit && !other) return;
    if (turned || other) { paintBack(deg); paintLining(deg); }
    paintFront(deg, light);
    paintFlap(deg, light);
    painted = { angle: deg, light, key };
  }

  // ---- Confetti ----
  // Throw pieces up and out of a point. Each depth keeps only its own pieces, as 1.x's two layers did.
  function burst(from: { x: number; y: number }, count: number, force = 1) {
    for (const layer of [far, near]) {
      for (let i = 0; i < count; i++) {
        const depth = 0.55 + random() * 1.05, angle = -Math.PI / 2 + (random() - 0.5) * 2.2, speed = (360 + random() * 620) * force * depth;
        const piece: Piece = {
          x: from.x + (random() - 0.5) * 90, y: from.y, vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed,
          depth, size: 6 + random() * 8, angle: random() * 360, spin: (random() - 0.5) * 520,
          flip: random() * 6.28, tumble: 4 + random() * 9, sway: random() * 6.28, age: 0, life: 2.3 + random() * 1.1,
          shape: Math.floor(random() * 4), tone: TONES[Math.floor(random() * TONES.length)],
        };
        if (depth > 1.34 === (layer === near)) layer.push(piece);
      }
    }
  }
  function fly(list: Piece[], dt: number) {
    const slow = Math.exp(-1.9 * dt);
    for (let i = list.length - 1; i >= 0; i--) {
      const p = list[i];
      p.age += dt;
      if (p.age >= p.life || p.y > height + 30) { list.splice(i, 1); continue; }
      // Air takes the speed off quickly, then the piece drifts down, swaying as it tumbles.
      p.vx *= slow; p.vy = p.vy * slow + 520 * p.depth * dt;
      p.x += (p.vx + Math.sin(p.age * 3.1 + p.sway) * 34 * p.depth) * dt; p.y += p.vy * dt;
      p.angle += p.spin * dt; p.flip += p.tumble * dt;
    }
  }
  function rr(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
    ctx.beginPath(); ctx.moveTo(x + r, y); ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r); ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath();
  }
  let farDrawn = false, nearDrawn = false;
  function paintConfetti(canvas: HTMLCanvasElement, list: Piece[], isNear: boolean, drawn: boolean) {
    if (!list.length && !drawn) return false;
    const s = prepare(canvas, width, height);
    if (!s) return false;
    const { ctx, dpr } = s;
    for (const p of list) {
      // Pieces fade out with age and toward the stage's top and bottom, so none is cut off at an edge.
      const fade = clamp((p.life - p.age) / 0.55, 0, 1) * clamp((height - p.y) / 46, 0, 1) * clamp(p.y / 34, 0, 1) * clamp(p.age / 0.05, 0, 1);
      if (fade <= 0) continue;
      const turnK = Math.cos(p.flip), size = p.size * p.depth;
      // The underside of a piece is a little darker than its face.
      const shade = turnK < 0 ? 0.8 : 1, v = parseInt(p.tone.slice(1), 16);
      const color = `rgba(${Math.round(((v >> 16) & 255) * shade)},${Math.round(((v >> 8) & 255) * shade)},${Math.round((v & 255) * shade)},${(fade * (p.depth < 0.8 ? 0.8 : 1)).toFixed(3)})`;
      ctx.save();
      ctx.translate(p.x, p.y); ctx.rotate((p.angle * Math.PI) / 180); ctx.scale(1, Math.max(0.12, Math.abs(turnK)));
      const draw = () => {
        switch (p.shape) {
          case 0: rr(ctx, -size / 2, -size * 0.31, size, size * 0.62, 1.5); ctx.fill(); break;
          case 1: ctx.beginPath(); ctx.arc(0, 0, size * 0.4, 0, Math.PI * 2); ctx.fill(); break;
          case 2: rr(ctx, -size * 0.8, -size * 0.13, size * 1.6, size * 0.26, 1); ctx.fill(); break;
          default: ctx.beginPath(); ctx.moveTo(0, -0.6 * size); ctx.lineTo(0.56 * size, 0.44 * size); ctx.lineTo(-0.56 * size, 0.44 * size); ctx.closePath(); ctx.fill();
        }
      };
      // Out of focus at both ends of the depth range.
      const sigma = isNear ? 2.8 : p.depth < 0.8 ? 1.3 : 0;
      if (sigma) blurred(ctx, dpr, sigma, color, draw);
      else { ctx.fillStyle = color; draw(); }
      ctx.restore();
    }
    return list.length > 0;
  }

  // ---- Poses ----
  /** Seen straight on from its own place through the stage's lens, then moved: flying far does not skew it. */
  const pose = (p: number, tn: number, roll: number, size: number, x: number, y: number) =>
    `translate(${x.toFixed(2)}px, ${y.toFixed(2)}px) perspective(${LENS}px) rotateY(${tn.toFixed(2)}deg) rotateX(${p.toFixed(2)}deg) rotate(${roll.toFixed(2)}deg) scale(${size.toFixed(4)})`;

  function apply() {
    motions.forEach((c, i) => {
      const el = cardEls[i];
      if (!el) return;
      el.style.transform = pose(c.pitch.x, c.turn.x, c.roll.x, c.size.x, c.x.x, c.y.x);
      el.style.opacity = c.shown ? "1" : "0";
      const sh = shineEls[i];
      if (sh) { sh.style.transform = `translateX(${((c.turn.x / 22) * CW).toFixed(1)}px) rotate(18deg)`; sh.style.opacity = clamp((Math.abs(c.turn.x) + Math.abs(c.pitch.x)) / 16, 0, 1).toFixed(3); }
    });
    let lightAt: { x: number; y: number }, reach: number;
    if (phase === "resting") { lightAt = { x: width / 2, y: TOP + CH / 2 }; reach = 150; }
    else {
      // Before the cue: a quick shake, as if something inside wants out.
      const opening = phase === "opening";
      const shake = opening && clock < 0.7 ? 6.5 * Math.sin(clock * 29) * Math.exp(-clock * 5.2) : 0;
      const fall = dropAt < 0 ? 0 : clamp((clock - dropAt) / 0.5, 0, 1), bob = phase === "sealed" ? Math.sin(idle * 1.7) * 5 : 0;
      const squeeze = 1 - push.x * 0.05 + (opening && clock < 0.3 ? -0.05 * Math.sin((clock / 0.3) * Math.PI) : 0);
      // Its work done, the envelope sinks a little and fades. It is gone before it could reach the stage's edge.
      const away = smooth(fall);
      const lean = pose(pitch.x, turn.x + (phase === "sealed" ? Math.sin(idle * 1.1) * 3 : 0), shake + away * 4, squeeze * (1 - 0.06 * away), away * 6, bob + away * 26);
      const opacity = Math.pow(1 - fall, 1.6).toFixed(3);
      for (const layer of [backLayer, frontLayer]) if (layer) { layer.style.transform = lean; layer.style.opacity = opacity; }
      const deg = clamp(flap.x, 0, 178);
      const hinge = `perspective(${LENS}px) rotateX(${deg.toFixed(2)}deg)`;
      if (flapCanvas) { flapCanvas.style.transform = hinge; flapCanvas.style.visibility = deg < 90 ? "visible" : "hidden"; }
      if (liningCanvas) { liningCanvas.style.transform = hinge; liningCanvas.style.visibility = deg > 90 ? "visible" : "hidden"; }
      paintEnvelope(deg, turn.x / 17 + (phase === "sealed" ? Math.sin(idle * 1.1) * 0.35 : 0) + flap.x / 400);
      lightAt = { x: width / 2, y: ENV_Y + HEAD + 70 }; reach = 190;
      if (floorEl) {
        floorEl.style.transform = `translate(${(width / 2 + away * 6 - 118).toFixed(1)}px, ${TALL - 22 - 33}px)`;
        floorEl.style.opacity = clamp((1 - fall) * (1 - bob / 30), 0, 1).toFixed(3);
      }
    }
    // A soft pool of light behind the subject. It reaches nothing before the stage ends, so it has no edge.
    reach = Math.min(reach, Math.min(lightAt.y, height - lightAt.y)) - 2;
    const key = `${width}|${height}|${dark}|${reach}|${lightAt.y}`;
    if (key !== lightKey && lightEl) {
      lightKey = key;
      const peak = (dark ? 30 : 150) / 255;
      const stops = Array.from({ length: 11 }, (_, i) => `rgba(255,255,255,${(peak * (1 - smooth(i / 10))).toFixed(4)}) ${i * 10}%`).join(", ");
      lightEl.style.background = `radial-gradient(${Math.min(reach * 2.6, width / 2 - 2).toFixed(1)}px ${reach.toFixed(1)}px at ${lightAt.x}px ${lightAt.y}px, ${stops})`;
    }
  }

  // ---- Frames ----
  function advance(dt: number) {
    if (!app.motion) {
      for (const s of [pitch, turn, push, flap]) s.snap(s.to);
      for (const c of motions) { c.x.snap(c.x.to); c.y.snap(c.y.to); c.size.snap(c.size.to); c.roll.snap(c.roll.to); c.turn.snap(0); c.pitch.snap(0); }
      far = []; near = [];
      return false;
    }
    if (phase === "opening") {
      clock += dt;
      while (cues.length && cues[0][0] <= clock) cues.shift()![1]();
    }
    if (phase === "sealed") idle += dt;
    for (let left = dt; left > 0; left -= 1 / 240) {
      const h = Math.min(left, 1 / 240);
      pitch.step(h, 120, 14); turn.step(h, 120, 14); push.step(h, 260, 20); flap.step(h, 58, 7.2);
      for (const c of motions) { c.x.step(h, c.stiff, c.damp); c.y.step(h, c.stiff, c.damp); c.size.step(h, c.stiff, c.damp); c.roll.step(h, c.stiff, c.damp); c.turn.step(h, 90, 10); c.pitch.step(h, 90, 10); }
    }
    fly(far, dt); fly(near, dt);
    const settled = motions.every((c) => c.x.still(0.05) && c.y.still(0.05) && c.pitch.still(0.05) && c.turn.still(0.05) && c.roll.still(0.05) && c.size.still(0.001));
    if (phase === "opening" && !cues.length && clock > 2.5 && settled) finish();
    // The envelope breathes while it waits.
    return phase === "resting" ? !settled || far.length + near.length > 0 : true;
  }
  function frame(now: number) {
    const dt = Math.min((now - last) / 1000, 1 / 24);
    last = now;
    const moving = advance(dt);
    apply();
    farDrawn = paintConfetti(farCanvas, far, false, farDrawn);
    nearDrawn = paintConfetti(nearCanvas, near, true, nearDrawn);
    raf = moving && !document.hidden ? requestAnimationFrame(frame) : 0;
  }
  function wake() {
    if (!raf && stage) { last = performance.now(); raf = requestAnimationFrame(frame); }
  }

  // ---- Cards ----
  function deal(list: Card[], visible: boolean) {
    shown = list;
    motions = list.map((_, i) => ({ x: new Spring(), y: new Spring(), pitch: new Spring(), turn: new Spring(), roll: new Spring(restRoll(i)), size: new Spring(1), stiff: 150, damp: 15, shown: visible }));
  }
  function rest(snap: boolean) {
    if (hovered >= 0) return;
    motions.forEach((c, i) => {
      c.shown = true; c.x.to = 0; c.y.to = 0; c.size.to = 1; c.turn.to = 0; c.pitch.to = 0; c.roll.to = restRoll(i);
      if (snap || !app.motion) { c.x.snap(0); c.y.snap(0); c.size.snap(1); c.turn.snap(0); c.pitch.snap(0); c.roll.snap(restRoll(i)); }
    });
    apply(); wake();
  }
  // The lifted card rises, grows and turns toward the pointer; its neighbours step aside so it is seen whole.
  function hover(card: number, within = { x: 0.5, y: 0.5 }) {
    if (phase !== "resting") return;
    if (card !== hovered) { hovered = card; pressed = false; }
    const s = step();
    motions.forEach((c, i) => {
      c.stiff = 190; c.damp = 19;
      if (i === card) {
        c.y.to = -16; c.size.to = pressed ? 1.02 : 1.08; c.roll.to = 0; c.x.to = 0;
        c.turn.to = (clamp(within.x, 0, 1) - 0.5) * 26; c.pitch.to = -(clamp(within.y, 0, 1) - 0.5) * 20;
      } else {
        const d = card < 0 ? 0 : place(i) - place(card);
        c.x.to = Math.sign(d) * (d === 0 ? 0 : (s < CW ? 22 : 8) / Math.abs(d)); c.y.to = 0; c.size.to = 1; c.roll.to = restRoll(i); c.turn.to = 0; c.pitch.to = 0;
      }
    });
    wake();
  }
  function copy(card: number) {
    // The card nods once to say it was taken.
    const c = motions[card];
    c.size.to = hovered === card ? 1.08 : 1;
    if (app.motion) c.turn.v += 520;
    oncopy?.(shown[card]);
    wake();
  }

  // ---- Pointer ----
  const local = (e: PointerEvent) => { const r = stage.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
  let pointing = $state(false);
  function track(e: PointerEvent) {
    const at = local(e);
    if (phase === "sealed") {
      // The envelope leans toward the pointer, a little, from anywhere on the stage.
      const cx = width / 2, cy = ENV_Y + HEAD + H / 2;
      turn.to = clamp((at.x - cx) / 260, -1, 1) * 17; pitch.to = -clamp((at.y - cy) / 220, -1, 1) * 12;
      pointing = bodyHit(at.x, at.y);
    } else if (phase === "resting") {
      // Hit-test the resting places, not the lifted cards, so a card does not flee the pointer.
      const inSlot = (i: number, lip = 0) => { const s = slot(i); return at.x >= s.x && at.x <= s.x + s.w && at.y >= s.y - lip && at.y <= s.y + s.h; };
      let found = -1;
      for (let i = n() - 1; i >= 0 && found < 0; i--) if (inSlot(i)) found = i;
      // The lifted card keeps the pointer while it is over the strip the card rose from.
      if (found < 0 && hovered >= 0 && inSlot(hovered, 18)) found = hovered;
      const s = found >= 0 ? slot(found) : null;
      hover(found, s ? { x: (at.x - s.x) / s.w, y: (at.y - s.y) / s.h } : undefined);
      pointing = found >= 0;
    }
    wake();
  }
  function leave() {
    pitch.to = turn.to = 0; pressed = false; push.to = 0; pointing = false;
    hover(-1);
    wake();
  }
  function press(e: PointerEvent) {
    const at = local(e);
    if (phase === "sealed" && bodyHit(at.x, at.y)) { pressed = true; push.to = 1; wake(); }
    else if (phase === "resting" && hovered >= 0) { pressed = true; motions[hovered].size.to = 1.02; wake(); }
  }
  function release() {
    if (!pressed) return;
    pressed = false; push.to = 0;
    if (phase === "sealed") open();
    else if (phase === "resting" && hovered >= 0) copy(hovered);
    wake();
  }

  // ---- The opening ----
  /** Plays the whole opening: the core is asked for the palette first, so the envelope only opens for a pack that exists. */
  export async function open() {
    if (phase !== "sealed" || !onopen || busy) return;
    busy = true;
    const next = await onopen();
    busy = false;
    if (!next) return;
    deal(next, false);
    phase = "opening"; pointing = false;
    await tick();
    if (!app.motion) { finish(); return; }
    clock = 0; pitch.to = turn.to = 0; push.to = 0; cues = [];
    // The flap swings open and overshoots a little before it rests.
    cues.push([0.26, () => (flap.to = 166)]);
    cues.push([0.56, () => burst(mouth(), 96)]);
    for (let i = 0; i < next.length; i++) {
      const card = i;
      cues.push([0.66 + i * 0.1, () => launch(card)]);
      cues.push([1.86 + (next.length - 1 - i) * 0.05, () => settle(card)]);
    }
    cues.push([1.72, () => (dropAt = clock)]);
    cues.sort((a, b) => a[0] - b[0]);
    wake();
  }
  // A card is pushed up out of the pocket and joins the fan held in its mouth.
  function launch(card: number) {
    const c = motions[card], s = slot(card), cx = s.x + CW / 2, cy = s.y + CH / 2;
    const fan = (place(card) - mid()) * 6, radians = (fan * Math.PI) / 180, pivot = 150;
    c.shown = true;
    c.x.snap(width / 2 - cx); c.y.snap(ENV_Y + HEAD + H / 2 + 10 - cy);
    c.size.snap(0.84); c.turn.snap(card % 2 === 0 ? 34 : -34); c.pitch.snap(-14); c.roll.snap(0);
    // The fan turns about a point below the cards, so their feet stay inside the pocket.
    c.x.to = width / 2 + pivot * Math.sin(radians) - cx; c.y.to = 150 + pivot * (1 - Math.cos(radians)) - cy;
    c.size.to = 1; c.turn.to = 0; c.pitch.to = 0; c.roll.to = fan;
    c.y.v = -520; c.stiff = 120; c.damp = 13.5;
    burst(mouth(), 9, 0.8);
  }
  // The fan opens into the row. Each card turns a little on its way and lands with a soft bounce.
  function settle(card: number) {
    const c = motions[card];
    c.x.to = 0; c.y.to = 0; c.roll.to = restRoll(card); c.turn.v += card % 2 === 0 ? 230 : -230; c.pitch.v += 90;
    c.stiff = 132; c.damp = 14;
  }
  function finish() {
    if (phase === "resting") return;
    phase = "resting"; cues = []; dropAt = -1;
    rest(true);
    ondone?.();
  }

  // Later visits build the cards at rest; a change of format, theme or language reaches them in place.
  $effect(() => {
    const list = cards;
    if (phase !== "resting" || !list.length) return;
    if (list.length !== motions.length) { deal(list, true); placed = false; }
    else shown = list;
  });
  // Poses depend on the places just laid out, so they are set once layout has finished.
  $effect(() => {
    void width; void shown; void app.language;
    tick().then(() => { if (phase === "resting") { rest(!placed); placed = true; } else { painted.key = ""; apply(); wake(); } });
  });
  $effect(() => { void dark; painted.key = ""; lightKey = ""; wake(); });

  onMount(() => {
    const ro = new ResizeObserver(([e]) => (width = e.contentRect.width));
    ro.observe(stage);
    document.fonts?.ready.then(() => { painted.key = ""; wake(); });
    const visible = () => { if (!document.hidden) wake(); };
    document.addEventListener("visibilitychange", visible);
    return () => { ro.disconnect(); cancelAnimationFrame(raf); raf = 0; document.removeEventListener("visibilitychange", visible); };
  });
</script>

<div
  class="stage"
  class:pointing
  bind:this={stage}
  style:height="{height}px"
  onpointermove={track}
  onpointerleave={leave}
  onpointerdown={press}
  onpointerup={release}
  role="presentation"
>
  <div class="light" bind:this={lightEl} aria-hidden="true"></div>
  {#if phase !== "resting"}<div class="floor" bind:this={floorEl} aria-hidden="true"></div>{/if}
  <canvas class="confetti far" bind:this={farCanvas} style:width="{width}px" style:height="{height}px" aria-hidden="true"></canvas>
  {#if phase !== "resting"}
    <div class="layer back" bind:this={backLayer} style:left="{envX() - RX}px" style:top="{ENV_Y - RY}px" aria-hidden="true">
      <canvas bind:this={backCanvas} style:width="{RW}px" style:height="{RH}px"></canvas>
      <canvas class="flap" bind:this={liningCanvas} style:width="{FW}px" style:height="{FH}px"></canvas>
    </div>
  {/if}
  {#each shown as c, i (i)}
    {@const s = slot(i)}
    <button
      class="card"
      bind:this={cardEls[i]}
      style:left="{s.x}px"
      style:top="{s.y}px"
      style:background={c.hex}
      style:color={c.ink}
      style:z-index={hovered === i ? 60 : 10 + i}
      tabindex={phase === "resting" ? 0 : -1}
      onfocus={(e) => { if (phase === "resting" && (e.currentTarget as HTMLElement).matches(":focus-visible")) hover(i); }}
      onblur={() => hovered === i && hover(-1)}
      onkeydown={(e) => { if (phase === "resting" && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); copy(i); } }}
      aria-label="{c.label} {c.code}"
      dir={app.language === "ar" ? "rtl" : "ltr"}
    >
      <span class="role">{c.label}</span>
      <span class="foot">
        <span class="name">{c.name}</span>
        <strong class="code" class:long={c.code.length > 9}>{c.code}</strong>
      </span>
      <span class="shine" bind:this={shineEls[i]} aria-hidden="true"></span>
    </button>
  {/each}
  {#if phase !== "resting"}
    <div class="layer front" bind:this={frontLayer} style:left="{envX() - RX}px" style:top="{ENV_Y - RY}px" aria-hidden="true">
      <canvas bind:this={frontCanvas} style:width="{RW}px" style:height="{RH}px"></canvas>
      <canvas class="flap" bind:this={flapCanvas} style:width="{FW}px" style:height="{FH}px"></canvas>
    </div>
  {/if}
  {#if phase === "sealed"}
    <!-- The envelope's body as one control for the keyboard and assistive technology; the pointer is
         handled by the stage, which leans the envelope from anywhere. -->
    <button
      class="hit"
      style:left="{envX() - 18}px"
      style:top="{ENV_Y + HEAD - 18}px"
      aria-label={t("افتح باقة اليوم")}
      onclick={(e) => { if (e.detail === 0) open(); }}
    ></button>
  {/if}
  <canvas class="confetti near" bind:this={nearCanvas} style:width="{width}px" style:height="{height}px" aria-hidden="true"></canvas>
</div>

<style>
  .stage {
    position: relative;
    overflow: hidden;
    direction: ltr;
  }
  .stage.pointing {
    cursor: pointer;
  }
  .light {
    position: absolute;
    inset: 0;
    z-index: 0;
    pointer-events: none;
  }
  .floor {
    position: absolute;
    left: 0;
    top: 0;
    width: 236px;
    height: 66px;
    z-index: 0;
    pointer-events: none;
    background: radial-gradient(closest-side, rgb(0 0 0 / 0.42), rgb(0 0 0 / 0.22) 45%, transparent);
  }
  :global([data-theme="light"]) .floor {
    background: radial-gradient(closest-side, rgb(0 0 0 / 0.17), rgb(0 0 0 / 0.09) 45%, transparent);
  }
  .confetti {
    position: absolute;
    left: 0;
    top: 0;
    pointer-events: none;
  }
  .confetti.far {
    z-index: 0;
  }
  .confetti.near {
    z-index: 80;
  }
  .layer {
    position: absolute;
    width: 284px;
    height: 388px;
    pointer-events: none;
    /* The envelope turns about the middle of its body. */
    transform-origin: 142px 206px;
    will-change: transform;
  }
  .layer.back {
    z-index: 1;
  }
  .layer.front {
    z-index: 50;
  }
  .layer canvas {
    position: absolute;
    left: 0;
    top: 0;
  }
  /* The flap hinges on the envelope's top edge. */
  .layer .flap {
    left: 42px;
    top: 138px;
    transform-origin: 100px 6px;
  }
  .hit {
    all: unset;
    position: absolute;
    width: 224px;
    height: 280px;
    z-index: 70;
    border-radius: 22px;
    pointer-events: none;
  }
  .hit:focus-visible {
    outline: 2px solid var(--color-brand);
    outline-offset: 4px;
  }
  .card {
    all: unset;
    position: absolute;
    width: 150px;
    height: 210px;
    box-sizing: border-box;
    border-radius: 18px;
    border: 1px solid var(--glass-rim);
    box-shadow: 0 14px 30px rgb(0 0 0 / 0.35);
    overflow: hidden;
    opacity: 0;
    /* Resting cards take the pointer through the stage, which hit-tests their resting places. */
    pointer-events: none;
    will-change: transform;
  }
  .card:focus-visible {
    outline: 3px solid var(--color-brand);
    outline-offset: 3px;
  }
  .role {
    position: absolute;
    inset-inline: 14px;
    top: 12px;
    font-size: var(--text-lead);
    font-weight: 600;
    line-height: 1.4;
  }
  .foot {
    position: absolute;
    inset-inline: 14px;
    bottom: 12px;
    display: flex;
    flex-direction: column;
    gap: 1px;
  }
  .name {
    font-size: var(--text-caption);
    opacity: 0.78;
  }
  .foot .code {
    font-size: 13px;
    font-weight: 700;
    text-align: start;
  }
  .foot .code.long {
    font-size: 10.5px;
  }
  /* A band of light that crosses the card as it turns. */
  .shine {
    position: absolute;
    left: -90px;
    top: -63px;
    width: 330px;
    height: 336px;
    opacity: 0;
    pointer-events: none;
    background: linear-gradient(90deg, rgb(255 255 255 / 0) 36%, rgb(255 255 255 / 0.32) 50%, rgb(255 255 255 / 0) 64%);
  }
</style>
