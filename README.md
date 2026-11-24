# كلمنتينة · Klementina

**بواسطة بيدر للتكنولوجيا · by Bayder Technology**

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
تحويل PDF إلى Word يقرأ النص الحقيقي داخل الملف وينقل معه الصور والألوان والخطوط والخلفيات والعناوين والقوائم والجداول، ويضبط اتجاه العربي من اليمين، ولا يحتاج Microsoft Office. النسخة «طبق الأصل» تضع كل سطر فوق خطوطه ونقاطه في مكانه بالضبط.

تحويل PDF إلى PowerPoint يصنع شريحة لكل صفحة بنفس شكلها، والنص فيها نصوص حقيقية قابلة للتعديل (العربي من اليمين، والأرقام كما هي)، بدون الحاجة لـ PowerPoint. وتحويل PDF أو CSV إلى Excel لا يحتاج Excel، ويحفظ العربي سليماً والأرقام كأرقام والورقة من اليمين لليسار. الملفات الممسوحة ضوئياً أو المحفوظة كصور تُقرأ بقارئ النصوص المدمج في Windows (عربي وإنجليزي)، مع إعادة بناء الجداول والألوان وقص الصور منها ووضعها في أماكنها، ومع الصفحات ذات العمودين.

## الأدوات
- **الصور:** ضغط، ضغط لحجم أو نسبة محددة، تعديل الصورة (تعريض، سطوع، تباين، ظلال، حرارة الألوان…)، قص، رسم وكتابة، إخفاء أجزاء (أسود/بكسلة/تمويه)، إضافة خلفية، كولاج، دمج في PDF، قراءة رمز QR، تصغير، تدوير، قلب، أبيض وأسود، معلومات الملف، حذف البيانات المخفية.
- **الفيديو:** ضغط، ضغط لحجم محدد، قص المدة (مع معاينة وشريط لقطات ومسطرة زمنية؛ I و O لتحديد البداية والنهاية والأسهم للتنقل)، السرعة، دمج، تقسيم، قص الأبعاد، تطنين، القنوات، 720p، كتم الصوت، تدوير، لقطة، معلومات وحذف البيانات.
- **الصوت:** ضغط، ضغط لحجم محدد، قص المدة (بشكل الموجة مع قص الصمت تلقائياً)، تطنين (Bleep)، القنوات (مونو/ستيريو ومستوى كل جهة)، توحيد الصوت، السرعة، دمج، تقسيم، تعديل بيانات الأغنية، حذف البيانات.
- **PDF:** ضغط، ضغط لحجم محدد، دمج، تقسيم، ترتيب الصفحات (سحب، تدوير، حذف)، استخراج صفحات، تدوير، معلومات وحذف البيانات.
- **لكل الملفات:** ضغط ZIP، بصمة SHA-256، خصائص.

## الإجراءات
نسخ، نسخ المسار، نقل إلى، نسخ إلى، الطرفية، تعويم (رف عائم تسحب منه الملف لاحقاً)، مشاركة (مشاركة Windows/Nearby Share)، إظهار في المجلد، فتح بـ.

## اللغات
الواجهة متوفرة بـ 30 لغة، وتختار لغة Windows تلقائياً أول مرة، ويمكن تغييرها من الإعدادات:
العربية، English، Français، Español، Deutsch، Português، Italiano، Nederlands، Türkçe، فارسی، اردو، हिन्दी، বাংলা، Bahasa Indonesia، Bahasa Melayu، Русский، Українська، Polski، Čeština، Română، Ελληνικά، עברית، 简体中文، 繁體中文، 日本語، 한국어، Tiếng Việt، ไทย، Svenska، Kiswahili.

العربية والإنجليزية مكتوبتان داخل البرنامج، وكل لغة أخرى ملف نصي في مجلد `lang`. الترجمات غير العربية والإنجليزية أُعدّت بمساعدة الذكاء الاصطناعي، فإذا وجدت خطأً في لغتك فصحّحه وأرسل Pull Request.

لإضافة لغة: انسخ `lang/fr.txt` إلى `lang/<رمز اللغة>.txt` وترجم ما بعد علامة `=` في كل سطر (اترك `{0}` و`§HK§` و`\n` كما هي)، ثم أضف اللغة إلى قائمة `Languages` في `src/Strings.cs`، وافحص الملف بـ `python tools/check-lang.py`.

## ما يحتاجه كل نوع
| الميزة | يحتاج |
|---|---|
| كل ما سبق تقريباً | لا شيء — FFmpeg و qpdf و ZXing مضمّنة |
| تحويل ملفات Word / Excel / PowerPoint نفسها | Microsoft Office |

## التثبيت
نزّل `Klementina-Setup-*.exe` من صفحة [Releases](../../releases) وشغّله. لا يحتاج صلاحيات المسؤول.

## البناء
يستخدم مترجم C# الموجود أصلاً في Windows (.NET Framework 4.8)، فلا يحتاج Visual Studio ولا أي SDK.
ملف `ffmpeg.exe` غير موجود في المستودع لأن حجمه أكبر من حد GitHub، فنزّله أولاً مرة واحدة:
```
powershell -ExecutionPolicy Bypass -File tools\get-ffmpeg.ps1
build.cmd                  → bin\Klementina.exe
installer\build-setup.cmd  → dist\Klementina-Setup-1.1.0.exe   (يحتاج Inno Setup 6.7+)
tests\run-tests.cmd <مجلد فارغ>
```

## المكوّنات المضمّنة
- FFmpeg 9.0.2 (gyan.dev، رخصة GPL) — `vendor\ffmpeg` (يُنزَّل بـ `tools\get-ffmpeg.ps1`)
- qpdf 12.4.2 (رخصة Apache-2.0) — `vendor\qpdf`
- ZXing.Net 0.16.11 (رخصة Apache-2.0) — `vendor\zxing`

## الرخصة
© 2026 بيدر للتكنولوجيا (Bayder Technology). مرخّص بـ GPL-3.0، انظر ملف [LICENSE](LICENSE).

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
- **PDF:** compress, merge, split, reorder pages, extract pages, rotate, convert to Word with the pictures, colours, fonts, backgrounds, headings, lists and tables kept (no Office needed), with Arabic laid out right to left; the exact copy puts every line back on its underline and dots. PDF to PowerPoint makes a slide per page that looks like the page, with real, editable text boxes (no PowerPoint needed), and PDF or CSV to Excel needs no Excel either. Scanned pages and designs saved as pictures are read with the OCR built into Windows (Arabic and English), rebuilding tables, colours and two-column layouts, with the photos cut out and put back in place.
- **Actions:** copy, copy path, move to, copy to, open a terminal, a floating shelf, Windows Share, show in folder, open with.

Converting Word, Excel and PowerPoint files themselves needs Microsoft Office. Everything else works out of the box.

### Languages
The interface comes in 30 languages. Klementina picks the Windows display language on first run, and you can change it in Settings:
Arabic, English, French, Spanish, German, Portuguese, Italian, Dutch, Turkish, Persian, Urdu, Hindi, Bengali, Indonesian, Malay, Russian, Ukrainian, Polish, Czech, Romanian, Greek, Hebrew, Simplified Chinese, Traditional Chinese, Japanese, Korean, Vietnamese, Thai, Swedish and Swahili. Arabic, Persian, Urdu and Hebrew use a right-to-left layout.

Arabic and English are built into the code; every other language is a text file in `lang/`. The translations other than Arabic and English were made with AI assistance, so corrections from native speakers are very welcome: fix the line and open a pull request.

To add a language, copy `lang/fr.txt` to `lang/<code>.txt` and translate the text after `=` on each line (keep `{0}`, `§HK§` and `\n` as they are). Then add the language to the `Languages` list in `src/Strings.cs` and check the file with `python tools/check-lang.py`. A missing line falls back to English.

### Install
Download `Klementina-Setup-*.exe` from [Releases](../../releases) and run it. No admin rights needed.

### Build from source
Klementina builds with the C# compiler that already ships with Windows (.NET Framework 4.8). No Visual Studio or SDK is needed.

`ffmpeg.exe` is not stored in the repository because it is larger than GitHub's file size limit. Download it once first:
```
powershell -ExecutionPolicy Bypass -File tools\get-ffmpeg.ps1
build.cmd                  → bin\Klementina.exe
installer\build-setup.cmd  → dist\Klementina-Setup-1.1.0.exe   (needs Inno Setup 6.7+)
tests\run-tests.cmd <empty folder>
```

### Third-party components
- FFmpeg 9.0.2 (gyan.dev build, GPL), `vendor\ffmpeg`
- qpdf 12.4.2 (Apache-2.0), `vendor\qpdf`
- ZXing.Net 0.16.11 (Apache-2.0), `vendor\zxing`

### License
© 2026 Bayder Technology. Licensed under GPL-3.0. See [LICENSE](LICENSE).
