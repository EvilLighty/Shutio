# CHANGELOG

Chronological record of implementation changes, fixes, and architectural
decisions for Shutio (the SalsaNOW capture tool). Most recent first.

Format: `## [date]` sections with **Added / Changed / Fixed / Verified / Docs**.

---

## 2026-11-09 — v1.1.1: recording-stop fix, 60 fps, Esc stop shortcut

A small patch release fixing a recording bug found in live use, plus two recording
conveniences. Self-test stays **13/13, exit 0**; no feature removals, no API breaks.

**Fixed**
- **Couldn't stop a recording with the mic on → "recording failed: encoding error."**
  The microphone is a live, never-ending input. After the video pipe (`pipe:0`) closed,
  ffmpeg kept waiting on the endless audio stream, so "Stop" hung until the timeout
  force-killed ffmpeg mid-flush and left a corrupt MP4. `RecordingController` now passes
  **`-shortest`**, so ffmpeg finalizes the moment the (finite) video input ends — even
  while the mic is still streaming. Verified with a faithful reproduction: before → 15 s
  hang + killed + corrupt file; after → clean exit in ~150 ms with a valid H.264 + AAC
  MP4 (0 corrupt frames on a full decode). No effect when recording video-only.
- **A friendly key name like `Esc` was rejected as "no valid shortcut."** The key
  resolver only matched the exact WPF enum name (`Escape`), so the `Esc` default for the
  new stop shortcut (and `Space`, `Tab`, `Delete`, …) failed to parse. `HotkeyGesture`
  now resolves common friendly names to the right key (both for typing and, for the
  stop row, for a bare key with no modifier).

**Added**
- **Stop‑recording shortcut (default `Esc`, changeable).** A new **Stop recording** row
  in Settings → Shortcuts, recorded exactly like the other shortcuts. Unlike them it may
  be a **single bare key** (`Esc`, `P`, `F9`, …) or a full combo (`Ctrl+Shift+P`). It is
  **armed only while a recording is in progress** (registered on start, released on stop),
  so a bare key never shadows normal typing in other apps. Routes through the same stop
  path as the indicator's stop button; the button always remains a fallback if the key
  can't be registered.
- **60 fps recording option.** 60 was already allowed by the engine (5–60) but wasn't
  offered in the UI. Now available in the recording setup and Settings alongside
  10 / 15 / 24 / 30, and accepted by `Settings.Sanitize` and the recording setup's
  allowed set.

**Verified**
- Self-test **13/13, exit 0** — including new assertions that `Esc` → `Key.Escape`,
  `Ctrl+N` → `Key.N`, and that a bare key is still rejected for an always‑on shortcut.
- Live recordings on the installed copy: **30 fps** and **60 fps** clips both produced
  2560×1440 H.264 + AAC (mic) MP4s that pass a full decode with **zero corrupt frames**,
  and both stopped cleanly with the mic on (the previously-failing scenario).

---

## 2026-11-09 — v1.1.0: reliable shortcuts, single-display recording, mic audio, modern cards

**Added**
- **Single-display recording.** The recording setup dialog now offers a **Single
  display** source alongside Full desktop / Window / Region, giving recording the
  same source options as screenshots. It captures the primary monitor's bounds
  (`CaptureService.MonitorAt(0,0).Bounds`).
- **Optional microphone audio in recordings.** A new **Record microphone audio**
  toggle (in the recording setup and in Settings) plus a **microphone picker**.
  When on, the mic is captured and the MP4 gets an **AAC 160 kb/s** audio track
  muxed next to the H.264 video. Off by default, so recordings stay video-only
  until you opt in.
  - New `AudioDevices.Enumerate()` asks the *same* ffmpeg binary to list its
    DirectShow audio devices (`-list_devices 1`), so the names we hand back are
    exactly the ones it accepts (no name-mismatch surprises).
  - `RecordingController` adds a second `-f dshow -i audio="<name>"` input and
    maps `0:v:0` + `1:a:0` with `-c:a aac -b:a 160k` (or `-an` when off).
  - New `AudioEnabled` + `Microphone` settings (trimmed/capped in
    `Settings.Sanitize`).
  - New themed `ModernComboBox` / `ModernComboBoxItem` styles (none existed
    before) drive the mic pickers.

**Changed**
- **Mode cards "pop out".** `ModeCardButton` corner radius 12 → 16 and a static
  soft `DropShadowEffect` so the cards lift off the surface. The earlier
  hover-shadow `TargetName="CardShadow"` setters were **removed**: a WPF `Effect`
  is not a named visual, so triggers referencing it crashed at startup with
  `Child with Name 'CardShadow' not found in VisualTree`.
- **Version 1.0.0 → 1.1.0** across the app (`SnippingTool.csproj`
  `Version`/`FileVersion`, `app.manifest` identity), the installer
  (`install.ps1`, `offline-installer` banner + registry `DisplayVersion`) and the
  updater (banner + registry).

**Fixed**
- **Settings shortcut rows now show *and* record the combo.** The hotkey chip was
  a `TextBox`, but its `ModernTextBox` template renders pre-set text **blank**, so
  the combo was invisible and editing was unreliable. Replaced with a
  `ContentControl` holding a `Border`+`TextBlock` chip. Click to focus, type a new
  combo (or press **Escape** to cancel); `GotKeyboardFocus` /
  `LostKeyboardFocus` / `PreviewKeyDown` drive record/cancel. Public API kept
  (`Gesture`, `GestureChanged`, `IsValid`, static `Display()`). Verified: all seven
  rows show `Ctrl + Shift + S/A/W/F/M/O/E` and are editable.

**Verified**
- Self-test **13/13, exit 0** on the feature build.
- **Window min/max/close buttons rendered as blank/tofu squares on the kiosk.**
  Two compounding causes. (1) The icon font list was `Segoe Fluent Icons, Segoe MDL2
  Assets`, but the locked-down kiosk image does **not** ship Segoe Fluent Icons, and
  WPF does not reliably fall through to the second family per‑glyph when the primary
  is missing — so the glyph buttons fell back to the system font and showed as squares.
  (2) To make the buttons **bulletproof on any machine**, they were redrawn as
  **vector shapes** (minimize = line, maximize = square, restore = double‑square,
  close = X) in `Controls.xaml` (`TitleBarMinButton` / `TitleBarMaxButton` /
  `TitleBarRestoreButton` / `TitleBarCloseButton`, all inheriting the button
  `Foreground`), with the maximize↔restore swap driven by swapping `Style` on
  `StateChanged` in `MainWindow` and `EditorWindow`. Each shape is explicitly centered
  (`HorizontalAlignment`/`VerticalAlignment = Center`) — Shapes default to the
  top‑left corner, which is what left the close X off‑center initially. `IconFont` is
  still reordered to `Segoe MDL2 Assets, Segoe Fluent Icons` and the one hard‑coded
  font string in `HistoryWindow` fixed, so the *other* icon glyphs (toolbar, cards,
  history) resolve to a font present on every Windows 10/11. Verified on the installed
  copy: minimize/maximize/close and all toolbar/card icons render, centered.
- Audio pipeline end to end: MP4 with **both** `h264` video **and**
  `aac 44100 Hz stereo 160 kb/s` produced, exit 0.
- Three surfaces screenshotted: mode cards (rounder + shadow), recording setup
  (Full desktop / Single display / Window / Region + mic picker), Settings
  (hotkey chips + audio toggle + mic combo showing the NVIDIA mic).

---

## 2026-11-09 — Single-file distribution (tidied the install folder)

**Changed**
- **The install folder went from ~256 files to 8.** A self-contained .NET app
  normally ships the whole runtime as ~249 loose DLLs (180 MB), which made the
  folder look like a mess. The app is now published **single-file**
  (`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` +
  `EnableCompressionInSingleFile`, baked into `SnippingTool.csproj`), so the entire
  .NET + WPF runtime is folded into **one** `Shutio.exe` (73 MB — *smaller* than the
  249 loose DLLs it replaces, thanks to compression).
  - What stays loose (intentionally): `Shutio.exe`, `UninstallShutio.exe`,
    `Assets\app.ico` + `logo.png` (shortcut + in-app logo), `Tools\ffmpeg.exe`
    (recording engine), your `Data\`, and the small `README`/`FEATURES`/launcher.
  - Installed copy `I:\Shutio` is now **8 items** (5 files + Assets/Data/Tools)
    with **0 loose DLLs**; verified self-contained (self-test passes with no loose
    DLLs present) and the WPF GUI launches.
  - Baked into the csproj so every build (app, installer, updater) is single-file;
    the installer/updater payloads (which copy the app folder) inherited it
    automatically. Repro tooling: `Tools\RebuildAppSingleFile.ps1`.

**Verified**
- Self-test **13/13, exit 0** on the single-file app folder AND the installed copy
  (run with the loose DLLs removed, proving self-containment).
- WPF GUI smoke test: app launches and stays up under single-file.
- Desktop/Start-Menu shortcut target + icon still resolve; launcher `.cmd` works.

---

## 2026-11-09 — Shortcut display, dynamic data path, offline updater, real icon (this session)

**Fixed**
- **Settings → Keyboard shortcuts:** each row now shows the action label, then a
  **compact chip with the actual key combination** (e.g. `Ctrl + Shift + S`) right
  next to it, plus a per-row **Reset** link. The value is always rendered the way a
  human expects it — `HotkeyBox.Display()` normalises both live-recorded and
  default gestures to `Ctrl + Shift + S` (no more inconsistent `Ctrl+Shift+S` from
  the defaults table). Recording a new combo now gives **live confirmation** in the
  status line ("Recorded … for … — press Save changes to apply") so the change is
  visibly reflected next to the shortcut.
- **App icon / logo was a generic blue placeholder, not the user's brand.** Now the
  icon and in-app logo are derived from the user's artwork `I:\shutio.png`
  (shutter + viewfinder + red record dot + play button). A square **emblem** is
  cropped from the artwork (the "Shutio" wordmark is dropped since it already shows
  as text next to the icon) and used for:
  - `Assets/logo.png` — in-app UI + window title-bar icon (embedded resource),
  - `Assets/app.ico` — a fresh multi-size (16-256) icon for the exe (taskbar /
    Alt-Tab / file explorer), the tray, and the Desktop / Start-Menu shortcuts.
  Reproducible tooling: `Tools\MakeLogo.ps1` (crop to `logo.png`) and
  `Install\src\make-ico\` (C# `MakeIco`, PNG source to multi-res `app.ico`).
  Two follow-up defects were found and fixed after the icon first landed:
  - **Desktop / Start-Menu tile rendered blank/broken.** The `MakeIco` generator
    wrote the `BITMAPINFOHEADER` `biPlanes` and `biBitCount` as 4-byte ints instead
    of 2-byte shorts, inflating every small-size DIB by 4 bytes and shifting its
    fields (so the 16-128 entries were malformed; only the 256 PNG was valid). The
    shell reads the loose `app.ico` at 32/48 px for the desktop tile and rejected
    those corrupt entries. Fixed: both now written as `(short)`; all 7 entries
    verified byte-valid, and the 32 px entry renders the emblem.
  - **Emblem was off-centre (black band on top).** The first crop used a square
    that was not centred on the emblem. `Tools\MakeLogo.ps1` now crops the emblem's
    **tight bounding box** and places it **centred** (contain, aspect preserved) on
    the 1024 canvas, so the top/bottom margins are symmetric.
  For both fixes the app exe, `Shutio-setup.exe`, `Shutio-Updater.exe` and the
  installed copy `I:\Shutio` were rebuilt/re-pushed (payloads carry the fix), the
  icon cache was cleared and Explorer restarted. Self-test stayed **13/13, exit 0**.

**Changed**
- **Main window footer:** the hard-coded
  `Portable · data stays in I:\Apps\SnippingTool\Data` was removed. It is now set
  from the **real install location** (`AppPaths.Data` = `AppContext.BaseDirectory
  \Data`), so the text matches wherever the user actually installed/copied the app.

**Added**
- **Offline updater — `Shutio-Updater.exe`** (single self-contained file, ~158 MB,
  whole app embedded) in `I:\Apps\Shutio-Share\`. It repairs an ALREADY-INSTALLED
  copy in place: finds the install (Apps-&-features entry / beside itself / common
  defaults / folder picker), closes a running Shutio, overwrites the app files, and
  **preserves the `Data\` folder** (snips, recordings, settings, history). It then
  runs the built-in self-test to prove the update is healthy (13/13). No admin, no
  internet, no PowerShell. Flags: `--yes`, `--no-verify`, `--launch`, `--silent`.
  Build source + `rebuild.ps1` in `Install\src\offline-updater\`.

**Verified**
- Clean Debug build (0 errors); Release publish → `Shutio.exe`; **self-test 13/13,
  exit 0**.
- **Updater end-to-end:** simulated a broken install (missing `Shutio.dll`) with a
  sentinel in `Data\`; ran `Shutio-Updater.exe <path> --yes --silent` → 258 files
  restored, `Shutio.dll` back, **sentinel + `settings.json` preserved**, self-test
  **13/13**, exit 0.
- **Icon:** the new emblem is present in `Assets/logo.png` + `Assets/app.ico`
  (source, app folder, and the installed copy `I:\Shutio`); the PNG icon is
  embedded in `Shutio.exe` (verified by extracting it from the exe's resources);
  installer + updater payloads carry it; Desktop / Start-Menu shortcuts re-pointed
  to the new `app.ico`; self-test still **13/13, exit 0**.

---

## 2026-11-09 — Rebranded to "Shutio" (this session)

**Changed**
- **Product renamed to "Shutio"** — the user's chosen short, one-word name (verified
  via web search to not collide with any existing app) and the user's own logo
  (shutter aperture + play button + viewfinder corners + red record dot).
- **Executable:** `SnippingTool.exe` → **`Shutio.exe`** (and `Shutio.dll` /
  `Shutio.deps.json` / `Shutio.runtimeconfig.json`). Old-name orphans removed after
  publish. Launcher renamed to **`Start Shutio.cmd`**.
- **Installers:** `InstallSnippingTool.exe` → **`Shutio-setup.exe`**;
  `UninstallSnippingTool.exe` → **`UninstallShutio.exe`**. Share folder moved
  `I:\Apps\SnippingTool-Share\` → **`I:\Apps\Shutio-Share\`**; desktop copy is now
  **`Shutio-setup.exe`**. Uninstaller process lookup now targets `Shutio`.
- **Icons / logo:** regenerated `Assets\app.ico` as a classic multi-size (16–128 px)
  icon from `Assets\logo.png` (512×512). The logo is the in-app UI image (title bar,
  history, settings, editor) and the source of the app/installer icon.
- **User-facing strings** (window titles, tray tooltip, toasts, About, log banners,
  self-test report header) now say "Shutio". Installer registry entry, shortcut
  names, Apps-&-features display name, and fallback install path all use "Shutio".

**Kept (for stability / internal-only)**
- Folder path `I:\Apps\SnippingTool`, C# namespace `SnippingTool`, source folder
  `Source\SnippingTool\`, and the csproj file name `SnippingTool.csproj`.
- `SelfTest.cs` OCR test still draws/asserts "Snipping Tool 12345" (test fixture,
  not a brand string).

**Verified**
- Clean Debug build (0 errors); Release publish → `Shutio.exe`; **self-test 13/13,
  exit 0** (report header now "Shutio — self-test report").
- Rebuilt both installers; verified the full **isolated share → install → 13/13 →
  uninstall** cycle using only `Shutio-setup.exe` (no source app present), with the
  deployed `UninstallShutio.exe` cleanly removing the folder, shortcuts, and the
  Apps-&-features entry.

**Docs**
- README, AGENTS.md, HANDOFF.md, TODO.md rebranded to "Shutio" (folder/namespace/source
  references intentionally left as-is); share-folder `README.txt` rewritten.

---

## 2026-11-09 — TRUE OFFLINE installer + standalone uninstaller (this session)

**Added**
- **`I:\Apps\SnippingTool-Share\InstallSnippingTool.exe`** (~205 MB) — a complete,
  self-extracting **offline** installer. The *entire application* is embedded inside
  this one file (as a compressed archive), plus a built-in uninstaller. Share this
  single `.exe` with anyone: double-click → pick a folder → done. No internet, no
  admin, no PowerShell, no other files needed. Works on any Windows 10/11 x64 PC.
- **`I:\Apps\SnippingTool-Share\UninstallSnippingTool.exe`** (~47 MB) — a standalone
  offline uninstaller. Removes the installed app + Start Menu/Desktop shortcuts + the
  Apps & features entry; keeps `Data\` unless `--remove-data` is passed. Supports
  `--silent`, `--yes`, and an install-path argument.
- At install time the installer **deploys its own `UninstallSnippingTool.exe`** into
  the install folder and points the Apps & features entry at it, so every install is
  self-uninstalling.
- **`I:\Apps\SnippingTool-Share\README.txt`** — plain-language instructions.
- Reproducible build source in `Install\src\offline-installer\`
  (`uninstaller\`, `installer\`, and **`rebuild.ps1`**), using the shared `I:\DevTools` SDK.

**Verified (full share→install→run→uninstall cycle)**
- Copied ONLY `InstallSnippingTool.exe` to an isolated folder (simulating a USB copy
  with no source app present) → ran it → it extracted the full app (257 files),
  deployed the uninstaller, created shortcuts + Apps & features entry.
- The installed copy passed the **13/13 self-test**.
- Ran the DEPLOYED `UninstallSnippingTool.exe --silent --remove-data` → removed the
  folder, both shortcuts, and the registry entry. All confirmed clean.

**Changed**
- Removed the earlier "launcher-style" `InstallSnippingTool.exe` (which copied the
  app from the source folder and so could not be shared standalone) from `Install\`;
  it is superseded by the offline self-extracting installer.
- The installer ships a **clean** app (no personal `Data\`), which is correct for
  distribution; the local PowerShell `Install\install.ps1` still carries over your own
  `Data\` if you prefer to reinstall in place.

**Docs**
- README install section + data-layout tree updated; share folder documented.

---

## 2026-11-09 — Standalone `.exe` installer (this session)

**Added**
- **`Install\InstallSnippingTool.exe`** — a double-clickable, self-contained installer
  (~47 MB, no PowerShell, no admin, no internet). Implements the same install flow as
  the PowerShell installer: native "choose a folder" dialog, copies the lean app,
  carries over existing `Data\` (snips/settings), creates **Start Menu + Desktop**
  shortcuts, writes a per-user **Apps & features** entry, and drops a self-service
  uninstaller into the install folder.
  - Also accepts a CLI path: `InstallSnippingTool.exe "C:\Wherever"`, and
    `--skip-prompts` for automation.
  - Build source: `Install\src\` (`Installer.csproj` + `Program.cs`), published
    self-contained single-file from the shared `I:\DevTools` SDK.
- Verified end-to-end: ran the `.exe` to a test path → **13/13 self-test from the
  installed copy** → Start Menu + Desktop shortcuts + HKCU entry created → clean
  removal of the test install.

**Docs**
- README install section now leads with the `.exe`; layout tree updated.

---

## 2026-11-09 — Shared build tooling at `I:\DevTools` (this session)

**Changed**
- Relocated the build tooling from the per-project sibling
  `I:\Apps\SnippingTool-DevTools\` to the SHARED, project-agnostic `I:\DevTools\`,
  so any .NET project on this machine can reuse the same SDK + caches:
  - `I:\DevTools\dotnet\` (SDK 10.0.100) — also added to the **user PATH**.
  - `I:\DevTools\nuget\` (NuGet cache) — set as user `NUGET_PACKAGES`.
  - `I:\DevTools\dotnet-home\` — set as user `DOTNET_CLI_HOME`.
  - `I:\DevTools\ffmpeg\` (full ffmpeg distribution) + `ffmpeg.zip` / SDK zip.
  - `I:\DevTools\project-caches\snippingtool\bin` + `obj` — this project's artifacts.
- Removed the now-empty `I:\Apps\SnippingTool-DevTools\` folder.

**Verified**
- `dotnet --version` → **10.0.100** from the new shared location.
- Rebuilt the SnippingTool project against the shared SDK → **0 errors** (cross-project
  reusability confirmed). Re-cleaned the regenerated `bin`/`obj`; app folder back to 288 MB.
- User env (reversible, `HKCU`): PATH now includes `I:\DevTools\dotnet`;
  `NUGET_PACKAGES` / `DOTNET_CLI_HOME` point into `I:\DevTools` — so a fresh shell can
  just run `dotnet`.

**Docs**
- AGENTS / README / HANDOFF / TODO updated to point all build tooling at `I:\DevTools`.

---

## 2026-11-09 — Folder slim-down + simple installer (this session)

**Added**
- **Simple installer** in `Install\` (no admin, no internet, no extra software):
  - `Install Snipping Tool.cmd` → double-click to open a native "choose a folder"
    dialog (Windows `FolderBrowserDialog`).
  - `install.ps1` copies the lean, self-contained app to the chosen path, carries
    over existing `Data\` (snips / history / settings), creates **Start Menu +
    Desktop** shortcuts, registers a per-user **Apps & features** entry, and drops
    an `Uninstall Snipping Tool.cmd` into the install folder.
  - `uninstall.ps1` removes the shortcuts + registry entry and (with your OK) the
    folder; preserves `Data\` unless you opt in. Both support `-SkipPrompts` for
    automation.
- Verified end-to-end: installed to a test path → **13/13 self-test from the
  installed copy**, GUI launched, clean shutdown; uninstall removed everything.

**Changed / slimmed (main folder 2,456 MB → 288 MB, ~88% smaller)**
- Moved build-only tooling OUT of the project folder (nothing deleted) to
  `I:\Apps\SnippingTool-DevTools\`: the .NET SDK (`dotnet`, 770 MB), the NuGet
  cache (`.nuget`, 380 MB), the full ffmpeg distribution (314 MB) + its zips,
  and the `Source\...\bin` + `obj` build artifacts.
- Kept in the project folder: the runnable app (self-contained runtime +
  `Tools\ffmpeg.exe`), `Assets\`, the C# **source code** (0.4 MB), docs, and the
  user's `Data\`.
- The running app is unaffected (it only ever reads `Tools\ffmpeg.exe`, not the
  full ffmpeg folder, and never needs the SDK at runtime).

**Docs**
- README now documents install/uninstall; HANDOFF/TODO updated.

**Rebuilding after the slim-down** (this sibling path was later relocated to the
SHARED `I:\DevTools` — see the entry above; the sibling no longer exists):
```powershell
# Current shared location (as of the next entry up):
$env:DOTNET_CLI_HOME='I:\DevTools\dotnet-home'
$env:NUGET_PACKAGES ='I:\DevTools\nuget'
& 'I:\DevTools\dotnet\dotnet.exe' build `
  'I:\Apps\SnippingTool\Source\SnippingTool\SnippingTool.csproj' -c Debug -v q -nologo
```

---

## 2026-11-09 — Recovery, verification & continuity docs (this session)

**Verified**
- Reproduced a clean compile of the current source: `dotnet build -c Debug`
  → **0 Warning(s), 0 Error(s)**.
- Ran the deployed self-test (`SnippingTool.exe --selftest`) → **13/13 PASS,
  exit code 0**. Evidence in `Data\Tests\selftest-report.txt`.
- Launched the real GUI on the kiosk → main window rendered correctly
  (5 capture modes, after-capture toggles, delay selector, recent snips with
  thumbnails, floating toolbar, Settings/History). Log confirms
  `App started. Main visible: True; hotkeys: Shortcuts ready; ffmpeg: True.`
  and all 7 hotkeys registered. Clean shutdown confirmed.
- Confirmed empirically (via `GetProcAddress`) that `GetIconInfo` and
  `DrawIconEx` are exported by **user32.dll**, not gdi32.dll — validating the
  current P/Invoke declarations.

**Docs (created for AI continuity)**
- Added `AGENTS.md`, `HANDOFF.md`, `TODO.md`, and this `CHANGELOG.md`.
- Existing `README.md` and `FEATURES.md` retained as the user-facing docs.

---

## 2026-11-08 / 2026-11-09 (early) — Initial implementation & first bug fix

**Added** (full feature set)
- WPF main window + capture overlay for Rectangle / Freeform / Window /
  Full-desktop / Single-display capture, with capture-delay countdown.
- Editor: pen, highlighter, eraser, line/ruler, crop, undo/redo, rotate,
  resize (aspect lock + presets), zoom/pan, palette + custom color, 6 thicknesses,
  save as PNG/JPG/BMP, copy to clipboard.
- Windows OCR panel (`Windows.Media.Ocr`) with copy text + no-text handling.
- MP4 screen recording via bundled ffmpeg (H.264, libx264/ultrafast) with
  optional cursor overlay, setup dialog, and always-on-top indicator.
- History with thumbnails, search, and mode filters.
- Settings (JSON) with theme, defaults, file-name pattern, tray/floating
  toolbar, recording fps, and 7 global hotkeys with conflict detection.
- Tray icon, floating capture toolbar, toast notifications, single instance.
- Headless self-test suite (`--selftest`) covering 13 subsystems.
- Portable .NET SDK + ffmpeg bundled under `Tools\` for offline rebuilds.

**Fixed**
- **Cursor overlay in recordings**: `GetIconInfo` and `DrawIconEx` were first
  declared against `gdi32.dll`, which does **not** export them on this OS.
  Every recording frame logged `Unable to find an entry point named
  'GetIconInfo' in DLL 'gdi32.dll'` and the cursor was never drawn. Moved both
  to **`user32.dll`** (confirmed to export them), and added one-shot error
  suppression in `CaptureService.DrawCursor`. Verified fixed: the
  `IncludeCursor = true` MP4 self-test passes with **no** cursor-overlay errors.
- Log hygiene: cursor-overlay failures now log once instead of ~30×/frame.

**Decisions**
- **Self-contained x64 publish** into the app folder so the app runs without a
  system .NET install and without admin rights.
- **All data under `I:\Apps\SnippingTool\Data\`** — nothing leaves the folder
  except an optional clipboard copy.
- **Windows-key hotkeys rejected by design**; `RegisterHotKey` is the only
  global-shortcut mechanism (works while hidden/minimized).
- **Software H.264 encoding** (libx264) for reliability over a hardware encoder.
- **Freeform** outside-area alpha is preserved in PNG only; JPG/BMP flatten it
  (no alpha channel) — documented, not a bug.

**Known limitations** (carried forward)
- Global hotkeys depend on the desktop shell allowing `RegisterHotKey`; the
  kiosk shell may refuse them (floating toolbar / tray are the fallback).
- Protected/DRM/hardware-overlay windows may fall back to a frozen-desktop crop.
- No audio track in recordings (video only by design).
- OCR quality depends on the installed Windows OCR language pack.
