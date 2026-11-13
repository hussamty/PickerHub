// Rust owns all color calculations. The view reads each result before the next ABI call.
const $ = (selector) => document.querySelector(selector);
const main = $("#main"),
  dialog = $("#dialog"),
  body = $("#dialogBody");
const hexPattern = /^#[0-9a-f]{6}$/i;
const packed = (hex) => parseInt(hex.slice(1), 16);
const escapeHTML = (value) =>
  String(value).replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const e = escapeHTML;
const decoder = new TextDecoder();
let wasm;
function result(ptr) {
  return JSON.parse(
    decoder.decode(new Uint8Array(wasm.memory.buffer, ptr, wasm.output_len())),
  );
}
const describe = (hex) => result(wasm.describe(packed(hex)));
const harmonies = (hex, mode) => result(wasm.harmony(packed(hex), mode));
const contrast = (fg, bg) => result(wasm.contrast(packed(fg), packed(bg)));

const paths = {
  pick: "M14 5l5 5 M13 7l4 4-9 9H4v-4z M14 6l3-3a2 2 0 0 1 3 3l-3 3",
  palette: "M4 6h16v13H4z M4 14h16 M9 6v8 M15 6v8",
  contrast: "M12 3a9 9 0 1 0 0 18 9 9 0 1 0 0-18 M12 3v18 M14 6v12 M16 8v8",
  image: "M3 4h18v16H3z M3 16l5-5 4 4 4-6 5 6 M8 8h.1",
  gradient: "M3 5h18v14H3z M6 6v12 M9 6v12 M13 6v12 M18 6v12",
  history: "M3 6v5h5 M4 10a8 8 0 1 1 1 7 M12 7v5l4 2",
  moon: "M19 15A8 8 0 0 1 9 4a8 8 0 1 0 10 11z",
  sun: "M12 8a4 4 0 1 0 .01 0z M12 2v2 M12 20v2 M2 12h2 M20 12h2 M5 5l1.5 1.5 M17.5 17.5L19 19 M5 19l1.5-1.5 M17.5 6.5L19 5",
  backup: "M4 4h16v5H4z M5 9v11h14V9 M9 13h6",
  copy: "M8 8h12v13H8z M16 5V3H3v13h2",
  plus: "M12 5v14 M5 12h14",
  close: "M5 5l14 14 M19 5L5 19",
  edit: "M4 16L16 4l4 4L8 20H4z M13 7l4 4",
  down: "M12 4v12 M6 11l6 6 6-6 M4 20h16",
  trash: "M3 6h18 M8 6V3h8v3 M6 6l1 15h10l1-15 M10 10v7 M14 10v7",
  up: "M12 19V5 M6 11l6-6 6 6",
  next: "M12 5v14 M6 13l6 6 6-6",
  info: "M12 3a9 9 0 1 0 .01 0z M12 11v6 M12 7h.01",
  screen: "M3 4h18v13H3z M12 17v4 M7 21h10",
  save: "M5 4h14v17l-7-4-7 4z",
  grid: "M3 3h8v8H3z M14 3h7v8h-7z M3 14h8v7H3z M14 14h7v7h-7z",
};
const icon = (name) =>
  '<svg class="icon" aria-hidden="true" viewBox="0 0 24 24"><path d="' +
  paths[name] +
  '"/></svg>';
const button = (act, label, symbol = "", cls = "", id = "") =>
  '<button type="button" class="' +
  cls +
  '" data-act="' +
  act +
  '" data-id="' +
  e(id) +
  '">' +
  (symbol ? icon(symbol) : "") +
  e(label) +
  "</button>";
const tool = (act, label, symbol, id = "", disabled = false) =>
  '<button type="button" class="iconbtn" title="' +
  e(label) +
  '" aria-label="' +
  e(label) +
  '" data-act="' +
  act +
  '" data-id="' +
  e(id) +
  '" ' +
  (disabled ? "disabled" : "") +
  ">" +
  icon(symbol) +
  "</button>";
const pages = {
  picker: ["ملتقط الألوان", "pick", "افحص لوناً، وانسخ قيمه أو أضفه إلى لوحتك."],
  palettes: ["لوحات الألوان", "palette", "اجمع ألوان مشروعك ورتّبها وصدّرها."],
  contrast: ["فحص التباين", "contrast", "اختبر وضوح النص فوق الخلفية."],
  images: ["تحليل الصور", "image", "استخرج الألوان الأكثر حضوراً في صورتك."],
  gradients: [
    "محرّر التدرجات",
    "gradient",
    "اضبط نقاط اللون وعاين التدرج مباشرة.",
  ],
  history: ["سجل الألوان", "history", "ارجع إلى الألوان التي اخترتها مؤخراً."],
};
const modes = [
  "متجاورة",
  "متكاملة",
  "ثلاثية",
  "متكاملة منقسمة",
  "أحادية اللون",
  "عالية التباين",
];
const uid = () => crypto.randomUUID();
const KEY = "pickerhub.workspace.v1";
const initial = () => ({
  version: 1,
  color: "#87CEEB",
  limit: 32,
  theme: "light",
  history: [],
  palettes: [{ id: uid(), name: "لوحتي الأولى", colors: [] }],
  gradients: [],
});
const named = (s) =>
  typeof s === "string" && s.trim().length > 0 && s.length <= 100;
const isHex = (value) => typeof value === "string" && hexPattern.test(value);
const validId = (s) => typeof s === "string" && /^[a-zA-Z0-9-]{1,64}$/.test(s);
const uniqueIds = (arr) => new Set(arr.map((x) => x.id)).size === arr.length;
const isStop = (s) =>
  s && isHex(s.hex) && Number.isInteger(s.pos) && s.pos >= 0 && s.pos <= 100;
function validState(s) {
  return (
    s &&
    s.version === 1 &&
    isHex(s.color) &&
    ["light", "dark"].includes(s.theme) &&
    Number.isInteger(s.limit) &&
    s.limit >= 1 &&
    s.limit <= 256 &&
    Array.isArray(s.history) &&
    s.history.length <= s.limit &&
    s.history.every(isHex) &&
    Array.isArray(s.palettes) &&
    s.palettes.length > 0 &&
    s.palettes.length <= 256 &&
    s.palettes.every(
      (p) =>
        p &&
        validId(p.id) &&
        named(p.name) &&
        Array.isArray(p.colors) &&
        p.colors.length <= 2048 &&
        p.colors.every(
          (c) => c && validId(c.id) && named(c.name) && isHex(c.hex),
        ) &&
        uniqueIds(p.colors),
    ) &&
    uniqueIds(s.palettes) &&
    Array.isArray(s.gradients) &&
    s.gradients.length <= 512 &&
    s.gradients.every(
      (g) =>
        g &&
        validId(g.id) &&
        named(g.name) &&
        ["linear", "radial"].includes(g.type) &&
        Number.isInteger(g.angle) &&
        g.angle >= 0 &&
        g.angle <= 360 &&
        Array.isArray(g.stops) &&
        g.stops.length >= 2 &&
        g.stops.length <= 32 &&
        g.stops.every(isStop),
    ) &&
    uniqueIds(s.gradients)
  );
}
let state,
  storageProblem = "",
  page = "picker",
  mode = 0,
  paletteId,
  bg = "#FFFFFF",
  fg,
  imageInfo = null;
let grad = {
  type: "linear",
  angle: 110,
  stops: [
    { hex: "#214B74", pos: 0 },
    { hex: "#87CEEB", pos: 55 },
    { hex: "#F7D7AA", pos: 100 },
  ],
};
function readState() {
  const raw = localStorage.getItem(KEY);
  if (!raw) return null;
  const parsed = JSON.parse(raw);
  if (!validState(parsed))
    throw Error("تعذّرت قراءة المكتبة. استعد نسخة احتياطية للمتابعة.");
  return parsed;
}
function toast(message) {
  const n = $("#toast");
  n.textContent = message;
  n.className = "show";
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => (n.className = ""), 3200);
}
async function change(mutate, { reset = false } = {}) {
  const write = () => {
    let s;
    if (reset) s = structuredClone(state);
    else {
      try {
        s = readState() || structuredClone(state);
      } catch {
        throw Error(
          "تعذّرت قراءة التخزين المحلي. استعد نسخة احتياطية أو اسمح بالتخزين.",
        );
      }
    }
    mutate(s);
    if (!validState(s))
      throw Error("تجاوزت حدود المكتبة أو أدخلت قيمة غير صالحة.");
    localStorage.setItem(KEY, JSON.stringify(s));
    state = s;
    storageProblem = "";
    render();
  };
  // Serialize updates from tabs on platforms supporting the Web Locks API.
  if (navigator.locks) await navigator.locks.request(KEY, write);
  else write();
}
function selectedPalette(s = state) {
  return s.palettes.find((p) => p.id === paletteId) || s.palettes[0];
}
async function pick(hex, open = false) {
  if (!hexPattern.test(hex)) throw Error("أدخل ست خانات لونية بصيغة HEX.");
  hex = hex.toUpperCase();
  await change((s) => {
    s.color = hex;
    s.history = [hex, ...s.history.filter((x) => x !== hex)].slice(0, s.limit);
  });
  if (open) navigate("picker");
}
function safe(fn) {
  return async (...args) => {
    try {
      await fn(...args);
    } catch (err) {
      const msg =
        err?.name === "QuotaExceededError"
          ? "امتلأ التخزين المحلي. احذف عناصر أو صدّر نسخة احتياطية."
          : err?.message || "تعذّرت العملية.";
      if (dialog.open) {
        let notice = body.querySelector(".dialog-feedback");
        if (!notice) {
          notice = document.createElement("p");
          notice.className = "dialog-feedback";
          body.append(notice);
        }
        notice.textContent = msg;
      } else toast(msg);
    }
  };
}
async function copy(text) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    throw Error("تعذّر النسخ. اسمح للمتصفح بالوصول إلى الحافظة.");
  }
  toast("نُسخت القيمة إلى الحافظة");
}
function download(name, text, mime = "text/plain;charset=utf-8") {
  const url = URL.createObjectURL(new Blob([text], { type: mime }));
  const a = document.createElement("a");
  a.href = url;
  a.download = name.replace(/[\\/:*?"<>|]/g, "-");
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 30000);
}
function modal(title, html) {
  if (dialog.open) dialog.close();
  dialog.className = "";
  $("#dialogTitle").textContent = title;
  body.innerHTML = html;
  dialog.showModal();
}
const closeModal = () => dialog.close();
function askName(title, value, callback) {
  modal(
    title,
    '<form id="nameForm" class="stack"><label class="field">الاسم<input id="nameValue" maxlength="100" required value="' +
      e(value) +
      '"></label><button class="primary">حفظ</button></form>',
  );
  $("#nameValue").select();
  $("#nameForm").onsubmit = safe(async (ev) => {
    ev.preventDefault();
    const name = $("#nameValue").value.trim();
    if (!named(name)) throw Error("أدخل اسماً.");
    await callback(name);
    closeModal();
  });
}
function confirmAction(title, message, callback) {
  modal(
    title,
    '<div class="stack"><p>' +
      e(message) +
      '</p><div class="actions">' +
      button("confirm", title, "trash", "danger") +
      button("cancel", "إلغاء") +
      "</div></div>",
  );
  body.querySelector("[data-act=confirm]").onclick = safe(async () => {
    await callback();
    closeModal();
  });
}
function navigate(key) {
  if (!Object.hasOwn(pages, key)) return;
  page = key;
  location.hash = key;
  render();
  window.scrollTo(0, 0);
}
function head(actions = "") {
  return (
    '<div class="pagehead"><div><h1>' +
    pages[page][0] +
    "</h1><p>" +
    pages[page][2] +
    '</p></div><div class="actions">' +
    actions +
    "</div></div>"
  );
}
function options(items, selected) {
  return items
    .map(
      ([key, label]) =>
        '<option value="' +
        e(key) +
        '" ' +
        (String(key) === String(selected) ? "selected" : "") +
        ">" +
        e(label) +
        "</option>",
    )
    .join("");
}
const paletteOptions = () =>
  options(
    state.palettes.map((p) => [p.id, p.name]),
    selectedPalette().id,
  );
const dot = (hex, act = "select") =>
  '<button class="dot" title="' +
  e(hex) +
  '" aria-label="' +
  e(hex) +
  '" style="--color:' +
  hex +
  '" data-act="' +
  act +
  '" data-id="' +
  hex +
  '"></button>';
function asColor(hex) {
  return { id: uid(), hex, name: describe(hex).name };
}
function addColors(colors) {
  modal(
    "إضافة إلى لوحة",
    '<div class="stack"><div class="historystrip">' +
      colors
        .map((h) => '<span class="dot" style="--color:' + h + '"></span>')
        .join("") +
      '</div><label class="field">اللوحة<select id="dest">' +
      paletteOptions() +
      "</select></label>" +
      button("addNow", "إضافة الألوان", "plus", "primary") +
      "</div>",
  );
  body.querySelector("[data-act=addNow]").onclick = safe(async () => {
    const id = $("#dest").value;
    await change((s) => {
      const p = s.palettes.find((p) => p.id === id);
      if (!p) throw Error("حُذفت اللوحة. اختر لوحة أخرى.");
      p.colors.push(...colors.map(asColor));
    });
    paletteId = id;
    if (page === "palettes") render();
    closeModal();
    toast("أُضيفت الألوان إلى اللوحة");
  });
}
function renderPicker() {
  const c = describe(state.color),
    colors = harmonies(state.color, mode);
  return (
    head(button("capture", "التقاط من الشاشة", "pick", "primary")) +
    '<div class="grid"><section class="swatch-card"><div class="swatch-face" style="--color:' +
    c.hex +
    ";--swatchInk:" +
    c.ink +
    '"><div class="chip"><i></i>اللون المحدد</div><div><div class="herohex">' +
    c.hex +
    '</div><div class="swatch-name"><strong>' +
    c.name +
    '</strong><span lang="en">' +
    c.english +
    '</span></div></div></div><div class="cardfoot">' +
    button("saveColor", "إضافة إلى لوحة", "plus") +
    '<div class="actions">' +
    tool("exportColor", "تصدير اللون", "down") +
    tool("details", "تفاصيل اسم اللون", "info") +
    tool("editColor", "تعديل اللون", "edit") +
    '</div></div></section><section class="panel formats-panel"><div class="sectionhead"><h2>صيغ اللون</h2>' +
    tool("copyAll", "نسخ جميع الصيغ", "copy") +
    '</div><div class="formats">' +
    ["HEX", "RGB", "RGBA", "HSL", "HSV", "HSB", "CMYK"]
      .map((k) => [k, c.formats[k]])
      .map(
        ([k, v]) =>
          '<button class="format" data-act="copyFormat" data-id="' +
          k +
          '" title="نسخ ' +
          k +
          '"><b>' +
          k +
          "</b><code>" +
          e(v) +
          "</code>" +
          icon("copy") +
          "</button>",
      )
      .join("") +
    "</div></section></div>" +
    '<section class="panel block"><div class="sectionhead"><h2>تناغم الألوان</h2><select id="mode" aria-label="قاعدة التناغم">' +
    options(
      modes.map((m, i) => [i, m]),
      mode,
    ) +
    '</select></div><div class="harmonybar">' +
    colors
      .map(
        (h) =>
          '<button style="--color:' +
          h +
          ";--swatchInk:" +
          describe(h).ink +
          '" data-act="harmonyColor" data-id="' +
          h +
          '">' +
          h +
          "</button>",
      )
      .join("") +
    '</div><div class="panel-footer"><span class="help">اختر عيّنة لفحصها أو حفظها.</span>' +
    button("saveHarmony", "إضافة المجموعة إلى لوحة", "plus", "linkbtn") +
    "</div></section>" +
    '<section class="panel block"><div class="sectionhead"><h2>الألوان الأخيرة</h2>' +
    button("history", "عرض السجل", "history", "linkbtn") +
    "</div>" +
    (state.history.length
      ? '<div class="historystrip">' +
        state.history
          .slice(0, 16)
          .map((h) => dot(h))
          .join("") +
        "</div>"
      : '<p class="help">يظهر هنا أول لون تلتقطه أو تختاره.</p>') +
    "</section>"
  );
}
function render() {
  document.documentElement.dataset.theme = state.theme;
  $("#theme").innerHTML = icon(state.theme === "light" ? "moon" : "sun");
  $("#backup").innerHTML = icon("backup");
  $("#closeDialog").innerHTML = icon("close");
  $("#nav").innerHTML = Object.entries(pages)
    .map(
      ([key, p]) =>
        '<button class="navitem" data-page="' +
        key +
        '" ' +
        (key === page ? 'aria-current="page"' : "") +
        ">" +
        icon(p[1]) +
        p[0] +
        "</button>",
    )
    .join("");
  main.innerHTML =
    (storageProblem ? '<p class="panel">' + e(storageProblem) + "</p>" : "") +
    {
      picker: renderPicker,
      palettes: renderPalettes,
      history: renderHistory,
      contrast: renderContrast,
      images: renderImages,
      gradients: renderGradients,
    }[page]();
  if (page === "images") wireImage();
}
