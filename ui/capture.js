let captureSession = null;
function releaseCapture() {
  if (!captureSession) return;
  const s = captureSession;
  captureSession = null;
  s.stream?.getTracks().forEach((t) => t.stop());
  cancelAnimationFrame(s.frame);
  if (s.video) s.video.srcObject = null;
}
function captureMenu() {
  modal(
    "التقاط من الشاشة",
    '<div class="stack">' +
      button("liveCapture", "فتح المعاينة المكبّرة", "screen", "primary") +
      '<p class="help">اختر شاشة أو نافذة. حرّك المؤشر داخل المعاينة وافحص البكسل، ثم انقر لتثبيت اللون.</p>' +
      (window.EyeDropper
        ? button("directCapture", "قطّارة النظام المباشرة", "pick")
        : '<p class="help">للقطّارة المباشرة افتح التطبيق في متصفح يدعم EyeDropper.</p>') +
      "</div>",
  );
}
async function directCapture() {
  try {
    const sample = await new EyeDropper().open();
    await pick(sample.sRGBHex);
    closeModal();
  } catch (err) {
    if (err.name !== "AbortError")
      throw Error("تعذّر فتح قطّارة النظام. استخدم المعاينة المكبّرة.");
  }
}
async function liveCapture() {
  if (!navigator.mediaDevices?.getDisplayMedia)
    throw Error(
      "التقاط الشاشة غير متاح في هذا المتصفح. افتح الملف في Chrome أو Edge أو استورد لقطة شاشة.",
    );
  const startButton = body.querySelector("[data-act=liveCapture]");
  startButton.disabled = true;
  let stream;
  try {
    stream = await navigator.mediaDevices.getDisplayMedia({
      audio: false,
      video: {
        width: { ideal: 16384 },
        height: { ideal: 8640 },
        frameRate: { ideal: 12, max: 24 },
        cursor: "never",
        resizeMode: "none",
      },
      selfBrowserSurface: "exclude",
    });
  } catch (err) {
    startButton.disabled = false;
    throw Error(
      err.name === "NotAllowedError"
        ? "أُلغي اختيار الشاشة أو رُفض الإذن. أعد فتح المعاينة واختر الشاشة المطلوبة."
        : "تعذّر بدء الالتقاط. تحقق من أذونات تسجيل الشاشة وبوابة مشاركة سطح المكتب.",
    );
  }
  if (!dialog.open || !startButton.isConnected) {
    stream.getTracks().forEach((t) => t.stop());
    return;
  }
  releaseCapture();
  const s = {
    stream,
    frame: 0,
    x: 0,
    y: 0,
    zoom: 12,
    hex: null,
    pending: false,
  };
  captureSession = s;
  dialog.classList.add("capture-dialog");
  $("#dialogTitle").textContent = "معاينة الشاشة";
  body.innerHTML =
    '<div class="capture-view"><video id="screenView" tabindex="0" aria-label="معاينة الشاشة، تحرّك بالأسهم واختر بمفتاح الإدخال" muted autoplay playsinline></video><b id="cross" aria-hidden="true"></b></div><div class="lensrow"><canvas id="lens" class="lens" width="145" height="145"></canvas><div class="stack"><strong id="pixelInfo" dir="ltr"></strong><small id="pixelRgb" dir="ltr"></small><small id="pixelCoords" dir="ltr"></small><label class="unit">التكبير<select id="zoom">' +
    options(
      [4, 8, 12, 20, 28].map((x) => [x, x + "×"]),
      12,
    ) +
    '</select></label><span class="help">انقر للاختيار، أو استخدم الأسهم ثم Enter.</span></div>' +
    button("commitPixel", "اختيار البكسل", "pick", "primary") +
    "</div>";
  const video = $("#screenView");
  s.video = video;
  video.srcObject = stream;
  const lens = $("#lens"),
    ctx = lens.getContext("2d", { willReadFrequently: true });
  const single = document.createElement("canvas");
  single.width = single.height = 1;
  const sample = single.getContext("2d", { willReadFrequently: true });
  let lastHex;
  function draw() {
    if (!video.videoWidth || !video.videoHeight || video.readyState < 2) return;
    const w = video.videoWidth,
      h = video.videoHeight;
    s.x = Math.max(0, Math.min(w - 1, s.x));
    s.y = Math.max(0, Math.min(h - 1, s.y));
    const z = s.zoom,
      mid = Math.floor(lens.width / 2),
      start = mid - Math.floor(z / 2);
    const radius = Math.ceil(mid / z) + 1;
    const x0 = Math.max(0, s.x - radius),
      x1 = Math.min(w, s.x + radius + 1),
      y0 = Math.max(0, s.y - radius),
      y1 = Math.min(h, s.y + radius + 1);
    ctx.fillStyle = "#475869";
    ctx.fillRect(0, 0, lens.width, lens.height);
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(
      video,
      x0,
      y0,
      x1 - x0,
      y1 - y0,
      start + (x0 - s.x) * z,
      start + (y0 - s.y) * z,
      (x1 - x0) * z,
      (y1 - y0) * z,
    );
    if (z >= 8) {
      ctx.beginPath();
      ctx.strokeStyle = "#20283044";
      ctx.lineWidth = 1;
      for (let i = start % z; i < lens.width; i += z) {
        ctx.moveTo(i + 0.5, 0);
        ctx.lineTo(i + 0.5, lens.height);
        ctx.moveTo(0, i + 0.5);
        ctx.lineTo(lens.width, i + 0.5);
      }
      ctx.stroke();
    }
    ctx.strokeStyle = "#000";
    ctx.lineWidth = 3;
    ctx.strokeRect(start, start, z, z);
    ctx.strokeStyle = "#fff";
    ctx.lineWidth = 1;
    ctx.strokeRect(start, start, z, z);
    sample.drawImage(video, s.x, s.y, 1, 1, 0, 0, 1, 1);
    const rgb = sample.getImageData(0, 0, 1, 1).data;
    s.hex =
      "#" +
      Array.from(rgb.slice(0, 3), (v) => v.toString(16).padStart(2, "0"))
        .join("")
        .toUpperCase();
    if (s.hex !== lastHex) {
      const info = describe(s.hex);
      $("#pixelInfo").textContent = s.hex + " · " + info.name;
      $("#pixelRgb").textContent = info.formats.RGB;
      lastHex = s.hex;
    }
    $("#pixelCoords").textContent =
      "x " + s.x + "  /  y " + s.y + "  ·  " + w + " × " + h;
    const cross = $("#cross");
    cross.style.left =
      video.offsetLeft + ((s.x + 0.5) * video.clientWidth) / w + "px";
    cross.style.top =
      video.offsetTop + ((s.y + 0.5) * video.clientHeight) / h + "px";
  }
  const commit = safe(async () => {
    if (s.pending || !s.hex) return;
    s.pending = true;
    try {
      await pick(s.hex);
      closeModal();
      toast("التُقط اللون");
    } finally {
      s.pending = false;
    }
  });
  function point(ev) {
    const box = video.getBoundingClientRect();
    s.x = Math.floor(((ev.clientX - box.left) * video.videoWidth) / box.width);
    s.y = Math.floor(((ev.clientY - box.top) * video.videoHeight) / box.height);
    draw();
  }
  video.onpointermove = point;
  video.onclick = (ev) => {
    point(ev);
    commit();
  };
  video.onkeydown = (ev) => {
    const offsets = {
      ArrowUp: [0, -1],
      ArrowDown: [0, 1],
      ArrowLeft: [-1, 0],
      ArrowRight: [1, 0],
    };
    if (ev.key === "Enter") {
      ev.preventDefault();
      commit();
    } else if (offsets[ev.key]) {
      ev.preventDefault();
      s.x += offsets[ev.key][0];
      s.y += offsets[ev.key][1];
      draw();
    }
  };
  $("#zoom").onchange = (ev) => {
    s.zoom = Number(ev.target.value);
    draw();
  };
  body.querySelector("[data-act=commitPixel]").onclick = commit;
  stream.getVideoTracks()[0].addEventListener("ended", () => {
    if (captureSession === s) {
      closeModal();
      toast("انتهت مشاركة الشاشة");
    }
  });
  try {
    await video.play();
  } catch {
    if (captureSession === s) {
      releaseCapture();
      throw Error("تعذّر بدء المعاينة. أغلق النافذة وأعد المحاولة.");
    }
    return;
  }
  if (captureSession !== s) return;
  s.x = Math.floor(video.videoWidth / 2);
  s.y = Math.floor(video.videoHeight / 2);
  video.focus();
  let last = 0;
  function frame(time) {
    if (captureSession !== s) return;
    if (time - last > 80) {
      draw();
      last = time;
    }
    s.frame = requestAnimationFrame(frame);
  }
  frame(100);
}
dialog.addEventListener("close", () => {
  if (!dialog.open) {
    releaseCapture();
    body.replaceChildren();
  }
});
window.addEventListener("pagehide", releaseCapture);
