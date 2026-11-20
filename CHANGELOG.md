# 2.0.0-beta.1 — PickerHub on Tauri

- Rebuild the app on Tauri 2: Rust for everything below the interface, and an interface in Svelte 5, TypeScript and Tailwind CSS 4 in the system WebView. Same seven tools, same look, same Arabic-first layout. The 1.x Avalonia app still builds unchanged beside it.
- Keep libraries interchangeable: 2.0 reads and writes `workspace.json` schema 2, the palette-of-the-day state and full backups exactly as 1.x does, keeps fields it does not know, takes the same `desktop.lock`, and saves a copy of the 1.x library before its first write. Tested by opening 2.0-written libraries in 1.x.
- Pick colors with the native samplers in `crates/capture`: X11, the Wayland portal (or the GNOME Color Picker extension), Windows and macOS. The tray offers pick and copy, pick and edit, quick pick and multi-pick; the optional global shortcut works on X11, Windows and macOS, and through the GlobalShortcuts portal on Wayland.
- Ship far smaller: the release binary needs no .NET runtime.
- Use Noto Kufi Arabic 2.102. Later builds lose the dots of a final yeh in WebKit («بني» drawn as «بنى»); a WebView check guards it.
- Bring back the live magnifier lens on X11 and Windows, and the screen-sharing lens on Wayland, now on every monitor; a capture-mode button beside the eyedropper on Wayland chooses the system picker or the live lens and starts or ends screen sharing.
- Open the palette of the day as 1.x did: the foil envelope drawn in two layers, its flap swinging back on its hinge to show the lining, the cards dealt into a fan and settled into a row, confetti at two depths, and a floor light and shadow.
- Keep the local timing log (off by default): operation names and error codes only, bounded and written off the main thread, and included in the support report.
- Tighten the content security policy: no inline styles, no plugins, no base or form targets. A WebView check walks every page, sheet and animation and fails on any refusal.
- Pass an axe-core audit on every page and state; headings, field labels, focus kept inside sheets and returned on close, and arrow keys in segmented choices.
- Prepare releases: the final name PickerHub with the `pickerhub` package, which upgrades 1.x in place and replaces the 2.0 alphas; signed updates checked only on request; and a release workflow that writes the update manifest and signs the Windows and macOS installers when their certificates are set (docs/release.md). A manual beta checklist is in docs/beta-checklist.md.

# 1.2.0 — compact image palettes and copying from color actions

- Open a picture with more than five colors in compact view: the guide marks and labels only its five most dominant colors, largest first. A Show all toggle beneath the picture brings back every color and then offers Show top 5; the guide redraws in place with its leader motion, so the page keeps its scroll position, and reduced motion switches at rest. Each new picture opens in compact view again.
- Save palette and export take the colors on show: the five most dominant in compact view, every color after Show all.
- Copy a color's code from its color actions, in the format chosen in Settings, wherever the actions open: image labels, sampled pixels and palette swatches.

# 1.1.0 — image color guide and perceptual color names

- Name colors by how they look. One classifier in the engine reads each color in OKLCH and names everything in the app: the editor, palettes, the library, the image guide, sampled pixels and the daily cards. A color is gray only when its chroma is genuinely low, so a muted or dark purple such as #7B5EA7 is now Medium purple, not Gray; faintly tinted grays keep their tint (Purple gray, Slate gray, Taupe), and in the dark, where a little chroma shows plainly, a dark brown or an eggplant is named brown or purple rather than a tinted gray. Purple, violet, magenta, blue, brown and pink each have their own family with fixed hue, lightness and chroma limits, and colors near a limit are compared with the neighbouring family too, so names do not flip between nearly equal colors.
- Name image colors from a robust representative: each color is its group's weighted OKLab median, snapped to a shade that occurs in the picture, so shadows, highlights, compression fringes and noise do not pull it toward gray. Nearly transparent pixels are no longer counted.
- Show the sampled pixel's name beside its code. Swatches saved by 1.0, and palette files exported from it, take the new names once when they still carry their old automatic name; names you typed are kept.
- Show an analyzed image's colors where they are instead of as a list of bars. Each color gets a glass mark inside its own area of the picture and a leader out to a glass label with its name, code and share; the picture sits in a frosted-glass frame. Leaders draw outward one after another and the labels settle as they arrive.
- Locate every color in the engine: `extract_at` segments the picture by nearest color and marks each color at the deepest point of its largest region, so a mark never lands on an edge between colors.
- Route the leaders without collisions: labels stand in evenly spaced columns, the marks are split at their median so no leader crosses sides, and a least-total-length matching leaves no two leaders crossing; a leader that would graze another mark bends round it. The self-test checks 800 random layouts; the UI test checks a real picture in both themes and languages.

# 1.0.0 — contrast handoff and image pixel sampling

- Send a swatch directly to the text or background input of Contrast Check. Save both colors as semantic roles in a palette, with confirmation when overwriting existing roles.
- Sample original image pixels by clicking the preview or using the keyboard. Copy the sample, open it in the Color Editor, or send it to Contrast Check. Very large pasted bitmaps use the bounded preview and are labeled accordingly.
- Include the distinct tray actions, reusable Wayland sharing session, decision focus, and compact palette layout introduced in 0.8.1.

# 0.8.1 — tray actions, Wayland sharing and compact palettes

- Pick and copy returns to the current tool and confirms the copied format. Pick and edit selects HEX in the Color Editor and preserves the clipboard. Quick Pick copies silently and keeps the workbench hidden.
- Start Wayland screen sharing from the tray or Capture Mode. All pick actions reuse the live session. Permission and recovery decisions bring PickerHub forward and take keyboard focus.
- Show the first palette swatches in compact windows. Move duplicate removal into Palette Tools and preserve the swatch controls when a search leaves the visible results unchanged.

# 0.8.0 — quicker picking, compact palettes and project search

- Add Quick Pick to the tray and global shortcut settings. It copies the selected color and leaves the foreground window in focus. Copy confirmations show the color and format, and multi-pick confirmations include the count.
- Show the Wayland capture mode and active sharing beside the eyedropper. Change mode, stop sharing, or switch to the system picker after a live-capture error. Capture shortcuts respect open dialogs.
- Correct linear and radial gradient preview geometry to match the generated CSS. Preview hue and spectrum changes while editing the color.
- Keep HEX, copy, and add-to-palette actions visible at compact sizes. Move search and paging above the swatches and render 24 swatches at a time.
- Search across projects, palettes, favorites, colors, and semantic roles. Import PickerHub JSON palettes with a preview, either as a new palette or by merging and skipping duplicate colors.
- Add a build identifier to diagnostics and record startup, capture, clipboard, and search timings when local logging is enabled.

# 0.7.0 — typography, window state and the daily envelope

- Set the interface in Plus Jakarta Sans for Latin and Noto Kufi Arabic for Arabic, in place of IBM Plex Sans Arabic. Both are geometric, low in contrast and share a 536-unit x-height, so mixed lines read as one face. Each ships as Regular, Medium, SemiBold and Bold instances; Noto Kufi Arabic's line box is tightened to 1.08/0.52 em so Arabic sits centred in pills and buttons. `scripts/fonts.py` cuts the instances from the upstream variable fonts.
- Put every size on one type scale (11, 12.5, 13, 14, 15, 17, 20, 24, 28), use SemiBold for headings and Medium for controls, set Arabic at 1.7 and Latin at 1.5 line height, and track large Latin headings in slightly. Arabic is never letter-spaced.
- Even out spacing: 24-pixel pane padding, 20-pixel gaps between page sections, 12-pixel gaps inside them, and a clear rhythm between the page title and its content.
- Fix the maximized window controls: the middle action now restores a maximized window to its previous size instead of disappearing, so Minimize is never mistaken for Restore. A real-pointer UI test clicks Restore and checks the window state.
- Redesign the palette-of-the-day envelope with exact geometry: one corner radius for every outer corner, a filleted V mouth and flap tip, a flap that turns in perspective onto a pinstriped lining, a seal carrying the PickerHub mark, debossed type, real blurred shadows and dithered gradients. The envelope now sinks and fades instead of falling past the stage, so nothing is cut off at its edge.
- Add `--preview-envelope` (and a launcher action on Linux) to open the envelope in a temporary library: replay it with the button or R, in either language or theme, without touching your own library.
- Keep every tool in the navigation rail on screen in short windows: rows shrink to fit, down to 30 pixels, and the minimum window height is now 480. UI tests check that every page fits its width and that top-bar actions never overlap, from 560 to 1680 pixels wide in both languages.
- Fix the native picker test, which still clicked where the eyedropper sat before the one-row top bar; it now finds the button by its brand yellow.
- Replace the mascot with an original PickerHub mark: an eyedropper written as one broad-nib stroke in the shape of a P. A rounded rubber bulb grows from a slim neck where the bowl meets the stem, the body sweeps round the eyedropper's lens of pixels, and the stem narrows smoothly into a fine tip from which a white drop of the picked colour falls. `scripts/mark.py` generates the source; every PNG, ICO and ICNS size is regenerated, and the envelope's seal reads the same source.
- Read English from the left everywhere: the color specimen puts its name at the left and its code and actions at the right, daily cards start with the first role at the left with left-aligned labels, codes in lists sit at the left, and the card pager's arrows point the English way. Every daily palette now has an English story, so the English page shows no Arabic. UI tests check each language's reading order.
- Keep Arabic headings at the right after a change of language: the page heading, the error banner and the daily cards used to keep the direction they were built with, so switching from English to Arabic in Settings left Arabic headings at the far left. Direction now lives on the surfaces that hold text and follows every switch, and names set inside sentences are bidi-isolated. The colour-vision labels in the contrast matrix are translated, so Arabic no longer shows English words at the left.
- Move status news out of the top bar into a toast: a frosted-glass pane that rises from the bottom corner where reading starts (right in Arabic, left in English), shows a swatch when the message names a color, and dismisses itself after a few seconds. UI tests check its corner in both languages, its dismissal, and the headings after a runtime language switch.
- Arrange the top bar as one row: identity, eyedropper and status on the leading side; undo, redo, theme and window actions on the trailing side. This returns 40 pixels of height to the page.
- Give undo and redo curved-arrow icons that mirror with the language, and make the window actions borderless.
- Drop the status, then the wordmark, as the window narrows; actions always remain.

# 0.6.1 — header, navigation and application identity

- Hide the maximize/resize button while maximized; restore by double-clicking the titlebar. Native macOS controls remain unchanged.
- Group the titlebar and toolbar in one aligned glass surface; reserve the upper row for window movement and controls, and simplify status at compact widths.
- Use a continuous 460 ms sidebar transition with a 160 ms pointer-exit grace period, reversible without restarting the animation. Reduced motion remains immediate.
- Use the new mascot consistently in the titlebar, window, tray, executable and app bundles. Add Linux panel-size icons and verify Windows PE icon resources, macOS bundle icons and Debian launcher assets in CI.

# Unreleased

- Add the PickerHub mascot as the app icon: a glossy drop of color with eyes, brand yellow on a brand-black tile. It is the window and taskbar icon, the Windows executable icon, the macOS bundle icon and the Linux launcher icon, and it sits beside the wordmark in the title bar.
- Install the launcher icon into the hicolor theme from the Debian package and ship `pickerhub.png` in the Linux archive.
- Add `scripts/icons.py`, which renders `desktop/Assets/icon.svg` into the PNG, ICO and ICNS files.

# 0.6.0 — native workbench and image input

- Paste image pixels or a single image file with Ctrl+V / Cmd+V, or drag a PNG, JPEG or WebP onto Image Analysis. Shared cancellable analysis preserves the previous result on failure.
- Fill palette grid/list cards and history deck fronts with the exact swatch color and contrast-aware text.
- Keep card navigation responsive during a flip; invalidate stale animation callbacks and bound the visible card stack.
- Use the shared glass material for in-window tooltips, menus and sheets; refit open overlays when resizing.
- Fix clipped bottom actions by bounding the viewport and keeping bottom spacing inside the scrollable content. Add rounded custom scrollbars with stable drag targets.
- Default branded Linux/Windows titlebar with drag, double-click maximize/restore, minimize, close and edge/corner resizing. Keep native macOS window controls.
- Honor an explicit X11 session even if a native library populates a Wayland display hint.
- Add image clipboard/drop/cancellation, rapid card navigation, real wheel/thumb scrolling and window-manager interaction regressions in Arabic and English.

# 0.5.1 — glass and liquid consistency

- Replace fragile blur-threshold button links with curved, outlined bridges that remain visible in dark and light themes and respond to keyboard focus.
- Align popup themes with the workbench; use a shared glass finish for tooltips, fields and selected menu rows.
- Constrain dropdowns to the available space above or below their anchor.
- Add real pointer and menu bounds checks, with Arabic/English light/dark preview captures.

# 0.5.0 — testing release

- A second launch activates the existing window instead of opening a locked library.
- Continuous picking shows a count and six recent colors inside the live lens.
- Clipboard acknowledgements no longer delay returning to the app or the next pick.
- Cached Wayland streams retain the last frame when the desktop is static.
- Compact color editor, expandable extra formats, wrapping history, responsive navigation and reset scroll on tool changes.
- Optional local, bounded diagnostics with explicit export; no automatic reporting.
- Public MIT repository and native Windows/macOS/Linux build checks. Unsigned preview packages, not a signed production release.

# سجل التغييرات

## 0.4.1 — استقرار الالتقاط والإعدادات

- إصلاح انهيار التطبيق عند عرض رسالة خطأ ثانية، مع اختبار إعادة إنتاج السبب.
- إبقاء جلسة العدسة مفتوحة خلال الالتقاط المتعدد، وتحديث البكسل حتى دون تحريك المؤشر.
- تطبيق خيارات النسخ واللغة والمظهر فوراً، واحترام النسخ التلقائي عبر الاختصار العام.
- حفظ المكتبة على عامل خلفي متسلسل، مع دمج التغييرات السريعة وانتظار اكتمال الحفظ عند الإغلاق وإظهار فشل الحفظ مع إعادة المحاولة.
- تسريع التراجع والإعادة بدمج إعادة الرسم، وإغلاق القائمة الجانبية عند العودة من القطّارة.
- إصلاح قص زر الاختيار وزوايا حقول الأرقام، وإزالة تمدد الأزرار الذي كان يحرك العناصر المجاورة.
- تقليل إعادة رسم العدسة للبكسلات الثابتة وإدارة موارد تظليل Skia صراحةً.
- تهيئة حافظة X11 على الجلسات الجديدة، وإظهار رقم الإصدار في الإعدادات وتوحيده مع الحزمة.

## 0.4.0 — مرشح محلي للإصدار

- نسخ تلقائي قابل للضبط، قائمة نظام واختصارات عامة اختيارية، والتقاط عدة ألوان متتابعة إلى اللوحة.
- رسائل خطأ دائمة مع استعادة المكتبة، نسخ سليمة تلقائية، تراجع وإعادة، ونسخ احتياطية تشمل حالة الباقات اليومية.
- شبكة وقائمة قابلة للبحث، مفضلة ومشاريع وتحديد متعدد ونقل وتصدير وحذف وإزالة المكرر.
- تحليل صور في الخلفية وإلغاء التحليل، مخابئ محدودة، وحفظ تركيز محرر الألوان أثناء التعديل.
- واجهة إنجليزية اختيارية، اتباع مظهر النظام وتقليل الشفافية، مع الحفاظ على العربية.
- أدوار دلالية وCSS وDesign Tokens للفواتح والدواكن، مصفوفة تباين ومعاينات رؤية الألوان وأرشيف للباقات، مع OKLCH ووسم CMYK بالتقريبي.
- ترقية المكتبة إلى الصيغة 2، اختبارات ترقية واستعادة وحزم مستقلة، ومسار توقيع وتوثيق Windows/macOS يحتاج بيانات الناشر.

## غير منشور — تغييرات التصميم السابقة

- تجربة جديدة ثلاثية الأبعاد لـ«باقة اليوم»: ظرف لامع يميل نحو المؤشر، يُفتح غطاؤه وتخرج منه البطاقات واحدة تلو الأخرى مع قصاصات زينة، ثم تستقر في صف وتبقى قابلة للتحويم والنسخ.
- إصلاح إغلاق التطبيق فجأة بعد دقائق من الاستخدام: أسطح تمويه الزجاج صارت تُنشأ وتُحرَّر على خيط الرسم وحده، ولم تعد تتراكم في الذاكرة.
- تحليل الصور يستخرج حتى 16 لوناً بذكاء: يجمع الدرجات المتشابهة في لون واحد ويُبقي الألوان الصغيرة المميّزة التي كانت تضيع.
- قطّارة بهوية التطبيق: عدسة سوداء بإطار أصفر للبكسل المختار، بتحديث 60 إطاراً وتكبير انسيابي.
- خلفية ناعمة بلا حلقات أو حواف ظاهرة في التدرج.
- خط IBM Plex Sans Arabic للواجهة كلها بدل Noto Sans Arabic.
- رسالة الحالة تعود إلى وضعها بعد خمس ثوانٍ، وحواف الألواح وظلالها لم تعد تُقصّ عند أطراف منطقة التمرير.
- «باقة اليوم»: باقة ألوان مستوحاة من الطبيعة تُفتح من مغلّف مرة كل 24 ساعة، بأدوار جاهزة ونسختين داكنة وفاتحة، وتعمل محلياً دون اتصال.
- هوية لونية جديدة من ألوان TrineHub: أسود ‎#111111‎ وأصفر ‎#FFF000‎ وأبيض، بوضع داكن أساسي ووضع فاتح معكوس يبقى فيه الأصفر ثابتاً.
- أسطح زجاجية للواجهة، مع تمويه حقيقي لما خلف شريط التنقل وبطاقات الألوان.
- شريط تنقل زجاجي ملتصق بحافة النافذة: يتّسع عند المرور عليه، ويُثبَّت بالنقر على المقبض، وتتبع المؤشرَ فيه كبسولة تمييز.
- بطاقات ألوان زجاجية متراكبة تنفرج عند المرور عليها وتُقلب لإظهار قياسات اللون.
- عناصر تحكم سائلة: الأزرار تنتفخ وتنضغط وتندمج مع جاراتها، والقوائم تنبثق من حقولها.
- مفتاح جديد للمظهر في الشريط العلوي: يُنقر أو يُسحب، ويتحوّل فيه القمر إلى شمس.
- نوافذ الإعدادات والتأكيد والتسمية وقوائم الاختيار صارت ألواحاً زجاجية داخل النافذة بالحركة نفسها.
- صفحة الملتقط تظهر كاملة دون تمرير، وعنوان الصفحة ووصفها في سطر واحد.
- استجابة أسرع: تمويه الزجاج بربع الدقة وعلى معالج الرسوميات، وحركات أقصر، وتبديل المظهر دون إعادة بناء الصفحة.
- نقل ألوان الواجهة وحالات عناصر التحكم إلى desktop/Theme.axaml، وخيار «تقليل الحركة» في الإعدادات.

## 0.3.5 · 2026-11-13

- اختيار لون واحد على Wayland باستخدام عدسة GNOME Color Picker أو منتقي الألوان النظامي.
- إتاحة العدسة الحية ومشاركة الشاشات وتبديل النوافذ أثناء الالتقاط كخيار صريح في الإعدادات.

## 0.3.4 · 2026-11-13

- عرض البكسلات الحية وتبديل النوافذ باستخدام Alt+Tab أثناء اختيار اللون على Linux وWindows.
- إعادة تركيز مدخلات القطّارة بعد تبديل النافذة على Wayland حتى يلغي Esc الاختيار.
- اختيار الشاشات عبر بوابة المشاركة مرة لكل جلسة، مع زر إيقاف المشاركة وإعادة اختيار الشاشات في الإعدادات.

## 0.3.1 · 2026-11-13

- إخفاء نافذة التطبيق أثناء الالتقاط واستعادتها بعد الاختيار أو الإلغاء أو رفض الإذن.
- عدسة مشتركة على Linux وWindows تعرض شبكة البكسلات والعينة وقيم HEX وRGB مع حركة المؤشر.
- التقاط لقطة ثابتة بعد إخفاء النافذة ودعم التكبير بالعجلة والتنقل بالأسهم.

## 0.3.0 · 2026-11-13

- إعادة بناء الواجهة بأدوات جانبية ثابتة ومحرر تشبع وسطوع وتدرج لوني تفاعلي.
- صيغ قابلة للنسخ بمحاذاة واضحة، وقوائم سياقية للوحات، ومعاينة تباين مجاورة للنتائج.
- توحيد الوضعين الفاتح والداكن، وتحسين النوافذ الصغيرة، وإضافة أيقونات Tabler.

## 0.2.0 · 2026-11-13

- تطبيقات سطح مكتب أصلية ذاتية الاحتواء لثلاثة أنظمة، مع محرك Rust محلي ومكتبة ملفات مستقلة.
- زر القطّارة متاح في جميع الصفحات، مع التقاط النظام مباشرةً واختصار F8.
- تدعم الاستعادة ملفات مكتبة الإصدار 0.1.

## 0.1.0 · 2026-11-13

- إصدار أول لمحرك Rust وواجهة عربية محلية تدعم المظهرين.
- قطّارة نظام ومعاينة شاشة قابلة للتكبير مع شبكة بكسلات وتحكم بلوحة المفاتيح.
- سبع صيغ للألوان، وأسماء عربية وإنجليزية، وست قواعد للتناغم.
- لوحات قابلة للترتيب والنقل والتصدير، وسجل قابل للضبط ونسخ احتياطية.
- تحليل صور PNG وJPEG وWEBP، وفحص تباين النصوص، ومحرر تدرجات خطية ودائرية.
- حزم محمولة للأنظمة الثلاثة وحزمة Debian ودليل تثبيت واختبارات محرك وواجهة.
