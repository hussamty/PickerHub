// Run after scripts/build.py. Install playwright-core and set CHROME_PATH if needed.
const { chromium } = require("playwright-core");
const assert = require("node:assert/strict");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const fs = require("node:fs/promises");
const os = require("node:os");

(async () => {
  const browser = await chromium.launch({
    executablePath: process.env.CHROME_PATH || "/usr/bin/google-chrome",
    args: [
      "--use-fake-ui-for-media-stream",
      "--use-fake-device-for-media-stream",
      "--auto-select-desktop-capture-source=Entire screen",
    ],
  });
  const context = await browser.newContext({
    viewport: { width: 1366, height: 875 },
    acceptDownloads: true,
  });
  const p = await context.newPage();
  p.setDefaultTimeout(6000);
  const failures = [],
    connections = [];
  p.on("pageerror", (err) => failures.push(err.message));
  p.on("request", (req) => {
    if (/^https?:/.test(req.url())) connections.push(req.url());
  });
  const $ = (s) => p.locator(s),
    act = (a) =>
      $('[data-act="' + a + '"]')
        .first()
        .click(),
    nav = (a) => $('[data-page="' + a + '"]').click();
  const snapshot = async () => {
    await p.evaluate(() =>
      navigator.locks?.request("pickerhub.workspace.v1", () => {}),
    );
    return p.evaluate(() =>
      JSON.parse(localStorage.getItem("pickerhub.workspace.v1")),
    );
  };
  const file =
    process.argv[2] || path.join(__dirname, "../dist/PickerHub.html");
  const scratch = await fs.mkdtemp(path.join(os.tmpdir(), "pickerhub-check-"));
  try {
    await p.goto(pathToFileURL(path.resolve(file)).href);
    await $(".herohex").waitFor();
    assert.equal(await $(".herohex").innerText(), "#87CEEB");
    if (process.env.PICKERHUB_PREVIEW)
      await p.screenshot({
        path: process.env.PICKERHUB_PREVIEW,
        fullPage: true,
      });
    await act("editColor");
    await $("#editHex").fill("#EE3355");
    await $("#editForm button").click();
    await $("dialog").waitFor({ state: "hidden" });
    assert.equal(await $(".herohex").innerText(), "#EE3355");
    await act("saveColor");
    await act("addNow");
    await $("#mode").selectOption("2");
    await act("saveHarmony");
    await act("addNow");
    await nav("palettes");
    assert.equal((await snapshot()).palettes[0].colors.length, 4);
    await act("renameColor");
    await $("#nameValue").fill("وردي الاختبار");
    await $("#nameForm button").click();
    await $("dialog").waitFor({ state: "hidden" });
    await act("newPalette");
    await $("#nameValue").fill("لوحة ثانية");
    await $("#nameForm button").click();
    await $("dialog").waitFor({ state: "hidden" });
    const palettes = (await snapshot()).palettes;
    await $("#paletteSelect").selectOption(palettes[0].id);
    await act("moveColor");
    await $("#moveDest").selectOption(palettes[1].id);
    await act("moveNow");
    await $("dialog").waitFor({ state: "hidden" });
    assert.equal(
      (await snapshot()).palettes[1].colors[0].name,
      "وردي الاختبار",
    );
    await act("down");
    await act("delColor");
    for (const [ext, pattern] of Object.entries({
      css: /:root \{\n  --color-1-/,
      scss: /\$color-1-/,
      json: /"colors":/,
      csv: /name,hex\r\n/,
      txt: /#[A-F0-9]{6}/,
      gpl: /^GIMP Palette\nName:/,
    })) {
      await act("exportPalette");
      await $("#exportFormat").selectOption(ext);
      const event = p.waitForEvent("download");
      await act("doExport");
      const item = await event;
      const out = path.join(scratch, "colors." + ext);
      await item.saveAs(out);
      assert.match(await fs.readFile(out, "utf8"), pattern);
    }
    await nav("contrast");
    await $("#fg").fill("#888888");
    await $("#bg").fill("#888888");
    assert.equal(await $(".ratio").innerText(), "1.00:1");
    await act("applySuggestion");
    assert.ok(parseFloat(await $(".ratio").innerText()) >= 4.5);
    await nav("gradients");
    await act("addStop");
    await $("#gradType").selectOption("radial");
    await act("saveGrad");
    await $("#nameValue").fill("تدرج تجريبي");
    await $("#nameForm button").click();
    await $("dialog").waitFor({ state: "hidden" });
    assert.equal((await snapshot()).gradients[0].type, "radial");
    await act("newGrad");
    await act("loadGrad");
    assert.equal(await $("#gradType").inputValue(), "radial");
    await nav("images");
    for (const mime of ["png", "jpeg", "webp"]) {
      const bytes = await p.evaluate((mime) => {
        const c = document.createElement("canvas");
        c.width = 4;
        c.height = 2;
        const ctx = c.getContext("2d");
        ctx.fillStyle = "red";
        ctx.fillRect(0, 0, 4, 2);
        ctx.fillStyle = "blue";
        ctx.fillRect(0, 0, 1, 2);
        return c.toDataURL("image/" + mime).split(",")[1];
      }, mime);
      await $("#imageFile").setInputFiles({
        name: "test." + mime,
        mimeType: "image/" + mime,
        buffer: Buffer.from(bytes, "base64"),
      });
      await p.getByText("test." + mime, { exact: true }).waitFor();
      if (mime === "png")
        assert.equal(await $(".percent").first().innerText(), "75.0%");
    }
    await act("saveImage");
    await $("#nameForm button").click();
    await $("dialog").waitFor({ state: "hidden" });
    assert.ok((await snapshot()).palettes.length === 3);
    await $("#backup").click();
    const bd = p.waitForEvent("download");
    await act("backupSave");
    const backupFile = path.join(scratch, "backup.json");
    await (await bd).saveAs(backupFile);
    await nav("history");
    await act("clearHistory");
    await act("confirm");
    await $("dialog").waitFor({ state: "hidden" });
    assert.equal((await snapshot()).history.length, 0);
    await $("#backup").click();
    const invalid = await snapshot();
    invalid.color = ["#87CEEB"];
    await $("#restore").setInputFiles({
      name: "invalid.json",
      mimeType: "application/json",
      buffer: Buffer.from(JSON.stringify(invalid)),
    });
    await $(".dialog-feedback").waitFor();
    assert.deepEqual((await snapshot()).history, []);
    await $("#restore").setInputFiles(backupFile);
    await act("confirm");
    await $("dialog").waitFor({ state: "hidden" });
    assert.ok((await snapshot()).history.length);
    await $("#theme").click();
    await p.waitForFunction(
      () => document.documentElement.dataset.theme === "dark",
    );
    await p.reload();
    await $(".navitem").first().waitFor();
    assert.equal((await snapshot()).theme, "dark");
    await nav("picker");
    await act("capture");
    await act("liveCapture");
    await $("#pixelInfo")
      .filter({ hasText: /#[0-9A-F]{6}/ })
      .waitFor();
    const before = await $("#pixelCoords").textContent();
    await $("#screenView").press("ArrowLeft");
    assert.notEqual(await $("#pixelCoords").textContent(), before);
    await $("#zoom").selectOption("20");
    await act("commitPixel");
    await p.waitForFunction(() => !document.querySelector("dialog").open);
    assert.equal(
      (await snapshot()).history[0],
      await $(".herohex").innerText(),
    );
    await p.setViewportSize({ width: 390, height: 844 });
    for (const key of [
      "picker",
      "palettes",
      "gradients",
      "contrast",
      "history",
      "images",
    ]) {
      await nav(key);
      assert.ok(
        await p.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        "overflow " + key,
      );
    }
    assert.deepEqual(failures, []);
    assert.deepEqual(connections, []);
    console.log(
      "PASS: offline Rust/Wasm UI, color formats, harmony, library edits, export, contrast, gradients, PNG/JPEG/WebP, backup/restore, persistence, simulated live capture, mobile layout.",
    );
  } finally {
    await browser.close();
    await fs.rm(scratch, { recursive: true, force: true });
  }
})().catch((err) => {
  console.error(err);
  process.exitCode = 1;
});
