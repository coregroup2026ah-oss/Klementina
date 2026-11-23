; Installer for Klementina. Build with: installer\build-setup.cmd  (needs Inno Setup 6.7+)
#define AppVersion "1.0.0"
#define Root ".."
#ifndef FfmpegDir
  #define FfmpegDir Root + "\vendor\ffmpeg"
#endif

[Setup]
AppId={{8F3C2A51-6B1E-4C7D-9A2E-4B6D1F0C9E21}
AppName=كلمنتينة Klementina
AppVersion={#AppVersion}
AppVerName=كلمنتينة Klementina {#AppVersion}
AppPublisher=Klementina
DefaultDirName={localappdata}\Programs\Klementina
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#Root}\dist
OutputBaseFilename=Klementina-Setup-{#AppVersion}
SetupIconFile={#Root}\bin\Klementina.ico
UninstallDisplayIcon={app}\Klementina.exe
UninstallDisplayName=كلمنتينة Klementina
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ShowLanguageDialog=yes
LanguageDetectionMethod=none
CloseApplications=force
RestartApplications=no

; ---- look: dark theme over a warm background, with the tangerine artwork (art\ is drawn by Klementina.exe --installer-art) ----
WizardStyle=modern dark hidebevels includetitlebar
WizardBackColor=#1c120d
WizardBackImageFile=art\back_596.png,art\back_994.png,art\back_1272.png,art\back_1630.png
WizardImageFile=art\panel_202.png,art\panel_336.png,art\panel_430.png,art\panel_534.png
WizardSmallImageFile=art\small_58.png,art\small_97.png,art\small_124.png,art\small_159.png

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
arabic.WelcomeLabel1=أهلاً بك في كلمنتينة
arabic.WelcomeLabel2=حوّل صورك وفيديوهاتك ومستنداتك بلمسة، وأنت تسحبها.%n%nاسحب أي ملف واضغط Shift، فتظهر لك عجلة الصيغ تحت المؤشر. أفلته على الصيغة التي تريدها، وتُحفظ النسخة الجديدة بجانب الأصلية.%n%nكل شيء يتم على جهازك، ولا يُرفع أي ملف إلى الإنترنت.
arabic.FinishedHeadingLabel=كلمنتينة جاهزة
arabic.FinishedLabelNoIcons=تم تثبيت كلمنتينة. تجدها بجانب الساعة كبرتقالة صغيرة.%n%nجرّبها الآن: اسحب أي ملف واضغط Shift.
arabic.FinishedLabel=تم تثبيت كلمنتينة. تجدها بجانب الساعة كبرتقالة صغيرة.%n%nجرّبها الآن: اسحب أي ملف واضغط Shift.
english.WelcomeLabel1=Welcome to Klementina
english.WelcomeLabel2=Convert your photos, videos and documents in one move, right while you drag them.%n%nDrag any file and tap Shift: a wheel of formats appears under the cursor. Drop it on the one you want and the new copy is saved next to the original.%n%nEverything happens on your PC. Nothing is uploaded.
english.FinishedHeadingLabel=Klementina is ready
english.FinishedLabelNoIcons=Klementina is installed. Look for the little tangerine next to the clock.%n%nTry it now: drag any file and tap Shift.
english.FinishedLabel=Klementina is installed. Look for the little tangerine next to the clock.%n%nTry it now: drag any file and tap Shift.

[CustomMessages]
arabic.StartupTask=تشغيل كلمنتينة تلقائياً مع بدء Windows
english.StartupTask=Start Klementina automatically with Windows
arabic.LaunchApp=تشغيل كلمنتينة الآن
english.LaunchApp=Launch Klementina now

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#Root}\bin\Klementina.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#FfmpegDir}\ffmpeg.exe"; DestDir: "{app}\ffmpeg"; Flags: ignoreversion
Source: "{#FfmpegDir}\LICENSE.txt"; DestDir: "{app}\ffmpeg"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#Root}\vendor\qpdf\*"; DestDir: "{app}\qpdf"; Flags: ignoreversion
Source: "{#Root}\vendor\zxing\zxing.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Klementina"; Filename: "{app}\Klementina.exe"
Name: "{autodesktop}\Klementina"; Filename: "{app}\Klementina.exe"; Tasks: desktopicon

[Registry]
; same value the app's own "Start with Windows" setting uses, so the two stay in sync
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Klementina"; ValueData: """{app}\Klementina.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\Klementina.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM Klementina.exe"; Flags: runhidden; RunOnceId: "StopKlementina"
