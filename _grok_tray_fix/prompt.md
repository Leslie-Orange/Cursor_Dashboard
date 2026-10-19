# Task: Fix Windows tray icon + right-click menu for CursorQuotaPet

You are working in: E:\Cursor_Dashboard\Windows\Codings
Primary file to edit: QuotaUi.cs
Build with: powershell -ExecutionPolicy Bypass -File .\build-win.ps1
Do NOT commit git changes. Do NOT modify Mac/ folder. Do NOT change unrelated features.

## Goal 1 — Redesign the system tray status icon (NOT the .exe app icon unless necessary)

Current implementation: `TrayIconFactory.Create` in QuotaUi.cs draws a 32x32 tray icon with:
- Dark nearly-black plate: `Color.FromArgb(235, 36, 48, 62)` filled ellipse — USER HATES THIS BLACK BACKGROUND
- Two lines of `QuotaFormatter.ShortNumber(primary/secondary)` colored by `GlassTheme.Emphasis(...)`

Requirements:
1. Keep the SAME functionality: still show primary + secondary remaining quota as short numbers, still color by GlassTheme.Emphasis (mint/warning/danger).
2. NO black / charcoal / near-black solid background plate. Transparent canvas is OK; soft light/frosted Win11-style plate is OK; subtle gradient/glass is OK.
3. Visual style must match Windows 11 Fluent / modern aesthetic: soft corners or soft disc, clean typography, good contrast on BOTH light and dark taskbar themes, readable at 16x16 and 32x32.
4. Prefer pure GDI+ code in TrayIconFactory (runtime-drawn). You may also regenerate CursorQuotaPet.ico ONLY if the win32icon resource is part of what users see in the tray — but the live tray icon comes from TrayIconFactory; prioritize that.
5. Keep anti-aliased rendering; destroy GDI handles correctly (existing DestroyIcon pattern).
6. Update/add any unit tests in Program.cs ONLY if there are existing tray-icon visual tests that would break; otherwise keep tests green with build-win.ps1.

Design direction (you choose exact colors, stay coherent with GlassTheme mint/cyan/peach/lavender):
- Soft translucent light disc or rounded squircle with subtle border, NO opaque black fill
- Numbers centered, slightly larger or clearer font if readable
- Optional hairline ring / soft glow that looks native on Win11

## Goal 2 — Bug: right-click tray menu items cannot be selected until user left-clicks first

Symptoms:
- Right-click tray icon → context menu appears but mouse cannot highlight/click items
- After a left-click (e.g. open/close popup), subsequent right-click menu works normally

Current code path in QuotaApplicationContext (QuotaUi.cs):
- `_notifyIcon.ContextMenuStrip = _menu;`
- `MouseDown` → HideTip()
- `MouseUp` right button → `QueueTrayMenuFallback()` which BeginInvoke `_menu.Show(screenPoint)` if menu not already visible
- Hover tip (`QuotaTrayTipForm`) is topmost layered UI; HideTip on down/up

This is a classic WinForms NotifyIcon + ContextMenuStrip foreground/activation bug, possibly worsened by:
- double-show (NotifyIcon auto menu + QueueTrayMenuFallback)
- tip window / popup stealing activation
- missing SetForegroundWindow on the notify icon message window before Show

Fix requirements:
1. Menu items must be selectable on the FIRST right-click without any prior left-click.
2. Keep existing menu items and actions: 显示额度 / 立即刷新 / 打开 Cursor 用量页 / 退出
3. Prefer a robust Win11-safe fix:
   - Avoid fighting NotifyIcon's built-in ContextMenuStrip show (either own the show path OR use built-in, not both conflicting)
   - Call SetForegroundWindow on the NotifyIcon native window (or equivalent) before showing the menu
   - Ensure tip is fully hidden and does not sit above the menu or steal mouse
   - Optional: PostMessage WM_NULL trick after Show if needed
4. Do not break left-click toggle popup behavior or hover tip.
5. Add a brief comment explaining why the foreground activation is required.

## Workflow
1. Read QuotaUi.cs sections: TrayIconFactory, QuotaApplicationContext tray/menu handlers, NativeMethods, tip forms.
2. Implement icon redesign + menu fix.
3. Run build-win.ps1 and fix compile errors.
4. Summarize what changed and why the menu bug is fixed.
5. If you generate preview PNGs of the new tray icon look, save them under E:\Cursor_Dashboard\_grok_tray_fix\previews\ (optional).

Start now. Implement both fixes completely.
