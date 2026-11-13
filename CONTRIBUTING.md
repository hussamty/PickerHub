# المساهمة

استخدم قالب **Issues** للإبلاغ عن خلل أو اقتراح ميزة. أرفق إصدار PickerHub، ونظام التشغيل، ومعمارية الجهاز، ونوع جلسة Linux، وخطوات إعادة المشكلة، والقيم المتوقعة والفعلية. تكفي صورة اختبار مصطنعة لفحص مشكلة ألوان.

نفّذ حسابات اللون في src، والواجهة في desktop، والالتقاط الخاص بالنظام في desktop/Picker.cs وdesktop/native. استخدم دليلاً منفصلاً لبيانات اختبارات سطح المكتب. عند اقتراح تغيير، أرفق طريقة التحقق، واختباراً للحالة المتأثرة، ولقطة واجهة عند تغيير شكلها.

```sh
cargo fmt --check
cargo clippy --all-targets --locked -- -D warnings
cargo test --locked
python3 scripts/build.py --packages
```

على Linux شغّل اختبار الشاشة المعزولة كما في tests/native_x11.py. قبل نشر إصدار اختبر القطّارة من زرها فوق نافذة أخرى وعلى شاشة ثانية، والإلغاء بـEsc، وحفظ المكتبة واستعادتها. اختبر الإذن النظامي على Wayland وmacOS. يبني سير العمل كل حزمة على نظامها ويجمع ملفات التسليم وبصماتها عند إنشاء وسم الإصدار.

```sh
xvfb-run -a -s '-screen 0 1400x900x24 -noreset' python3 tests/native_x11.py
```

يتحقق الاختبار التالي من طلب الإذن وتغيّر بكسلات مصدر مولّد أثناء فتح العدسة عبر PipeWire. يحتاج إلى python3-dbus وpython3-gi وGStreamer وملحقاته وPipeWire وWirePlumber 0.5 أو أحدث. يمكن تجربة الإلغاء والرفض بضبط PICKERHUB_PORTAL_RESULT إلى 1 أو 2.

```sh
dbus-run-session -- xvfb-run -a -s '-screen 0 1400x900x24 -noreset' sh tests/run_portal_test.sh
```
