# Feature status

Legend: **Yes** = implemented and verified on the kiosk. **Partial** = implemented
with a documented limitation. **No** = not available by design or blocked by the
environment.

## Capture

| Feature | Status | Notes |
| --- | --- | --- |
| Rectangular capture | Yes | Live size readout and pixel-accurate crop. |
| Freeform capture | Yes | Transparent outside the lasso in PNG. |
| Window capture | Yes | Preview on hover; PrintWindow content; desktop-crop fallback. |
| Full desktop capture | Yes | Single virtual-desktop image. |
| Individual monitor capture | Yes | Monitor under the pointer. |
| Multi-monitor / negative coordinates | Yes | Uses the Win32 virtual screen, including left/up monitors. |
| Mixed per-monitor DPI | Partial | DPI-aware capture; extreme mixed-DPI layouts not lab-tested. |
| Capture delay 0 / 3 / 5 / 10 s | Yes | Countdown overlay. |
| Mouse cursor in screenshots | Yes | Rendered from the current cursor bitmap. |
| Hide/restore app windows during capture | Yes | Avoids the app covering the target. |

## Editor

| Feature | Status |
| --- | --- |
| Pen | Yes |
| Highlighter (semi-transparent, wide) | Yes |
| Eraser (stroke eraser) | Yes |
| Straight line / ruler | Yes |
| Crop | Yes |
| Undo / redo | Yes |
| Rotate left / right | Yes |
| Resize with aspect lock and presets | Yes |
| Color palette + custom color | Yes |
| Thickness presets 2-22 px | Yes |
| Zoom in / out / fit / pan | Yes |
| Save as PNG / JPG / BMP | Yes |
| Copy image to clipboard | Yes |
| OCR text extraction and text copy | Yes |
| Flattened save of annotations | Yes |

## Output and storage

| Feature | Status |
| --- | --- |
| Auto-save with configurable folder | Yes |
| Auto-copy to clipboard | Yes |
| Auto-open editor (toggle) | Yes |
| Configurable file-name pattern | Yes, with live preview |
| History with thumbnails | Yes |
| Search and mode filters | Yes |
| Open / copy / delete / open folder from history | Yes |
| Everything stored under `Data\` | Yes |

## Shortcuts and integration

| Feature | Status | Notes |
| --- | --- | --- |
| Seven configurable global shortcuts | Yes | `RegisterHotKey`; works while hidden. |
| Conflict detection and inline validation | Yes | Duplicate combos block Save. |
| Hotkeys paused while editing in Settings | Yes | Prevents Windows swallowing the combo. |
| Windows-key shortcuts | No | Deliberately rejected. |
| Floating capture toolbar | Yes | Optional, draggable, always on top. |
| Tray icon and context menu | Yes | WinForms `NotifyIcon`. |
| Toast notifications | Yes | Optional. |
| Start minimized / minimize / close to tray | Yes | Optional. |
| Single instance | Yes | Second launch activates the first. |

## OCR

| Feature | Status | Notes |
| --- | --- | --- |
| Windows OCR engine | Yes | `Windows.Media.Ocr`. |
| Text extraction with line/character counts | Yes | 998-character sample verified. |
| Copy OCR text | Yes | Verified against the clipboard. |
| No-text handling | Yes | Message shown and copy disabled. |
| Language selection | Partial | Uses installed Windows OCR language packs via `OcrLanguage`. |

## Screen recording

| Feature | Status | Notes |
| --- | --- | --- |
| Full desktop recording | Yes | Verified 2560x1440 H.264 MP4. |
| Single-display recording | Yes | Primary monitor, added in v1.1.0. |
| Window recording | Yes | Uses the selected window bounds. |
| Region recording | Yes | Drag a region in the setup dialog. |
| Frame rate 10 / 15 / 24 / 30 | Yes | |
| Mouse cursor overlay | Yes | Optional. |
| MP4 output | Yes | ffmpeg bundled at `Tools\ffmpeg.exe`. |
| Recording indicator with stop control | Yes | Elapsed time and frame count. |
| Microphone audio | Yes | Optional; AAC 160 kb/s muxed, mic picker. |
| System-audio (loopback) capture | No | Microphone audio only. |
| Hardware encoder selection | No | Uses libx264 software encoding. |

## UI and platform

| Feature | Status |
| --- | --- |
| Windows 11 Fluent-inspired styling | Yes |
| Rounded corners, shadows, modern typography | Yes |
| Light / dark / system themes with live preview | Yes |
| Smooth transitions and hover states | Yes |
| No admin, UAC, service, driver, MSI, or registry writes | Yes |
| Self-contained x64 build | Yes |
| Portable data, no temp-session dependency | Yes |
| Headless self-test suite | Yes, 13/13 passing |

## Not implemented / out of scope

- System-audio (loopback) recording (microphone audio is implemented).
- Windows-key global shortcuts.
- Cloud upload, sharing links, or accounts.
- Built-in video trimming.
- Automatic OCR-language download (uses what Windows already has installed).
- Scrolling-window ("scrolling capture") stitching.
- Scheduled or interval capture.
- Direct printing.
