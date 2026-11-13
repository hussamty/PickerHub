function editColor() {
  const h = state.color;
  modal(
    "تعديل اللون",
    '<form id="editForm" class="stack"><div class="unit"><input id="editSwatch" aria-label="اختيار اللون" type="color" value="' +
      h +
      '"><label class="field fill">HEX<input class="hexinput" dir="ltr" id="editHex" value="' +
      h +
      '" maxlength="7" required pattern="#?[A-Fa-f0-9]{6}"></label></div><button class="primary">اعتماد اللون</button></form>',
  );
  $("#editSwatch").oninput = (ev) =>
    ($("#editHex").value = ev.target.value.toUpperCase());
  $("#editHex").oninput = (ev) => {
    const v = ev.target.value.replace(/^#?/, "#");
    if (hexPattern.test(v)) $("#editSwatch").value = v;
  };
  $("#editForm").onsubmit = safe(async (ev) => {
    ev.preventDefault();
    await pick($("#editHex").value.trim().replace(/^#?/, "#"));
    closeModal();
  });
}
const actions = {
  ...libraryActions,
  cancel: closeModal,
  editColor,
  capture: captureMenu,
  liveCapture,
  directCapture,
  history: () => navigate("history"),
  select: (id) => pick(id, true),
  saveColor: () => addColors([state.color]),
  saveHarmony: () => addColors(harmonies(state.color, mode)),
  details: () => {
    const c = describe(state.color);
    modal(
      "التسمية العربية",
      '<div class="stack"><div class="row">' +
        dot(c.hex, "noop") +
        "<h3>" +
        c.name +
        '</h3><span dir="ltr">' +
        c.english +
        "</span></div><p>الدرجة الأساسية " +
        c.base +
        "، والتصنيف " +
        c.shade +
        ".</p><p>أقرب عيّنة مسمّاة <code>" +
        c.reference +
        "</code></p></div>",
    );
  },
  copyFormat: (id) => copy(describe(state.color).formats[id]),
  copyHex: copy,
  copyAll: () =>
    copy(
      Object.entries(describe(state.color).formats)
        .map(([k, v]) => k + ": " + v)
        .join("\n"),
    ),
  harmonyColor: (id) => {
    modal(
      describe(id).name,
      '<div class="stack"><div class="gradient-preview" style="background:' +
        id +
        '"></div><code>' +
        id +
        '</code><div class="actions">' +
        button("pickHarmony", "فحص اللون", "pick", "primary", id) +
        button("addHarmony", "إضافة إلى لوحة", "plus", "", id) +
        "</div></div>",
    );
  },
  pickHarmony: async (id) => {
    await pick(id);
    closeModal();
  },
  addHarmony: (id) => addColors([id]),
  saveImage: () => {
    const p = imagePalette();
    askName("حفظ لوحة الصورة", p.name, async (name) => {
      const id = uid();
      paletteId = id;
      await change((s) => s.palettes.push({ ...p, id, name }));
      navigate("palettes");
    });
  },
  exportImage: () => exportMenu(imagePalette()),
  exportColor: () =>
    exportMenu({
      name: describe(state.color).name,
      colors: [asColor(state.color)],
    }),
  newGrad: () => {
    editingGradient = null;
    grad = {
      type: "linear",
      angle: 90,
      stops: [
        { hex: state.color, pos: 0 },
        { hex: "#FFFFFF", pos: 100 },
      ],
    };
    render();
  },
};
document.addEventListener(
  "click",
  safe(async (ev) => {
    const n = ev.target.closest("button");
    if (!n) return;
    if (n.dataset.page) navigate(n.dataset.page);
    else if (n.id === "theme")
      await change((s) => (s.theme = s.theme === "light" ? "dark" : "light"));
    else if (n.id === "backup") backupMenu();
    else if (n.id === "closeDialog") closeModal();
    else if (actions[n.dataset.act]) await actions[n.dataset.act](n.dataset.id);
  }),
);
document.addEventListener(
  "change",
  safe(async (ev) => {
    const n = ev.target;
    switch (n.id) {
      case "mode":
        mode = Number(n.value);
        render();
        break;
      case "paletteSelect":
        paletteId = n.value;
        render();
        break;
      case "historyLimit": {
        const limit = Number(n.value);
        if (!Number.isInteger(limit) || limit < 1 || limit > 256) {
          render();
          throw Error("اختر عدداً بين 1 و256.");
        }
        await change((s) => {
          s.limit = limit;
          s.history = s.history.slice(0, limit);
        });
        break;
      }
      case "fg":
        fg = n.value.toUpperCase();
        render();
        break;
      case "bg":
        bg = n.value.toUpperCase();
        render();
        break;
      case "gradType":
        grad.type = n.value;
        render();
        break;
    }
  }),
);
document.addEventListener("input", (ev) => {
  const n = ev.target;
  if (n.id === "angle") {
    grad.angle = Number(n.value);
    refreshGradient();
  }
  if (n.dataset.stopPos !== undefined) {
    grad.stops[Number(n.dataset.stopPos)].pos = Number(n.value);
    n.parentElement.querySelector("output").textContent = n.value + "%";
    refreshGradient();
  }
  if (n.dataset.stopColor !== undefined) {
    grad.stops[Number(n.dataset.stopColor)].hex = n.value.toUpperCase();
    refreshGradient();
  }
});
window.addEventListener("hashchange", () => {
  const key = location.hash.slice(1);
  if (Object.hasOwn(pages, key) && page !== key) {
    page = key;
    render();
  }
});
window.addEventListener("storage", (ev) => {
  if (ev.key === KEY && wasm) {
    try {
      state = readState() || initial();
      render();
    } catch {
      toast("تعذّرت قراءة تحديث المكتبة.");
    }
  }
});
async function start() {
  const bytes = Uint8Array.from(atob(WASM), (c) => c.charCodeAt(0));
  wasm = (await WebAssembly.instantiate(bytes, {})).instance.exports;
  try {
    state = readState() || initial();
    if (!localStorage.getItem(KEY))
      localStorage.setItem(KEY, JSON.stringify(state));
  } catch {
    state = initial();
    storageProblem =
      "تعذّر فتح المكتبة المحلية. تحقق من أذونات التخزين أو استعد نسخة احتياطية.";
  }
  fg = state.color;
  page = Object.hasOwn(pages, location.hash.slice(1))
    ? location.hash.slice(1)
    : "picker";
  render();
}
start().catch(() => {
  main.textContent =
    "تعذّر تشغيل محرك Rust. افتح التطبيق في متصفح حديث يدعم WebAssembly.";
});
