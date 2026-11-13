# كتيب تثبيت PickerHub

## اختيار النسخة

يمكن تشغيل ملف **PickerHub.html** نفسه على الأنظمة الثلاثة. انسخه إلى مجلد ثابت وافتحه بمتصفح سطح مكتب حديث يدعم WebAssembly والتخزين المحلي. يوفر Chrome وEdge أيضاً القطّارة المباشرة عند إتاحتها من النظام. تعرض المعاينة المكبّرة المصادر التي يسمح النظام بمشاركتها. يحتاج الإصدار الجاهز إلى المتصفح فقط. [توثيق قطّارة Chromium](https://developer.chrome.com/docs/capabilities/web-apis/eyedropper)

في تسليم المشروع المحلي ستجد الملفات في مجلد `dist`. عند نشر إصدار على GitHub اختر **Releases ← الإصدار المطلوب ← Assets** ثم اسم الحزمة الوارد أدناه. قارن بصمة SHA-256 بملف `SHA256SUMS` المرفق بالإصدار. البصمة تساعد على اكتشاف تلف التنزيل. احتفظ بنسخة JSON من المكتبة قبل نقل الملف أو تحديث التطبيق.

## Windows

1. نزّل `PickerHub-0.1.0-Windows.zip`. اختر **Extract All** وضع المجلد مثلاً في مجلد التطبيقات الشخصي. تشغيل الملف من داخل معاينة ZIP قد يغيّر مسار التخزين في كل مرة.
2. افتح `PickerHub.html` بالنقر المزدوج. إذا فتحه محرر نصوص، استخدم **Open with** واختر Edge أو Chrome. يمكنك إنشاء اختصار له على سطح المكتب.
3. افتح «التقاط من الشاشة» واختر قطّارة النظام أو المعاينة. للانتقال إلى شاشة أخرى اخترها من مربع مشاركة الشاشة عند بدء جلسة جديدة.
4. للتحقق من الملف في PowerShell شغّل `Get-FileHash .\PickerHub-0.1.0-Windows.zip -Algorithm SHA256`.

**البناء من المصدر.** ثبّت Rust عبر rustup، وPython 3.9 أو أحدث، وأدوات C++ وWindows SDK من Visual Studio Build Tools لتشغيل اختبارات المضيف. فك ضغط المصدر أو استنسخ مستودعه. افتح PowerShell في المجلد الذي يحتوي على Cargo.toml.

```powershell
rustup target add wasm32-unknown-unknown
cargo test --locked
py -3 scripts/build.py --packages
Start-Process .\dist\PickerHub.html
```

**حل المشكلات.** أعد فتح الطرفية إذا لم تتعرّف على Cargo أو Python بعد تثبيتهما. عند ظهور خطأ `link.exe` راجع مكوّنات C++ وWindows SDK. إذا منعت سياسة جهاز العمل مشاركة الشاشة، يمكن استخدام محرر HEX أو تحليل لقطة محفوظة. عند عدم ظهور شاشة ثانية تحقق من إعدادات العرض واختر مشاركة الشاشة الكاملة.

## Linux

**Debian وUbuntu ومشتقاتهما.** حمّل `pickerhub_0.1.0_all.deb` ثم نفّذ من مجلد التنزيل. يضيف التثبيت مدخلاً في قائمة التطبيقات وأمر تشغيل.

```sh
sudo apt install ./pickerhub_0.1.0_all.deb
pickerhub
```

**Fedora وArch وopenSUSE وتوزيعات أخرى.** استخدم الحزمة المحمولة `PickerHub-0.1.0-Linux.tar.gz`. يتطلب المشغّل `xdg-open` من حزمة `xdg-utils`. يمكن أيضاً فتح HTML مباشرة من المتصفح.

```sh
mkdir -p "$HOME/.local/opt"
tar -xzf PickerHub-0.1.0-Linux.tar.gz -C "$HOME/.local/opt"
"$HOME/.local/opt/PickerHub/pickerhub"
```

**البناء.** ثبّت rustup وPython 3.9+ وأدوات بناء المضيف. تتضمنها `build-essential` على Ubuntu، و`gcc` على Fedora، و`base-devel` على Arch. أضف `dpkg-deb` لبناء حزمة Debian. من جذر المصدر نفّذ الآتي.

```sh
rustup target add wasm32-unknown-unknown
cargo test --locked
python3 scripts/build.py --packages
xdg-open dist/PickerHub.html
```

**Wayland.** عند طلب المعاينة يُظهر النظام نافذة لاختيار الشاشة. يحتاج المسار إلى PipeWire وبوابة XDG وخلفية ScreenCast المناسبة لسطح المكتب، مثل GNOME أو KDE. إذا غاب مربع الاختيار أو كانت الشاشة سوداء، افحص حالة الخدمات في جلسة المستخدم، واختر شاشة كاملة وجرّب نافذة غير محمية. استخدم مدير حزم التوزيعة لإكمال المكوّن المفقود. [وثائق بوابة الشاشة](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.impl.portal.ScreenCast.html)

```sh
echo "$XDG_SESSION_TYPE"
systemctl --user status pipewire xdg-desktop-portal
journalctl --user -u xdg-desktop-portal -b
```

**X11.** شغّل المتصفح من جلسة سطح المكتب التي تحتوي على الشاشة المطلوبة. إذا لم تظهر إلا نافذة أو تبويب، بدّل إلى فئة الشاشة الكاملة داخل المنتقي. توفر جلسات SSH والآلات الافتراضية شاشات الجلسة التي يعمل فيها المتصفح. إذا تعذر المسار المباشر، استخدم المعاينة أو استورد لقطة PNG.

## macOS

1. نزّل `PickerHub-0.1.0-macOS.zip` وفك الضغط في Finder. انقل **PickerHub.app** إلى Applications وافتحه. يفتح المشغّل التطبيق المحلي في متصفحك الافتراضي.
2. إذا حجب Gatekeeper المشغّل غير الموقّع، يمكنك فتح ملف **PickerHub.html** المحمول مباشرةً بمتصفحك، أو بناؤه من المصدر. تتناول Apple رسائل التحذير ومصدر البرنامج في دليلها. [دليل Apple](https://support.apple.com/guide/mac-help/mh40617/mac)
3. عند أول التقاط افتح **System Settings ← Privacy & Security ← Screen & System Audio Recording** واسمح للمتصفح الذي يعرض PickerHub بتسجيل الشاشة، ثم أعد تشغيله إن طلب النظام ذلك. افتح المعاينة واختر الشاشة المطلوبة مجدداً. [أذونات التسجيل لدى Apple](https://support.apple.com/guide/mac-help/mchld6aa7d23/mac)
4. إذا غابت القطّارة المباشرة من المتصفح استخدم المعاينة أو افتح HTML باستخدام Chrome أو Edge. لا يتطلب تحليل الصور إذن تسجيل الشاشة.
5. للتحقق من الحزمة استخدم `shasum -a 256 PickerHub-0.1.0-macOS.zip`.

**البناء من المصدر.** ثبّت أدوات سطر أوامر Xcode باستخدام `xcode-select --install` وثبّت Rust عبر rustup وPython 3.9+. نفّذ الآتي من مجلد المصدر على Intel أو Apple Silicon.

```sh
rustup target add wasm32-unknown-unknown
cargo test --locked
python3 scripts/build.py --packages
open dist/PickerHub.html
```

## دقة البكسل ومكتبة المستخدم

- إذا عرض المتصفح تدفقاً مصغّراً بسبب إعدادات الشاشة أو التحجيم، تقيس المعاينة بكسلات **التدفق** وتعرض أبعاده. اختر الشاشة الكاملة وقارن أبعادها المعروضة بدقتها الأصلية. استخدم الأسهم للوصول إلى بكسل بين موضعي مؤشر في معاينة مصغّرة.
- قد يغيّر مسار الفيديو قيم القنوات قليلاً أثناء التحويل اللوني. في اختبار X11 لعينة مرجعية بقيمة `#87CEEB` أعطت المعاينة `#86CFEB`. عند الحاجة إلى مطابقة قيمة تصميم محددة، استخدم القطّارة المباشرة حيث تتاح أو أدخل HEX من المصدر.
- عند استخدام المعاينة على الشاشة التي تعرض التطبيق نفسه قد يتكرر منظر النافذة. شارك النافذة المطلوبة أو انقل PickerHub إلى الشاشة الثانية.
- تذكّر المتصفح والمسار الذي فتحت منه التطبيق. عند اختفاء اللوحات بعد نقل الملف افتح النسخة السابقة، نزّل نسخة احتياطية، ثم استعدها من الشريط العلوي في النسخة الجديدة. يعتمد سلوك التخزين المحلي للملفات على المتصفح. [توثيق localStorage](https://developer.mozilla.org/en-US/docs/Web/API/Window/localStorage)
- إذا حظر المتصفح التخزين أو الحافظة محلياً، اسمح بهما في إعدادات المتصفح أو افتح الملف من متصفح سطح مكتب آخر. يمكن أيضاً تقديم **مجلد dist فقط** محلياً بأمر `python3 -m http.server 8765 --bind 127.0.0.1 --directory dist` ثم فتح `http://127.0.0.1:8765/PickerHub.html`. يمثل هذا العنوان مكتبة مستقلة، فاستعد النسخة الاحتياطية عند الانتقال إليه. أغلق الخادم بـCtrl+C.
- لتنظيف التثبيت المحمول احذف مجلد التطبيق بعد حفظ النسخة الاحتياطية. لإزالة حزمة Debian استخدم `sudo apt remove pickerhub`. تُدار بيانات المكتبة بصورة مستقلة ضمن ملف تعريف المتصفح.

يتطلب البناء الأول اتصالاً لتنزيل مكوّنات Rust وتبعيات Cargo المثبتة في Cargo.lock. [تثبيت Rust](https://www.rust-lang.org/tools/install)
