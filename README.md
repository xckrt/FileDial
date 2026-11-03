English · [Русский](README.ru.md)

# FileDial — prototype

Select a video, image, audio file, archive, PDF or document in File Explorer or on the desktop and press
**Ctrl+Alt+K**: a radial menu (dial) with actions for that file type appears at the cursor. Everything works offline.

| Type | Extensions | Sectors (inactive ones in italics) |
|------|------------|------------------------------------|
| Video | mp4, mov, mkv, webm | Compress to 25 MB, Trim, Make GIF, Extract audio, Frame to image, Subtitles, Remove sound, **Format ›** MP4 · MKV · WebM · MOV |
| Images | jpg, jpeg, png, webp, bmp, gif, tif, tiff | **Format ›** JPG · PNG · WebP · BMP · TIFF · GIF, Resize to 1920 px, Rotate right, Rotate left, Mirror, Compress to 1 MB, *Remove background*, To PDF |
| Audio | mp3, wav, flac, m4a, aac, ogg, opus, wma | **Format ›** MP3 · M4A · WAV · FLAC · OGG · Opus, Normalize volume, Remove noise, Speed up ×1.5, Slow down ×0.75, To mono, Trim, To text |

| Archives | zip, 7z, rar, tar, tar.gz/tgz, tar.bz2, tar.xz | Extract here, Extract to folder, Extract to…, Test archive, **Format ›** ZIP · 7z, Contents, Password-protect, Split into parts | |
| PDF | pdf | Split into pages, Extract pages, Rotate right, Rotate left, Pages to images, Recognize text, Password-protect, *Merge* (for several PDFs) | |
| Documents | docx, doc, rtf, odt · xlsx, xls, ods · pptx, ppt, odp | To PDF, **Format ›** (text documents: DOCX · ODT · RTF; spreadsheets: XLSX · ODS · CSV; presentations: PPTX · ODP), To text, Extract images, Compress images, Remove personal data, Password-protect, *Combine into PDF* (for several documents) | |
| File parts | name.zip.001, .002, … | compact dial: **Enter or click — join parts** | |
| Everything else | txt, psd, folders, files of different types… | compact dial: **Enter or click — compress to ZIP** | |

**Format ›** is a group sector (marked with a triangle at its outer edge): hovering outside the dial opens a
fan of formats. The file's own format is inactive in the fan ("Already PNG").
The only sector still inactive is "Remove background" (it needs an image neural network). "Subtitles" and
"To text" work once a speech recognition model is available — see [Speech recognition](#speech-recognition).

### Multiple files

Select several files **of the same type** (for example, 5 images): the dial shows "5 images" and the total size,
and the chosen action runs on each file in turn. The window shows overall progress
("2 of 5 · photo.png · Processing") and a summary at the end: "Done: 4 of 5. Skipped: 1…". An error on one file
does not stop the others; "Cancel" stops everything. Each file gets its own history entry.

- Files already in the target format are skipped (not copied). A format in the fan is inactive only if **all**
  files are already in it.
- "Compress to…" is inactive only if **all** files are already under the target; smaller ones are skipped.
- "Trim" works on one file only.
- Images: "Combine into PDF" makes one document, one image per page, in selection order.
- Mixed types, folders or more than 200 files show "Not supported yet" with the reason.

### PDF from images

"To PDF" (one image) and "Combine into PDF" (several) write the PDF themselves, without third-party libraries: each
image is converted to JPEG (transparency on white), and the page size is the image size at 96 dpi. Name: for one
image, `name.pdf`; for several, named after the folder (`Vacation.pdf`).

## Requirements

- Windows 10/11 x64.
- .NET 10 SDK — only for building from source; the `FileDial.exe` built by `publish.ps1` does not need .NET.
- ffmpeg — the app installs it itself in one click (see [Installing ffmpeg](#installing-ffmpeg)). Not needed for
  PDFs and documents (except "Compress images" in a document).
- For documents, "To PDF", "Format", "Combine into PDF" and text from DOC/RTF/PPT need **Microsoft Office** or
  **LibreOffice** (either one; if both are present, Office is used). Without them these sectors are inactive
  with a reason; everything else works. "Password-protect" for a document works only through Office.
- "Recognize text" in PDFs uses the text recognition built into Windows: it needs a language with the optical
  character recognition component (usually installed together with the display language).

## Building and running

```powershell
dotnet build
dotnet test
dotnet run --project src/FileDial.App
```

The built app: `src/FileDial.App/bin/Debug/net10.0-windows/FileDial.exe`.
Administrator rights are neither needed nor requested (`asInvoker`). There are no windows, only a tray icon;
to quit, right-click the icon → "Exit". A second instance does not start.

If the hotkey is already taken by another program, FileDial says so and immediately opens the settings so you
can pick another shortcut (without a hotkey the app stays in the tray and does not quit).

## Settings

Tray → "Settings…" or double-click the icon. Changes apply on "Save"; if they cannot be applied (for example, the
shortcut is taken), the window stays open and shows the reason.

| Setting | Default | What it does |
|---------|---------|--------------|
| Language | Match Windows | Interface language: "Match Windows", Русский or English (see [Languages](#languages)). The chosen language is shown immediately; without "Save" the previous one comes back |
| Hotkey | Ctrl+Alt+K | Click the field and press a shortcut. It needs Ctrl, Alt or Win plus a letter, digit or F1–F24. The new shortcut is registered right away; if it is taken, the old one stays |
| Start with Windows | off | An entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — no administrator rights, current user only |
| Appearance | Graphite | Theme of the dial and windows: "Graphite" (dark, lime accent), "Brass" (warm dark, brass accent), "Paper" (light, blue accent), "Match Windows" — Graphite or Paper depending on the Windows app theme. The chosen theme is shown immediately; without "Save" the previous one comes back |
| Animations | on | Turns off all FileDial animations (animations turned off in Windows are respected too) |
| Keep action history | on | Records every action in the history (see below). Turning it off does not delete existing entries |
| Where to save results | next to the original file | Or in a chosen folder. Temporary files are also created there, so the source folder stays clean |
| Open the result folder when done | off | Shows the result in File Explorer right away |
| Video compression target | 25 MB | 8 / 10 / 25 / 50 / 100 MB. Changes the sector label, the "already smaller" check and the name (`name_10mb.mp4`) |
| Image compression target | 1 MB | 500 KB / 1 / 2 / 5 MB, likewise |
| ffmpeg | auto | Folder with `ffmpeg.exe` and `ffprobe.exe`; checked first. The field shows which ffmpeg will be used |

## Languages

The interface is available in English and Russian. By default it follows the Windows display language: Russian
on Russian, Belarusian and Kazakh Windows, English otherwise. You can change it in Settings → "Language" or in the
installer. Output file names follow the interface language too (`name_25mb.mp4` in English,
`имя_25мб.mp4` in Russian).

All strings live in `src/FileDial.Core/Localization`: the abstract `Texts` class with one subclass per language
(`EnTexts`, `RuTexts`). Adding a language means adding one more class derived from `Texts` and registering its code
in `Lang`; a string missing from a language is a build error, not an empty label in a window.

## Speech recognition

"Subtitles" (video → `name.srt`) and "To text" (audio → `name.txt`) use the `whisper` filter built into
ffmpeg 8 and later (whisper.cpp). Recognition runs offline; it needs a model, which you do not have to find and
download by hand:

- **From the dial.** Without a model, "Subtitles" and "To text" are inactive with the hint "Needs a speech
  recognition model — Enter: download". Enter or a click opens the settings directly at the right section.
- **In the settings** → "Speech recognition": the buttons "Download: base (148 MB)" and
  "Download: accurate (487.6 MB)". Progress and "Cancel" are shown right there. The file is downloaded from the
  official whisper.cpp repository on Hugging Face into a temporary `.part` file, checked by size and SHA-256, and
  only then moved to `%LOCALAPPDATA%\FileDial\models`. A damaged or incomplete file is deleted. This is needed once.
- **Bundled.** Put `ggml-*.bin` into a `models` folder next to `FileDial.exe` and the model is found
  automatically, with no internet needed at all (this is how a fully offline distribution is built).

Model selection: the one set explicitly in the settings → from `models` next to the app → the downloaded one; the
accurate (small) model is preferred over the base one. Recognition language: "Auto", "Русский" (Russian) or
"English" (for short recordings it is better to set the language). If ffmpeg is built without whisper, you get
"Your ffmpeg is built without whisper" (the gyan.dev "full" builds of ffmpeg 8+ include it).
If the file contains no speech, no result is created ("No speech found"). SRT numbering starts at 1.

## History

Tray → "History…". Each action from the dial is an entry: what was done, to which file, when, and how it ended
("Done", "Nothing to do", "Cancelled", "Failed" — with the reason). Entries are grouped by day
("Today", "Yesterday", a date). The "Show result" and "Show original" buttons open File Explorer with the file
selected; if the file has since been deleted or moved, the button is not shown and the result reads
"The result was deleted or moved". The window updates itself while open. "Clear" works on the second click (the
first asks "Really clear?").

History is stored only on this computer: `%APPDATA%\FileDial\history.json`, the last 200 entries.
A corrupted file does not get in the way — history simply starts over.

Settings are stored in `%APPDATA%\FileDial\settings.json` (readable JSON, non-ASCII characters such as Cyrillic
are kept as is). The file is written atomically; if it is corrupted or contains odd values, FileDial uses the
defaults or the nearest valid values and does not crash.

## Building for distribution

```powershell
.\publish.ps1            # tests → publish → zip and installer
.\publish.ps1 -SkipTests # without tests
```

Output:

- `dist\FileDial-Setup-<version>.exe` (~190 MB) — the installer, see [Installation](#installation);
- `dist\FileDial-<version>-win-x64.zip` (~73 MB) — the portable version: exe and README, runs from anywhere;
- `dist\FileDial\FileDial.exe` — the exe itself (.NET included, nothing to install).

The script prints the SHA-256 of the archive and the installer.
The version comes from `<Version>` in `src\FileDial.App\FileDial.App.csproj`; the exe has its own icon and file
information.

- The exe is deliberately not compressed: a compressed single-file app unpacks its assemblies into memory on every
  start, so the tray app takes ~240 MB instead of ~110 MB, and compression barely shrinks the zip.
- ffmpeg and the speech recognition model are not included in the distribution — the app installs them itself in
  one click. For a fully offline distribution, put `ffmpeg.exe`, `ffprobe.exe` and `models\ggml-base.bin` (or
  `ggml-small.bin`) into `dist\FileDial` and repackage: `.\publish.ps1 -KeepExtras`.

## Installation

Run `FileDial-Setup-<version>.exe`. No administrator rights are needed: everything is installed for you only.

- On the left is the same dial as in the app: it fills clockwise during installation and fades out during removal.
- **Folder** — `%LOCALAPPDATA%\Programs\FileDial` by default; "Change…" picks any folder of your own. If you pick a
  shared folder (`D:\Apps`), the app goes into `D:\Apps\FileDial`. The installer checks write permission and free
  space right away: Program Files requires an administrator, and you are told so before installation.
- **Appearance** — Graphite, Brass, Paper or "Match Windows"; the installer recolors itself immediately. The
  interface language can be chosen here as well.
- **Options** — start with Windows, Start menu shortcut, desktop shortcut.
- At the end — how to use it (3 steps) and a "Start FileDial" checkbox.
- **Update** — the installer of the new version: it recognizes the installed copy, offers "Update", keeps the same
  folder and does not touch settings or history. A running FileDial is closed cleanly (running jobs are cancelled,
  temporary files are removed) and, if the checkbox is set, started again after the update.
- **Uninstall** — "Settings → Apps → FileDial → Uninstall". This removes the app, shortcuts, autostart and the
  entry in Installed apps; from the folder only FileDial's own files (FileDial.exe, README.md), and the folder
  itself if it is left empty. With the checkbox, settings, history, downloaded ffmpeg and models are removed too
  (their size is shown). Without it everything is kept for a future install.

How it works: the installer is the same `FileDial.exe`. The word "Setup" in the file name (or the `--install` switch)
opens the setup wizard, `--uninstall` opens removal. This way the distribution does not carry a second copy of .NET.

For administrators — without windows:

```powershell
.\FileDial-Setup-0.2.0.exe --install --quiet [--dir "D:\Apps"] [--no-autostart]
"%LOCALAPPDATA%\Programs\FileDial\FileDial.exe" --uninstall --quiet [--remove-data]
```

Exit code: 0 — done, 1 — error (details in `%LOCALAPPDATA%\FileDial\errors.log`), 2 — the folder is not suitable.

The installer is not digitally signed, so Windows SmartScreen may show "Unknown publisher" on first run:
"More info → Run anyway".

## Installing ffmpeg

You do not need to install ffmpeg by hand: if it is missing, FileDial shows a notification at the tray icon on
startup ("FileDial: ffmpeg needed" — click to install), and the window of any action shows the
"Install ffmpeg with one click" button. Both lead to Settings → ffmpeg → "Install ffmpeg (about 170 MB)", with
progress and "Cancel" right there.

- The **gyan.dev "full"** build is installed (it includes whisper for subtitles): the current version is read from
  `release-version`, the checksum from the `.sha256` file next to the archive. The `.7z` archive is unpacked by
  the `tar.exe` built into Windows (libarchive) — no third-party programs are needed.
- If there is no `tar.exe` (older Windows 10) or gyan.dev is unavailable, the fallback is the BtbN build from GitHub
  (`.zip`, SHA-256 from the GitHub API). It has no whisper: everything works except subtitles and "To text".
- The archive is downloaded to a temporary file and checked by SHA-256; only `ffmpeg.exe` and `ffprobe.exe` are
  taken from it, into `%LOCALAPPDATA%\FileDial\ffmpeg` (no administrator rights). Old files are replaced only after
  a successful check and unpacking; temporary files are deleted.
- If the ffmpeg found has no whisper, the settings offer "Install ffmpeg with speech recognition";
  if ffmpeg was installed by the app, "Update ffmpeg".

Search order: the folder from the settings → next to `FileDial.exe` → `%LOCALAPPDATA%\FileDial\ffmpeg` → `PATH`.
For an offline distribution, put `ffmpeg.exe` and `ffprobe.exe` next to `FileDial.exe`.

## How to use

1. Select one file of a supported type (see the table above) in File Explorer or on the desktop.
2. Press **Ctrl+Alt+K** — the dial opens at the cursor.
3. Pick a sector with the mouse or the **1–8** keys (1 is the top sector, then clockwise); the arrow keys move
   around the dial. A click or **Enter** runs the action.
   On the "Format" sector a fan of formats opens outside the dial: hover over a format and click it.
   With the keyboard: select "Format" by its digit and press **Enter** to open the fan; then **1–N** or the arrows,
   **Enter** to run, **Esc** or **Backspace** to go back to the main dial.
4. **Esc**, a click in the center or outside the dial, a right-click, or switching to another window closes the dial
   without changing anything.

For other file types, folders and multiple selected files, a small dial without sectors is shown with
"Not supported yet". Long names are shortened in the middle, keeping the extension visible.
The center of the dial shows the name and size, plus the duration (video, audio) or resolution (images) if ffmpeg
is found. Hovering over an inactive sector explains why it is unavailable.
If the active window is not File Explorer, nothing is selected or the file is unavailable, a short notification
appears at the cursor.

### Animations

The dial is designed like a rotary knob: a scale of ticks runs around the rim (long ones opposite the sectors), and
each sector has an icon, a label and its key number in the corner. On opening, the knob turns another 18° and the
sectors light up clockwise (about 300 ms). The pointer notch on the rim moves to the selected sector by the
shortest path and settles with a slight bounce, like a detent. The selected sector smoothly changes color and moves
outward a little. Formats in the fan unfold from under the "Format" sector one after another. After a choice, the
sector moves further out, the dial turns another 8° and fades out; Esc closes it the same way, only faster.
Notifications fade in and out, the progress bar moves smoothly, and for short operations without percentages a
segment runs along it.
If animation effects are turned off in Windows (Settings → Accessibility → Visual effects), everything is applied
instantly.

### Video: other actions

- "Make GIF" — 12 fps, long side up to 480 px, its own palette. Only for clips up to 30 s
  (for longer ones the sector is inactive with a reason). Name: `name.gif`.
- "Extract audio" — if the audio is already AAC/MP3/Opus/Vorbis/FLAC, it is copied without re-encoding (`.m4a`,
  `.mp3`, `.opus`, `.ogg`, `.flac`); otherwise it is encoded to AAC 192 kbit/s (`.m4a`). Inactive for videos without
  sound.
- "Frame to image" — a lossless PNG, the frame at one third of the duration (the first frame is often black).
  Name: `name_frame.png`.
- "Remove sound" — the video is copied without re-encoding into the same container. Name: `name_muted.ext`.
  Inactive if there is no sound.
- "Format" — to MP4. If the video is already H.264 and AAC, it is only remuxed (seconds, lossless);
  otherwise H.264 CRF 20 + AAC 192 kbit/s. Inactive for `.mp4`.

### PDF

The original PDF is not changed. PDF parsing and assembly use the **PDFsharp 6.2.4** library (MIT license; versions
from 6.5 on are released under AGPL, so the version is pinned). Page rendering and recognition use the PDF engine
built into Windows.

- **Split into pages** — a `name (pages)` folder with files `name — page 01.pdf`, … Inactive for a single-page PDF.
  The page count is shown in the center of the dial.
- **Extract pages** — a window where you write the pages: `1-3, 5, 8-` (to the end); the order is kept as written.
  Errors ("The document has 12 pages", "the start is after the end") are shown immediately. Result:
  `name_pages_1-3,5.pdf`.
- **Rotate right / left** — all pages by 90°, `name_rotated.pdf`; bookmarks and properties are kept.
- **Pages to images** — PNG at 150 dpi on a white background in a `name (images)` folder.
- **Recognize text** — each page is recognized by the built-in Windows OCR (300 dpi) in the language from the
  Windows settings; works for scans too. Result: `name.txt` (UTF-8), with pages separated by headings.
- **Password-protect** — enter the password twice; `name_protected.pdf` with AES-256 encryption.
- **Merge** — select several PDFs: one file in selection order, named after the folder (`Reports_merged.pdf`).
- Password-protected PDFs: a window asks for the password and repeats the action (as with archives). A copy made from
  a protected PDF is rebuilt from its pages and has no password — bookmarks are not carried over in that case.

### Documents

The original document is not changed. The newer formats (DOCX, XLSX, PPTX, ODT, ODS, ODP) are zip files with XML:
FileDial reads and writes text, images and author information itself. Everything else is done by the installed Office
(invisibly, without macros, with the document opened read-only) or, if there is no Office, by LibreOffice.

- **To PDF** and **Format ›** — `name.pdf`, `name.odt` and so on next to the original. DOC → DOCX is here as well.
  CSV gets the first sheet.
- **To text** — `name.txt` (UTF-8): paragraphs for text documents, slide by slide for presentations. For
  spreadsheets, use "Format → CSV".
- **Extract images** — as they are stored inside, without recompression, into a `name (images)` folder.
- **Compress images** — photos larger than 2000 px are downsized and JPEGs recompressed; image names and formats do
  not change, and the document opens as before: `name_compressed.docx`. If there is no gain, it says so.
- **Remove personal data** — `name_clean.docx`: no author or "last modified by", company and manager, or print
  date; in comments and tracked changes the name is replaced with "Author", and accounts (email) and a template path
  like `C:\Users\name\…` are removed. Text, comments and tracked changes stay.
- **Password-protect** — a copy `name_protected.docx` with a password to open (via Office).
- **Combine into PDF** — select several documents (they can be mixed: a letter, a spreadsheet, a presentation):
  one PDF in selection order.
- An Office instance that hangs (for example, waiting for an answer in a hidden window) is terminated after
  5 minutes; cancelling also terminates only the Office process that FileDial started. A Word or PowerPoint you have
  open is not touched.

### Archives

No third-party programs are needed: zip is read and written by .NET, other formats by the `tar.exe` built into
Windows (libarchive; available since Windows 10 1803). The original archive is neither changed nor deleted.

- **Extract here** is "smart": if the archive contains a single top-level folder (or a single file), it appears
  right next to the archive, without "Archive\Archive\…"; otherwise a folder named after the archive is created.
  **Extract to folder** always extracts into a folder named after the archive. **Extract to…** extracts into a
  chosen folder. Existing files are not overwritten (`name_2`).
- Extraction goes into a temporary `.filedial-…` folder: after an error, a password prompt or a cancel, nothing
  "half-extracted" is left behind. Entries like `../` that point outside the folder are rejected.
- zip files created by older File Explorer versions (names in the system OEM code page, e.g. cp866 on Russian
  Windows, without the UTF-8 flag) are extracted with correct non-Latin names.
- **Test archive** reads the whole content and verifies checksums without writing anything to disk.
- **Format › ZIP / 7z** — repacking (for example, rar → zip for people without an archiver). 7z compresses better
  than zip.
- **Compress to ZIP** — for documents, folders and selections of files of different types: Enter or a click in the
  compact dial. One folder or file → `name.zip`, several → named after their folder. Names are stored in UTF-8,
  empty folders are kept.
- **Contents** — a window with the file list, without extracting: a folder tree (as in File Explorer: folders, then
  files), sizes, a total like "N files · X MB unpacked" and an "Extract here" button. The list is virtualized, so
  an archive with tens of thousands of files opens without delay.
- The center of the archive dial shows the size and the number of files.
- **Password-protect** — the password is entered twice (at least 6 characters), and `name_protected.zip` appears
  next to the archive with **AES-256** encryption (WinZip AE-2 format: opened by 7-Zip, WinRAR and Windows 11
  File Explorer). .NET cannot write such archives, so FileDial implements the format from the specification
  (PBKDF2-HMAC-SHA1, AES-CTR, HMAC authentication code), and tests check it with an independent reader, libarchive.
  File names in a ZIP are visible without the password — that is how the format works. Files larger than 4 GB do not
  fit into a password-protected archive yet.
- **Split into parts** — choose the part size: 10 MB, 25 MB (email, messengers), 100 MB, 700 MB, 1 GB,
  4 GB (FAT32 flash drives); the window shows the number of parts right away, and sizes not smaller than the file are
  inactive. The files `name.zip.001`, `.002`, … appear next to it — a plain byte split that 7-Zip and WinRAR
  understand (open .001 and they join the rest themselves). Parts are written to temporary files and renamed only at
  the end, so an error or a cancel leaves no "half a set"; taken names are not overwritten (`name_2.zip.001`).
- **Join parts** — select any part (or all of them): the compact dial shows "name · N parts · size", and Enter joins
  them. FileDial checks that the parts are consecutive and of the same size (the last one not larger) and assembles
  the file next to them; if the original is still there, the result is `name_2.zip`. The parts are not deleted.
- **Password-protected archives** (ZIP: ZipCrypto and AES): when extracting, testing or repacking, a window asks for
  the password; a wrong one gives "Wrong password. Try again." and another attempt; nothing "half-extracted" is left
  behind. The built-in `tar.exe` cannot open encrypted 7z and RAR archives — FileDial says so. The password is never
  saved; it is passed to `tar.exe` as an argument (a limitation of the tool) and therefore must be representable in
  the ANSI code page.
- A limitation of Windows `tar.exe`: it receives paths in the system ANSI code page (cp1251 on Russian Windows).
  Names **inside** the archive and the destination folder can be anything (`Ünïcödé`, `日本`) — FileDial passes the
  folder as the working directory and an archive path with such characters as a short 8.3 name. If short names are
  disabled on the drive, you get a clear error. A 7z is not created from a file or folder whose **name** contains such
  characters — FileDial suggests ZIP instead (written by .NET, no restrictions).

### Trim

After choosing "Trim", a window opens with the file's duration and the "Start" and "End" fields. Time can be entered
as `83`, `83.5`, `1:23`, `1:23.5` or `1:02:03` (a decimal comma is accepted too); errors and the segment length are
shown immediately, and the "Trim" button is enabled only for a valid segment. For video there is a "Frame-accurate"
checkbox: when on, the video is re-encoded (H.264/AAC, for WebM VP9/Opus) and cut exactly; when off, it is copied
without re-encoding in seconds, but the cut points shift to the nearest keyframes. Audio is always cut exactly.
Name: `name_trimmed.ext`.

### Video formats

- MP4: H.264 + AAC are copied, otherwise H.264 CRF 20 + AAC 192 kbit/s.
- MOV: H.264/HEVC + AAC are copied, otherwise H.264 + AAC.
- WebM: VP8/VP9/AV1 + Opus/Vorbis are copied, otherwise VP9 (CRF 32) + Opus 128 kbit/s — noticeably slower.
- MKV: always a lossless copy (MKV accepts almost any codec).

### Images

- The result is a new file next to the original; the original is not changed, and metadata (EXIF) is not carried
  over to the result.
- Formats: JPG, PNG, WebP, BMP, TIFF (LZW), GIF (own palette). "Rotate left" gives `name_rotated_left`,
  "Mirror" flips horizontally, `name_mirrored`.
- "To JPG" and "To BMP": transparent areas are filled with white (JPG has no transparency), quality `-q:v 2`, 4:4:4.
- "To WebP": quality 90, transparency is kept. "To PNG" is lossless.
- "Resize to 1920 px": long side 1920, aspect ratio kept (lanczos). Inactive if the image is already 1920 px or
  smaller. Name: `name_1920px.ext`.
- "Rotate right": 90° clockwise. Name: `name_rotated.ext`.
- Rotation and resizing keep the JPG/PNG/WebP format; BMP, GIF and TIFF are saved as PNG.
- For animated GIF/WebP the first frame is used.
- "Compress to 1 MB" — a JPG with the best quality that fits into 1,000,000 bytes (binary search over the quality
  scale). If even the lowest quality does not fit, the image is downsized in 25% steps.
  Inactive for files under 1 MB. Name: `name_1mb.jpg`.
- Changing the format keeps the name (`photo.png` → `photo.jpg`); if the name is taken, `photo_2.jpg`.
  Converting to the file's own format is inactive ("Already PNG").

### Audio

- "To MP3" — LAME VBR ~190 kbit/s; "To M4A" — AAC 192 kbit/s; "To WAV" — 16-bit PCM.
- "Normalize volume" — EBU R128 (`loudnorm`, −16 LUFS); the original format and sample rate are kept.
  Name: `name_normalized.ext`.
- "Remove noise" — `afftdn` noise reduction. Name: `name_denoised.ext`.
- "Speed up ×1.5" and "Slow down ×0.75" — without changing the pitch (`atempo`). Names: `name_x1.5.ext`,
  `name_x0.75.ext`.
- "To mono" — downmixes the channels into one, `name_mono.ext`. Inactive if the audio is already mono.
- Formats: MP3, M4A (AAC), WAV, FLAC, OGG (Vorbis), Opus.
- The first audio track is used; cover art is not carried over. Progress is shown in percent of the duration.

### Compress to 25 MB

- The target is 25,000,000 bytes. The duration comes from `ffprobe`.
- Two-pass H.264 encoding (`libx264`, preset medium) + AAC 128 kbit/s.
  Total bitrate = 25,000,000 × 8 × 0.97 / duration (3% headroom), video = total − 128.
- If less than 300 kbit/s remains for the video, the long side of the frame is reduced to 854 / 640 / 426 pixels
  (depending on the bitrate).
- The "Compress to 25 MB" sector is not offered if compression is unnecessary or impossible. The reason is shown in
  the center of the dial:
  - the file is already 25 MB or smaller — "The file is already under 25 MB";
  - less than 50 kbit/s would remain for the picture — "Videos over 18 min do not fit in 25 MB"
    (without audio the limit is about 64 minutes);
  - the file has no video track.

  The duration is checked with ffprobe when the dial opens (~50 ms, 2 s timeout). If ffmpeg is not found or ffprobe
  does not respond, the sector stays active and the compression window explains the problem.
- Before encoding, FileDial checks that the source folder is writable and the disk has at least 30 MB free
  (result + 20%). Otherwise you get a clear error right away, not a failure after several minutes of work.
- Multichannel audio (5.1, 7.1) is downmixed to stereo: 128 kbit/s across six channels sounds bad.
- Starting compression of the same file again while the first run is in progress does not start a second encode;
  it brings up the window that is already open.
- If the file still comes out larger than the target after the second pass, the second pass is repeated once
  with the bitrate reduced by the overshoot ("Pass 2 of 2, refining").
- Result: `name_25mb.mp4` next to the original; if the name is taken, `name_25mb_2.mp4`, `name_25mb_3.mp4`…
  Existing files are not overwritten. The original file is only read.
- Temporary files (`.filedial-*.tmp` next to the original and pass logs in `%TEMP%\FileDial`) are deleted on
  success, on error and on cancel.
- The progress window shows the stage and percentage (from `ffmpeg -progress`) and has a "Cancel" button.
  When finished, it shows the final size and a "Show in folder" button.

## Limitations

- **Windows running as administrator.** FileDial runs without administrator rights. If a File Explorer window is
  running elevated, Windows (UIPI and integrity levels) does not let a regular process read its selection.
  FileDial detects this and shows the notification "The window runs as administrator, so its selection is
  unavailable". Running FileDial as administrator for this is neither needed nor recommended.
- Focus: the dial takes keyboard focus right after opening (Windows allows this for the process that received the
  hotkey). If the system still does not hand over focus, the dial closes after ~0.4 s so it does not stay hanging
  over other windows.
- The selection is read only from regular File Explorer windows (`CabinetWClass`) and the desktop.
  Open/Save dialogs of other programs are not supported.
- Virtual items (for example, inside a zip archive, "This PC", the Recycle Bin) are treated as unavailable.
- FileDial does not open password-protected documents (DOCX and so on): Office is given a deliberately wrong
  password so it does not hang on a hidden prompt, and FileDial reports that the document is password-protected.
- "Remove personal data" and "Extract images" work only for the newer formats (DOCX, XLSX, PPTX, OpenDocument).
- Unexpected errors are written to `%LOCALAPPDATA%\FileDial\errors.log` and do not crash the app.

## How it works

```
src/FileDial.Core         logic without UI or WinAPI (tested)
  Menu/                   actions by file type, selection parsing → dial or notification
  Geometry/               sector geometry, window placement within the monitor
  Compression/            bitrate calculation, output name, ffmpeg/ffprobe arguments and launch, progress
  Jobs/                   common action interface, image and audio conversions (ffmpeg arguments)
  Selection/              selection read result, file check
  Settings/               settings, their storage and normalization, key combination
  History/                action history: recording, storage, grouping by day
  Localization/           interface strings: Texts and one subclass per language
src/FileDial.App          WPF application
  Hotkey/                 RegisterHotKey in a message window (no global hooks)
  Selection/              reading the selection through shell COM on a separate STA thread
  UI/                     dial, notification, action window, animations, DPI-aware placement
  Tray/                   tray icon
  Settings/               autostart through the registry
tests/FileDial.Core.Tests xUnit tests
```

Getting the selection (without emulating Ctrl+C and without the clipboard):

- `GetForegroundWindow` → window class.
- **File Explorer** (`CabinetWClass`): `Shell.Application.Windows()`, filtered by `HWND`. In Windows 11 all tabs
  of one window share one `HWND`, so for each candidate the tab window is obtained via
  `IServiceProvider::QueryService(SID_STopLevelBrowser) → IShellBrowser::GetWindow` and compared with the visible
  child `ShellTabWindowClass` — that is the active tab. If the active tab cannot be determined, the selection is not
  read (an error rather than data from another tab).
  Then `Document.SelectedItems()`.
- **Desktop** (`Progman` or `WorkerW` with `SHELLDLL_DefView`): `IShellWindows::FindWindowSW(SWC_DESKTOP)` →
  `IShellBrowser::QueryActiveShellView` → `IShellView::GetItemObject(SVGIO_BACKGROUND)` → `SelectedItems()`.
- All COM calls run on a separate STA thread with a 5 s timeout; COM objects are released and exceptions are
  caught.
- ffmpeg/ffprobe are started through `ProcessStartInfo.ArgumentList` — each argument separately, so paths with
  spaces and non-ASCII characters (such as Cyrillic) are passed without manual escaping.

## Tests

`dotnet test` runs:

- unit tests: bitrate and downscaling calculation, sector geometry and mouse hit testing, keys 1–8, window
  placement near edges and on a monitor with negative coordinates, DPI scaling, choice of actions by file type,
  selection parsing (one file / several / folder / nothing / unavailable), output name generation, ffmpeg lookup,
  parsing of `-progress` and ffprobe output, ffmpeg arguments;
- image and audio integration tests with real ffmpeg: transparent PNG → JPG with a white background (pixel check),
  downscaling to 1920×960, rotation, WebP, WAV → MP3/M4A without overwriting a taken name, volume normalization
  keeping the sample rate, a clear error for an "image" without a picture;
- compression integration tests with real ffmpeg (skipped if ffmpeg is not found): compression in a folder with
  Cyrillic characters and spaces, size not above the target, original unchanged, temporary files removed,
  no overwriting (`_2`), downscaling, "already under the target", cancel;
- PDFs and documents: the page syntax "1-3, 5, 8-", splitting, extraction in the given order, rotation, merging,
  passwords (no password — prompt, wrong one — retry), text and images from DOCX and ODT, "Remove personal data"
  (properties, comments, tracked changes, template path; mimetype first in ODT), inactive sectors with reasons.
  Conversion through Office and LibreOffice was checked manually (scenarios 69–73);
- the installer (on temporary folders and a separate registry key): the "…\FileDial" folder, permissions and space,
  copying with progress, the Installed apps entry, autostart, shortcuts, theme; an update does not reset settings;
  cancelling does not leave half an exe; uninstall removes only its own files, data only with the checkbox, and does
  not touch other programs' autostart entries.

## Manual test scenario

Prepare a video larger than 25 MB (for example, 1–3 minutes of 1080p). Start `FileDial.exe` **without**
administrator rights and make sure the tray icon appears and there are no other windows.

The scenarios assume the English interface; with the Russian interface, labels and file names are in Russian.

| # | Scenario | Steps | Expected result |
|---|----------|-------|-----------------|
| 1 | File on the desktop | Put a video on the desktop, select it, Ctrl+Alt+K | The dial at the cursor, name and size in the center; 8 sectors, only "Compress to 25 MB" is active |
| 2 | Compression | In the dial, click "Compress to 25 MB" (or 1, Enter) | Progress window, the percentage grows, the UI stays responsive; at the end the size is ≤ 25 MB and "Show in folder" opens the folder with `name_25mb.mp4` selected |
| 3 | Two File Explorer tabs | A window with two tabs in different folders, a different video selected in each; switch between the tabs and press Ctrl+Alt+K | Every time the dial shows the file from the **active** tab, never from the other one |
| 4 | Non-ASCII names and spaces | A video at `D:\Мои видео\Отпуск в горах\клип 1.mp4` (a Cyrillic path), compress it | Compression succeeds, `клип 1_25mb.mp4` appears next to it |
| 5 | Repeated compression | Compress the same file again | `…_25mb_2.mp4` appears, the first result is untouched |
| 6 | Multiple files | Select 3 images, Ctrl+Alt+K; then an image and an mp3 | A "3 images" dial with actions; for different types, "Files of different types · Not supported yet" |
| 7 | Another type | Select a `.jpg` or `.pdf` | A small dial without sectors with the name, size and "Not supported yet" |
| 8 | Nothing selected | Clear the selection in File Explorer, Ctrl+Alt+K | The notification "Nothing is selected" at the cursor |
| 9 | Not File Explorer | Notepad or a browser is active, Ctrl+Alt+K | The notification "The active window is not File Explorer" |
| 10 | Esc and clicks | Open the dial: Esc; again — click in the center; again — click outside the dial; again — Alt+Tab | The dial closes, files do not change |
| 11 | Inactive sector | Hover over "Trim" or press 2, then Enter or click | The hint "Not available in the prototype", nothing runs |
| 12 | Cancelling compression | Start compression, click "Cancel" (or close the window) | "Cancelled"; next to the original there is no `.filedial-*.tmp` or `_25mb.mp4`, `%TEMP%\FileDial` is empty |
| 13 | File under 25 MB | Select a video < 25 MB, Ctrl+Alt+K | The "Compress to 25 MB" sector is inactive, the center reads "The file is already under 25 MB"; click/Enter do nothing |
| 13a | Long video | Select a video longer than 18 minutes (larger than 25 MB), Ctrl+Alt+K | Size and duration in the center; "Compress to 25 MB" is inactive with the reason "Videos over 18 min do not fit in 25 MB" |
| 13c | Read-only folder | A video > 25 MB in a folder without write permission (for example, `C:\Program Files\…`), compress it | Immediately the message "No permission to write to the file's folder…", ffmpeg is not started |
| 13d | Repeated start | While compressing, select the same file again and choose "Compress" | The notification "This is already running for this file", the current window comes to the front |
| 13b | Long name | A video with a name of 50+ characters | The name in the center with "…" in the middle, the extension visible |
| 14 | No ffmpeg | Temporarily remove ffmpeg from PATH and from the app folder, restart FileDial | A tray notification; in the action window, "Install ffmpeg with one click" → settings → installation with progress; after it, actions work |
| 15 | Second monitor | Move File Explorer to a second monitor (preferably with a different scale, for example 100% and 150%), open the dial at the very edge | The dial is on the same monitor as the cursor, does not go past the edge, has the right size and is sharp |
| 16 | Screen edge | Open the dial near a screen corner and near the taskbar | The dial is entirely within the work area |
| 17 | Hotkey taken | Close FileDial, take Ctrl+Alt+K with the script below, start FileDial | The message "Ctrl+Alt+K is already used by another program."; FileDial does not stay in the tray without a hotkey. Starting it again while FileDial is running reports "FileDial is already running" |
| 18 | Administrator window | Start File Explorer as administrator (for example, from Task Manager with the "Create this task with administrative privileges" checkbox: `explorer.exe /separate`), select a file, Ctrl+Alt+K | A notification about administrator rights, no crash |
| 19 | Exit | Tray → "Exit" during compression | ffmpeg terminates, temporary files are deleted, the FileDial process exits |
| 20 | Image | Select a PNG with transparency larger than 1920 px, open the dial | Size and resolution in the center; "To PNG" is inactive ("Already PNG"). "To JPG" → `name.jpg` next to it with a white background; "Resize to 1920 px" → `name_1920px.png` |
| 21 | Small image | An image smaller than 1920 px | "Resize to 1920 px" is inactive, with the size in the reason |
| 22 | Audio | Select an MP3, open the dial | Duration in the center; "To MP3" is inactive. "To WAV" and "Normalize volume" create files next to it, progress in percent |
| 23 | Animations | Open the dial, move the mouse over the sectors, close with Esc | The dial turns and the sectors light up in turn, highlight with the sector moving out, a quick fade-out with a turn; nothing flickers or jerks |
| 24 | Animations off | Turn off "Animation effects" in Windows settings, repeat 23 | Everything appears and changes instantly |
| 26 | Video: GIF and frame | A short clip (< 30 s): "Make GIF", "Frame to image" | `name.gif` (480 px) and `name_frame.png` next to it; for a clip longer than 30 s, GIF is inactive |
| 27 | Video: sound | A clip with sound: "Extract audio", "Remove sound"; then a clip without sound | `name.m4a` and `name_muted.mp4`; for the silent clip both sectors are inactive |
| 28 | Video: format | A `.mkv` or an iPhone `.mov`: "Format" | `name.mp4` next to it, opens in the default player; for `.mp4` the sector is inactive |
| 29 | Image: 1 MB | A photo larger than 1 MB: "Compress to 1 MB" | `name_1mb.jpg` no larger than 1,000,000 bytes, quality looks decent |
| 30 | Audio: noise and speed | A voice recording: "Remove noise", "Speed up ×1.5" | Two files next to it; the sped-up one is 1.5 times shorter, the voice does not sound like a cartoon |
| 31 | Format fan | Image → hover over "Format", move the mouse outward to "WebP", click | The fan opens smoothly and does not close when crossing the gap; `name.webp` appears next to it |
| 32 | Fan from the keyboard | Press 1, Enter, arrows, Enter; then Esc | Format chosen without the mouse; Esc returns to the main dial, a second Esc closes it |
| 33 | Fan near the edge | Open the dial near the top edge of the screen on a video (the "Format" sector is at the top left) | The dial shifts so that the fan is entirely on screen |
| 34 | Video trimming | Clip → "Trim", enter 0:10 and 0:20, "Trim" | `name_trimmed.mp4` 10 s long; with "Frame-accurate" off it is faster, cut points ±1–2 s |
| 35 | Trim: input errors | Enter "abc", an end before the start, an end past the duration | Clear text under the fields, the button is disabled |
| 36 | Settings: hotkey | Tray → Settings, click the field, press Ctrl+Shift+D, "Save" | The dial opens with Ctrl+Shift+D, Ctrl+Alt+K no longer works; the tray tooltip is updated |
| 37 | Settings: hotkey taken | Take a shortcut with the script from scenario 17 and choose it in the settings | The message "already used", the window does not close, the previous shortcut keeps working |
| 38 | Settings: 10 MB target | Choose 10 MB, open the dial on a 15 MB video | The "Compress to 10 MB" sector is active; the result `name_10mb.mp4` is ≤ 10,000,000 bytes |
| 39 | Settings: results folder | Choose a folder, run any action | The result is in the chosen folder, nothing new next to the original |
| 40 | Settings: autostart | Turn it on, sign out of Windows and back in | FileDial is in the tray after sign-in; turning it off removes the entry from `Run` |
| 41 | Settings: corrupted file | Close FileDial, write garbage into `settings.json`, start it | Starts with default settings, no errors |
| 42 | History | Run a couple of actions (cancel one), open Tray → History | Entries under "Today" with statuses; "Show result" opens the file in File Explorer |
| 43 | History: deleted result | Delete a result, open the history | Instead of the button — "The result was deleted or moved" |
| 44 | History off | Turn it off in the settings, run an action | No new entry, old ones are kept, the window shows the hint "recording is off in settings" |
| 45 | Batch | 3 WAV and 1 MP3 → Format → MP3 | Progress "1 of 4 · …"; three MP3s next to them, the MP3 skipped; 4 entries in the history |
| 46 | PDF | 4 images in different formats (one PNG with transparency) → "Combine into PDF" | `Folder.pdf` with 4 pages in selection order, transparent areas on white |
| 47 | Batch: cancel | 5 videos → "Compress to 25 MB", "Cancel" on the second one | The first result stays, the rest are not started, no temporary files |
| 48 | Subtitles without a model | Delete the model from `%LOCALAPPDATA%\FileDial\models`, open the dial on a video, choose "Subtitles" | The hint "Needs a speech recognition model — Enter: download"; Enter opens the settings at the speech section; "Download: base" with progress, then "downloaded", the sectors are active |
| 49 | Subtitles | Language "English" (or the language of the recording); a video with speech → "Subtitles" | `name.srt` next to it with timecodes; opens in a player together with the video |
| 50 | To text | A voice recording → "To text" | `name.txt` next to it with the recognized text |
| 51 | Archive: extract | A zip with one folder inside → "Extract here"; a zip with several files → the same | In the first case the folder appears right next to the archive, in the second a folder named after the archive |
| 52 | Archive from an old File Explorer | A zip with Cyrillic names created with "Send to → Compressed (zipped) folder" on Russian Windows 7 | File names are correct Cyrillic, not garbled |
| 53 | RAR / 7z | rar or 7z → "Extract to…", choose a folder | Extracted into the chosen folder; "Test archive" reports it is OK |
| 54 | Compress to ZIP | Select a pdf and a folder, Ctrl+Alt+K, Enter | A compact dial "Enter or click — compress to ZIP"; an archive named after the folder next to them |
| 55 | Archive without ffmpeg | Temporarily remove ffmpeg, extract an archive | Archives work without ffmpeg |
| 56 | Archive: contents | 7z or zip → "Contents" | A tree with sizes and a total; "Extract here" from the window works |
| 57 | Archive in a "日本" folder | Put a 7z into a folder with a Japanese name → extract, test, contents | Everything works (through the short 8.3 name) |
| 58 | Password-protect | zip → "Password-protect", password twice | `name_protected.zip`; opens in 7-Zip/WinRAR/File Explorer only with the password |
| 59 | Password-protected archive | Extract `name_protected.zip` through the dial: wrong password, then the right one | "Wrong password. Try again.", then extraction; "Cancel" in the password window cancels the action |
| 60 | Split into parts | An archive of 300+ MB → "Split into parts" → 25 MB | `name.001…` next to it, as many parts as the window said; 7-Zip opens .001 |
| 61 | Join parts | Select .003 → Enter | The joined file next to them (`_2` if the original is in place); "Test archive" reports it is OK |
| 62 | Appearance | Settings → "Brass", then "Paper", then "Cancel" | The settings window recolors immediately; after "Cancel" the dial and windows are back in the previous theme. With "Save" the theme stays, also after a restart |
| 63 | Pointer | Open the dial, move the mouse from sector 8 to sector 1 | The notch takes the short way across the top (not around the whole dial) and settles with a slight bounce |
| 64 | PDF: split | A PDF with 5+ pages → "Split into pages" | A `name (pages)` folder with one file per page; the original is unchanged |
| 65 | PDF: extract | "Extract pages" → `3, 1-2` | The window immediately says "Result: 3 pages."; `name_pages_3,1-2.pdf` opens at page 3 |
| 66 | PDF: merge | Select 3 PDFs → "Merge" | One PDF named after the folder, pages in selection order |
| 67 | PDF: password | "Password-protect", then "Rotate right" on the result | Edge asks for the password; rotation asks for the password, a wrong one gives "Wrong password. Try again." and another attempt |
| 68 | PDF: OCR | A scan (a PDF made from a photo of a page) → "Recognize text" | `name.txt` with the page text, the summary names the language |
| 69 | DOCX → PDF | A Word document → "To PDF" | `name.pdf` in a few seconds; Word does not appear on screen and closes after the work |
| 70 | No Office | On a machine without Office and LibreOffice, open the dial on a DOCX | "To PDF", "Format", "Combine into PDF" are inactive: "Needs Microsoft Office or LibreOffice"; "To text" and "Remove personal data" work |
| 71 | Personal data | A DOCX with comments and tracked changes → "Remove personal data" | The copy's properties have no author or company, comments show "Author"; text and tracked changes are in place |
| 72 | Compress images | A DOCX with several phone photos → "Compress images" | `name_compressed.docx` is noticeably smaller, the images are in place and sharp |
| 73 | Combine into PDF | Select a DOCX, an XLSX and a PPTX → "Combine into PDF" | One PDF with all three in order |
| 74 | Installation | Run `FileDial-Setup-0.2.0.exe`, keep all defaults → "Install" | The dial fills, "Done" with three steps; FileDial is in Start and in Installed apps, the tray icon is there, Ctrl+Alt+K works |
| 75 | Custom folder | "Change…" → `C:\Program Files` | "No permission to write to this folder…", the button is disabled; on another drive — `D:\…\FileDial`, installation succeeds |
| 76 | Update | With FileDial running, run the installer again → "Reinstall" | The running copy closes itself and starts again after "Done"; hotkey and history are unchanged |
| 77 | Uninstall | Installed apps → FileDial → "Uninstall", leave the data checkbox unchecked | The uninstall window, the dial fades out; after a couple of seconds the app folder is gone, no shortcut or autostart; settings are kept |
| 78 | Theme in the installer | Choose "Paper" | The installer turns light at once; after installation FileDial also uses the "Paper" theme |
| 25 | File disappeared | Open the dial on an image, delete the file in another window, choose "To JPG" | The message "Original file not found…" without technical details |

Script for scenario 17 (holds Ctrl+Alt+K while the PowerShell window is open; Enter releases it):

```powershell
Add-Type -Name Hk -Namespace W -MemberDefinition '[DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint m, uint vk); [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);'
[W.Hk]::RegisterHotKey([IntPtr]::Zero, 1, 3, 0x4B); Read-Host 'Ctrl+Alt+K is taken, press Enter to release'; [W.Hk]::UnregisterHotKey([IntPtr]::Zero, 1)
```
