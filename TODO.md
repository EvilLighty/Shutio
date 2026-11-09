# TODO.md - Shutio

Prioritized task list, completed work, and known bugs. Keep the most urgent
open items at the top. Move items between sections as they change state.

---

## Open

- [ ] **Note — shared build tooling:** the build tooling now lives in the SHARED
      `I:\DevTools\` (reused by other .NET projects; the SDK is on the user PATH and
      `NUGET_PACKAGES` / `DOTNET_CLI_HOME` point there). Keep it — do **not** delete
      it. This project's own artifacts: `I:\DevTools\project-caches\snippingtool\`.
- [ ] **Editor self-test hardening (Low)** — Add headless assertions for the
      editor transform math (crop / rotate / resize) in `SelfTest.cs`, so the
      editor is covered by the non-interactive suite, not only live kiosk runs.
- [ ] **Demo capture (Low)** — Record a short demo (MP4/GIF) of a capture +
      editor session for the README. Manual (needs real mouse input).
- [ ] **Convenience (Low)** — "Repeat last capture" tray/toolbar action.
- [ ] **Docs (Low)** — Add the four continuity files to the README's "Data
      layout" tree so a human reader sees them too.

## Completed

- [x] **v1.1.0 feature release (2026-11-09):**
  - **Reliable shortcut rows** — the Settings hotkey chip was rewritten as a
    `TextBlock`-based `ContentControl` (the old `TextBox` chip rendered pre-set
    combos blank). Click to record, Escape to cancel; all seven combos now show
    and edit. Public API (`Gesture`/`GestureChanged`/`IsValid`/`Display()`) kept.
  - **Single-display recording** — new "Single display" (monitor) source in the
    recording setup, for parity with screenshots.
  - **Microphone audio** — a "Record microphone audio" toggle + mic picker in both
    the recording setup and Settings; AAC 160 kb/s muxed into the MP4. Mic list
    from `AudioDevices.Enumerate()` (asks the same ffmpeg to list dshow devices).
    New themed `ModernComboBox`. Off by default.
  - **Modern mode cards** — rounder corners (12→16) + a soft drop-shadow so the
    cards pop; removed the crashing hover-shadow `TargetName` triggers.
  - **Version** 1.0.0 → 1.1.0 (csproj, app.manifest, installer, updater).
  - Verified: self-test 13/13; combined video+audio MP4 (h264 + aac 44100/160k).
- [x] **Real icon / logo (2026-11-09):** replaced the generic blue placeholder with
       the user's brand from `I:\shutio.png`. Cropped a square **emblem** (shutter +
       viewfinder + red dot + play) → `Assets/logo.png` (in-app UI + title-bar) and
       rebuilt `Assets/app.ico` (multi-size 16–256) for the exe / tray / shortcuts.
       Rebuilt app + installer + updater (payloads carry it) and updated the
       installed copy `I:\Shutio`; refreshed Desktop / Start-Menu shortcut icons.
       Tooling: `Tools\MakeLogo.ps1`, `Install\src\make-ico\`.
 - [x] **Single-file distribution (2026-11-09):** the install folder went from ~256
        files to 8 by folding the whole .NET + WPF runtime into one `Shutio.exe`
        (73 MB, compressed — smaller than the 249 loose DLLs it replaced). Baked
        `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` +
        `EnableCompressionInSingleFile` into `SnippingTool.csproj`; installer/updater
        payloads inherit it. `I:\Shutio` now has 0 loose DLLs; verified self-contained
        (self-test 13/13 with the loose DLLs removed) + WPF GUI launches.
        Tooling: `Tools\RebuildAppSingleFile.ps1`.
- [x] **Offline updater (2026-11-09):** `I:\Apps\Shutio-Share\Shutio-Updater.exe`
       (~158 MB, single self-contained file) repairs an already-installed copy in
       place — finds the install, closes a running instance, overwrites app files,
       **keeps the `Data\` folder**, then runs the self-test (13/13). Build source +
       `rebuild.ps1` in `Install\src\offline-updater\`. Verified end-to-end (broken
       install → restored → Data preserved → 13/13).
- [x] **Shortcut display (2026-11-09):** Settings shortcut rows now show a compact
       chip with the actual key combination next to each action label + a Reset link;
       `HotkeyBox.Display()` normalises the combo (`Ctrl + Shift + S`); recording
       gives a live "Recorded … — press Save changes" confirmation.
- [x] **Dynamic data path (2026-11-09):** main-window footer no longer hard-codes
       `I:\Apps\SnippingTool\Data`; it now reflects the real install location
       (`AppPaths.Data`).
- [x] **Folder slim-down (2026-11-09):** 2,456 MB → 288 MB. Build tooling (SDK,
      NuGet cache, full ffmpeg + zips, `bin`/`obj`) moved out of the app folder.
      App still passes 13/13 from the main folder.
- [x] **Shared build tooling (2026-11-09):** relocated that tooling to the
      project-agnostic `I:\DevTools\` (SDK on the user PATH; `NUGET_PACKAGES` /
      `DOTNET_CLI_HOME` point there). Re-verified the project builds against the
      shared SDK (0 errors), so it is reusable by other .NET projects.
- [x] **TRUE OFFLINE installer + uninstaller (2026-11-09):**
      `I:\Apps\Shutio-Share\Shutio-setup.exe` (~205 MB, self-extracting,
      whole app embedded — share this one file, no internet/admin/PowerShell) and
      `UninstallShutio.exe` (~47 MB, `--silent`/`--yes`/`--remove-data`).
      Verified the full share→install→run→uninstall cycle from an isolated copy.
      Build source + `rebuild.ps1` in `Install\src\offline-installer\`.
- [x] **Rebrand to "Shutio" (2026-11-09):** renamed the user-facing name +
      `SnippingTool.exe` → `Shutio.exe`, installers → `Shutio-setup.exe` /
      `UninstallShutio.exe`, share folder → `I:\Apps\Shutio-Share\`, and the
      Start Menu / Desktop / Apps-&-features names; new multi-size `app.ico` from the
      user's logo. Kept folder path, C# namespace, source folder, csproj name. Rebuilt
      app (13/13) + both installers; re-verified the isolated install→13/13→uninstall
      cycle. Desktop installer → `Shutio-setup.exe`.
- [x] **Simple installer (2026-11-09):** `Install\Install Shutio.cmd` opens a
      native "choose a folder" dialog; copies the lean app, carries over existing
      snips/settings, creates Start Menu + Desktop shortcuts + an Apps & features
      entry, and a self-service uninstall. Verified: install → 13/13 → launch →
      clean shutdown; uninstall removed everything.
- [x] **Docs (2026-11-09):** README install section; AGENTS/HANDOFF/TODO/CHANGELOG
      updated for the new layout + build-tooling location.
- [x] Full capture suite (rectangle, freeform, window, full desktop, display)
      with delay countdown and app-window hide/restore.
- [x] Editor: pen, highlighter, eraser, line/ruler, crop, undo/redo, rotate,
      resize (aspect lock + presets), zoom/pan, palette + custom color, 6
      thicknesses, PNG/JPG/BMP save, clipboard copy.
- [x] Windows OCR panel with copy + no-text handling.
- [x] MP4 recording via bundled ffmpeg (H.264) + setup dialog + indicator +
      optional cursor overlay.
- [x] History with thumbnails, search, and mode filters.
- [x] Settings (JSON), theme (light/dark/system), 7 global hotkeys with
      conflict detection, tray icon, floating toolbar, toasts, single instance.
- [x] Portable .NET SDK + ffmpeg bundled under `Tools\` for offline rebuilds.
- [x] Headless self-test suite (`--selftest`), 13 checks.
- [x] **Bug fix:** cursor overlay P/Invoke DLL (`gdi32.dll` → `user32.dll`) +
      one-shot error logging (was flooding the log ~30×/frame).
- [x] **Recovery (2026-11-09):** recovered prior work, verified clean build,
      13/13 self-test, live GUI launch + clean shutdown.
- [x] **Docs (2026-11-09):** created AGENTS.md, HANDOFF.md, TODO.md, CHANGELOG.md.

## Known bugs (all documented; none block shipping)

- Global hotkeys may be blocked by the kiosk shell — use the floating toolbar
  or tray menu (by design; see README "Known limitations").
- Window snip of protected/DRM windows falls back to a frozen-desktop crop.
- Freeform outside-area transparency is preserved only in PNG (JPG/BMP flatten).
- Recordings require `Tools\ffmpeg.exe`; microphone audio is optional (off by
  default). System-audio (loopback) capture is not implemented.
- OCR depends on the installed Windows OCR language pack (`OcrLanguage` in
  `Data\settings.json`).

## Parking lot (explicitly out of scope — do not start without a request)

- System-audio (loopback) recording, cloud upload/sharing, accounts.
- Built-in video trimming, scrolling-window capture stitching.
- Scheduled/interval capture, direct printing.
