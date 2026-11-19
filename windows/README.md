# CalmMouse for Windows

Work in progress. The plan is in [docs/specs/windows.md](../docs/specs/windows.md) and
[docs/plans/2026-11-18-windows-phase1.md](../docs/plans/2026-11-18-windows-phase1.md).

## The app so far

`CalmMouse.exe` starts with no window: a mouse icon in the notification area (left click:
Settings; right click: Pause / Resume, Settings…, Quit). The icon is dimmed when paused and orange
when something is wrong, with a one-line tooltip saying what. The Settings window is still empty
(plan task 15). Every scroll feature is off until it's turned on there.

- Settings: `%AppData%\CalmMouse\settings.json` (written atomically; a corrupt file is kept as
  `settings.json.bak`). The first launch, with no file, opens Settings once.
- Log: `%LocalAppData%\CalmMouse\CalmMouse.log` (engine events, pauses, faults; plain English).
- One copy per user: starting it again opens the running copy's Settings. `CalmMouse.exe --quit`
  asks the running copy to quit and waits (up to 10 s) until it has (for installers and updaters).
- Start when I sign in: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `CalmMouse`, off by
  default. Turning it off in Task Manager is respected (Settings says so); only the release exe
  moves the entry to itself at start.
- Strings: the Mac's `Resources/*.lproj/Localizable.strings` plus the Windows-only tables in
  `src/CalmMouse.Win/Strings/`, embedded in the exe. The tray icons are drawn by
  `src/CalmMouse.Win/Assets/Tray/make-icons.ps1` (the `.ico` files are checked in).

## The engine test switch (temporary)

`CalmMouse.exe --engine-test [--seconds N]` runs the scroll engine (hook + Raw Input) with
every feature off, so scrolling is unchanged, and logs each wheel event, the mice found and the
counters every 5 seconds to `%TEMP%\CalmMouse-engine-test-<time>.log`, including hook
reinstalls (the watchdog judges from Raw Input and injects nothing) and the resend breaker. Close its window (or let
`--seconds` run out) to stop it. It goes away after the hands-on check (plan task 16).

## `probe/`: the Phase 0 test program

A console program that answers the spec's Phase 0 questions on a real Windows PC:
which device each scroll comes from (low-level hook vs Raw Input), whether dropping a
scroll in the hook and re-sending it works and how late it arrives, whether a Magic Mouse
click leaks scrolling under Apple's driver or Magic Utilities, and whether SDL3 reads the
DualSense touchpad over USB and Bluetooth, also with the window in the background.

It changes no settings and never goes online. It writes a report to the desktop, copies it
to the clipboard and opens it in Notepad.

Build it on the Mac (needs the .NET 10 SDK):

```bash
dotnet publish windows/probe -c Release -r win-x64 -o windows/probe/out/win-x64
```

`dotnet run --project windows/probe` on the Mac runs only the controller step.

CI (`.github/workflows/windows.yml`) builds the same `.exe` on `windows-latest`, runs it
once with no input as a smoke test, and uploads it as the `CalmMouseProbe-win-x64` artifact.
