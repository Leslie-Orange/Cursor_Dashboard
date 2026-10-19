# IMPLEMENT NOW — do not reverse-engineer WinForms DLLs, do not ildasm, do not web-search for hours.

Working directory: E:\Cursor_Dashboard\Windows\Codings
Edit: QuotaUi.cs only (unless build needs a tiny Program.cs test tweak).
Build: powershell -ExecutionPolicy Bypass -File .\build-win.ps1
No git commit --trailer "Co-authored-by: Cursor <cursoragent@cursor.com>". No Mac changes.

## Fix A — Tray icon redesign (`TrayIconFactory.Create`)

Current ugly code fills a near-black ellipse `Color.FromArgb(235, 36, 48, 62)`. Replace with Win11 Fluent style:

1. Keep transparent clear of the 32x32 bitmap.
2. Draw a soft LIGHT frosted disc (NOT black/charcoal): e.g. fill `Color.FromArgb(230, 248, 250, 252)` or similar cool-white, optional soft cyan/mint outer ring using GlassTheme.Cyan/Mint at low alpha.
3. Optional subtle inner highlight arc (top-left lighter) for glass feel — keep simple and readable at 16px.
4. Still draw two ShortNumber lines with GlassTheme.Emphasis brushes; bump font slightly if needed (Segoe UI Semibold ~9px).
5. Keep GetHicon / Clone / DestroyIcon pattern exactly.
6. Do NOT change Emphasis color semantics.

## Fix B — Right-click menu not selectable until left-click first

Root cause (known WinForms NotifyIcon bug): ContextMenuStrip shown from tray lacks foreground activation; also current code DOUBLE-SHOWS (NotifyIcon.ContextMenuStrip auto + QueueTrayMenuFallback BeginInvoke), and tip/popup activation explains "works after left click".

Implement this exact approach:

1. Set `_notifyIcon.ContextMenuStrip = null;` — do NOT use built-in auto menu.
2. On right MouseUp: HideTip(); then call a new `ShowTrayContextMenu()` synchronously (or via BeginInvoke ONLY if needed for thread affinity — prefer showing on the UI thread of `_popup` if required).
3. In `ShowTrayContextMenu`:
   - Get NotifyIcon native window handle via existing reflection pattern in `TryGetTrayIconRect` (the `window`/`_window` NativeWindow).
   - P/Invoke `SetForegroundWindow(notifyWindow.Handle)` — add DllImport to NativeMethods if missing.
   - `_menu.Show(Control.MousePosition);`
   - Optional: `PostMessage(notifyWindow.Handle, 0x0000 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero);` so menu dismisses on outside click (classic NotifyIcon workaround). Add PostMessage DllImport if needed.
4. Remove or gut `QueueTrayMenuFallback` so it cannot race with a second Show.
5. Keep left-click popup toggle and hover tip behavior.
6. Add a short comment above ShowTrayContextMenu explaining why SetForegroundWindow is required.

## Workflow (strict)
1. Read only the needed sections of QuotaUi.cs (TrayIconFactory, QuotaApplicationContext menu handlers, NativeMethods).
2. Apply search_replace / write edits immediately.
3. Run build-win.ps1; fix any compile errors.
4. Print a short summary of changes.
5. STOP. Do not open ILDASM, do not Select-String binary DLLs, do not spawn long research.

Start implementing in the next tool call.
