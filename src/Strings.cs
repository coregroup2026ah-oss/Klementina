using System.Collections.Generic;
using System.Linq;

namespace Klementina
{
    static class S
    {
        static readonly Dictionary<string, string[]> map = new Dictionary<string, string[]>
        {
            // wheels
            { "convert", new[] { "تحويل", "Convert" } },
            { "tools", new[] { "أدوات", "Tools" } },
            { "actions", new[] { "إجراءات", "Actions" } },
            { "more", new[] { "المزيد", "More" } },
            { "cancel", new[] { "إلغاء", "Cancel" } },
            { "files_n", new[] { "{0} ملفات", "{0} files" } },
            { "folder", new[] { "مجلد", "Folder" } },
            { "no_options", new[] { "لا توجد خيارات", "No options" } },
            { "drop_here", new[] { "حرّك إلى خيار", "Move onto an option" } },

            // tools
            { "compress", new[] { "ضغط", "Compress" } },
            { "resize", new[] { "تصغير ½", "Resize ½" } },
            { "rotate", new[] { "تدوير", "Rotate" } },
            { "flip", new[] { "قلب", "Flip" } },
            { "gray", new[] { "أبيض وأسود", "Grayscale" } },
            { "strip", new[] { "حذف البيانات", "Strip info" } },
            { "combine_pdf", new[] { "دمج في PDF", "Combine PDF" } },
            { "copy_image", new[] { "نسخ الصورة", "Copy image" } },
            { "edit", new[] { "تعديل", "Edit" } },
            { "p720", new[] { "720p", "720p" } },
            { "mute", new[] { "كتم الصوت", "Mute" } },
            { "trim", new[] { "قص المدة", "Trim" } },
            { "frame", new[] { "لقطة", "Frame" } },
            { "normalize", new[] { "توحيد الصوت", "Normalize" } },
            { "mono", new[] { "أحادي", "Mono" } },
            { "extract", new[] { "فك الضغط", "Extract" } },
            { "zip", new[] { "ضغط ZIP", "Zip" } },
            { "hash", new[] { "بصمة SHA", "Hash" } },
            { "props", new[] { "خصائص", "Properties" } },

            { "compress_to", new[] { "ضغط لحجم…", "Compress to…" } },
            { "compress_now", new[] { "{0} — الحجم الآن {1}", "{0} — currently {1}" } },
            { "compress_pct", new[] { "صغّره إلى نسبة من حجمه:", "Shrink to a share of its size:" } },
            { "custom_size", new[] { "حجم محدد", "Exact size" } },
            { "target_mb", new[] { "الحجم المطلوب (ميغابايت) — عند اختيار «حجم محدد»", "Target size (MB) — when “Exact size” is picked" } },
            { "pdf_raster_note", new[] { "ملفات PDF: الضغط يصغّر الصور داخل الملف ويبقي النص قابلاً للتحديد. فقط إذا طلبت حجماً صغيراً جداً قد تتحوّل الصفحات إلى صور.", "PDFs: compression shrinks the pictures inside and keeps text selectable. Only for a very small target size may pages be turned into images." } },
            { "compress_how", new[] { "كيف تريد ضغطه؟", "How much?" } },
            { "lv_light", new[] { "خفيف", "Light" } },
            { "lv_balanced", new[] { "متوازن (موصى به)", "Balanced (recommended)" } },
            { "lv_strong", new[] { "قوي", "Strong" } },
            { "lv_max", new[] { "أقصى ضغط", "Maximum" } },
            { "by_percent", new[] { "نسبة من الحجم", "Percent of size" } },
            { "compress_levels_note", new[] { "خفيف = أعلى جودة · أقصى = أصغر حجم. «نسبة من الحجم» تستخدم الاختيار أعلاه، و«حجم محدد» تستخدم الرقم بالميغابايت.", "Light = best quality · Maximum = smallest. “Percent of size” uses the choice above; “Exact size” uses the MB number." } },
            { "merge", new[] { "دمج", "Merge" } },
            { "join", new[] { "دمج", "Join" } },
            { "split", new[] { "تقسيم", "Split" } },
            { "split_how", new[] { "كيف تريد تقسيمه؟", "How should it be split?" } },
            { "split_halves", new[] { "نصفين", "In half" } },
            { "split_equal", new[] { "أجزاء متساوية", "Equal parts" } },
            { "split_every", new[] { "كل عدد من الصفحات", "Every N pages" } },
            { "split_ranges", new[] { "نطاقات أحددها", "My own ranges" } },
            { "split_each", new[] { "كل صفحة ملف", "One file per page" } },
            { "split_parts_n", new[] { "عدد الأجزاء (لـ «أجزاء متساوية»)", "Number of parts (for “Equal parts”)" } },
            { "split_every_n", new[] { "عدد الصفحات في كل ملف (لـ «كل عدد من الصفحات»)", "Pages per file (for “Every N pages”)" } },
            { "split_ranges_hint", new[] { "النطاقات مفصولة بفواصل، كل نطاق ملف (مثال: 1-100، 101-250، 251-365)", "Ranges separated by commas, one file each (e.g. 1-100, 101-250, 251-365)" } },
            { "split_parts", new[] { "عدد الأجزاء:", "Number of parts:" } },
            { "split_note", new[] { "يُقسَّم بالتساوي بدون إعادة ترميز، فيكون سريعاً ويحافظ على الجودة.", "Split evenly without re-encoding — fast and lossless." } },
            { "extract_pages", new[] { "استخراج صفحات", "Extract pages" } },
            { "pdf_has_pages", new[] { "{0} فيه {1} صفحة.", "{0} has {1} pages." } },
            { "pages_hint", new[] { "الصفحات (مثال: 1-3، 5، 8-10)", "Pages (e.g. 1-3, 5, 8-10)" } },
            { "collage", new[] { "كولاج", "Collage" } },
            { "read_qr", new[] { "قراءة QR", "Read QR" } },
            { "no_code", new[] { "لم يُعثر على رمز QR أو باركود في الصورة", "No QR code or barcode found in the image" } },
            { "code_copied", new[] { "نُسخ محتوى الرمز إلى الحافظة", "Code contents copied to the clipboard" } },
            { "speed", new[] { "السرعة", "Speed" } },
            { "speed_pick", new[] { "اختر السرعة الجديدة:", "Pick the new speed:" } },
            { "share", new[] { "مشاركة", "Share" } },

            // editor windows
            { "edit_photo", new[] { "تعديل الصورة", "Edit photo" } },
            { "crop", new[] { "قص", "Crop" } },
            { "annotate", new[] { "رسم وكتابة", "Annotate" } },
            { "redact", new[] { "إخفاء", "Redact" } },
            { "add_bg", new[] { "إضافة خلفية", "Add BG" } },
            { "metadata", new[] { "معلومات الملف", "Metadata" } },
            { "save_copy", new[] { "حفظ نسخة", "Save copy" } },
            { "reset", new[] { "إعادة ضبط", "Reset" } },
            { "light", new[] { "الإضاءة", "Light" } },
            { "exposure", new[] { "التعريض", "Exposure" } },
            { "brightness", new[] { "السطوع", "Brightness" } },
            { "contrast", new[] { "التباين", "Contrast" } },
            { "highlights", new[] { "المناطق الفاتحة", "Highlights" } },
            { "shadows", new[] { "الظلال", "Shadows" } },
            { "whites", new[] { "الأبيض", "Whites" } },
            { "blacks", new[] { "الأسود", "Blacks" } },
            { "gamma", new[] { "غاما", "Gamma" } },
            { "color", new[] { "اللون", "Color" } },
            { "temperature", new[] { "الحرارة", "Temperature" } },
            { "tint", new[] { "الصبغة", "Tint" } },
            { "saturation", new[] { "التشبّع", "Saturation" } },
            { "vibrance", new[] { "الحيوية", "Vibrance" } },
            { "quick", new[] { "سريع", "Quick" } },
            { "auto_enhance", new[] { "تحسين تلقائي", "Auto enhance" } },
            { "black_white", new[] { "أبيض وأسود", "Black & white" } },
            { "aspect", new[] { "النسبة", "Aspect" } },
            { "free", new[] { "حر", "Free" } },
            { "original", new[] { "الأصلية", "Original" } },
            { "rotate_flip", new[] { "تدوير وقلب", "Rotate & flip" } },
            { "rotate_left", new[] { "تدوير لليسار", "Rotate left" } },
            { "rotate_right", new[] { "تدوير لليمين", "Rotate right" } },
            { "flip_h", new[] { "قلب أفقي", "Flip horizontal" } },
            { "flip_v", new[] { "قلب عمودي", "Flip vertical" } },
            { "crop_hint", new[] { "اسحب على الصورة لتحديد منطقة القص، واسحب الزوايا لتعديلها.", "Drag on the image to choose the area; drag the corners to adjust." } },
            { "tool", new[] { "الأداة", "Tool" } },
            { "t_pen", new[] { "قلم", "Pen" } },
            { "t_highlight", new[] { "تظليل", "Highlight" } },
            { "t_arrow", new[] { "سهم", "Arrow" } },
            { "t_line", new[] { "خط", "Line" } },
            { "t_rect", new[] { "مستطيل", "Box" } },
            { "t_ellipse", new[] { "دائرة", "Circle" } },
            { "t_text", new[] { "نص", "Text" } },
            { "thickness", new[] { "السُمك", "Thickness" } },
            { "redact_style", new[] { "طريقة الإخفاء", "Redact style" } },
            { "r_black", new[] { "أسود", "Black" } },
            { "r_pixel", new[] { "بكسلة", "Pixelate" } },
            { "r_blur", new[] { "تمويه", "Blur" } },
            { "redact_hint", new[] { "اسحب مستطيلاً فوق كل جزء تريد إخفاءه (وجوه، أرقام، عناوين…).", "Drag a box over each part you want hidden (faces, numbers, addresses…)." } },
            { "annotate_hint", new[] { "اختر الأداة واللون ثم ارسم على الصورة. للنص: اختر «نص» وانقر مكانه.", "Pick a tool and colour, then draw. For text: pick “Text” and click where it goes." } },
            { "undo", new[] { "تراجع", "Undo" } },
            { "text_prompt", new[] { "اكتب النص:", "Type the text:" } },
            { "background", new[] { "الخلفية", "Background" } },
            { "bg_photo", new[] { "صورة من جهازك…", "Your own image…" } },
            { "layout", new[] { "التنسيق", "Layout" } },
            { "padding", new[] { "المسافة", "Padding" } },
            { "corners", new[] { "الزوايا", "Corners" } },
            { "shadow", new[] { "الظل", "Shadow" } },
            { "ratio", new[] { "النسبة", "Ratio" } },
            { "auto", new[] { "تلقائي", "Auto" } },
            { "field", new[] { "الحقل", "Field" } },
            { "value", new[] { "القيمة", "Value" } },
            { "tag_title", new[] { "العنوان", "Title" } },
            { "tag_artist", new[] { "الفنان", "Artist" } },
            { "tag_album", new[] { "الألبوم", "Album" } },
            { "tag_date", new[] { "السنة", "Year" } },
            { "tag_genre", new[] { "النوع", "Genre" } },
            { "tag_comment", new[] { "تعليق", "Comment" } },
            { "edit_tags", new[] { "تعديل البيانات", "Edit tags" } },
            { "save_tags", new[] { "حفظ نسخة بالبيانات", "Save tagged copy" } },
            { "remove_all_meta", new[] { "حذف كل البيانات المخفية", "Remove all hidden data" } },
            { "close", new[] { "إغلاق", "Close" } },

            // audio/video windows
            { "bleep", new[] { "تطنين", "Bleep" } },
            { "channels", new[] { "القنوات", "Channels" } },
            { "crop_video", new[] { "قص الأبعاد", "Crop video" } },
            { "no_duration", new[] { "تعذّر قراءة مدة الملف", "Couldn't read the file's duration" } },
            { "play_selection", new[] { "تشغيل المقطع المحدد", "Play selection" } },
            { "play_original", new[] { "تشغيل الأصلي", "Play original" } },
            { "play_bleeped", new[] { "تشغيل مع التطنين", "Play with bleeps" } },
            { "play_output", new[] { "تشغيل النتيجة", "Play output" } },
            { "trim_silence", new[] { "قص الصمت من الأطراف تلقائياً", "Trim silent ends automatically" } },
            { "snapshot", new[] { "لقطة من هنا", "Snapshot here" } },
            { "save_bleeped", new[] { "حفظ نسخة مُطنَّنة", "Save bleeped copy" } },
            { "apply", new[] { "تطبيق وحفظ نسخة", "Apply" } },
            { "bleep_hint", new[] { "اسحب على الموجة فوق كل كلمة تريد إخفاءها.", "Drag across the waveform over each word to bleep." } },
            { "bleeps", new[] { "التطنينات", "Bleeps" } },
            { "stereo", new[] { "ستيريو", "Stereo" } },
            { "left", new[] { "يسار", "Left" } },
            { "right", new[] { "يمين", "Right" } },
            { "start", new[] { "البداية", "Start" } },
            { "end", new[] { "النهاية", "End" } },

            { "reorganize", new[] { "ترتيب الصفحات", "Reorganize" } },
            { "rotate_selected", new[] { "تدوير المحدد", "Rotate selected" } },
            { "delete_selected", new[] { "حذف المحدد", "Delete selected" } },
            { "select_all", new[] { "تحديد الكل", "Select all" } },
            { "organize_hint", new[] { "اسحب الصفحات لترتيبها. Ctrl+نقر لتحديد أكثر من صفحة.", "Drag pages to reorder. Ctrl+click to select several." } },
            { "page_n", new[] { "صفحة {0}", "Page {0}" } },

            // actions
            { "copy", new[] { "نسخ", "Copy" } },
            { "copy_path", new[] { "نسخ المسار", "Copy path" } },
            { "move_to", new[] { "نقل إلى…", "Move to…" } },
            { "copy_to", new[] { "نسخ إلى…", "Copy to…" } },
            { "terminal", new[] { "الطرفية", "Terminal" } },
            { "float", new[] { "تعويم", "Float" } },
            { "reveal", new[] { "إظهار", "Reveal" } },
            { "open_with", new[] { "فتح بـ…", "Open with…" } },

            // results
            { "done", new[] { "تم", "Done" } },
            { "created_n", new[] { "تم إنشاء {0} ملفات", "Created {0} files" } },
            { "working", new[] { "جارٍ العمل…", "Working…" } },
            { "failed", new[] { "تعذّر التنفيذ", "Couldn't finish" } },
            { "already_small", new[] { "الملف مضغوط أصلاً — لم يُنشأ ملف جديد", "Already as small as it gets — no new file" } },
            { "copied",new[] { "نُسخ إلى الحافظة", "Copied to clipboard" } },
            { "hash_copied", new[] { "نُسخت بصمة SHA-256 إلى الحافظة", "SHA-256 copied to clipboard" } },
            { "hotkey_taken", new[] { "الاختصار ‪Ctrl+Alt+§HK§‬ مستخدم من برنامج آخر، فلن يعمل من لوحة المفاتيح. السحب مع Shift يعمل كالمعتاد.", "Ctrl+Alt+§HK§ is used by another program, so the keyboard shortcut won't work. Dragging with Shift still works." } },
            { "select_first", new[] { "حدد ملفاً في مستكشف الملفات أولاً، ثم اضغط ‪Ctrl+Alt+§HK§‬", "Select a file in File Explorer first, then press Ctrl+Alt+§HK§" } },
            { "pick_dest", new[] { "اختر المجلد", "Choose a folder" } },
            { "trim_title", new[] { "قص المدة", "Trim" } },
            { "trim_start", new[] { "البداية (ثانية أو دقيقة:ثانية)", "Start (seconds or mm:ss)" } },
            { "trim_end", new[] { "النهاية (اتركه فارغاً للنهاية)", "End (blank = to the end)" } },
            { "ok", new[] { "موافق", "OK" } },
            { "need_ffmpeg", new[] { "يتطلب FFmpeg", "Needs FFmpeg" } },
            { "need_office", new[] { "يتطلب Office", "Needs Office" } },
            { "need_ffmpeg_long", new[] {
                "تحويل الصوت والفيديو يحتاج برنامج FFmpeg المجاني.\n\n1) نزّل نسخة Windows من الموقع الرسمي.\n2) ضع ملف ffmpeg.exe بجانب Klementina.exe، أو حدد مكانه من الإعدادات.\n\nهل تريد فتح صفحة التنزيل؟",
                "Audio and video conversion needs the free FFmpeg program.\n\n1) Download the Windows build from the official site.\n2) Put ffmpeg.exe next to Klementina.exe, or choose its location in Settings.\n\nOpen the download page?" } },
            { "need_office_long", new[] {
                "تحويل مستندات Word وExcel وPowerPoint يحتاج Microsoft Office مثبتاً على الجهاز.",
                "Converting Word, Excel and PowerPoint documents needs Microsoft Office installed." } },

            // tray & dialogs
            { "tray_help", new[] { "طريقة الاستخدام", "How to use" } },
            { "tray_settings", new[] { "الإعدادات", "Settings" } },
            { "tray_pause", new[] { "إيقاف مؤقت", "Pause" } },
            { "tray_exit", new[] { "خروج", "Exit" } },
            { "tray_tip", new[] { "كلمنتينة — اسحب ملفاً واضغط Shift", "Klementina — drag a file and tap Shift" } },
            { "paused", new[] { "كلمنتينة متوقفة مؤقتاً", "Klementina is paused" } },
            { "welcome", new[] { "كلمنتينة تعمل الآن. اسحب أي ملف واضغط Shift أثناء السحب.", "Klementina is running. Drag any file and tap Shift while dragging." } },

            { "set_title", new[] { "إعدادات كلمنتينة", "Klementina Settings" } },
            { "set_lang", new[] { "اللغة", "Language" } },
            { "set_drag", new[] { "إظهار العجلة عند ضغط مفتاح أثناء السحب", "Show the wheel when a key is tapped while dragging" } },
            { "set_hotkey", new[] { "اختصار لوحة المفاتيح ‪Ctrl+Alt+§HK§‬ للملف المحدد", "Keyboard shortcut Ctrl+Alt+§HK§ for the selected file" } },
            { "set_startup", new[] { "التشغيل مع بدء Windows", "Start with Windows" } },
            { "set_keys", new[] { "المفاتيح", "Keys" } },
            { "set_quality", new[] { "جودة JPEG", "JPEG quality" } },
            { "set_ffmpeg", new[] { "مسار FFmpeg", "FFmpeg path" } },
            { "set_browse", new[] { "استعراض…", "Browse…" } },
            { "set_status", new[] { "الأدوات المتوفرة", "Available engines" } },
            { "sec_general", new[] { "عام", "General" } },
            { "sec_keyboard", new[] { "لوحة المفاتيح", "Keyboard" } },
            { "sec_output", new[] { "الملفات الناتجة", "Output" } },
            { "set_save", new[] { "حفظ", "Save" } },
            { "set_hotkey_letter", new[] { "حرف الاختصار مع Ctrl+Alt", "Shortcut letter with Ctrl+Alt" } },
            { "set_cancel", new[] { "إلغاء", "Cancel" } },
            { "app_name", new[] { "كلمنتينة", "Klementina" } },
            { "tagline", new[] { "حوّل ملفاتك بلمسة وأنت تسحبها", "Convert files right where you drag them" } },
            { "no_text", new[] { "لم يُعثر على نص في الملف", "No text found in this PDF" } },
            { "made_by", new[] { "بواسطة بيدر للتكنولوجيا", "by Bayder Technology" } },
            { "set_dup_keys", new[] { "يجب أن يكون لكل عجلة مفتاح مختلف.", "Each wheel needs a different key." } },
            { "found", new[] { "موجود", "found" } },
            { "missing", new[] { "غير موجود", "not found" } },

            { "help_title", new[] { "طريقة الاستخدام", "How to use" } },
            { "help_body", new[] {
                "١. ابدأ بسحب أي ملف (أو عدة ملفات) من المستكشف أو سطح المكتب.\n" +
                "٢. أثناء السحب اضغط ضغطة سريعة على أحد المفاتيح:\n" +
                "      {0}  ←  عجلة التحويل (PNG، JPG، MP3، PDF…)\n" +
                "      {1}  ←  عجلة الأدوات (ضغط، تدوير، قص…)\n" +
                "      {2}  ←  عجلة الإجراءات (نسخ، نقل، تعويم…)\n" +
                "٣. أفلت الملف على الخيار الذي تريده. أفلته في المنتصف للإلغاء.\n\n" +
                "يمكنك التبديل بين العجلات أثناء السحب بضغط مفتاح آخر.\n\n" +
                "من لوحة المفاتيح: حدد ملفاً في المستكشف واضغط ‪Ctrl+Alt+§HK§‬، ثم استخدم الأسهم للاختيار، وEnter للتنفيذ، وTab للتبديل بين العجلات، وEsc للإلغاء.\n\n" +
                "تُحفظ النتيجة دائماً كنسخة جديدة بجانب الملف الأصلي، وكل المعالجة تتم على جهازك دون رفع أي ملف.",
                "1. Start dragging any file (or several) from File Explorer or the desktop.\n" +
                "2. While dragging, tap a key:\n" +
                "      {0}  →  Convert wheel (PNG, JPG, MP3, PDF…)\n" +
                "      {1}  →  Tools wheel (compress, rotate, trim…)\n" +
                "      {2}  →  Actions wheel (copy, move, float…)\n" +
                "3. Drop the file on the option you want. Drop in the middle to cancel.\n\n" +
                "Tap another key while dragging to switch wheels.\n\n" +
                "Keyboard: select a file in File Explorer and press Ctrl+Alt+§HK§, then use the arrow keys to choose, Enter to apply, Tab to switch wheels and Esc to cancel.\n\n" +
                "Results are always saved as a new copy next to the original, and everything is processed on your PC — nothing is uploaded." } },
            { "help_close", new[] { "فهمت", "Got it" } },
        };

        /// Interface languages: code and name in that language. Arabic and English are built in (the table above);
        /// every other language is a lang\<code>.txt file embedded in the exe, one "key = text" per line. A missing
        /// key falls back to English, so a partial translation still works.
        public static readonly string[][] Languages =
        {
            new[] { "ar", "العربية" }, new[] { "en", "English" }, new[] { "fr", "Français" }, new[] { "es", "Español" },
            new[] { "de", "Deutsch" }, new[] { "pt", "Português (Brasil)" }, new[] { "it", "Italiano" }, new[] { "nl", "Nederlands" },
            new[] { "tr", "Türkçe" }, new[] { "fa", "فارسی" }, new[] { "ur", "اردو" }, new[] { "hi", "हिन्दी" },
            new[] { "bn", "বাংলা" }, new[] { "id", "Bahasa Indonesia" }, new[] { "ms", "Bahasa Melayu" }, new[] { "ru", "Русский" },
            new[] { "uk", "Українська" }, new[] { "pl", "Polski" }, new[] { "cs", "Čeština" }, new[] { "ro", "Română" },
            new[] { "el", "Ελληνικά" }, new[] { "he", "עברית" }, new[] { "zh", "简体中文" }, new[] { "zh-TW", "繁體中文" },
            new[] { "ja", "日本語" }, new[] { "ko", "한국어" }, new[] { "vi", "Tiếng Việt" }, new[] { "th", "ไทย" },
            new[] { "sv", "Svenska" }, new[] { "sw", "Kiswahili" },
        };

        static readonly HashSet<string> rtlLanguages = new HashSet<string> { "ar", "fa", "ur", "he" };

        public static bool IsLanguage(string code) { return code != null && Languages.Any(l => l[0] == code); }

        /// The interface is in Arabic (Arabic-only touches such as Arabic file-name words).
        public static bool Ar { get { return Settings.Lang == "ar"; } }

        /// The interface reads right to left (Arabic, Persian, Urdu, Hebrew): mirrors layouts and menus.
        public static bool Rtl { get { return rtlLanguages.Contains(Settings.Lang); } }

        static string loadedCode;
        static Dictionary<string, string> loaded;

        /// Texts of an embedded language file ("lang.<code>.txt"); empty when there is none.
        public static Dictionary<string, string> LoadLanguage(string code)
        {
            var d = new Dictionary<string, string>();
            using (var st = typeof(S).Assembly.GetManifestResourceStream("lang." + code + ".txt"))
            {
                if (st == null) return d;
                using (var r = new System.IO.StreamReader(st, System.Text.Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
                    }
                }
            }
            return d;
        }

        static Dictionary<string, string> Extra()
        {
            string code = Settings.Lang;
            if (code == "ar" || code == "en") return null;
            if (loadedCode != code) { loaded = LoadLanguage(code); loadedCode = code; }
            return loaded;
        }

        /// A text in the current language; also used for words inside file names ("sfx.<key>").
        public static string TryT(string key)
        {
            var extra = Extra();
            string text;
            if (extra != null && extra.TryGetValue(key, out text)) return text;
            string[] v;
            if (map.TryGetValue(key, out v)) return Ar ? v[0] : v[1];
            return null;
        }

        public static string T(string key)
        {
            return (TryT(key) ?? key).Replace("§HK§", Settings.HotkeyLetter);
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }
    }
}
