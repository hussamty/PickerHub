function exportPalette(p, format) {
  const rows = p.colors;
  const token = (c, i) =>
    "color-" +
    (i + 1) +
    "-" +
    c.name
      .normalize("NFKC")
      .toLowerCase()
      .replace(/[^\p{L}\p{N}-]+/gu, "-")
      .replace(/^-+|-+$/g, "");
  const quoted = (s) => '"' + s.replaceAll('"', '""') + '"';
  const csvCell = (s) => quoted(/^[\s]*[=+@-]|^[\t\r\n]/.test(s) ? "'" + s : s);
  let text,
    ext = format;
  switch (format) {
    case "css":
      text =
        ":root {\n" +
        rows
          .map((c, i) => "  --" + token(c, i) + ": " + c.hex + ";")
          .join("\n") +
        "\n}\n";
      break;
    case "scss":
      text =
        rows.map((c, i) => "$" + token(c, i) + ": " + c.hex + ";").join("\n") +
        "\n";
      break;
    case "json":
      text = JSON.stringify(
        { name: p.name, colors: rows.map(({ name, hex }) => ({ name, hex })) },
        null,
        2,
      );
      break;
    case "csv":
      text =
        "\uFEFFname,hex\r\n" +
        rows.map((c) => csvCell(c.name) + "," + quoted(c.hex)).join("\r\n");
      break;
    case "txt":
      text = p.name + "\n" + rows.map((c) => c.name + "  " + c.hex).join("\n");
      break;
    case "gpl":
      text =
        "GIMP Palette\nName: " +
        p.name.replace(/[\r\n]/g, " ") +
        "\nColumns: 8\n#\n" +
        rows
          .map(
            (c) =>
              [1, 3, 5]
                .map((i) =>
                  parseInt(c.hex.slice(i, i + 2), 16)
                    .toString()
                    .padStart(3),
                )
                .join(" ") +
              "\t" +
              c.name.replace(/[\r\n]/g, " "),
          )
          .join("\n");
      break;
    default:
      throw Error("صيغة تصدير غير صالحة.");
  }
  download(p.name + "." + ext, text);
  toast("جُهّز ملف التصدير");
}
function exportMenu(p) {
  if (!p.colors.length) throw Error("أضف لوناً إلى اللوحة أولاً.");
  modal(
    "تصدير الألوان",
    '<div class="stack"><p>' +
      e(p.name) +
      " · " +
      p.colors.length +
      ' لون</p><label class="field">صيغة الملف<select id="exportFormat">' +
      options(
        [
          ["css", "متغيرات CSS"],
          ["scss", "متغيرات SCSS"],
          ["json", "JSON"],
          ["csv", "CSV"],
          ["txt", "TXT"],
          ["gpl", "لوحة GIMP / Inkscape (.gpl)"],
        ],
        "css",
      ) +
      "</select></label>" +
      button("doExport", "تنزيل الملف", "down", "primary") +
      "</div>",
  );
  body.querySelector("[data-act=doExport]").onclick = () => {
    exportPalette(p, $("#exportFormat").value);
    closeModal();
  };
}
function renderPalettes() {
  const p = selectedPalette();
  paletteId = p.id;
  return (
    head(button("newPalette", "لوحة جديدة", "plus", "primary")) +
    '<section class="panel"><div class="sectionhead"><label class="field">اللوحة الحالية<select id="paletteSelect">' +
    paletteOptions() +
    '</select></label><div class="actions">' +
    tool("renamePalette", "تعديل اسم اللوحة", "edit") +
    tool("delPalette", "حذف اللوحة", "trash", "", state.palettes.length === 1) +
    button("exportPalette", "تصدير", "down") +
    "</div></div>" +
    '<div class="historystrip">' +
    p.colors.map((c) => dot(c.hex)).join("") +
    '</div><div class="panel-footer"><h3>ألوان اللوحة (' +
    p.colors.length +
    ")</h3>" +
    button("saveColor", "إضافة اللون المحدد", "plus", "linkbtn") +
    "</div>" +
    (p.colors.length
      ? p.colors
          .map(
            (c, i) =>
              '<div class="listrow">' +
              dot(c.hex) +
              '<div class="meta"><b>' +
              e(c.name) +
              "</b><code>" +
              c.hex +
              "</code></div>" +
              tool("copyHex", "نسخ HEX", "copy", c.hex) +
              tool("renameColor", "تعديل اسم اللون", "edit", c.id) +
              tool("up", "تقديم اللون", "up", c.id, i === 0) +
              tool(
                "down",
                "تأخير اللون",
                "next",
                c.id,
                i === p.colors.length - 1,
              ) +
              button("moveColor", "نقل", "", "compact", c.id) +
              tool("delColor", "حذف اللون", "trash", c.id) +
              "</div>",
          )
          .join("")
      : '<p class="empty">لوحتك جاهزة. أضف اللون المحدد أو اختر عيّنة من الملتقط.</p>') +
    "</section>"
  );
}
function renderHistory() {
  return (
    head(button("clearHistory", "مسح السجل", "trash")) +
    '<section class="panel"><div class="sectionhead"><h2>الألوان المحفوظة (' +
    state.history.length +
    ')</h2><label class="unit">الحد الأقصى <input id="historyLimit" type="number" min="1" max="256" value="' +
    state.limit +
    '"></label></div>' +
    (state.history.length
      ? state.history
          .map(
            (h) =>
              '<div class="listrow">' +
              dot(h) +
              '<div class="meta"><b>' +
              describe(h).name +
              "</b><code>" +
              h +
              "</code></div>" +
              button("select", "فحص اللون", "", "compact", h) +
              tool("copyHex", "نسخ HEX", "copy", h) +
              tool("delHistory", "حذف من السجل", "trash", h) +
              "</div>",
          )
          .join("")
      : '<p class="empty">التقط لوناً ليظهر هنا.</p>') +
    "</section>"
  );
}
function editPaletteItem(id, mutate) {
  const pid = selectedPalette().id;
  return change((s) => {
    const p = s.palettes.find((p) => p.id === pid);
    const i = p?.colors.findIndex((c) => c.id === id) ?? -1;
    if (i < 0) throw Error("تغيّرت اللوحة. افتحها مجدداً.");
    mutate(p, i, s);
  });
}
function backupMenu() {
  modal(
    "النسخ الاحتياطي والاستعادة",
    '<div class="stack"><p>تتضمن النسخة اللوحات والتدرجات والسجل والإعدادات.</p>' +
      button("backupSave", "تنزيل نسخة JSON", "down") +
      '<label class="field">استعادة مكتبة من ملف JSON<input id="restore" type="file" accept=".json,application/json"></label></div>',
  );
  $("#restore").onchange = safe(async (ev) => {
    const file = ev.target.files[0];
    if (!file) return;
    if (file.size > 16 * 1024 * 1024) throw Error("الملف أكبر من 16 ميغابايت.");
    let imported;
    try {
      imported = JSON.parse(await file.text());
    } catch {
      throw Error("تعذّرت قراءة ملف JSON.");
    }
    if (!validState(imported))
      throw Error("اختر نسخة احتياطية صالحة من PickerHub.");
    confirmAction(
      "استعادة المكتبة",
      "ستحل النسخة المختارة محل المكتبة الحالية.",
      async () => {
        await change(
          (s) => {
            for (const key of [
              "version",
              "color",
              "theme",
              "limit",
              "palettes",
              "history",
              "gradients",
            ])
              s[key] = imported[key];
          },
          { reset: true },
        );
        toast("استُعيدت المكتبة");
      },
    );
  });
}
function renderContrast() {
  const r = contrast(fg, bg);
  const status = r.aa
    ? "ناجح"
    : r.large
      ? "يحتاج إلى تحسين للنص العادي"
      : "غير مناسب";
  const check = (label, pass) =>
    '<div class="check">' +
    label +
    "<strong>" +
    (pass ? "✓ ناجح" : "× غير مناسب") +
    "</strong></div>";
  return (
    head() +
    '<section class="panel"><div class="row">' +
    colorField("fg", "لون النص", fg) +
    colorField("bg", "لون الخلفية", bg) +
    button("swap", "تبديل اللونين") +
    button("useFg", "استخدام اللون المحدد للنص") +
    '</div><div class="contrastSample" style="color:' +
    fg +
    ";background:" +
    bg +
    '"><strong>معاينة النص الكبير</strong><p>هكذا يبدو النص العادي على الخلفية المختارة.</p></div><div class="sectionhead"><div><span class="ratio">' +
    r.ratio.toFixed(2) +
    ":1</span><p>" +
    status +
    '</p></div><span class="muted" dir="ltr">WCAG 2.2</span></div><div class="checks">' +
    check("AA · نص عادي", r.aa) +
    check("AA · نص كبير", r.large) +
    check("AAA · نص عادي", r.aaa) +
    check("AAA · نص كبير", r.aaaLarge) +
    "</div>" +
    (!r.aa
      ? '<div class="panel-footer"><span>لون نص مقترح <code>' +
        r.suggestion +
        "</code></span>" +
        button("applySuggestion", "تطبيق الاقتراح", "pick") +
        "</div>"
      : "") +
    "</section>"
  );
}
function colorField(id, label, value) {
  return (
    '<label class="field">' +
    label +
    '<span class="unit"><input id="' +
    id +
    '" type="color" value="' +
    value +
    '"><code>' +
    value +
    "</code></span></label>"
  );
}
function gradientCSS(g) {
  const stops = g.stops
    .slice()
    .sort((a, b) => a.pos - b.pos)
    .map((s) => s.hex + " " + s.pos + "%")
    .join(", ");
  return g.type === "linear"
    ? "linear-gradient(" + g.angle + "deg, " + stops + ")"
    : "radial-gradient(circle, " + stops + ")";
}
function renderGradients() {
  const css = gradientCSS(grad);
  return (
    head(
      button("newGrad", "تدرج جديد", "plus") +
        button("saveGrad", "حفظ التدرج", "save", "primary"),
    ) +
    '<section class="panel stack"><div class="row"><label class="unit">النوع<select id="gradType">' +
    options(
      [
        ["linear", "خطي"],
        ["radial", "دائري"],
      ],
      grad.type,
    ) +
    "</select></label>" +
    (grad.type === "linear"
      ? '<label class="unit">الاتجاه<input id="angle" type="range" min="0" max="360" value="' +
        grad.angle +
        '"><output id="angleOut">' +
        grad.angle +
        "°</output></label>"
      : "") +
    '</div><div class="gradient-preview" style="background:' +
    css +
    '"></div><div class="sectionhead"><h2>نقاط اللون</h2>' +
    button("addStop", "إضافة نقطة", "plus") +
    "</div><div>" +
    grad.stops
      .map(
        (s, i) =>
          '<div class="listrow stop"><input type="color" aria-label="لون النقطة ' +
          (i + 1) +
          '" data-stop-color="' +
          i +
          '" value="' +
          s.hex +
          '"><input type="range" aria-label="موقع النقطة ' +
          (i + 1) +
          '" data-stop-pos="' +
          i +
          '" min="0" max="100" value="' +
          s.pos +
          '"><output>' +
          s.pos +
          "%</output>" +
          tool("delStop", "إزالة نقطة", "trash", i, grad.stops.length === 2) +
          "</div>",
      )
      .join("") +
    '</div><code class="plain-code" id="gradCode">background: ' +
    css +
    ';</code><div class="actions">' +
    button("copyGrad", "نسخ CSS", "copy") +
    button("exportGrad", "تنزيل CSS", "down") +
    '</div></section><section class="panel block"><h2>مكتبة التدرجات</h2>' +
    (state.gradients.length
      ? state.gradients
          .map(
            (g) =>
              '<div class="listrow"><div class="strip" style="width:100px;background:' +
              gradientCSS(g) +
              '"></div><b class="fill wrap">' +
              e(g.name) +
              "</b>" +
              button("loadGrad", "فتح", "", "compact", g.id) +
              tool("delGrad", "حذف التدرج", "trash", g.id) +
              "</div>",
          )
          .join("")
      : '<p class="empty">احفظ التدرج الحالي لإضافته إلى المكتبة.</p>') +
    "</section>"
  );
}
let editingGradient = null;
function refreshGradient() {
  const css = gradientCSS(grad);
  $(".gradient-preview").style.background = css;
  $("#gradCode").textContent = "background: " + css + ";";
  if ($("#angleOut")) $("#angleOut").textContent = grad.angle + "°";
}
const libraryActions = {
  newPalette: () =>
    askName("إنشاء لوحة", "", async (name) => {
      const id = uid();
      paletteId = id;
      await change((s) => s.palettes.push({ id, name, colors: [] }));
    }),
  renamePalette: () => {
    const p = selectedPalette();
    askName("تعديل اسم اللوحة", p.name, (name) =>
      change((s) => {
        const target = s.palettes.find((x) => x.id === p.id);
        if (!target) throw Error("حُذفت اللوحة.");
        target.name = name;
      }),
    );
  },
  delPalette: () => {
    const p = selectedPalette();
    confirmAction("حذف اللوحة", "حذف «" + p.name + "» بكل ألوانها؟", () =>
      change((s) => {
        if (s.palettes.length <= 1) throw Error("احتفظ بلوحة واحدة على الأقل.");
        s.palettes = s.palettes.filter((x) => x.id !== p.id);
      }),
    );
  },
  renameColor: (id) => {
    const c = selectedPalette().colors.find((c) => c.id === id);
    askName("تسمية اللون", c.name, (name) =>
      editPaletteItem(id, (p, i) => (p.colors[i].name = name)),
    );
  },
  delColor: (id) => editPaletteItem(id, (p, i) => p.colors.splice(i, 1)),
  up: (id) =>
    editPaletteItem(id, (p, i) => {
      if (i > 0)
        [p.colors[i - 1], p.colors[i]] = [p.colors[i], p.colors[i - 1]];
    }),
  down: (id) =>
    editPaletteItem(id, (p, i) => {
      if (i + 1 < p.colors.length)
        [p.colors[i + 1], p.colors[i]] = [p.colors[i], p.colors[i + 1]];
    }),
  moveColor: (id) => {
    const p = selectedPalette(),
      others = state.palettes.filter((x) => x.id !== p.id);
    if (!others.length) throw Error("أنشئ لوحة ثانية لنقل اللون إليها.");
    modal(
      "نقل اللون",
      '<div class="stack"><label class="field">اللوحة المستقبِلة<select id="moveDest">' +
        options(
          others.map((p) => [p.id, p.name]),
          others[0].id,
        ) +
        "</select></label>" +
        button("moveNow", "نقل اللون", "palette", "primary") +
        "</div>",
    );
    body.querySelector("[data-act=moveNow]").onclick = safe(async () => {
      const dest = $("#moveDest").value;
      await editPaletteItem(id, (p, i, s) => {
        const target = s.palettes.find((x) => x.id === dest);
        if (!target) throw Error("حُذفت اللوحة المستقبِلة.");
        target.colors.push(...p.colors.splice(i, 1));
      });
      closeModal();
      toast("نُقل اللون");
    });
  },
  exportPalette: () => exportMenu(selectedPalette()),
  delHistory: (id) =>
    change((s) => (s.history = s.history.filter((h) => h !== id))),
  clearHistory: () =>
    confirmAction("مسح السجل", "مسح جميع الألوان من السجل؟", () =>
      change((s) => (s.history = [])),
    ),
  backupSave: () => {
    download(
      "PickerHub-backup.json",
      JSON.stringify(state, null, 2),
      "application/json",
    );
    closeModal();
  },
  swap: () => {
    [fg, bg] = [bg, fg];
    render();
  },
  useFg: () => {
    fg = state.color;
    render();
  },
  applySuggestion: () => {
    fg = contrast(fg, bg).suggestion;
    render();
  },
  addStop: () => {
    if (grad.stops.length >= 32) throw Error("الحد الأقصى 32 نقطة.");
    grad.stops.push({ hex: state.color, pos: 50 });
    render();
  },
  delStop: (id) => {
    if (grad.stops.length > 2) grad.stops.splice(Number(id), 1);
    render();
  },
  saveGrad: () => {
    const saved = state.gradients.find((g) => g.id === editingGradient);
    askName("حفظ التدرج", saved?.name || "تدرجي", async (name) => {
      const id = saved?.id || uid();
      const item = { ...structuredClone(grad), name, id };
      await change((s) => {
        const idx = s.gradients.findIndex((g) => g.id === id);
        if (idx < 0) s.gradients.push(item);
        else s.gradients[idx] = item;
      });
      editingGradient = id;
      toast("حُفظ التدرج");
    });
  },
  loadGrad: (id) => {
    const g = state.gradients.find((g) => g.id === id);
    editingGradient = id;
    grad = structuredClone(g);
    render();
    window.scrollTo(0, 0);
  },
  delGrad: (id) =>
    confirmAction("حذف التدرج", "حذف التدرج من المكتبة؟", () =>
      change((s) => (s.gradients = s.gradients.filter((g) => g.id !== id))),
    ),
  exportGrad: () =>
    download(
      "gradient.css",
      ".gradient {\n  background: " + gradientCSS(grad) + ";\n}\n",
    ),
  copyGrad: () => copy("background: " + gradientCSS(grad) + ";"),
};
