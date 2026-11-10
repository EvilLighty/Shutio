# Shutio for Windows 11 (portable)

A self-contained, no-admin screenshot and screen-recording app for Windows 10/11
(64-bit). It is a native WPF application styled like the Windows 11 Snipping Tool
and stores everything inside its own folder.

- **Where it lives:** anywhere you put it. The installer lets you pick the folder;
  the portable version runs from any folder, drive, or USB stick.
- **Your data:** the `Data\` folder right next to the app (snips, recordings,
  settings, history). It moves with the app, and the exact path is shown in
  Settings. The snip *save folder* is itself a setting you can change.
- **Runtime:** self-contained .NET 10 (x64). No .NET install, MSI, service, driver,
  registry change, UAC prompt, or Windows-key shortcut is used.
- **Tested on:** Windows 11 (build 22621), 2560x1440 display at 150% scaling.

## Quick start

Double-click **`Shutio.exe`** (or `Start Shutio.cmd`) in the folder you installed
it into.

The main window opens. Use **New snip**, a capture card, the floating toolbar, the
notification-area icon, or a global shortcut.

To hide the window but keep shortcuts alive, close it with **Close to tray** enabled
in Settings (or use the tray button on the title bar).

## Install it anywhere (optional) — or share it with someone

There is a **complete offline installer** that contains the whole app inside one
file. It needs no internet, no admin rights, and no other files — so you can copy
**`Shutio-setup.exe`** to a USB stick or email it
and it installs on any Windows 10/11 (64-bit) PC.

**To install:**
1. Double-click **`Shutio-setup.exe`**.
2. Pick the folder to install into (any writable location), then **Select Folder**.
3. It extracts the app, adds its own uninstaller there, and creates Start Menu +
   Desktop shortcuts plus an **Apps & features** entry.

**To remove it:**
- Double-click **`UninstallShutio.exe`** (in the folder it was installed to), or
- **Apps & features → Shutio → Uninstall**.
- Your saved snips/recordings in `Data\` are **kept** unless you choose to delete them
  (the uninstaller also supports `--silent`, `--yes`, and `--remove-data` for automation).

> Both `.exe` files ship with the releases (or in your share folder). The app is
> built as a self-contained .NET program — no PowerShell or extra runtime required.
> To rebuild the installer from source:
> `Install\src\offline-installer\rebuild.ps1`. A simpler PowerShell installer that
> also keeps your local `Data\` is in `Install\install.ps1`.

## Update an existing install (offline)

If the app is already installed somewhere, **`Shutio-Updater.exe`** (from the
release; the whole new app is inside) repairs that copy **in place**:

1. Double-click **`Shutio-Updater.exe`**.
2. It finds the install (Apps & features entry, or the folder it sits in, or a common
   default), asks you to confirm, then closes any running Shutio.
3. It overwrites the application files and **keeps your `Data\` folder** (snips,
   recordings, settings, history) untouched.
4. It runs the built-in self-test to prove the update is healthy (13/13).

No internet, no admin, no PowerShell needed. Useful flags:

| Flag | Meaning |
| --- | --- |
| `--yes` | skip the confirmation prompt |
| `--no-verify` | skip the self-test step |
| `--launch` | start the app after updating |
| `--silent` | no dialogs at all (for automation) |

To rebuild the updater from source: `Install\src\offline-updater\rebuild.ps1`.

## Default global shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+Shift+S` | Rectangular snip |
| `Ctrl+Shift+A` | Freeform snip |
| `Ctrl+Shift+W` | Window snip |
| `Ctrl+Shift+F` | Full desktop snip |
| `Ctrl+Shift+M` | Single display snip |
| `Ctrl+Shift+O` | Open snip history |
| `Ctrl+Shift+E` | Open editor for the latest snip |

Shortcuts are registered with `RegisterHotKey`, so they work while the main window
is hidden or minimized. They can be changed in **Settings > Keyboard shortcuts**.
While Settings is open the shortcuts are temporarily released so an
already-assigned combination can be recorded; they are registered again when
Settings closes. Duplicate assignments are detected, explained inline, and block
saving. Windows-key shortcuts are intentionally rejected.

> Some kiosk/shell environments block global hotkeys. When that happens use the
> floating toolbar (Settings > Show the floating capture toolbar) or the tray menu.

## Capture modes

1. **Rectangle** - drag a rectangle; size and position readout follows the pointer.
2. **Freeform** - drag a freehand outline. Pixels outside the outline are fully
   transparent (PNG); the desktop remains visible through them.
3. **Window** - hover to preview and click a top-level window. Uses
   `DwmGetWindowAttribute` for accurate bounds and `PrintWindow(PW_RENDERFULLCONTENT)`
   for content, with a frozen-desktop crop fallback for protected windows.
4. **Full desktop** - one click captures the entire virtual desktop, including
   every monitor.
5. **Display** - one click captures the monitor under the pointer.

A capture delay of 0, 3, 5, or 10 seconds can be selected before capture. A
countdown is displayed. App windows are hidden while the overlay is shown and
restored afterwards.

## Editor

Open automatically after capture (optional) or from a history card.

- Pen, highlighter (semi-transparent), eraser, straight line/ruler, crop.
- 12-color palette plus a custom color picker; thickness presets 2, 4, 7, 11, 16,
  and 22 px.
- Undo/redo with `Ctrl+Z` / `Ctrl+Y`, copy with `Ctrl+C`, save with `Ctrl+S`.
- Rotate left/right, resize dialog with aspect-ratio lock and 25-100-200% presets.
- Zoom out/in, fit to window, and pan with the mouse.
- Save as PNG, JPG, or BMP. Copy image to the clipboard.
- OCR panel: extract text with Windows OCR and copy the recognized text.

## OCR

Uses the built-in Windows `Windows.Media.Ocr` engine with the installed English
(United States) language pack. Results show line and character counts. If a snip
has no text, the panel says so and disables copying. If another OCR language is
installed, set `OcrLanguage` in `Data\settings.json` (for example `fr-FR`), or leave
it empty for the system default.

## History

Every saved snip is indexed in `Data\history.json` with a thumbnail. The history
window supports:

- Search by file name, path, mode, or date.
- Filters for Rectangle, Freeform, Window, Full desktop, and Display.
- Open in editor, copy image, delete, and open the save folder.
- 6 cards are shown per page, with recent snips also listed on the main window.

## Screen recording (optional)

**Record** opens a setup dialog with:

- Source: full desktop, a single display, a window, or a dragged region.
- Frame rate: 10, 15, 24, or 30 fps.
- Optional mouse-cursor overlay.
- Optional **microphone audio**: a toggle plus a microphone picker. When on, the
  MP4 gets an AAC 160 kb/s audio track alongside the H.264 video. Off by default.

Recording pipes raw frames to `Tools\ffmpeg.exe` and produces an H.264 MP4 in
`Data\Recordings`. A small always-on-top indicator shows elapsed time and frame
count, with a Stop button. Screenshot capture is unaffected while idle.

ffmpeg is bundled; if `Tools\ffmpeg.exe` is missing the app still takes
screenshots and reports that recording is unavailable. Audio is only recorded
when the toggle is on and a microphone is selected.

## Settings

`Data\settings.json` is human-readable and is created with safe defaults on first
run. Editable in the app:

- Theme: System, Light, or Dark (live preview).
- Default capture mode, delay, auto-copy, auto-save, open editor, notifications.
- Save folder and file-name pattern, with a live preview. Default pattern:
  `Snip_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}`.
- Show the floating capture toolbar.
- Minimize/close to tray and start minimized.
- Recording frame rate, cursor inclusion, and optional microphone audio
  (toggle + mic picker).
- All seven global shortcuts, with conflict detection and per-action reset.

Saving applies changes immediately and re-registers shortcuts.

## Data layout

```
<install folder>          (wherever you put it — any drive, any depth)
  Shutio.exe                the app (self-contained, ~73 MB)
  Start Shutio.cmd          portable launcher
  UninstallShutio.exe       standalone uninstaller (added by the installer)
  Install\                  installer/updater source + build scripts
  README.md                 this file
  FEATURES.md               implemented / unavailable matrix
  CHANGELOG.md              change history
  Assets\                   icons and logo
  Tools\ffmpeg.exe          recording engine (bundled with releases)
  Source\SnippingTool\      full C# source
  Data\                     your data (created on first run — delete to reset)
    settings.json           settings (human-readable JSON)
    history.json            snip history index
    Screenshots\            default snip output (the save folder is a setting)
    Thumbnails\             history thumbnails
    Recordings\             MP4 output
    Exports\                Shutio Cut video exports
    Logs\                   daily app logs
    Tests\                  self-test reports
```

Nothing is written outside this folder (the Windows clipboard is used only when
you copy a snip or OCR text).

## Reset

Close the app, then delete these to return to defaults:

- `Data\settings.json`
- `Data\history.json`
- the contents of `Data\Screenshots`, `Data\Thumbnails`, `Data\Recordings`,
  `Data\Tests`, and `Data\Logs`

The app recreates the folders and default settings on the next launch.

## Build from source

Requires the .NET 10 SDK on the PATH (any x64 Windows machine). From the repo root:

```powershell
# Compile check
dotnet build Source\SnippingTool\SnippingTool.csproj -c Debug

# Self-contained single-file publish (the single-file settings live in the csproj)
dotnet publish Source\SnippingTool\SnippingTool.csproj -c Release -r win-x64 `
  --self-contained true -o .\publish -p:DebugType=None -p:DebugSymbols=false
```

For recording support, place an `ffmpeg.exe` in `Tools\` next to the published
app (releases bundle one; the source tree deliberately does not).

## Self-test

```powershell
.\Shutio.exe --selftest        # run from the app folder
```

The headless suite exercises paths, settings round-trip, PNG/JPEG/BMP encoding,
bitmap transforms, crop/rotate/resize math, freeform alpha, history persistence,
file-name patterns, hotkey parsing, and ffmpeg availability. The report is written
to `Data\Tests\selftest-report.txt`; exit code 0 means all tests passed.

## Known limitations

- Global hotkeys depend on the desktop shell allowing `RegisterHotKey`. Locked-down
  shells can refuse them; use the floating toolbar or tray menu instead.
- Windows-key combinations are not supported by design.
- Window capture of protected/DRM/hardware-overlay windows may fall back to a
  desktop crop; it then includes whatever is on screen.
- Freeform transparency is preserved in PNG. JPG and BMP have no alpha channel, so
  the outside area is flattened.
- Screen recording requires `Tools\ffmpeg.exe`. Microphone audio is optional
  (off by default) and depends on the selected mic delivering frames.
- OCR quality depends on the installed Windows language pack and on text size.
- The UI is designed for one or more monitors at 100-200% scaling; extreme
  mixed-DPI layouts have not been lab-tested beyond 150% scaling.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| Shortcuts do nothing | Enable the floating toolbar or tray icon; another app may own the combination. Check the status chip on the main window. |
| Recording disabled | Confirm `Tools\ffmpeg.exe` exists. |
| OCR finds nothing | Snip larger/clearer text, or install the Windows OCR language pack. |
| Window snip shows the desktop | The target uses a protected surface; the app fell back to the frozen-desktop crop. |
| Settings/history look clipped | Resize the window or scroll; content is scrollable on small displays. |
| App does not start | Check `Data\Logs\app-<date>.log`; confirm the folder is writable. |

## Verification performed

The published build was exercised end to end on the reference machine:

- Rectangle, freeform (alpha 0 outside / 255 inside), full desktop (2560x1440),
  single display (2560x1440), and window capture (2560x1368 via PrintWindow).
- Editor: pen, highlighter transparency, eraser, line, crop (81x92), rotate (92x81),
  resize (180x120), zoom, color/thickness, undo/redo, save, copy.
- OCR: 998 characters from a text-rich snip, copy to clipboard; zero-text snip
  disables copying.
- History: cards, thumbnails, search, mode filter, editor launch.
- Recording: valid H.264 MP4 (and, when enabled, a muxed AAC mic-audio track),
  indicator, setup dialog with Full desktop / Single display / Window / Region.
- Settings: light/dark preview, save persistence, floating toolbar, shortcut
  conflict detection, hotkey suspend/resume.
- Global shortcut dispatch (`WM_HOTKEY`) and the fallback capture path.
- Headless self-test: 13/13 passed.
