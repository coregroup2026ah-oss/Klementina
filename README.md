# كلمنتينة · Klementina

محوّل ومحرّر ملفات لـ Windows يعمل من مكان المؤشر مباشرة: اسحب الملف، اضغط مفتاحاً، وأفلته على ما تريد.
مستوحى من فكرة Tangerine على الماك، ومبني من الصفر لـ Windows وبواجهة عربية.

## الاستخدام
| أثناء السحب اضغط | العجلة |
|---|---|
| **Shift** | تحويل الصيغة |
| **Ctrl** | الأدوات |
| **Alt** | الإجراءات |

- أفلت في منتصف العجلة للإلغاء، واضغط مفتاحاً آخر أثناء السحب للتبديل بين العجلات. إذا كثرت الخيارات، قف على «المزيد» لتنتقل للصفحة التالية.
- من لوحة المفاتيح: حدد ملفاً في المستكشف ثم `Ctrl+Alt+Z` (يمكن تغيير الحرف من الإعدادات)، الأسهم للاختيار، `Enter` للتنفيذ، `Tab` للتبديل، `Esc` للإلغاء.
- النتيجة تُحفظ دائماً كنسخة جديدة بجانب الأصل (مثل `صورة-مضغوط.jpg`)، ولا يُرفع أي ملف للإنترنت.

## التحويلات
| النوع | إلى |
|---|---|
| صور (JPG، PNG، WebP، HEIC، AVIF، TIFF، BMP، GIF، ICO، **SVG**) | PNG، JPG، WebP، AVIF، HEIC*، BMP، GIF، TIFF، ICO، PDF |
| فيديو (MP4، MOV، MKV، WebM، AVI، WMV…) | MP4، MOV، MKV، WebM، GIF، AVI، WMV، MP3، M4A |
| صوت (MP3، M4A، WAV، FLAC، OGG، Opus، AIFF، WMA) | كل صيغ الصوت |
| PDF | Word، PowerPoint، Excel، EPUB، Markdown، نص، PNG، JPG |
| مستندات (DOCX، DOC، RTF، ODT) | PDF، DOCX، ODT، RTF، نص، HTML، Markdown، EPUB |
| نصوص (TXT، Markdown، HTML) | HTML، PDF، DOCX، Markdown، EPUB |
| كتب EPUB | PDF، Word، Markdown، نص، HTML |
| عروض (PPTX، PPT، ODP) | PDF، PNG، JPG، PPTX |
| جداول (XLSX، XLS، ODS، CSV، TSV) | XLSX، CSV، TSV، PDF، ODS، HTML |
| ترجمات (SRT، VTT) | SRT، VTT، نص |
| أرشيفات (ZIP، TAR، GZ، RAR، 7Z) | ZIP، TAR، TAR.GZ |

\* HEIC يظهر فقط إذا كانت إضافة «HEIF Image Extensions» مثبتة في Windows.
تحويل PDF إلى Word/Excel/EPUB/Markdown يقرأ النص بقارئ النصوص المدمج في Windows (عربي وإنجليزي)، فيعمل حتى مع الملفات الممسوحة ضوئياً، لكنه ينقل النص وليس التنسيق.

## الأدوات
- **الصور:** ضغط، ضغط لحجم أو نسبة محددة، تعديل الصورة (تعريض، سطوع، تباين، ظلال، حرارة الألوان…)، قص، رسم وكتابة، إخفاء أجزاء (أسود/بكسلة/تمويه)، إضافة خلفية، كولاج، دمج في PDF، قراءة رمز QR، تصغير، تدوير، قلب، أبيض وأسود، معلومات الملف، حذف البيانات المخفية.
- **الفيديو:** ضغط، ضغط لحجم محدد، قص المدة (مع معاينة ولقطة)، السرعة، دمج، تقسيم، قص الأبعاد، تطنين، القنوات، 720p، كتم الصوت، تدوير، لقطة، معلومات وحذف البيانات.
- **الصوت:** ضغط، ضغط لحجم محدد، قص المدة (بشكل الموجة مع قص الصمت تلقائياً)، تطنين (Bleep)، القنوات (مونو/ستيريو ومستوى كل جهة)، توحيد الصوت، السرعة، دمج، تقسيم، تعديل بيانات الأغنية، حذف البيانات.
- **PDF:** ضغط، ضغط لحجم محدد، دمج، تقسيم، ترتيب الصفحات (سحب، تدوير، حذف)، استخراج صفحات، تدوير، معلومات وحذف البيانات.
- **لكل الملفات:** ضغط ZIP، بصمة SHA-256، خصائص.

## الإجراءات
نسخ، نسخ المسار، نقل إلى، نسخ إلى، الطرفية، تعويم (رف عائم تسحب منه الملف لاحقاً)، مشاركة (مشاركة Windows/Nearby Share)، إظهار في المجلد، فتح بـ.

## ما يحتاجه كل نوع
| الميزة | يحتاج |
|---|---|
| كل ما سبق تقريباً | لا شيء — FFmpeg و qpdf و ZXing مضمّنة |
| Word / Excel / PowerPoint، و PDF ← Word/Excel/PowerPoint | Microsoft Office |

## التثبيت
نزّل `Klementina-Setup-*.exe` من صفحة [Releases](../../releases) وشغّله. لا يحتاج صلاحيات المسؤول.

## البناء
يستخدم مترجم C# الموجود أصلاً في Windows (.NET Framework 4.8)، فلا يحتاج Visual Studio ولا أي SDK.
ملف `ffmpeg.exe` غير موجود في المستودع لأن حجمه أكبر من حد GitHub، فنزّله أولاً مرة واحدة:
```
powershell -ExecutionPolicy Bypass -File tools\get-ffmpeg.ps1
build.cmd                  → bin\Klementina.exe
installer\build-setup.cmd  → dist\Klementina-Setup-1.0.0.exe   (يحتاج Inno Setup 6.7+)
tests\run-tests.cmd <مجلد فارغ>
```

## المكوّنات المضمّنة
- FFmpeg 9.0.2 (gyan.dev، رخصة GPL) — `vendor\ffmpeg` (يُنزَّل بـ `tools\get-ffmpeg.ps1`)
- qpdf 12.4.2 (رخصة Apache-2.0) — `vendor\qpdf`
- ZXing.Net 0.16.11 (رخصة Apache-2.0) — `vendor\zxing`

## الرخصة
GPL-3.0، انظر ملف [LICENSE](LICENSE).

---

## English

**Klementina** is a file converter and editor for Windows that works right where your cursor is: drag a file, tap a key, and drop it on what you want. The interface is in Arabic and English. It was inspired by the idea of Tangerine for Mac and built from scratch for Windows.

### How it works
While dragging a file, tap **Shift** for the format wheel, **Ctrl** for tools, or **Alt** for actions. Drop in the middle of the wheel to cancel. You can also select a file in File Explorer and press `Ctrl+Alt+Z` (the letter can be changed in Settings).

The result is always saved as a new copy next to the original. Nothing is uploaded anywhere: everything runs on your PC.

### What it can do
- **Convert** images (including HEIC, AVIF and SVG), video, audio, PDF, Word/ODT/RTF documents, Markdown and HTML, EPUB, presentations, spreadsheets, subtitles and archives.
- **Images:** compress (also to a target size), adjust, crop, annotate, redact, add a background, collage, combine into PDF, read QR codes, resize, rotate, strip metadata.
- **Video and audio:** compress, trim with preview or waveform, speed, merge, split, crop, bleep, channels, normalize loudness, edit song tags.
- **PDF:** compress, merge, split, reorder pages, extract pages, rotate, convert to text or Word using the OCR built into Windows (Arabic and English).
- **Actions:** copy, copy path, move to, copy to, open a terminal, a floating shelf, Windows Share, show in folder, open with.

Converting to or from Word, Excel and PowerPoint formats needs Microsoft Office. Everything else works out of the box.

### Install
Download `Klementina-Setup-*.exe` from [Releases](../../releases) and run it. No admin rights needed.

### Build from source
Klementina builds with the C# compiler that already ships with Windows (.NET Framework 4.8). No Visual Studio or SDK is needed.

`ffmpeg.exe` is not stored in the repository because it is larger than GitHub's file size limit. Download it once first:
```
powershell -ExecutionPolicy Bypass -File tools\get-ffmpeg.ps1
build.cmd                  → bin\Klementina.exe
installer\build-setup.cmd  → dist\Klementina-Setup-1.0.0.exe   (needs Inno Setup 6.7+)
tests\run-tests.cmd <empty folder>
```

### Third-party components
- FFmpeg 9.0.2 (gyan.dev build, GPL), `vendor\ffmpeg`
- qpdf 12.4.2 (Apache-2.0), `vendor\qpdf`
- ZXing.Net 0.16.11 (Apache-2.0), `vendor\zxing`

### License
GPL-3.0. See [LICENSE](LICENSE).
