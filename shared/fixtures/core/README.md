# Golden traces for the core

The Windows port of `CalmMouseCore` must behave exactly like the Swift original. Translated tests
check the cases someone thought of; golden traces check everything else. The Swift core runs a set of
inputs and writes down every output; the C# port replays the same inputs and must get the same
outputs.

- `scenarios/` holds hand-written inputs, one file per scenario. Add one here when a behaviour
  deserves to be pinned down.
- `traces/` is written by Swift, never by hand: each scenario with its inputs and every output,
  plus 20 generated sequences per component (`generated-*.json`). Those come from a fixed-seed
  SplitMix64 in the Swift test, and their inputs are in the trace files, so nothing is generated
  on the C# side.

Who does what:

- `Tests/CalmMouseCoreTests/GoldenTraceTests.swift` builds the traces. With
  `CALMMOUSE_WRITE_TRACES=1 swift test --filter GoldenTraceTests` it writes them (and deletes any
  no scenario produces any more); without the variable it fails if the code no longer does what
  the committed traces say.
- The macOS CI job writes them on every run, uploads them as the `golden-traces` artifact, and
  fails if they differ from what is committed. To update them, put the artifact's files in
  `traces/` (replacing what is there), look at the diff, and commit. Take them from that
  artifact (CI runs on arm64 macOS), not from your own Mac: the CI check compares the files
  byte for byte, and an Intel Mac's `exp()` can differ in the last bit.
- `windows/tests/CalmMouse.Core.Tests/GoldenTraceTests.cs` replays every trace through the C#
  ports.

If you change how the core behaves, the traces change: commit the new ones and make the C# port
match.

## File format

A scenario is a JSON object:

```json
{
  "component": "ScrollBlocker",
  "description": "What this scenario shows, in a sentence or two.",
  "config": { },
  "steps": [ {"op": "magicMouseButton", "button": 0, "isDown": true, "t": 0} ]
}
```

`component` is `ScrollBlocker`, `WheelSmoother`, `ControllerPointer` or `TapRecognizer`. The file
name is the scenario's name (an optional `"name"` must match it). In a scenario, any field below
may be left out and takes the type's own default; fields nobody knows are an error.

A trace has the same fields plus `name`, `source` and `outputs`, one output per step. In a trace
every field is written out, defaults included, and the C# side insists on all of them.

Times (`t`, `dt`) are in seconds. Modifier keys are a number adding up command 1, option 2,
control 4 and shift 8. Numbers are compared within 1e-9 (absolute or relative); everything else
must match exactly.

### ScrollBlocker

- `config`: `{"base": {...}, "appRules": [...]}`.
  - `base`: `blockScrollWhileClicked`, `releaseGrace`, `deadZone`, `axisLock`,
    `axisLockThreshold`, `axisLockRatio`, `momentumEnabled`, `blockHorizontalScroll`,
    `disableScrollEntirely`, `modifierActions` (a list of `{"modifiers": 2, "action":
    "horizontal"}`; actions are `normal`, `horizontal`, `invert`, `zoomCommand`, `zoomControl`,
    `block`), `reverseMagicMouseScroll`, `reverseWheelScroll`, `blockScrollWhileWheelPressed`,
    `smoothWheelScroll`, `wheelScrollSpeed`. (Written field by field: `BlockerConfig`'s own JSON
    leaves out `reverseMagicMouseScroll`.)
  - each app rule: `bundleID`, `name`, `enabled`, and `blockScrollWhileClicked`,
    `disableScrollEntirely`, `blockHorizontalScroll`, `momentumEnabled`, `axisLock`, `deadZone`,
    where `null` means "inherit".
- steps:
  - `{"op": "setActiveApp", "bundleID": "com.apple.Safari"}` (or `null`)
  - `{"op": "magicMouseButton", "button": 0, "isDown": true, "t": 0}`, same for `wheelMouseButton`
  - `{"op": "scroll", "t", "fromMagicMouse", "fromWheelMouse", "isContinuous", "phase",
    "momentum", "deltaX", "deltaY", "modifiers", "wheel"}`. Phases: `none`, `mayBegin`, `began`,
    `changed`, `ended`, `cancelled`; momentum: `none`, `began`, `continued`, `ended`. `wheel` is
    `null` or `{"reverse", "blockWhilePressed", "smooth", "speed"}`.
  - `{"op": "liftBlockingImmediately"}`, `{"op": "forceReleaseAllButtons", "t": 5}`
- output: `decision` (`null` unless the step is a scroll; else `{"kind": "pass"}`, `{"kind": "drop"}`,
  `{"kind": "rewrite", "phase", "momentum", "zeroX", "zeroY", "swapAxes", "invert",
  "addModifiers", "removeModifiers"}` or `{"kind": "smooth", "dx", "dy"}`), `interruptsGlide`
  (scroll steps only), and `isBlocking`, `isBlockingWheel`, `anyButtonDown` right after the step.
  The two blocking checks use the step's time, or the last time seen for steps without one.

### WheelSmoother

- `config`: `timeConstant`, `minimumSpeed`.
- steps: `{"op": "add", "dx", "dy"}`, `{"op": "step", "dt"}`, `{"op": "cancel"}`.
- output: `step` (`{"x", "y"}` in whole pixels, or `null`), `isIdle`, `owedX`, `owedY`.
  The pixels come out of `exp()`, which can differ in the last bit between macOS and Windows. The
  C# side therefore accepts a pixel that comes out one frame earlier or later, and says so in the
  test output, but only when the pixels paid so far plus what is still owed add up to the same
  distance and the pixels paid on that axis are equal again by its next frame (and by the end of
  the trace).

### ControllerPointer

- `config`: `pointerSpeed`, `scrollSpeed`, `tapToClick`, `naturalScrolling`, `stickClicks`,
  `touchpadAspect`, `touchpadGain`, `touchpadScrollGain`, `tapMaxDuration`, `tapMaxTravel`,
  `stickDeadZone`, `stickMaxSpeed`, `stickMaxScroll`, `toggleHold`, `stickClickSettle`.
- steps: `{"op": "setActive", "active"}`, `{"op": "button", "button", "pressed", "t"}` (buttons:
  `south`, `east`, `touchpad`, `menu`, `options`, `leftStick`, `rightStick`),
  `{"op": "touchpad", "primary", "secondary", "t"}` (each `null` or `{"x", "y"}`),
  `{"op": "sticks", "left", "right"}`, `{"op": "tick", "dt", "t"}`.
- output: `actions` (each `{"kind": "move", "dx", "dy"}`, `{"kind": "scroll", "vertical",
  "horizontal"}`, `{"kind": "press" | "release" | "click", "button": "left" | "right"}` or
  `{"kind": "toggle"}`), `isActive`, `needsTicks`, `leftHeld`, `rightHeld`.

### TapRecognizer

- `config`: `enabled`, `sensitivity`, `maxDuration`, `maxMovement`, `minPeakSize`,
  `scrollCooldown`, `buttonCooldown`, `scrollActivityThreshold`, `tapZoneEnabled`, `tapZoneDepth`,
  `tapZoneEdgeMargin`, `frontIsHighY`, `tapRightClick`, `rightClickMode` (`rightSide` or
  `doubleTap`), `rightSideStart`, `doubleTapWindow`, `doubleTapMaxDistance`, `tapAndDrag`,
  `dragWindow`, `dragMaxDistance`, `dragSwipeCancelDistance`, `twoFingerDrag`,
  `twoFingerPairWindow`, `twoFingerLongPress`, `scrollBurstWindow`, `scrollBurstCancelThreshold`,
  `pressQuietPeriod`, `pairArmTTL`, `pairRearmCooldown`. `sensitivity` is applied first, since it
  sets `maxDuration`, `maxMovement` and `scrollCooldown`; the fields after it may then override
  those.
- steps: `{"op": "physicalButton", "isDown", "t"}`, `{"op": "noteScroll", "deltaMagnitude", "t"}`,
  `{"op": "handleFrame", "touches": [{"id", "x", "y", "size"}], "t", "cursorMoving"}`,
  `{"op": "disarmDrag"}`, `{"op": "reset"}`.
- output: `events` (`tap`, `rightTap`, `dragBegan`, `dragEnded`, `dragSwipeCancelled`,
  `dragCancelled`, `dragPressed`), `tapCount`, `dragActive`, `dragArmIsPair`, `lastRejection`
  (`null` or `poisoned`, `buttonDown`, `tooLong`, `movedTooMuch`, `tooSmall`, `buttonCooldown`,
  `scrollCooldown`, `outsideZone`).
