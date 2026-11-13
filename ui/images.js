let imageJob = 0;
function renderImages() {
  const info = imageInfo;
  return (
    head() +
    '<section class="panel"><div class="drop" id="drop">' +
    (info
      ? '<img class="thumb" alt="معاينة الصورة المختارة" src="' +
        info.url +
        '"><b class="wrap">' +
        e(info.name) +
        "</b>"
      : icon("image") + "<h2>اسحب صورة إلى هنا</h2>") +
    '<label class="field">اختيار صورة<input id="imageFile" type="file" accept="image/png,image/jpeg,image/webp"></label><small>PNG · JPG · WEBP</small></div><p role="status" id="imageStatus"></p></section>' +
    (info
      ? '<section class="panel block"><div class="sectionhead"><h2>لوحة الصورة</h2><div class="actions">' +
        button("saveImage", "حفظ كلوحة", "save") +
        button("exportImage", "تصدير", "down") +
        "</div></div>" +
        info.colors
          .map(
            (c) =>
              '<div class="listrow">' +
              dot(c.hex) +
              '<div class="meta"><b>' +
              c.name +
              "</b><code>" +
              c.hex +
              '</code></div><span class="percent">' +
              c.percent.toFixed(1) +
              '%</span><div class="meter"><i style="--color:' +
              c.hex +
              ";width:" +
              c.percent +
              '%"></i></div>' +
              button("select", "فحص", "", "compact", c.hex) +
              "</div>",
          )
          .join("") +
        '<p class="help">النسبة من مجموع البكسلات المرئية في العيّنة، بعد تجميع الدرجات المتقاربة.</p></section>'
      : "")
  );
}
function wireImage() {
  const input = $("#imageFile"),
    drop = $("#drop");
  input.onchange = safe(
    (ev) => ev.target.files[0] && analyzeImage(ev.target.files[0]),
  );
  drop.ondragover = (ev) => {
    ev.preventDefault();
    drop.classList.add("dragover");
  };
  drop.ondragleave = () => drop.classList.remove("dragover");
  drop.ondrop = safe(async (ev) => {
    ev.preventDefault();
    drop.classList.remove("dragover");
    if (ev.dataTransfer.files[0]) await analyzeImage(ev.dataTransfer.files[0]);
  });
}
// Inspect encoded dimensions before decoding high-resolution files.
function imageDimensions(bytes) {
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength),
    tag = (i) => String.fromCharCode(...bytes.subarray(i, i + 4));
  if (bytes[0] === 137 && tag(1).startsWith("PNG") && tag(12) === "IHDR")
    return [v.getUint32(16), v.getUint32(20)];
  if (tag(0) === "RIFF" && tag(8) === "WEBP") {
    if (tag(12) === "VP8X")
      return [
        1 + (v.getUint32(24, true) & 0xffffff),
        1 + (v.getUint32(26, true) >>> 8),
      ];
    if (tag(12) === "VP8 ")
      return [v.getUint16(26, true) & 0x3fff, v.getUint16(28, true) & 0x3fff];
    if (tag(12) === "VP8L") {
      const n = v.getUint32(21, true);
      return [(n & 0x3fff) + 1, ((n >>> 14) & 0x3fff) + 1];
    }
  }
  if (bytes[0] === 255 && bytes[1] === 216) {
    let pos = 2;
    while (pos + 8 < bytes.length) {
      if (bytes[pos++] !== 255) break;
      while (bytes[pos] === 255) pos++;
      const marker = bytes[pos++];
      if (
        marker >= 0xc0 &&
        marker <= 0xcf &&
        ![0xc4, 0xc8, 0xcc].includes(marker)
      )
        return [v.getUint16(pos + 5), v.getUint16(pos + 3)];
      if (marker === 0xda || marker === 0xd9) break;
      const length = v.getUint16(pos);
      if (length < 2) break;
      pos += length;
    }
  }
  throw Error("اختر صورة PNG أو JPG أو WEBP صالحة.");
}
async function analyzeImage(file) {
  const job = ++imageJob;
  if (file.size > 32 * 1024 * 1024)
    throw Error("اختر صورة أصغر من 32 ميغابايت.");
  const data = new Uint8Array(await file.arrayBuffer());
  let size;
  try {
    size = imageDimensions(data);
  } catch {
    throw Error("صيغة الصورة غير مدعومة أو الملف تالف.");
  }
  if (!size[0] || !size[1] || size[0] * size[1] > 40000000)
    throw Error("الحد الأقصى للصورة 40 مليون بكسل.");
  if ($("#imageStatus")) $("#imageStatus").textContent = "جارٍ تحليل الصورة…";
  const url = URL.createObjectURL(file);
  try {
    const img = new Image();
    img.src = url;
    await img.decode();
    const scale = Math.min(
      1,
      768 / Math.max(img.naturalWidth, img.naturalHeight),
    );
    const canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(img.naturalWidth * scale));
    canvas.height = Math.max(1, Math.round(img.naturalHeight * scale));
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
    const rgba = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
    const ptr = wasm.pixel_buffer(rgba.length);
    if (!ptr) throw Error("تجاوزت العينة حجم الذاكرة المتاح.");
    new Uint8Array(wasm.memory.buffer, ptr, rgba.length).set(rgba);
    const colors = result(wasm.extract(10));
    if (!colors.length)
      throw Error("الصورة شفافة بالكامل. اختر صورة تحتوي على بكسلات مرئية.");
    if (job !== imageJob) {
      URL.revokeObjectURL(url);
      return;
    }
    if (imageInfo) URL.revokeObjectURL(imageInfo.url);
    imageInfo = { name: file.name, url, colors };
    if (page === "images") render();
  } catch (err) {
    URL.revokeObjectURL(url);
    if ($("#imageStatus")) $("#imageStatus").textContent = "";
    throw err;
  }
}
const imagePalette = () => ({
  name:
    imageInfo.name
      .replace(/\.[^.]+$/, "")
      .trim()
      .slice(0, 100) || "لوحة الصورة",
  colors: imageInfo.colors.map((c) => asColor(c.hex)),
});
