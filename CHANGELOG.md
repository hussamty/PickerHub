# 0.7.0 — typography, window state and the daily envelope

- Set the interface in Plus Jakarta Sans for Latin and Noto Kufi Arabic for Arabic, in place of IBM Plex Sans Arabic. Both are geometric, low in contrast and share a 536-unit x-height, so mixed lines read as one face. Each ships as Regular, Medium, SemiBold and Bold instances; Noto Kufi Arabic's line box is tightened to 1.08/0.52 em so Arabic sits centred in pills and buttons. `scripts/fonts.py` cuts the instances from the upstream variable fonts.
- Put every size on one type scale (11, 12.5, 13, 14, 15, 17, 20, 24, 28), use SemiBold for headings and Medium for controls, set Arabic at 1.7 and Latin at 1.5 line height, and track large Latin headings in slightly. Arabic is never letter-spaced.
- Even out spacing: 24-pixel pane padding, 20-pixel gaps between page sections, 12-pixel gaps inside them, and a clear rhythm between the page title and its content.
- Fix the maximized window controls: the middle action now restores a maximized window to its previous size instead of disappearing, so Minimize is never mistaken for Restore. A real-pointer UI test clicks Restore and checks the window state.
- Redesign the palette-of-the-day envelope with exact geometry: one corner radius for every outer corner, a filleted V mouth and flap tip, a flap that turns in perspective onto a pinstriped lining, a seal carrying the PickerHub mark, debossed type, real blurred shadows and dithered gradients. The envelope now sinks and fades instead of falling past the stage, so nothing is cut off at its edge.
- Add `--preview-envelope` (and a launcher action on Linux) to open the envelope in a temporary library: replay it with the button or R, in either language or theme, without touching your own library.
- Keep every tool in the navigation rail on screen in short windows: rows shrink to fit, down to 30 pixels, and the minimum window height is now 480. UI tests check that every page fits its width and that top-bar actions never overlap, from 560 to 1680 pixels wide in both languages.
- Fix the native picker test, which still clicked where the eyedropper sat before the one-row top bar; it now finds the button by its brand yellow.
- Replace the mascot with an original PickerHub mark: the letter P built from the eyedropper lens, with the picked pixel in its bowl and the dropper's tip as its stem. All PNG, ICO and ICNS sizes are regenerated.
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
