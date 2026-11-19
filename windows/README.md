# CalmMouse for Windows

Not an app yet. The plan is in [docs/specs/windows.md](../docs/specs/windows.md).

## The engine test switch (temporary)

`CalmMouse.Win.exe --engine-test [--seconds N]` runs the scroll engine (hook + Raw Input) with
every feature off, so scrolling is unchanged, and logs each wheel event, the mice found and the
counters every 5 seconds to `%TEMP%\CalmMouse-engine-test-<time>.log`. Close its window (or let
`--seconds` run out) to stop it. It goes away once the tray app (plan task 13) is in.

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
