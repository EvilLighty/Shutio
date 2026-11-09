# AGENTS.md - AI Working Guide for Shutio

> This file is the first thing a new AI agent should read. It records the
> project's rules, environment constraints, conventions, and exact build/test
> commands so the project stays maintainable across sessions and models.

> ## 📍 START HERE (continuing from a previous session)
> **The app is complete, delivered, and rebranded to "Shutio".** Everything below
> is already on disk in `I:\Apps\SnippingTool\` — the AI's prior conversation does
> **not** need to be available to continue.
>
> - **App:** `I:\Apps\SnippingTool\Shutio.exe` (self-contained, passes `--selftest` 13/13).
> - **Share/installer:** `I:\Apps\Shutio-Share\Shutio-setup.exe` (whole app inside,
>   ~205 MB) + `UninstallShutio.exe`; a copy of `Shutio-setup.exe` is on the Desktop.
> - **Source of truth:** `I:\Apps\SnippingTool\Source\SnippingTool\` (namespace
>   `SnippingTool` is intentionally **kept**; only the *user-facing* name changed).
> - **Read next:** **`HANDOFF.md`** for the current state + exact next steps,
>   **`CHANGELOG.md`** for history (incl. the 2026-11-09 "Rebranded to Shutio" entry).
> - **Verify any change** with the recipe in `HANDOFF.md` §6: build → `Shutio.exe --selftest`
>   (must stay 13/13, exit 0).
>
> Nothing is blocked. If the user gives a new task, orient with `HANDOFF.md` first,
> make the smallest reliable change, then re-run the self-test and refresh the docs.

## What this project is

A **portable, no-administrator Windows 11 capture tool named "Shutio"** (a "Snipping Tool"-style app) written in
C# / .NET 10 WPF (+ WinForms for the tray icon). It lives entirely in
`I:\Apps\SnippingTool` and is designed for the restricted **SalsaNOW kiosk**.

Capabilities (all implemented and self-test verified):
- Capture: **Rectangle, Freeform, Window, Full desktop, Single display**
- Editor: pen, highlighter, eraser, line/ruler, crop, undo/redo, rotate,
  resize (aspect lock), zoom/pan, 12-color palette + custom color, 6 thicknesses
- Save as **PNG / JPG / BMP**; copy image to clipboard
- **Windows OCR** text extraction + copy (`Windows.Media.Ocr`)
- **MP4 screen recording** (ffmpeg, H.264) with optional mouse-cursor overlay
- Searchable **history** with thumbnails (stored under `Data\`)
- 7 configurable **global hotkeys** (`RegisterHotKey`), tray icon, optional
  floating toolbar, toast notifications, light/dark/system theme, single instance

## Environment — HARD CONSTRAINTS (never violate)

This is **not** a normal dev machine. It is a SalsaNOW kiosk session.

| Fact | Value |
| --- | --- |
| Windows account | `kiosk` (standard user) |
| Admin rights | **NONE** — do not request elevation, do not touch protected dirs |
| Persistent drive | `I:\` (this is where the project and its data live) |
| Model dir (assistant) | `I:\Ollama\Models` |
| Project root | `I:\Apps\SnippingTool` |
| Display | 2560×1440, 150% scaling (PerMonitorV2 DPI aware) |
| GPU | NVIDIA RTX 5080, 48 GiB (available, but the app does **not** use it) |
| Shell | Windows PowerShell; also `I:\Apps\SalsaNOW\SilentApps\Powershell\pwsh.exe` |

Rules:
- **No** system-wide installs, MSIs, services, drivers, registry writes, UAC.
- **No** admin-only tools (DISM, `sc`, `netsh` admin, etc.).
- Reuse tools already present under `Tools\` before downloading anything.
- `CloudStore` is the sanctioned way to install an application if one is truly needed.
- Keep everything (code, data, temp, build) under `I:\Apps\SnippingTool` where possible.
- Do **not** put secrets / API keys in any file or in terminal output.

## Repository layout

```
I:\Apps\SnippingTool\            (~288 MB — the runnable app + source + docs)
  Shutio.exe / .dll / .deps.json / .runtimeconfig.json         # published self-contained app
  Start Shutio.cmd                                              # portable launcher
  Install\    ← installers (Install Shutio.cmd, install.ps1, uninstall.ps1) + offline-installer source
  AGENTS.md   ← this file
  HANDOFF.md  ← current state + next steps (update every session)
  TODO.md     ← prioritized tasks, completed tasks, known bugs
  CHANGELOG.md← implementation history + architectural decisions
  README.md   ← user-facing docs (install/build/test/reset/troubleshooting)
  FEATURES.md ← implemented / partial / unavailable matrix
  Assets\     # app.ico, logo.png
  Tools\      # ffmpeg.exe (the only tool the app needs at runtime)
  Source\SnippingTool\   # full C# source (the source of truth)
  Data\       # runtime state (see below)

I:\DevTools\                     (SHARED, project-agnostic build tooling; NOT needed to run the app)
  dotnet\     # portable .NET SDK 10.0.100 (also on the user PATH)
  nuget\      # shared NuGet package cache
  dotnet-sdk-10.0.100-win-x64.zip
  ffmpeg\     # full ffmpeg distribution (the source of the standalone ffmpeg.exe)
  ffmpeg.zip
  dotnet-home\# DOTNET_CLI_HOME
  project-caches\snippingtool\bin / obj   # this project's build artifacts (rebuildable)
```

> **Note:** to keep the app folder light, the build tooling was moved out to the
> SHARED location `I:\DevTools\` above, so any .NET project on this machine can
> reuse the same SDK + caches. The app itself is completely self-contained and
> runs from the main folder alone. Rebuilding uses `I:\DevTools` (see below).
> The SDK is already on the user PATH and `NUGET_PACKAGES` / `DOTNET_CLI_HOME`
> point into `I:\DevTools`, so a fresh shell can just run `dotnet`.

`Data\` (created automatically, safe to delete to reset):
```
Data\
  settings.json   # human-readable settings (theme, modes, hotkeys, paths...)
  history.json    # list of snips (path, mode, size, timestamps)
  Screenshots\    # captured snips
  Thumbnails\     # history thumbnails
  Recordings\     # MP4 output
  Tests\          # self-test evidence (images, mp4, report)
  Logs\           # app-<date>.log, selftest-<stamp>.txt, selftest-latest.txt
  dotnet-home\    # DOTNET_CLI_HOME
  .nuget\         # NUGET_PACKAGES cache
```

## Build / test / run (exact commands)

Use the **bundled** SDK and keep NuGet + CLI caches inside the project so the
build is reproducible offline. From `I:\Apps\SnippingTool`:

```powershell
# Build tooling was slimmed out of the app folder into the SHARED "I:\DevTools".
# The SDK is on the user PATH and NUGET_PACKAGES / DOTNET_CLI_HOME already point
# into I:\DevTools, so a fresh shell can just run `dotnet build` on the project.
$dev    = 'I:\DevTools'
$dotnet = if (Test-Path "$dev\dotnet\dotnet.exe") { "$dev\dotnet\dotnet.exe" } else { 'dotnet' }  # PATH fallback
$env:DOTNET_CLI_HOME = "$dev\dotnet-home"
$env:NUGET_PACKAGES  = "$dev\nuget"
$proj   = 'I:\Apps\SnippingTool\Source\SnippingTool\SnippingTool.csproj'

# 1) Compile check (Debug) — expect "Build succeeded. 0 Warning(s) 0 Error(s)"
& $dotnet build $proj -c Debug -v q -nologo

# 2) Headless self-test (writes Data\Logs\selftest-latest.txt + Data\Tests\selftest-report.txt)
& 'I:\Apps\SnippingTool\Shutio.exe' --selftest            # exit code 0 = all pass

# 3) Publish a fresh self-contained x64 build OVER the app folder (DESTRUCTIVE to app folder)
& $dotnet publish $proj -c Release -r win-x64 --self-contained true `
     -o 'I:\Apps\SnippingTool' -p:DebugType=None -p:DebugSymbols=false -v q -nologo
Copy-Item 'I:\Apps\SnippingTool\Tools\ffmpeg\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe' `
     'I:\Apps\SnippingTool\Tools\ffmpeg.exe' -Force

# 4) Run the app (main window)
& 'I:\Apps\SnippingTool\Shutio.exe'
```

Useful CLI flags (all optional): `--selftest`, `--hidden`, `--capture=<mode>`,
`--record`, `--history`, `--settings`, `--exit`.

**Verify after any source change:** `build` → `--selftest` (must stay 13/13,
exit 0) → publish → launch → check `Data\Logs\app-<date>.log`.

## Architecture (high level)

- **Entry / args** — `App.xaml.cs`. Parses CLI flags; in `--selftest` mode runs
  `SelfTest.RunAsync()` then exits. Otherwise creates `AppHost`.
- **Coordinator** — `AppHost.cs`. Owns long-lived services and wires capture,
  editing, recording, tray, floating toolbar, hotkeys, and window lifecycle.
- **Core** — `Settings` (JSON load/save + `Sanitize()`), `HotkeyManager` +
  `HotkeyGesture` (`RegisterHotKey`), `NativeMethods` (all P/Invoke),
  `Logger`, `SingleInstance`, `ThemeManager`.
- **Capture** — `CaptureService` (BitBlt / `PrintWindow` / freeform alpha mask /
  monitor + window enumeration), `CaptureOverlayWindow`, `CountdownWindow`,
  `SelectionSurface`.
- **Editor** — `EditorWindow`, `ImageCanvas` (annotation surface), `ResizeDialog`.
- **Imaging** — `BitmapUtils` (crop/rotate/resize/save/load), `OcrService`
  (`Windows.Media.Ocr`).
- **Recording** — `RecordingController` (frames → ffmpeg stdin pipe),
  `RecordingSetupWindow`, `RecordingIndicatorWindow`.
- **Storage** — `HistoryService`, `SnipEntry`.
- **UI** — `MainWindow`, `HistoryWindow`, `SettingsWindow`,
  `FloatingToolbarWindow`, `ToastWindow`, `TrayIcon`, `MessageDialog`,
  `HotkeyBox`, `ProgressRing`.
- **Themes** — `Light.xaml`, `Dark.xaml`, `Controls.xaml`.

Key invariants:
- Everything the app writes stays under `Data\` (clipboard is the only external side effect).
- `Settings.Sanitize()` is the single place that repairs invalid settings; always call it on load and before save.
- Global hotkeys are temporarily released while Settings is open (so a combo can be re-recorded) and re-registered on close.
- Single instance via a named mutex; a second launch signals the first and exits.

## Code conventions

- .NET 10, `net10.0-windows10.0.19041.0`, `Nullable` + `ImplicitUsings` enabled.
- WPF types are used directly; `System.Drawing` and `System.Windows.Forms` are
  pulled in **only** where needed (tray, cursor, bitmap interop) via local
  aliases (`WinForms = System.Windows.Forms`). Do **not** add global
  `using System.Drawing` / `using System.Windows.Forms` — the csproj explicitly
  removes them to avoid `Point`/`Color`/`Button` ambiguity.
- P/Invoke lives in `Core\NativeMethods.cs`. **Get the DLL name right** — see
  the `GetIconInfo` / `DrawIconEx` note in CHANGELOG.md.
- Errors are logged through `Core\Logger` to `Data\Logs`. User-facing failures
  go through `UI\MessageDialog`.
- Prefer the smallest reliable change; do not rewrite working subsystems.

## Do / Don't

- ✅ Run the self-test after changes; keep it at 13/13 / exit 0.
- ✅ Update HANDOFF.md, TODO.md and CHANGELOG.md at the end of every session.
- ✅ Reuse the bundled SDK + ffmpeg; keep build state under `I:\`.
- ❌ Do not install system-wide software or require admin rights.
- ❌ Do not delete the existing source or data unless explicitly asked.
- ❌ Do not add paid APIs or remove the local-only (offline) design.
- ❌ Do not expose secrets, keys, or private config anywhere.
