# CHANGELOG

Chronological record of implementation changes, fixes, and architectural
decisions for Shutio (the SalsaNOW capture tool). Most recent first.

Format: `## [date]` sections with **Added / Changed / Fixed / Verified / Docs**.

---

## 2026-11-12 — 2.1.1 "Shutio 2.1.1" — false "Recording failed" fix

A quick follow-up to 2.1.0, fixing a false failure report seen on a real webcam PC.

**Fixed**
- **A good recording was reported as "Recording failed."** On a USB webcam the stop
  dialog quoted ffmpeg's dshow warning — `real-time buffer [USB2.0 HD UVC WebCam]
  (video input) too full or near too full (101% of size: 3041280 [rtbufsize
  parameter]). frame dropped!` — even though the encoder had exited 0 and the MP4 had
  been written. Root cause: the 2.1.0 error-attribution step scanned ffmpeg's stderr
  for a message to attach to a failed stop, and its fallback picked up **the last
  stderr line** — chatty health chatter included. Now:
  - stderr is only treated as an error source when the recording actually failed
    (missing file, non-zero exit, or a forced kill); on success it is logged but
    never fails the result;
  - device status chatter (dshow "real-time buffer" / "frame dropped" lines) is
    explicitly excluded from error candidates;
  - if no fatal line exists the app keeps its own accurate message instead of
    quoting a warning.
  The video produced by such a 2.1.0 run is normally intact and playable — the
  failure was a false report.

**Changed**
- **In-app updates now install from `Shutio-setup.exe`.** Releases ship the offline
  installer only (lean releases) — the update flow downloads it and runs it in place
  (`<install> --skip-prompts --launch`); `Data\` is not part of the installer payload
  and is preserved. Older releases' `Shutio-Updater.exe` is still preferred when
  present.
- Version 2.1.1 across the app, installer, updater, and uninstaller.

**Verified**
- Self-test **13/13, exit 0** on the rebuilt single-file app and the installed copy.

---

## 2026-11-12 — 2.1.0 "Shutio 2.1" — webcam fixes, live preview, in-app updates

A release focused on **making webcam recording actually work**: three reported
webcam bugs fixed (stop-fail, no-camera fail, camera left open after the app is
killed), a **live picture-in-picture preview** on your real screen while recording,
and **in-app update checking**. Self-test stays **13/13, exit 0**; offline; no new
paid dependencies.

**Fixed**
- **Recording with the webcam on → "Stop" fails with "recording failed".** The
  webcam is a live, never-ending input. With the default `overlay` behaviour
  (`eof_action=repeat`) the composite kept living after the finite screen pipe
  ended, so "Stop" hung until the timeout killed ffmpeg mid-flush (corrupt MP4,
  false failure). The filtergraph now uses **`overlay=…:eof_action=endall`** —
  the overlay ends with the screen and ffmpeg finalizes and exits on its own.
  Proven by A/B: the old graph hung and was killed at 20 s (107 MB written, the
  reported bug); the new graph exited by itself in ~3 s with a valid MP4
  (Duration exactly 00:00:03.00, clean decode).
- **Webcam on with no camera present → start "works" then stop fails with
  "The pipe is being closed".** `RecordingController.Start` now validates the
  webcam (and microphone) device **before** ffmpeg starts — a clear, actionable
  error at start instead of a delayed engine crash — and the UI never hands the
  "No webcam found" placeholder to the engine.
- **The webcam stayed grabbed after the app was closed or killed.** New
  `Core\ProcessGuard` ties the encoder to the app's lifetime with two layers:
  (1) a Windows job object with `KILL_ON_JOB_CLOSE` (the OS kills the encoder
  when the app's job handle closes); (2) a **guardian helper** — a hidden copy
  of `Shutio.exe` run in `--guardian <appPid> <encoderPid>` mode that blocks on
  the app's process object and kills the encoder the moment it is destroyed.
  Layer 2 only uses the most basic Win32 calls (open/wait/terminate), so it
  also covers locked-down machines where job-object configuration is policy-
  blocked (verified on this kiosk, where `SetInformationJobObject` is refused
  and the job layer is a no-op). Proven: hard-killing the "app" harness killed
  the stand-in encoder within seconds, zero orphans left.
- **Error attribution.** When the encoder exits early, the stop result now says
  "The recording engine exited before the recording finished." plus the useful
  line extracted from ffmpeg's stderr, instead of the generic pipe message.

**Added**
- **Live webcam preview on your actual screen while recording.** A small always-
  on-top window (`Recording\WebcamPreviewWindow`) mirrors the PiP exactly as
  composited into the MP4 — same scale, same mirror, same position — by cropping
  the composited `[vout]` stream (`[vout]split → crop → scale 320×180`, raw BGRA
  frames on stdout, drained by a reader thread). Because it derives from the
  (finite) composited output, it ends with the recording and never keeps the
  encoder alive. No second camera grab — one device, one stream.
- **In-app updates.** Settings → **Update** card with a **Check for updates**
  button (also in the tray menu), an optional **startup prompt** (Update /
  Later), and a **GitHub token** field for private repos. Checks the latest
  release of `EvilLighty/Shutio` (version compare), downloads the release's
  `Shutio-Updater.exe` into `%LocalAppData%\Shutio\Updates`, runs it
  (`<install> --silent --launch`), then exits. "Later" remembers the dismissed
  version so the prompt doesn't nag.
- **Version 2.1.0** everywhere: app (file/assembly 2.1.0.0), installer,
  uninstaller, updater.

**Verified**
- A/B synthetic cam-graph proof (finite screen feed + infinite cam feed, the
  exact filtergraph shape): new design (endall + `[vout]`-derived preview)
  exits by itself after the 3 s input ends — valid 00:00:03.00 MP4 plus exactly
  45 preview frames (10,368,000 B); the old 2.0.0 graph hung until killed at
  20 s (107 MB written) — the reported stop-fail bug, reproduced.
- Guardian hard-kill test (shipped `ProcessGuard.cs` compiled into a harness):
  harness + infinite stand-in encoder protected → harness hard-killed →
  **encoder dead, zero guardian orphans** (the webcam-release guarantee).
- Self-test **13/13, exit 0** on the rebuild and on the installed copy; GUI
  smoke OK; single-file app 73.3 MB.
- Installed copy `I:\Shutio` refreshed in place to **2.1.0.0** via
  `Shutio-Updater.exe` (Data\ preserved, self-test 13/13).

**Release artifacts**
- `I:\Apps\SnippingTool\Shutio.exe` — deployed app, **2.1.0.0**, self-test 13/13.
- `I:\Apps\Shutio-Share\Shutio-setup.exe` — true-offline installer (2.1.0 payload).
- `I:\Apps\Shutio-Share\Shutio-Updater.exe` — offline in-place updater (2.1.0).
- `I:\Apps\Shutio-Share\UninstallShutio.exe` — standalone uninstaller.

---

## 2026-11-10 — 2.0.0 "Shutio 2.0" — MAJOR RELEASE

Major release (from **1.1.1**) bundling a full generation jump: the redesigned
**single-window** UI (frosted glass · 3 tabs), the **inline screenshot editor**,
and the all-new **Shutio Cut** timeline video editor (≤6 tracks, split/cut/snap,
**MP4 export**) with a **smooth real-time preview**. All offline, on the bundled
ffmpeg, no new paid dependencies.

**Added**
- **Version 2.0.0** everywhere: `Shutio.exe` (file/assembly 2.0.0.0), installer,
  uninstaller, and updater all report 2.0.0 and stamp the Apps & features entry
  (was 1.0.1/1.1.1).
- **Smooth preview** (Phase C follow-up): playback no longer re-seeks ffmpeg per
  frame. One continuous `ffmpeg -f image2pipe` process (`Editing\PreviewDecoder.cs`)
  streams JPEG frames into a bounded, time-indexed buffer on a background thread;
  a 30 fps UI timer pulls the frame for the current playhead. Decouples fast
  decode from paced display → smooth playback; scrubbing still resolves frames
  from the buffer via `FrameAt`. Verified headless (`--previewtest`): frames at
  every sampled time, exit 0.

**Fixed**
- **Maximize flicker/loop (frosted glass).** Refreshing the glass backdrop
  briefly hides the window to sample the desktop behind it, then shows it again;
  WPF's `Show()` after `Hide()` drops a maximized window, and the state change
  that raise rescheduled the very next refresh — so a maximized window kept
  flashing hide/show forever. Refreshes triggered by the app's own Hide/Show
  are now suppressed (flag held until the dispatcher drains the events our own
  re-show raises), and the maximized state is explicitly restored afterwards.
  Verified by A/B: the pre-fix build visibly flickered on maximize (visibility
  toggling every ~500 ms); the fixed build holds MAXIMIZED with zero state
  transitions over 8 s.

**Release artifacts**
- `I:\Apps\SnippingTool\Shutio.exe` — deployed app, **2.0.0.0**, self-test 13/13.
- `I:\Apps\Shutio-Share\Shutio-setup.exe` — true-offline installer (whole app inside).
- `I:\Apps\Shutio-Share\Shutio-Updater.exe` — offline in-place updater (payload = 2.0.0).
- `I:\Apps\Shutio-Share\UninstallShutio.exe` — standalone uninstaller.
- Installed copy `I:\Shutio` refreshed to 2.0.0 in place (Data\ preserved).

**Contents** (details in the entries below)
- Single window · frosted glass (tunable blur/transparency) · 3 tabs
  (Screenshot · Video editing · Recording) · inline editor, no pop-ups.
- Inline screenshot editor: pen, shapes, text, resize/rotate, undo/redo, OCR.
- **Shutio Cut**: open media, up to **6 tracks** (Full frame + PiP video + mixed
  audio), split/cut, drag/trim/snap, **Export MP4** (H.264 + AAC, faststart).

---

## 2026-11-10 — Phase C: Shutio Cut timeline editor (≤6 tracks · MP4 export)

The **Video editing tab** is now a working Premiere‑lite timeline editor, **Shutio Cut**:
open a recording or audio file, put clips on up to **six tracks** (6 is the hard cap),
split/cut them, drag/trim with snapping, and **export an MP4** — all inline (no pop‑up)
and on the bundled ffmpeg, offline, no new paid dependencies. Self‑test stays **13/13, exit 0**.

**Added**
- **`CutPane`** (UserControl, `Editing\`) — the inline editor on the Video editing tab:
  a **single‑line toolbar** (Open · Add clip · Split · Cut · Snap toggle · +Video · +Audio ·
  timeline zoom −/100%/+ · a **`n/6 tracks`** counter · **Export MP4**), a **preview** with
  **transport** (Play/Pause · time · scrub slider · engine status), and a **timeline** with a
  ruler, a red **playhead**, and one row per track.
- **6‑track model** (`Editing\TimelineModel.cs`) — `TimelineProject` / `TimelineTrack` /
  `TimelineClip` with `TimelineConstants.MaxTracks = 6` (min 1). Track 0 is **Full frame**;
  later video tracks are **picture‑in‑picture** (Top/Bottom‑Left/Right); audio tracks are mixed.
  `+Video` / `+Audio` add tracks up to the cap, tracks can be removed, muted (audio) or hidden
  (video). The toolbar disables the add buttons and reports "Max 6 tracks reached" at the cap.
- **ffmpeg engine** (`Editing\`) — `FfmpegRunner` (run + probe for duration/dims/fps/streams),
  `FramePreview` (decode one frame at a time for the preview), and `TimelineExport` (builds the
  composite filtergraph: base canvas + per‑clip `trim`+`setpts`+`scale`, chained `overlay` with
  `enable='between(t,…)'`, and per‑clip `atrim`+`asetpts`+`adelay`+`amix=normalize=0`, then
  H.264/AAC MP4). `AppPaths.Exports` added for output.
- **Interactive timeline** — clip drag (move), edge **trim** (left/right), **select**, and
  **snap** to the playhead and neighbouring clip edges; ruler/track‑lane clicks move the playhead.
  **Split** divides the selected clip at the playhead (Ctrl+K); **Cut** removes the selection
  (Del/Backspace); **Space** plays/pauses the preview.
- **`--cutexport <input> [--out <path>]`** headless mode (and `--video` to open straight on the
  editor tab) for no‑GUI verification of the engine, mirroring the UI's export path.

**Fixed**
- **Export produced no file — `No such filter: ''`.** The audio chain's output **label**
  (`[a0]`) was comma‑joined in with the filters, leaving an empty filter after `aformat`.
  The label is now appended to the chain rather than added as a filter part. Caught by the
  headless export; the shipped app now exports a valid MP4.
- **CutPane crashed on the Video tab.** (a) the **Snap** ToggleButton used a `Button`‑typed
  style (`GhostButton`) — now a ToggleButton‑typed `SnapToggle` style with a checked state;
  (b) the same `RowDefinition` objects were added to both the gutter and lane grids (a
  RowDefinition belongs to one collection) — now built as two independent row sets.

**Verified**
- Self‑test **13/13, exit 0** after the build; single‑file publish **73.3 MB**, WPF smoke OK.
- **Headless engine, both Debug and the shipped single‑file app:** a real 1.6 s, 2560×1440
  H.264 + AAC recording on V1 (Full) + V2 (PiP) + A1 → **valid MP4 (≈2.6 MB, video + audio)**,
  exit 0. (The filtergraph was also proven in the shell before porting to C#.)
- **Live UI:** launched straight to the Video tab → the Shutio Cut editor renders with the
  single‑line toolbar, preview/transport ("Engine ready · ffmpeg.exe"), the timeline ruler,
  playhead, and the default **V1 Full** / **A1** tracks with mute/delete controls, plus the
  **2/6 tracks** counter.

**Docs**
- HANDOFF: Phase C marked done; editor row + next steps updated.

---

## 2026-11-10 — Redesigned as one window (glass · 3 tabs · inline editor)

A structural redesign into **a single window with three top-right tabs**
(Screenshot · Video editing · Recording), a **frosted-glass** surface, and an
**inline screenshot editor** — no pop-up windows. Still portable, offline, and on
the existing ffmpeg engine; no new paid dependencies. Self-test stays **13/13, exit 0**.

**Added**
- **Frosted-glass surface.** The main window samples the desktop behind it and renders
  a blurred, tinted backdrop (`GlassBackdrop`), with **tunable blur (0–4)** and
  **transparency (0–90%)** sliders in a new **Frosted glass** section of Settings, a
  **live preview** while dragging, and an **Enabled** toggle to switch it off entirely
  (reversible solid surface). Settings: `TranslucentWindow` / `GlassBlur` / `GlassOpacity`.
- **Settings gear in the title bar.** The old bottom "Settings" text button is gone;
  Settings now opens from a gear (⚙) icon in the title bar, between the Recording tab
  and the theme button.
- **Inline Recording tab.** The recording setup is no longer a pop-up — it is a tab in
  the main window: source (Full desktop / Single display / Window / Region), frame rate
  (15/30/60), resolution (Native/1080p/720p/540p), microphone audio (+ "save audio
  separately" → video-only MP4 + lossless .wav), cursor toggle, and a **webcam
  picture-in-picture** block with an always-visible **Choose webcam** picker, S/M/L size,
  and mirror. A PiP box can be dragged to position the camera overlay.
- **Inline screenshot editor (replaces the pop-up `EditorWindow`).** The Screenshot tab
  now hosts the editor in place: a **single-line toolbar** (New snip · Pen · Highlighter ·
  Eraser · Line · Crop · color/thickness · rotate L/R · resize · undo/redo · zoom −/%/+/fit ·
  OCR · Copy · Save) over the shared `ImageCanvas`, plus a status line (file · dimensions ·
  zoom · status). Capturing — or choosing a snip from history — now loads it into the tab
  instead of opening a window.

**Changed**
- `AppHost.OpenEditor` now routes to `MainWindow.LoadInlineEditor` (the inline pane)
  rather than constructing an `EditorWindow`; `OpenLatestEditor` prefers the inline
  editor when it already has an image; the capture toast's action now focuses the main
  window. The legacy `EditorWindow` still compiles but is no longer opened in the flow.
- New `Editor\EditorPane` (UserControl) re-hosts the editor logic (tools, palette,
  thickness, zoom, rotate/resize, undo/redo, save/copy, OCR, keyboard shortcuts) on the
  shared `ImageCanvas`.

**Verified**
- Self-test **13/13, exit 0** after each increment; single-file publish **73.3 MB**, WPF
  GUI smoke test alive.
- Live: 3-tab shell + gear Settings + glass sliders render; Recording tab with the
  "Choose webcam" picker renders (correctly "No webcam found" on a kiosk with no camera).
- Inline editor: captured a full-desktop snip → the Screenshot tab switched to the editor
  with the single-line toolbar + canvas (2560×1440 at 36%); drew a pen stroke (rendered),
  the status flipped to "Edited" with the dirty dot, and **Ctrl+Z** removed the stroke —
  confirming canvas input, dirty tracking, and undo all work inline.

**Kept for Phase C**
- **Video editing tab** is a placeholder ("Shutio Cut" — timeline editor with trim/cut,
  V1 + A1/A2/A3 tracks, export MP4) to be built next on the ffmpeg engine.

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
