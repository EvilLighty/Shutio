# HANDOFF.md - Current State of Shutio

> Read **AGENTS.md** first (rules + exact commands). This file is the
> point-in-time status snapshot: what exists, what has been verified, what is
> left, and the precise next step. Update it at the end of every session.

**Last updated:** 2026-11-10 (recovery → slim-down → installer → **rebranded to "Shutio"**
              → **shortcut display + dynamic data path + offline updater**
              → **v1.1.0** → **v1.1.1** → **redesign: single window · 3 tabs · frosted glass**
              → **inline screenshot editor (Phase B)** → **Shutio Cut timeline editor (Phase C)**
              → **v2.0.0 "Shutio 2.0" RELEASED (app + installer + updater + installed copy)**)
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
**Overall state:** 🟢 **v2.0.0 "Shutio 2.0" SHIPPED** — redesigned one window (glass · 3 tabs),
              inline screenshot editor, **Shutio Cut** timeline editor (≤6 tracks, MP4 export),
              **smooth real-time preview**. All at 2.0.0.0: deployed app, installed copy
              (`I:\Shutio`), offline installer/updater/uninstaller; self-test **13/13** everywhere.

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

 10. **v1.1.1:** fixed the "can't stop a recording with the mic on" bug (ffmpeg
     `-shortest`), accepted friendly key names (`Esc`) for shortcuts, added a
     **stop-recording** shortcut (default `Esc`, armed only while recording), and a
     **60 fps** option. Self-test 13/13, exit 0.

 11. **Redesign — single window, no pop-ups (Phase A + B), this session:**
     - **Three top-right tabs** (Screenshot · Video editing · Recording) in one window.
     - **Frosted-glass** surface: samples the desktop behind the window and renders a
       blurred, tinted backdrop; **Settings → Frosted glass** has an **Enabled** toggle,
       a **Blur (0–4)** and **Transparency (0–90%)** slider with a **live preview**;
       reversible to a solid surface. New `Core\GlassBackdrop` + `TranslucentWindow` /
       `GlassBlur` / `GlassOpacity` settings.
     - **Settings moved to a gear (⚙) icon** in the title bar (between Recording and the
       theme button); the old bottom "Settings" button is gone.
     - **Inline Recording tab** (replaces the pop-up setup): source, FPS 15/30/60,
       resolution Native/1080p/720p/540p, mic audio (+ "save audio separately" →
       video-only MP4 + lossless .wav), cursor, and a **webcam PiP** block with an
       always-visible **Choose webcam** picker, S/M/L size, mirror, and a draggable PiP
       preview.
     - **Inline screenshot editor (Phase B):** the Screenshot tab now hosts the editor
       in place — a **single-line toolbar** (New snip · Pen · Highlighter · Eraser · Line ·
       Crop · color/thickness · rotate L/R · resize · undo/redo · zoom −/%/+/fit · OCR ·
       Copy · Save) over the shared `ImageCanvas`, plus a status line. New
       `Editor\EditorPane` (UserControl) re-hosts the editor logic; `AppHost.OpenEditor`
       now routes here instead of opening `EditorWindow` (which still compiles but is
       retired from the flow). Verified live: capture → tab switches to editor, pen
       stroke renders, "Edited" dirty state, **Ctrl+Z** undoes.
     - **Video editing tab (Phase C — Shutio Cut):** now a working inline timeline editor
        (see item 12).

 12. **Phase C — Shutio Cut timeline editor (≤6 tracks, MP4 export), this session:**
      The Video editing tab is now a working Premiere-lite editor. A **single-line
      toolbar** (Open · Add clip · Split · Cut · Snap · +Video · +Audio · zoom · **n/6
      tracks** · Export MP4), a **preview** with transport (Play/Pause · time · scrub ·
      engine status), and a **draggable timeline** with a ruler, a red playhead, and up
      to **six tracks** (6 is the hard cap; track 0 = Full frame, later video tracks =
      picture-in-picture, audio tracks are mixed). New `Editing\` engine: `TimelineModel`
      (6-track cap), `FfmpegRunner` (run + probe), `FramePreview` (frame-at-a-time
      preview), `TimelineExport` (composite filtergraph → H.264/AAC MP4). Fixed two
      real bugs caught live: the audio **output label was comma-joined** into the filter
      chain (empty filter → "No such filter" → no file), and two **CutPane crashes**
      (a ToggleButton using a `Button`-typed style, and shared `RowDefinition` objects).
      Verified: self-test **13/13**; **headless `--cutexport`** on both Debug and the
      shipped single-file app produces a **valid 1.6 s 2560×1440 MP4 (video + audio),
      exit 0**; live UI renders the editor on the Video tab (V1 Full / A1, 2/6 counter,
      playhead, toolbar).

## 1. What is implemented (and verified this session)

| Area | Status | Evidence |
| --- | --- | --- |
| Rectangle / Freeform / Window / Full-desktop / Display capture | ✅ | Self-test (desktop, region, freeform alpha, window detection) + live UI |
| Capture delay (0/3/5/10 s) with countdown | ✅ | `CountdownWindow`, UI delay selector |
| Editor (pen/highlighter/eraser/line/crop/undo/redo/rotate/resize/zoom) — **inline on the Screenshot tab** | ✅ | `Editor\EditorPane` + shared `ImageCanvas`; live-verified (stroke, dirty, Ctrl+Z undo) |
| Save PNG/JPG/BMP + copy to clipboard | ✅ | Self-test round-trip + clipboard test |
| Windows OCR + copy text + no-text handling | ✅ | Self-test (recognized "Snipping Tool 12345") |
| MP4 recording (ffmpeg) + cursor overlay + indicator | ✅ | Self-test MP4 test with `IncludeCursor = true`, **no cursor errors** |
| History with thumbnails + search + filters | ✅ | Live UI shows recent snips; self-test persistence |
| Settings (JSON) + theme + hotkeys + conflict detection | ✅ | Self-test settings validation; live UI |
| Tray icon + floating toolbar + toasts + single instance | ✅ | Live UI (floating toolbar visible); source |
| No admin / portable / everything under `Data\` | ✅ | Self-contained publish; data layout |
| **Shutio Cut** timeline editor (≤6 tracks, split/cut/snap, MP4 export) | ✅ | `Editing\` engine + `CutPane`; headless `--cutexport` (valid MP4) + live UI on the Video tab |

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

The redesign is complete and **v2.0.0 "Shutio 2.0" is shipped**: Phases A (glass, 3 tabs,
recording, Settings), B (inline editor), and C (Shutio Cut timeline editor, ≤6 tracks,
MP4 export, smooth preview) are all done and verified (13/13). Version 2.0.0 is stamped in
the app (2.0.0.0), the installed copy (`I:\Shutio`, refreshed in place via the 2.0.0
updater — Data\ preserved, Apps & features shows 2.0.0), and the rebuilt offline
installer/updater/uninstaller in `I:\Apps\Shutio-Share\`. GitHub release notes are ready in
`I:\Apps\Shutio-Share\RELEASE-2.0.0.md`; the user uploads the executables to the GitHub
release (title "Shutio 2.0", tag v2.0.0).
Outstanding items, in priority order:

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
