# CalmMouse for Windows, Phase 1: implementation plan

Status: draft for Malik's OK, 2026-11-18. Spec: [docs/specs/windows.md](../specs/windows.md)
(section 5.2 has the Phase 0 results and the new Phase 1 scope: wheel mice + per-app rules).

Covered here: the parts every Phase 1 needs (the core port and its golden traces, the
hook + Raw Input engine, the tray app with its settings window) plus the wheel-mouse
features they now carry. **Not covered** (a later plan, once the app runs): code signing,
Velopack, winget, the release workflow (`win-v*` tags with `--latest=false`), and the
website's Windows download.

## How the work runs

- One fresh subagent per task below, given only that task's text, the files it names,
  and the house rules. After each task a second fresh subagent reviews the diff against
  the task's "done when" and the spec; fixes go back to the implementer before the next
  task starts. (The `superpowers:subagent-driven-development` skill isn't installed on
  this PC, so the same pattern runs by hand.)
- Each task ends in one commit by `MalikZ <220207332+Malik1942@users.noreply.github.com>`,
  no Co-Authored-By trailer, on `am/jolly-wozniak-3d3b19`. No merge to main, no tags.
- Tests first wherever there's logic: translated Swift tests go in before the C# code
  that makes them pass.
- House rules for anything a user sees: plain language, few settings, every new option off
  by default, every string in EN / 简体 / 繁體.

## Layout (spec 5.1, one addition)

```
shared/fixtures/core/           golden traces, written by Swift, read by C#
windows/
  CalmMouse.Windows.sln
  Directory.Build.props         EnableWindowsTargeting, nullable, warnings as errors, version
  src/CalmMouse.Core/           net10.0. Ports/ = the Swift port, nothing else.
                                Routing/ = the Windows scroll router (pure, see task 10)
  src/CalmMouse.Win/            net10.0-windows, WPF: hook, Raw Input, SendInput, tray, settings
  tests/CalmMouse.Core.Tests/   xUnit: translated tests, golden traces, router tests
  tests/CalmMouse.Win.Tests/    Windows-only: hook and SendInput integration
  probe/                        unchanged
```

The router sits in `CalmMouse.Core` (plain `net10.0`) rather than in `CalmMouse.Win`, so
its tests also run on the Mac and in Linux CI. It's kept in its own folder so the Swift
port stays a clean one-to-one copy.

## Tasks

### A. Ground work

**1. Solution skeleton and CI.** Create the layout above (`dotnet new sln --format sln`,
four projects, xUnit v3). `CalmMouse.Win` uses WPF with `ThemeMode="System"` (Fluent) and
shows an empty window. Add `Microsoft.Windows.CsWin32` to `CalmMouse.Win`. Extend
`.github/workflows/windows.yml`: path filters add `shared/**` and `Sources/CalmMouseCore/**`;
a `core` job runs `dotnet test` for Core.Tests on `ubuntu-latest`; a `win` job builds the
solution and runs both test projects on `windows-latest`; the probe job stays.
*Done when:* `dotnet build windows/CalmMouse.Windows.sln` and `dotnet test` pass locally
with zero tests, and the workflow file passes `actionlint` (or a careful read if it isn't
installed).

**2. Test-name parity guard.** A test in Core.Tests reads every `func test…` name from
`Tests/CalmMouseCoreTests/*.swift` and compares them with the C# test method names, file by
file (`ScrollBlockerTests.swift` ↔ `ScrollBlockerTests.cs`). It fails listing what's missing
on either side. Until a file is ported, it is listed in an explicit "not yet ported" set in
the test itself, so progress is visible and the set has to be empty by task 7.
*Done when:* the guard passes with every file in the "not yet" set, and fails when one name
is removed from that set without a matching C# test.

### B. Port CalmMouseCore (Swift → C#, test names unchanged)

Translation rules for every task in this section: structs → `record struct` or `sealed
record` (keep value semantics); `OptionSet` → `[Flags] enum`; enums with associated values
→ `abstract record` + subclasses; `TimeInterval` → `double` seconds; same defaults, same
constants, same comments where they explain behaviour. Codable types get `System.Text.Json`
converters that write the **same JSON shape as Swift** (the golden traces depend on it).
Each task removes its files from the parity guard's "not yet" set.

**3. Small types.** `AppVersion`, `SupportInvite`, `MouseKind`, `TrackingSpeed`,
`WheelSettings`, `Config` (`ModifierCombo`, `ScrollAction`, `BlockerConfig`, `AppRule`,
`RuleSet`). Tests: `AppVersionTests`, `SupportInviteTests`, `DeviceRulesTests`.

**4. Presets, conflicts and the mouse figure.** `Presets`, `MouseUtilityConflicts` (logic
and the Mac list as is; the Windows list lives in the app, task 14), `MouseFigure`.
Tests: `PresetTests`, `MouseFigureTests`.

**5. Scrolling.** `WheelSmoother`, `ScrollBlocker`. Tests: `ScrollBlockerTests`,
`FeatureTests`, `WheelMouseTests`, `MagicMouseReverseTests`. The biggest correctness risk in
the port; the reviewer compares the two files line by line, not only the test results.

**6. Controller.** `ControllerPointer`. Tests: `ControllerPointerTests`. (Not used until
the controller phase, but it's in the core and its tests are cheap to keep in step.)

**7. Taps.** `TapRecognizer`. Tests: `TapRecognizerTests`, `TapRightClickTests`. Only
used if we ever write the Phase 4 driver; ported now because the brief asks for every
Swift test and the parity guard needs it. *This is the one task that could wait:* 530
lines of code and 73 tests. Say if you'd rather skip it for now.
*Done when (whole section):* the parity guard's "not yet" set is empty, all 218 translated
tests pass.

### C. Golden traces

**8. Swift trace writer** (written here, run only by macOS CI, since Swift isn't on this PC).
`Tests/CalmMouseCoreTests/GoldenTraceTests.swift`, test-only, no change to `Sources/`.
- Scenarios: one JSON file per scenario in `shared/fixtures/core/scenarios/`, each a config
  plus a list of inputs (`ScrollBlocker`: active app, button up/down, scroll event;
  `WheelSmoother`: add, step(dt), cancel; `ControllerPointer`: setActive, button, touchpad,
  sticks, tick). Hand-written ones copy the inputs of the existing tests that matter most;
  generated ones come from a fixed-seed SplitMix64 written out in the test (not the system
  RNG), so both languages could recreate them.
- For each scenario the test writes `shared/fixtures/core/traces/<name>.json`: the inputs
  and every output. With `CALMMOUSE_WRITE_TRACES=1` it writes; without, it compares and
  fails on any difference.
- `ci.yml`, macOS job, before `swift test`: write the traces (`CALMMOUSE_WRITE_TRACES=1
  swift test --filter GoldenTraceTests`), upload `shared/fixtures/core/traces` as the
  `golden-traces` artifact (always, even if a later step fails), fail if
  `git status --porcelain shared/fixtures/core` shows any change, then the plain `swift test`.
  Writing first means even the first run, with no traces committed, hands back the files.
- The C# side gets a test with the same name, so the parity guard stays exact.

**9. C# trace replayer.** Core.Tests loads every trace, replays the inputs and compares
each output (doubles within 1e-9, everything else exact). The first traces come from CI:
**this needs the branch pushed to GitHub** so the macOS job can run and hand back the files.
I'll ask before the first push.
*Done when:* the macOS job is green, its traces are committed, and the C# replay passes
on them.

### D. The engine

**10. Scroll router (pure, in `CalmMouse.Core/Routing`).** The decisions of spec 1.1 design
A, with no Windows calls, so every rule is a unit test:
- Inputs: `HookWheel(t, delta, horizontal)`, `RawWheel(t, device, delta, horizontal)`,
  `RawButton(t, device, button, down)`, `Tick(t)`, `ForegroundChanged(exe, elevated)`,
  `Paused(bool)`.
- Outputs: the hook's answer (`Swallow` / `Pass`) and a list of `Send(delta, horizontal)`.
- Rules: physical wheel events are swallowed only while running, not paused, and the
  foreground window isn't elevated. Each swallowed event waits in a queue; the next Raw
  Input wheel from a real device takes it and goes through `ScrollBlocker` with that
  device's `WheelSettings` (pass → send as is, reverse → send negated, drop → nothing,
  smooth → into that device's `WheelSmoother`). Raw Input wheels with no device, or with our
  marker in `ulExtraInformation`, are our own resends and are ignored (Phase 0 showed they
  come back). **Touchpad safety:** a swallowed event nobody claims within 50 ms is sent on
  unchanged, so a device that never shows up in Raw Input still scrolls. Middle-button state
  per device feeds "don't scroll while the wheel is pressed".
- Units: the core's wheel maths is in Mac pixels. The router converts one notch (120) into
  the delta that makes the default speed scroll exactly what one plain notch scrolls, and
  converts the smoother's output back into wheel units, so smooth and unsmoothed scrolling
  cover the same distance. The constant gets tuned by hand on this PC in task 16.
- Per-app rules for wheel mice are Windows-only (the Swift core keeps wheel mice out of app
  rules): "don't smooth in this app" and "leave scrolling alone in this app". They're
  applied here, before `ScrollBlocker`, so the core stays untouched.

**11. Input thread (`CalmMouse.Win`).** A dedicated thread with its own message loop owns
`WH_MOUSE_LL`, a message-only window registered for Raw Input (`RIDEV_INPUTSINK`), and the
`SendInput` calls (marker in `dwExtraInfo`). The hook callback only asks the router and
returns: no allocation, no locks held across calls. Smoothing frames come from a
high-resolution waitable timer at the main display's refresh rate, running only while a
glide is in progress. A watchdog sends a marked zero-delta wheel every few seconds and
reinstalls the hook if it doesn't come through (Windows silently removes slow hooks). An
unhandled exception on this thread removes the hook first, then reports, so a crash never
leaves the mouse unable to scroll.

**12. Devices and the foreground app.** Raw Input device list with change notifications
(`RIDEV_DEVNOTIFY`); each mouse gets a stable key (VID, PID, product name) and a display
name, and precision touchpads are recognised and marked "left alone". Foreground tracking
with `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`: exe name, UWP apps resolved through
`ApplicationFrameHost`, and "elevated" when the process token can't be read or says so.
*Done when:* integration tests in Win.Tests (hook installs, a marked wheel passes, the
router's swallow/pass reaches the hook) pass on `windows-latest`.

### E. The app

**13. Tray, settings file, single instance.** Tray icon (`H.NotifyIcon.Wpf`) with Pause /
Settings… / Quit and the Mac version's colour states. One instance per user (named mutex).
Settings in `%AppData%\CalmMouse\settings.json`, written atomically, with a version field.
Start at login through `HKCU\…\Run`, **off by default**.

**14. Strings and the conflict notice.** Load the Mac's `Resources/*.lproj/Localizable.strings`
(keys are the English text) plus a Windows-only table of the same format under
`windows/src/CalmMouse.Win/Strings/`; follow the Windows display language, with a Language
choice in General (System / English / 简体中文 / 繁體中文). The conflict notice uses the
ported logic with a Windows list limited to tools that change scrolling (SmoothScroll,
X-Mouse Button Control, Logi Options+, Razer Synapse, Magic Utilities, AutoHotkey).

**15. Settings window.** Fluent WPF window, three pages on the left: **General** (pause,
start at login, language, conflict notice), **Devices** (each detected mouse with: smooth
scrolling, speed when smooth, reverse direction, don't scroll while the wheel is pressed,
all off by default; the touchpad listed as "CalmMouse leaves it alone"), **App rules** (add
an app from the ones running or by picking an .exe; per app: don't smooth here / leave
scrolling alone here). Explanations: hovering or focusing a setting shows its plain-language
explanation in a side panel, as on the Mac. Every string in all three languages.

### F. On this PC

**16. Hands-on check with Malik.** On this laptop with both wheel mice and the touchpad:
smoothing in Edge/Chrome, Explorer, Notepad, VS Code and an old Win32 app; reverse on one
mouse only; wheel-press blocking; touchpad still scrolls; Task Manager run as admin scrolls
normally; Pause; idle CPU near 0; tune the unit constant from task 10. Findings go into the
spec and become fix tasks.

## Questions before I start

1. Port `TapRecognizer` now (task 7) or leave it until the driver phase? I've planned to port
   it now, because the brief says every test.
2. Golden traces need the macOS CI job, so the branch has to be pushed to GitHub around
   task 9. OK to push `am/jolly-wozniak-3d3b19` then (no PR, no merge)?
