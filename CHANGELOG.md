# Changelog

SafeSnap is a personal side project, so this log stays short and plain.

## 1.2.0 (2026-11-17)

- **Auto-copy, on by default.** The current image goes to the clipboard after every capture, pixelate, highlight, arrow, undo and redo, so the clipboard always holds the latest version. The status bar shows *On clipboard* while it does. Turn it off with the **Auto-copy** switch in the status bar; the choice is saved for next time.
- **Clear clipboard button.** **Clear** in the toolbar empties the clipboard and confirms with a short "Clipboard cleared".
- **Fix: SafeSnap seemed to vanish after closing.** It was still running, but Windows 11 puts new tray icons in the hidden ^ overflow, and treats every new exe path (each portable download or install folder) as a new icon. SafeSnap now asks Windows once to show its icon on the taskbar, and leaves it alone if you hide it yourself later. The tray tip also mentions the right-click Open and Exit menu.

## 1.1.2 (2026-10-15)

- Minimize now goes to the tray, like closing the window.
- README: why keeping sensitive data out of screenshots matters (DLP).

## 1.1.1 (2026-10-14)

- Mosaic-grid Pixelate icon.
- Arrows are always large; the size controls are gone.

## 1.1.0 (2026-10-14)

- Highlight and Arrow tools alongside Pixelate.

## 1.0.1 (2026-10-14)

- Pixelate is always on (no mode to switch into) and a one-time tip on the first capture.
- Zip downloads with a short README.

## 1.0.0 (2026-10-14)

- First release: region capture across all monitors, destructive pixelation, copy as bitmap + PNG, save PNG, global Ctrl+Shift+S hotkey, tray, installer and portable exe.
