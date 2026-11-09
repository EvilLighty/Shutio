# HANDOFF.md - Current State of Shutio

> Read **AGENTS.md** first (rules + exact commands). This file is the
> point-in-time status snapshot: what exists, what has been verified, what is
> left, and the precise next step. Update it at the end of every session.

**Last updated:** 2026-11-09 (recovery → slim-down → installer → **rebranded to "Shutio"**
              → **shortcut display + dynamic data path + offline updater**
              → **v1.1.0: reliable shortcuts, single-display recording, mic audio, modern cards**)
**Build status:** ✅ Compiles cleanly (0 warnings, 0 errors)
**Self-test:** ✅ 13/13 PASS, exit code 0 (in-place, from an installed copy, and via the updater)
**Deployed app:** ✅ Launches and renders the full UI on the kiosk
**Folder size:** ✅ 2,456 MB → **288 MB** (build tooling moved to sibling)
**Installer:** ✅ TRUE OFFLINE self-extracting **`Shutio-setup.exe`** (~205 MB,
              whole app inside) + **`UninstallShutio.exe`** (~47 MB) in
              **`I:\Apps\Shutio-Share\`** — tested install→13/13→uninstall end-to-end
**Updater:** ✅ **`Shutio-Updater.exe`** (~158 MB, whole app inside) in
              **`I:\Apps\Shutio-Share\`** — repairs an installed copy IN PLACE,
              keeps `Data\`, then self-tests 13/13; verified end-to-end
**Icon:** ✅ Real brand icon/logo from the user's `I:\shutio.png` — emblem in
              `Assets/logo.png` + `Assets/app.ico` (exe, tray, shortcuts, in-app UI);
              app + installer + updater + installed copy all refreshed
**Distribution:** ✅ **Single-file.** The whole .NET + WPF runtime is folded into one
              `Shutio.exe` (73 MB). Install folder went from ~256 files → 8
              (`Shutio.exe`, `UninstallShutio.exe`, `Assets\`, `Data\`, `Tools\`, + small
              docs). Baked into `SnippingTool.csproj`; installer/updater payloads inherit it.
              Verified self-contained (self-test 13/13 with no loose DLLs) + WPF GUI launches.
**Overall state:** 🟢 **Feature-complete, verified, slimmed, installable, updatable, correctly branded.**

## 0. Session highlights (this session, in order)

1. Recovered and verified the prior model's complete implementation (13/13 self-test,
   live GUI, clean build). Created AGENTS/HANDOFF/TODO/CHANGELOG.
2. **Slimmed the folder:** moved all build-only tooling (SDK 770 MB, NuGet cache
   380 MB, full ffmpeg + zips, `bin`/`obj`) out of the app folder — nothing
   deleted. Main folder is now 288 MB and still passes 13/13.
3. **Made the build tooling shared:** relocated it to the project-agnostic
   `I:\DevTools\` (reusable by any .NET project on this machine), added the SDK to
   the user PATH, and pointed `NUGET_PACKAGES` / `DOTNET_CLI_HOME` there. Verified
   the project still builds against the shared SDK (0 errors).
4. **Added a simple installer** (`Install\`): native folder picker, copies the lean
   app, carries over existing snips/settings, creates Start Menu + Desktop shortcuts
   + an Apps & features entry, and a self-service uninstall. Installed to a test
   path → 13/13 → launched → clean shutdown; uninstall removed everything.
5. **Built the TRUE OFFLINE installer + standalone uninstaller:**
   `I:\Apps\Shutio-Share\Shutio-setup.exe` (~205 MB, the whole app is
   embedded inside — share this one file, no internet/admin/PowerShell needed) and
   `UninstallShutio.exe` (~47 MB). Verified the full share→install→run→uninstall
   cycle from an isolated copy: install → 13/13 → deployed uninstaller → clean removal.

6. **Rebranded the app to "Shutio":** the user's chosen name (verified free) + the
    user's logo (shutter + play + viewfinder + red dot). Renamed the *user-facing*
    name, `SnippingTool.exe` → **`Shutio.exe`**, installers → **`Shutio-setup.exe`** /
    **`UninstallShutio.exe`**, share folder → **`I:\Apps\Shutio-Share\`**, and the
    Start Menu / Desktop / Apps-&-features names. Generated a new multi-size `app.ico`
    from the logo. **Kept** the folder path `I:\Apps\SnippingTool`, the C# namespace
    `SnippingTool`, the source folder `Source\SnippingTool\`, and the csproj file name
    for stability. Rebuilt the app (13/13) and both installers; verified the full
    isolated install→13/13→uninstall cycle again. See CHANGELOG for the rebrand entry.

7. **Shortcut display, dynamic data path, and the offline updater:**
   - **Settings shortcuts:** each row is now **label + a compact chip showing the
     actual key combination** (e.g. `Ctrl + Shift + S`) + a **Reset** link. The value
     is normalised so it always reads `Ctrl + Shift + S` (fixing the inconsistent
     `Ctrl+Shift+S` from the defaults table), and recording a new combo shows a live
     "Recorded … — press Save changes" confirmation right there.
   - **Main window footer:** the hard-coded `I:\Apps\SnippingTool\Data` was replaced
     with the **real install location** (`AppPaths.Data`), so it matches wherever the
     user put the app.
   - **`Shutio-Updater.exe`** (~158 MB) built in `I:\Apps\Shutio-Share\` — it repairs
     an already-installed copy in place, keeps `Data\`, and self-tests 13/13. Verified:
     broken install → updater → files restored → Data preserved → 13/13.

8. **Real brand icon / logo:** the app had been using a generic blue placeholder,
   not the user's artwork. Generated a square **emblem** from the user's
   `I:\shutio.png` (shutter + viewfinder + red dot + play) and set it as
   `Assets/logo.png` (in-app UI + window title-bar) and `Assets/app.ico` (multi-size
   16–256 for the exe / tray / Desktop + Start-Menu shortcuts). Rebuilt the app, the
   **installer**, the **updater** (so their payloads carry the new icon), and pushed
   it to the installed copy `I:\Shutio`. Refreshed the shortcut icon cache. Verified
   the emblem is embedded in `Shutio.exe`, and self-test stayed **13/13**.
   Tooling kept for reproducibility: `Tools\MakeLogo.ps1`, `Install\src\make-ico\`.

9. **v1.1.0 feature release:** fixed the Settings shortcut rows (the chip is now a
   `TextBlock`-based `ContentControl`, so the combos actually display and can be
   edited reliably), added **Single display** recording for parity with screenshots,
   added **optional microphone audio** (toggle + mic picker, AAC 160 kb/s muxed into
   the MP4, mics enumerated by asking ffmpeg to list its dshow devices), and gave the
   **mode cards** rounder corners + a soft drop-shadow so they "pop out". Bumped the
   version 1.0.0 → 1.1.0 across the app, installer and updater. Verified: self-test
   13/13 and a combined video+audio MP4 (h264 + aac 44100/160k).

## 1. What is implemented (and verified this session)

| Area | Status | Evidence |
| --- | --- | --- |
| Rectangle / Freeform / Window / Full-desktop / Display capture | ✅ | Self-test (desktop, region, freeform alpha, window detection) + live UI |
| Capture delay (0/3/5/10 s) with countdown | ✅ | `CountdownWindow`, UI delay selector |
| Editor (pen/highlighter/eraser/line/crop/undo/redo/rotate/resize/zoom) | ✅ | Source clean; documented kiosk verification |
| Save PNG/JPG/BMP + copy to clipboard | ✅ | Self-test round-trip + clipboard test |
| Windows OCR + copy text + no-text handling | ✅ | Self-test (recognized "Snipping Tool 12345") |
| MP4 recording (ffmpeg) + cursor overlay + indicator | ✅ | Self-test MP4 test with `IncludeCursor = true`, **no cursor errors** |
| History with thumbnails + search + filters | ✅ | Live UI shows recent snips; self-test persistence |
| Settings (JSON) + theme + hotkeys + conflict detection | ✅ | Self-test settings validation; live UI |
| Tray icon + floating toolbar + toasts + single instance | ✅ | Live UI (floating toolbar visible); source |
| No admin / portable / everything under `Data\` | ✅ | Self-contained publish; data layout |

## 2. What has been tested (this session, on the kiosk)

1. **Compile** — `dotnet build -c Debug` → 0 warnings, 0 errors.
2. **Self-test** — `Shutio.exe --selftest` → **13/13 PASS, exit 0.**
3. **Live GUI launch** — app started, main window rendered all controls;
   log: `App started. Main visible: True; hotkeys: Shortcuts ready; ffmpeg: True.`;
   all 7 hotkeys registered; clean shutdown (process stopped, no orphans).
4. **P/Invoke sanity** — `GetProcAddress` confirms `GetIconInfo`/`DrawIconEx`
   are in **user32.dll** (matching the current `NativeMethods.cs`).

Not exercised here (GUI-only, require real mouse input): interactive drag
selections, editor painting gestures, recording a multi-window scenario. These
were reported as verified on the kiosk by the prior model and are covered by the
headless self-test where a non-interactive path exists.

## 3. Known bugs / limitations (all documented, none blocking)

- **Global hotkeys** can be refused by the kiosk shell (`RegisterHotKey`);
  fallbacks: floating toolbar + tray menu. Intended behavior.
- **Protected/DRM windows** fall back to a frozen-desktop crop for window snip.
- **Freeform transparency** is PNG-only; JPG/BMP flatten the outside area.
- **Recordings** need `Tools\ffmpeg.exe`. Microphone audio is optional (off by
  default, AAC 160 kb/s muxed); system-audio (loopback) capture is not implemented.
- **OCR** depends on the installed Windows OCR language pack.
- **Mixed-DPI** extreme layouts not lab-tested beyond the 150% kiosk display.

## 4. Current blockers

- **None.** The app builds, self-tests green, launches, and shuts down cleanly.

## 5. Exact next step(s)

The project is delivered (app + installer + **updater**). Outstanding items, in
priority order:

1. **Note (shared tooling)** — the build tooling now lives in the SHARED
   `I:\DevTools\` (~2.1 GB) and is reused by other .NET projects (the SDK is on the
   user PATH; `NUGET_PACKAGES` / `DOTNET_CLI_HOME` point there). Keep it — do not
   delete it. This project's own build artifacts are under
   `I:\DevTools\project-caches\snippingtool\` (regenerable).
2. **Optional** — Add a couple more headless self-test assertions for the editor
   math paths (crop/rotate/resize) to tighten the suite.
3. **Optional** — Add a "re-run last capture" convenience action.
4. **Optional** — Record a short demo MP4/GIF of the editor + capture for the
   README (needs real mouse input, so it is a manual step).

No action is required to ship: the current `I:\Apps\SnippingTool` folder is a
complete, runnable, self-contained product, and `Install\` ships it to any path.

## 6. How to re-verify quickly (copy-paste)

```powershell
cd 'I:\Apps\SnippingTool'
$dev = 'I:\DevTools'
$env:DOTNET_CLI_HOME = "$dev\dotnet-home"
$env:NUGET_PACKAGES  = "$dev\nuget"
& "$dev\dotnet\dotnet.exe" build 'Source\SnippingTool\SnippingTool.csproj' -c Debug -v q -nologo
& 'Shutio.exe' --selftest
# then read: Data\Tests\selftest-report.txt  (expect "Summary: 13 passed, 0 failed")
```
