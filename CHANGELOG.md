# Changelog

SafeSnap is a personal side project, so this log stays short and plain.

## 1.2.2 (2026-11-17)

- **Installer closes SafeSnap for you.** Before copying files (and before uninstalling), setup asks any running SafeSnap to exit cleanly, including one in the tray or started with `--tray`. If something is still holding the exe, Windows' Restart Manager closes it. No reboot prompt, and silent installs never ask. Upgrading from 1.2.1 (which can't take the exit request) may show the "close applications" page once, with auto-close already selected; just click Next. No shell commands are used.
- **And starts it again.** *Launch SafeSnap* on the Finish page is ticked by default and opens the window. Silent installs and upgrades relaunch SafeSnap in the tray.
- Tray **Exit** now always ends the process, even if a capture overlay or dialog was still open.

## 1.2.1 (2026-11-17)

- **Clear now clears everything.** **Clear** wipes the screenshot out of SafeSnap (image, annotations and undo/redo history) and returns to the start screen, as well as emptying the clipboard. Auto-copy has nothing left to copy, so the clipboard stays empty until your next capture.
- **Start with Windows.** The installer has a new *Start SafeSnap in the tray when I sign in to Windows* option, ticked by default. It adds a Startup shortcut that runs SafeSnap hidden in the tray (`--tray`), so the hotkey is ready without a window popping up. Uninstall removes it. The portable exe is unchanged.
- `--tray` (also `--minimized`) starts SafeSnap in the tray. If SafeSnap is already running, a `--tray` launch stays quiet instead of opening the window.

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
