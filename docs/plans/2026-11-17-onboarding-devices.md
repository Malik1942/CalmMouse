# Onboarding devices — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Onboarding opens on the devices CalmMouse supports, recognises the one you have (a Magic Mouse gets a rendered intro video), then walks you through only the steps for your devices; Settings › Devices shows turnable renders of the wheel mouse and the controller.

**Architecture:** Two halves. Assets come out of the existing Blender pipeline in `.claude/design/` (git-ignored): two new models (an ergonomic wheel mouse after Logitech's MX Master, a controller after the PS5 DualSense) rendered into the same 72-frame turntable format the Magic Mouse uses, plus a 3-second intro for the Magic Mouse encoded as HEVC with alpha. Code: the step order becomes a pure function in `CalmMouseCore`; the Magic Mouse stage (`MouseShowcase`/`MouseFrames`) learns to draw any of three `DeviceModel`s; onboarding gains a supported-devices page, a live detection page with an `AVPlayerLayer` intro, and wheel and controller pages.

**Tech Stack:** Swift 5.9 / SwiftPM, SwiftUI + AppKit, AVFoundation, XCTest; Blender 5.2 (Cycles, Metal), Python 3 + Pillow, ffmpeg (`hevc_videotoolbox`).

**Spec:** `docs/specs/onboarding-devices.md` (Chinese). Read it before starting any task.

## Global Constraints

- Deployment target macOS 13 (`Package.swift` `.macOS(.v13)`); nothing newer than macOS 13 APIs without an `if #available`.
- Every user-visible string goes through `L("…")` and has an entry in `Resources/en.lproj`, `Resources/zh-Hans.lproj` and `Resources/zh-Hant.lproj` `Localizable.strings` (`LocalizationTests` fails otherwise). SwiftUI `Text("literal")` keys must also be added to all three files.
- Plain-English UI copy: say what it does, no jargon. zh-Hans uses macOS terms: 点按, 轻点, 连按, 拖移, 辅助功能, 触控板.
- No brand artwork in any render: no Logitech logo, no PlayStation logo, no PS button glyph, no △○✕□ on the face buttons, no Apple logo.
- Nothing may keep a timer or display link running once nothing moves (the Settings stage is 0% CPU at rest; keep it that way, and stop the intro player when it ends).
- Wheel-mouse and controller defaults are unchanged: `reverseWheelScroll`, `smoothWheelScroll`, `controllerPointer` all default off.
- The onboarding route is fixed when the user leaves the detection page; plugging devices in later doesn't change it.
- Commits: author `MalikZ <220207332+Malik1942@users.noreply.github.com>` (repo-local config already set), **no** `Co-Authored-By` or any other trailer. Never commit `.claude/`.
- Build with `./build.sh` or `swift build --scratch-path ~/Library/Caches/CalmMouse/build` (the repo is in iCloud with a full quota; in-repo `.build` corrupts). Tests: `swift test --scratch-path ~/Library/Caches/CalmMouse/build`.
- Design sources live in `.claude/design/devices/` (and the existing `.claude/design/magic-mouse/`), never only in `/tmp` or the scratchpad.
- Approval gates (marked **GATE**) stop the task until Malik approves in chat. Do not render full turntables or the intro before the gate before them has passed.

Paths used below:

```
REPO=/Users/malik/Documents/CalmMouse/.claude/worktrees/onboarding-device-prompt-animation-182e2b
MM=/Users/malik/Documents/CalmMouse/.claude/design/magic-mouse
DEV=/Users/malik/Documents/CalmMouse/.claude/design/devices
BLENDER="$HOME/Library/Application Support/Steam/steamapps/common/Blender/Blender.app/Contents/MacOS/Blender"
```

## File map

| File | Responsibility |
|---|---|
| `Sources/CalmMouseCore/OnboardingRoute.swift` (new) | `DeviceKind`, `OnboardingStep`, `OnboardingRoute.steps(for:)` |
| `Tests/CalmMouseCoreTests/OnboardingRouteTests.swift` (new) | route tests |
| `Sources/CalmMouse/MouseShowcase.swift` | gains `DeviceModel`; `MouseFrames` keyed by model + finish; stories/ring/colour menu only for the Magic Mouse |
| `Tests/CalmMouseAppTests/DeviceModelTests.swift` (new) | model folders exist with 72 frames + home; intro files exist |
| `Sources/CalmMouse/SettingsWindow.swift` | Devices column uses turntables for wheel mice and controllers |
| `Sources/CalmMouse/Onboarding.swift` | window 680×520, route-driven navigation, existing steps (permission, preset, try it, done) |
| `Sources/CalmMouse/OnboardingDevices.swift` (new) | supported-devices page, detection page, wheel page, controller page |
| `Sources/CalmMouse/DeviceDetector.swift` (new) | 1 s live scan → `[DetectedDevice]` (pure `detected(…)` for tests) |
| `Tests/CalmMouseAppTests/DeviceDetectorTests.swift` (new) | detection composition tests |
| `Sources/CalmMouse/IntroVideoView.swift` (new) | `AVPlayerLayer` NSView: plays once, holds last frame, Reduce Motion |
| `Sources/CalmMouse/AppDelegate.swift` | passes `mice` and `gamepad` to the onboarding window |
| `Resources/Devices/{wheel,controller}/` (new) | `home.webp`, `turn-00…71.webp` |
| `Resources/MagicMouse/{white,black}/intro.mov` (new) | the intro |
| `Resources/*.lproj/Localizable.strings` | new strings |
| `build.sh` | copies `Resources/Devices` |
| `Package.swift` | links AVFoundation |
| `$DEV/render/*.py`, `$DEV/reference/` | Blender sources (git-ignored) |
| `$MM/render/magicmouse.py` | new `intro` view |

---

## Phase A — Assets

### Task 1: Shared studio module and the wheel-mouse model (desk still)

**Files:**
- Create: `$DEV/render/studio.py`, `$DEV/render/wheelmouse.py`, `$DEV/reference/` (reference photos, measurement only)
- Read: `$MM/render/magicmouse.py` (lines 245–585: materials, HDRI world, camera `orbit`, render settings)

**Interfaces:**
- Produces: `studio.setup(model_object, finish="white", view="desk", res=720, dist=39.0, samples=128)` — builds the HDRI world, shadow-catcher desk, desk camera and Cycles settings exactly as `magicmouse.py` does for `VIEW in ("desk","spin")` with `STUDIO="hdri"` and the white finish, except the camera distance. `studio.render_still(out)`, `studio.render_spin(out_pattern, frames=72, home_yaw=25.0)`.
- Produces: `wheelmouse.py` CLI `-- <out.png> <samples> <view: desk|spin|clay> [frames]`.

- [ ] **Step 1: Gather reference.** Use WebSearch to find a top view, a side view (right side), a left side view (thumb side) and a front view of the Logitech MX Master 3S. Save them into `$DEV/reference/mx-*.jpg`. They are for measuring proportions only and are never shipped. Published size: 124.9 mm long × 84.3 mm wide × 51 mm high.

- [ ] **Step 2: Write `studio.py`.** Lift, don't re-derive: copy from `magicmouse.py` the `principled`, `put` and `emitter` helpers, the HDRI world block (`if HDRI:` at ~442–470, white branch: `photo_studio_loft_hall_2k.hdr`, rotation 180°, saturation 0.1, `MM_HDRI_BLUR` 32, strength 1.0), the shadow-catcher desk (~305–313), `orbit` (~477) and the render block (~548–574: Cycles Metal GPU, denoise, film transparent, 16-bit RGBA PNG, `Khronos PBR Neutral`). Wrap them in functions:

```python
# studio.py — the Magic Mouse's desk studio, for any model. 1 unit = 1 cm.
import bpy, math, os
from mathutils import Vector
HDRI_DIR = "/Users/malik/Documents/CalmMouse/.claude/design/magic-mouse/hdri/"
TARGET = (0.0, 0.0, 0.75)
HOME_YAW = 25.0

def principled(name): ...      # copied
def put(node, name, value): ...  # copied
def emitter(name, loc, aim, w, h, strength, soft=0.5): ...  # copied

def world(finish="white"): ...  # copied HDRI block, file chosen by finish
def desk(): ...                 # copied shadow catcher
def camera(view, res, dist, el=40.0, lens=105.0, target=TARGET): ...  # orbit(0, el, dist, lens, target)
def render_settings(samples, res): ...  # copied render block

def setup(model, finish="white", view="desk", res=720, dist=39.0, samples=128):
    world(finish); desk(); camera(view, res, dist); render_settings(samples, res)
    model.rotation_euler = (0, 0, math.radians(HOME_YAW))

def render_still(out):
    bpy.context.scene.render.filepath = out
    bpy.ops.render.render(write_still=True); print("WROTE", out, flush=True)

def render_spin(model, pattern, frames=72):
    base = math.radians(HOME_YAW)
    for f in range(frames):
        model.rotation_euler = (0, 0, base + 2 * math.pi * f / frames)
        render_still(pattern.replace("####", "%03d" % f))
```

- [ ] **Step 3: Check `studio.py` reproduces the Magic Mouse framing.** Temporarily import the Magic Mouse mesh into `studio.setup` (or run `magicmouse.py` desk view) and confirm that the camera matrix matches `$MM/assets/camera-desk.json` for `dist=39`: run `$MM/render/camera.py` and compare the `viewMatrix` entries to 4 decimal places. Expected: identical.

- [ ] **Step 4: Work out the camera distance.** Every model must fit the 720 px frame at every angle of a turn, the way the Magic Mouse does. The Magic Mouse footprint diagonal is √(11.35² + 5.71²) = 12.71 cm at `dist` 39. Scale `dist` by the ratio of diagonals: wheel mouse √(12.49² + 8.43²) = 15.07 → `dist = 39 × 15.07 / 12.71 = 46.2`. Put `DIST = 46.2` at the top of `wheelmouse.py`.

- [ ] **Step 5: Model the wheel mouse in `wheelmouse.py`.** Build it the way `magicmouse.py` builds the Magic Mouse: cross-section rings lofted into a mesh (`build(name, rings)`), then a Subdivision Surface modifier (levels 2, render 3) and smooth shading. Parts, all in cm, origin at the centre of the footprint on the desk, +y towards the back (palm), −y towards the front (buttons):
  - **Body shell:** 18 cross-sections from y = −6.2 (front) to y = +6.2 (back). Each section is a closed curve: flat bottom at z = 0.25; right side rising steeply; top highest at x ≈ −0.6 (the left/thumb side is higher, the top slopes down to the right like the MX Master); left side bulging out into the **thumb rest**, a ledge at z ≈ 1.0–1.6 that sticks out to x = −4.2 in the middle third of the length. Peak height 5.1 at y ≈ +1.5. Read the half-widths and heights per section off the reference photos and keep them in a table `SECTIONS = [(y, left_x, right_x, top_z, peak_x, thumb_x, thumb_z), …]` at the top of the file.
  - **Button split:** a 0.06 cm deep, 0.05 cm wide groove from y = −6.2 to y = −1.0 along x = −0.4, and a cross groove where the buttons end (boolean difference with thin boxes, applied before subdivision).
  - **Main wheel:** in the groove at y = −3.8, a metal cylinder radius 1.25, width 0.75, axis along x, 60 % sunk into the body. Knurling: 48 shallow notches around the rim (array of thin boxes, boolean).
  - **Mode-shift button:** a 0.5 × 0.8 cm rounded rectangle 0.1 proud, at y = −2.1 behind the wheel.
  - **Thumb wheel:** on the left side above the thumb rest at y = −0.6, z = 2.3, a cylinder radius 0.85, width 0.45, axis along y.
  - **Two thumb buttons:** rounded pills 1.2 × 0.5 cm on the left side, at y = +0.9 and y = +2.2, z = 2.6, flush with 0.04 cm gaps.
  - **Base:** a 0.25 cm high slab under the footprint, inset 0.15 cm.
  - **Materials (graphite):** shell `Base Color (0.045, 0.047, 0.05)`, `Roughness 0.62`, `Coat Weight 0`; thumb rest and side panels rubber `(0.03, 0.03, 0.032)`, `Roughness 0.8`; wheels metal `Metallic 1`, `Base Color (0.35, 0.35, 0.37)`, `Roughness 0.28`; base black glossy `(0.01, 0.01, 0.01)`, `Roughness 0.2`. No logo anywhere.
  - The whole mouse is parented to one empty named `MODEL` so `studio` can rotate it.

- [ ] **Step 6: Render a clay pass and a material pass.**

```bash
cd "$DEV/render"
"$BLENDER" -b --factory-startup --python wheelmouse.py -- "$DEV/out/wheel-clay.png" 64 clay
"$BLENDER" -b --factory-startup --python wheelmouse.py -- "$DEV/out/wheel-desk.png" 256 desk
```

Expected: both PNGs are 720×720 with a transparent background, and the mouse fits inside the frame with a margin like `$MM/assets/desk-white.png`. Open them next to `$MM/assets/desk-white.png` and the reference photos, and adjust `SECTIONS` until the silhouette reads as that mouse.

- [ ] **Step 7: GATE — send Malik `wheel-desk.png` next to `desk-white.png`** (SendUserFile) and wait. Iterate on shape and material until he approves. Record what he said in the memory file `calmmouse-settings-redesign.md`, the same way his lighting feedback is recorded there.

No commit: everything in `$DEV` is git-ignored.

### Task 2: The controller model (desk still)

**Files:**
- Create: `$DEV/render/controller.py`, `$DEV/reference/ds-*.jpg`

**Interfaces:**
- Consumes: `studio.py` from Task 1.
- Produces: `controller.py` CLI with the same arguments as `wheelmouse.py`.

- [ ] **Step 1: Reference.** Find a top view, a front view and a back view of the white PS5 DualSense and save them to `$DEV/reference/ds-*.jpg`. Size: 160 mm wide × 106 mm deep × 66 mm high.

- [ ] **Step 2: Distance.** Diagonal √(16.0² + 10.6²) = 19.19 → `DIST = 39 × 19.19 / 12.71 = 58.9`.

- [ ] **Step 3: Model.** Origin at the footprint centre, +y = the back (the triggers), lying flat on the desk as a controller rests: on its grips, face up.
  - **Shell:** a top-view outline traced from the reference into ~40 points (`OUTLINE = [...]`, symmetric about x = 0). Extrude it to 3.2 cm with a 0.8 cm bevel. Loft the **grips** as two tapered tubes from (±5.2, −1.5) going down and back to (±6.4, −4.8), touching the desk at their ends. Use Subdivision Surface levels 2 / render 3.
  - **Centre panel (black):** the area between the grips and under the touchpad, a separate mesh 0.05 proud of the shell, outline `PANEL = [...]` from the reference.
  - **Touchpad:** a rounded rectangle 7.0 × 3.6 cm at the top centre, 0.08 cm concave, matte white.
  - **Light bars:** thin curved strips along both long sides of the touchpad, emission `(0.25, 0.55, 1.0)`, strength 1.5, so they read as lit but not glowing.
  - **Sticks:** two at (±2.4, −1.6), each a shaft (radius 0.45) plus a cap (radius 0.95, height 0.5) with a 0.08 dish and a rubber rim of 16 small bumps. Material: rubber `(0.03, 0.03, 0.035)`, roughness 0.75.
  - **D-pad:** four separate wedge keys around (−4.6, 0.9), black glossy.
  - **Face buttons:** four cylinders, radius 0.5, around (4.6, 0.9). Black, translucent (`Transmission 0.3`, `Roughness 0.25`), **no symbols**.
  - **Small buttons:** Create and Options as 0.25 × 0.7 cm pills beside the touchpad; a mute bar under the touchpad. **No PS button glyph**: put a plain round black button where the PS button is.
  - **Triggers and bumpers:** L1/R1 and L2/R2 at the back edge, black, visible from the desk camera only as edges.
  - **Materials:** shell white `(0.86, 0.86, 0.87)`, `Roughness 0.45`, `Coat 0.3` with `Coat Roughness 0.2` (satin, not glossy like the Magic Mouse); centre panel `(0.012, 0.012, 0.014)`, `Roughness 0.35`. No logo, no text.
  - Parent everything to the `MODEL` empty.

- [ ] **Step 4: Render clay + desk** exactly as in Task 1 Step 6, to `$DEV/out/controller-clay.png` and `$DEV/out/controller-desk.png`.

- [ ] **Step 5: GATE — send Malik `controller-desk.png`** and wait for approval. Iterate. Record the decisions in memory.

### Task 3: Turntables into the app

**Files:**
- Create: `$DEV/render/appassets_devices.py`, `Resources/Devices/wheel/*.webp`, `Resources/Devices/controller/*.webp`
- Modify: `build.sh:47`
- Test: `Tests/CalmMouseAppTests/DeviceModelTests.swift` (resource half; the model half comes in Task 6)

**Interfaces:**
- Produces: `Resources/Devices/<name>/home.webp` (720 px) and `turn-00.webp` … `turn-71.webp` (600 px), with `<name>` ∈ {`wheel`, `controller`}. Frame `i` is the model turned `5·i` degrees counter-clockwise from home, the same convention as `Resources/MagicMouse/<finish>/`.

- [ ] **Step 1: Write the failing resource test.**

```swift
import XCTest

/// The rendered devices the app draws: a still and 72 turn frames each, plus the Magic Mouse's intro.
final class DeviceAssetTests: XCTestCase {
    private let resources = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("Resources")

    func testEveryDeviceHasAStillAndSeventyTwoFrames() {
        for folder in ["MagicMouse/white", "MagicMouse/black", "Devices/wheel", "Devices/controller"] {
            let dir = resources.appendingPathComponent(folder)
            XCTAssertTrue(FileManager.default.fileExists(atPath: dir.appendingPathComponent("home.webp").path), folder)
            for i in 0..<72 {
                let name = String(format: "turn-%02d.webp", i)
                XCTAssertTrue(FileManager.default.fileExists(atPath: dir.appendingPathComponent(name).path), "\(folder)/\(name)")
            }
        }
    }
}
```

Put it in `Tests/CalmMouseAppTests/DeviceModelTests.swift`.

- [ ] **Step 2: Run it and check it fails.**

Run: `swift test --scratch-path ~/Library/Caches/CalmMouse/build --filter DeviceAssetTests`
Expected: FAIL, missing `Devices/wheel/home.webp`.

- [ ] **Step 3: Render the spins** (in the background; this takes a while):

```bash
cd "$DEV/render"
MM_SPIN_RES=600 "$BLENDER" -b --factory-startup --python wheelmouse.py -- "$DEV/out/spin600-wheel/f####.png" 128 spin 72
MM_SPIN_RES=600 "$BLENDER" -b --factory-startup --python controller.py -- "$DEV/out/spin600-controller/f####.png" 128 spin 72
```

Expected: 72 PNGs in each folder, frame 0 matching the approved desk still (at 600 px).

- [ ] **Step 4: Write `appassets_devices.py`.** It is `$MM/render/appassets.py` without the web sprite, parameterised by model:

```python
# appassets_devices.py <out dir> <repo> <wheel|controller> : Resources/Devices/<name>/ from the desk still + 600 px spin.
import os, sys
from PIL import Image, ImageFilter, ImageChops
R, REPO, NAME = sys.argv[1:4]
def clean(im, reach):
    im = im.convert("RGBA")
    a = im.split()[3]
    solid = a.point(lambda v: 255 if v > 250 else 0)
    near = solid.filter(ImageFilter.MaxFilter(reach | 1)).filter(ImageFilter.GaussianBlur(reach * 0.45))
    im.putalpha(ImageChops.multiply(a, near).point(lambda v: 0 if v < 3 else v))
    return im
out = f"{REPO}/Resources/Devices/{NAME}"
os.makedirs(out, exist_ok=True)
clean(Image.open(f"{R}/{NAME}-desk.png"), 61).save(f"{out}/home.webp", "WEBP", quality=90, method=6)
total = 0
for i in range(72):
    clean(Image.open(f"{R}/spin600-{NAME}/f{i:03d}.png"), 51).save(f"{out}/turn-{i:02d}.webp", "WEBP", quality=86, method=6)
    total += os.path.getsize(f"{out}/turn-{i:02d}.webp")
print(NAME, os.path.getsize(f"{out}/home.webp") // 1024, "KB still +", total // 1024, "KB frames")
```

Run it for both models:

```bash
python3 -I "$DEV/render/appassets_devices.py" "$DEV/out" "$REPO" wheel
python3 -I "$DEV/render/appassets_devices.py" "$DEV/out" "$REPO" controller
```

Expected: each prints about 1.5 MB in total (spec target). If either is over 2.5 MB, lower the frame quality to 80.

- [ ] **Step 5: Copy them in `build.sh`.** After line 47:

```zsh
# The rendered wheel mouse and controller: a still and 72 turn frames each.
cp -R Resources/Devices "$APP/Contents/Resources/"
```

- [ ] **Step 6: Run the test.** Expected: PASS.

- [ ] **Step 7: Commit.**

```bash
git add Resources/Devices build.sh Tests/CalmMouseAppTests/DeviceModelTests.swift
git commit -m "Rendered turntables of an ergonomic wheel mouse and a controller"
```

### Task 4: The Magic Mouse intro video

**Files:**
- Modify: `$MM/render/magicmouse.py` (add an `intro` view)
- Create: `$MM/render/intro.sh`, `Resources/MagicMouse/white/intro.mov`, `Resources/MagicMouse/black/intro.mov`
- Test: extend `DeviceAssetTests`

**Interfaces:**
- Produces: `Resources/MagicMouse/<finish>/intro.mov`: HEVC with alpha, 1080×1080, 30 fps, 90 frames (3.0 s). Its last frame is the desk view at home yaw, the same picture as `home.webp` at 1.5× the resolution.

- [ ] **Step 1: Write the failing test.** Add to `DeviceAssetTests`:

```swift
    func testTheMagicMouseIntroIsHEVCWithAlphaAndThreeSeconds() async throws {
        for finish in ["white", "black"] {
            let url = resources.appendingPathComponent("MagicMouse/\(finish)/intro.mov")
            let asset = AVURLAsset(url: url)
            let track = try XCTUnwrap(try await asset.loadTracks(withMediaType: .video).first, finish)
            let descriptions = try await track.load(.formatDescriptions)
            let format = try XCTUnwrap(descriptions.first)
            XCTAssertEqual(CMFormatDescriptionGetMediaSubType(format), kCMVideoCodecType_HEVCWithAlpha, finish)
            let seconds = try await asset.load(.duration).seconds
            XCTAssertEqual(seconds, 3.0, accuracy: 0.05, finish)
            let size = try FileManager.default.attributesOfItem(atPath: url.path)[.size] as! Int
            XCTAssertLessThanOrEqual(size, 4_000_000, "\(finish) intro is over the 4 MB budget")
        }
    }
```

Add `import AVFoundation` at the top of the file. The `async` load APIs are macOS 12+, which is fine for tests.

- [ ] **Step 2: Run it and check it fails** (the file is missing).

- [ ] **Step 3: Add the `intro` view to `magicmouse.py`.** In the camera block, add a branch before `elif VIEW in ("desk", "spin")`:

```python
elif VIEW == "intro":               # the onboarding intro: rises, turns 1¼ times, settles in the desk view
    res = (int(os.environ.get("MM_INTRO_RES", "1080")),) * 2
    orbit(0, 40, 39, 105, (0.0, 0.0, 0.75))
    MOUSE.rotation_euler = (0, 0, math.radians(HOME_YAW))
```

Add `"intro"` to `DESK = VIEW in ("desk", "spin")` so it is lit and has materials like the desk view. In the render block, add a branch before the final `else:`:

```python
elif VIEW == "intro":
    # 90 frames at 30 fps. 0–15: a thin blue light sweeps across. 9–66: the mouse comes up from
    # the bottom of the frame, turning 1¼ times and slowing, while the camera rises from 12° to
    # 40°. 66–84: a highlight crosses the lid. 85–89: still, and frame 89 is the desk view.
    def ease(t): return 1 - (1 - min(max(t, 0), 1)) ** 3
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 4, 0.05))
    bar = bpy.context.active_object
    bar.scale = (0.08, 30, 1)
    bar_mat, bp = principled("SweepLight")
    put(bp, "Emission Color", (0.16, 0.5, 1.0, 1)); put(bp, "Emission Strength", 0.0)
    put(bp, "Base Color", (0, 0, 0, 1)); put(bp, "Alpha", 0.0)
    bar.data.materials.append(bar_mat)
    streak_obj = bpy.data.objects.get("Streak")       # present when MM_STREAK > 0
    y0 = knob("MM_INTRO_Y0", -16.0)                   # how far towards the camera it starts
    for f in range(90):
        t = f / 89
        s = ease((f - 9) / 57)
        orbit(0, 12 + 28 * s, 39, 105, (0.0, 0.0, 0.75))
        MOUSE.location = (0, y0 * (1 - s), 0)
        MOUSE.rotation_euler = (0, 0, math.radians(HOME_YAW + 450 * (1 - s)))
        k = min(max(f / 15, 0), 1)
        bar.location.x = -30 + 60 * k
        glow = math.sin(math.pi * k) if f <= 15 else 0.0
        put(bp, "Emission Strength", 6.0 * glow); put(bp, "Alpha", glow)
        if streak_obj is not None:
            h = (f - 66) / 18
            streak_obj.location.x = -40 + 80 * min(max(h, 0), 1)
            streak_obj.hide_render = not (0 <= h <= 1)
        scene.render.filepath = OUT.replace("####", "%03d" % f)
        bpy.ops.render.render(write_still=True)
        print("WROTE", scene.render.filepath, flush=True)
```

`MOUSE` must be the parent of every Magic Mouse part. Check this where `MOUSE` is created; if parts aren't parented, parent them before the loop. The material on the bar needs `blend_method` set to alpha in Eevee, but in Cycles the alpha input is enough.

- [ ] **Step 4: Preview at low samples.** Render frames with 16 samples at 540 px and put frames 0, 10, 30, 50, 66, 75 and 89 in one contact sheet:

```bash
cd "$MM/render"
MM_STREAK=1.0 MM_INTRO_RES=540 "$BLENDER" -b --factory-startup --python magicmouse.py -- "$MM/out/intro-preview/f####.png" white 16 intro
python3 -I -c "
from PIL import Image; import sys
fs=[0,10,30,50,66,75,89]; ims=[Image.open(f'$MM/out/intro-preview/f{f:03d}.png') for f in fs]
s=Image.new('RGBA',(540*len(fs),540),(255,255,255,255))
[s.alpha_composite(im,(540*i,0)) for i,im in enumerate(ims)]; s.save('$MM/out/intro-sheet.png')"
```

Check: at frame 0 the mouse is entirely out of the frame below (if not, make `MM_INTRO_Y0` more negative); the blue line crosses between frames 0 and 15; at frame 89 there's no streak. Tune `MM_INTRO_Y0` and the 450° turn until the motion reads well.

- [ ] **Step 5: Check the last frame against the desk still.**

```bash
MM_INTRO_RES=720 "$BLENDER" -b --factory-startup --python magicmouse.py -- "$MM/out/intro-last/f####.png" white 128 intro  # stop after it writes f089, or render all
python3 -I -c "
from PIL import Image, ImageChops, ImageStat
a=Image.open('$MM/out/intro-last/f089.png').convert('RGBA'); b=Image.open('$MM/assets/desk-white.png').convert('RGBA')
print('mean abs diff', [round(v,2) for v in ImageStat.Stat(ImageChops.difference(a,b)).mean])"
```

Expected: every channel's mean difference is under 2 (render noise only). A larger difference means the camera or the yaw doesn't end where the desk view is; fix that before going on.

- [ ] **Step 6: Render both finishes for real** (in the background, about an hour):

```bash
cd "$MM/render"
for F in white black; do
  MM_STREAK=1.0 "$BLENDER" -b --factory-startup --python magicmouse.py -- "$MM/out/intro-$F/f####.png" $F 128 intro
done
```

- [ ] **Step 7: Encode as HEVC with alpha.** Create `$MM/render/intro.sh`:

```zsh
#!/bin/zsh
# intro.sh <finish> <repo> : PNG frames → Resources/MagicMouse/<finish>/intro.mov (HEVC with alpha).
set -euo pipefail
F=$1; REPO=$2
SRC=/Users/malik/Documents/CalmMouse/.claude/design/magic-mouse/out/intro-$F
ffmpeg -y -framerate 30 -i "$SRC/f%03d.png" -c:v hevc_videotoolbox -alpha_quality 0.9 \
  -b:v 7M -tag:v hvc1 -pix_fmt bgra -movflags +faststart "$REPO/Resources/MagicMouse/$F/intro.mov"
ls -l "$REPO/Resources/MagicMouse/$F/intro.mov"
```

Run it for `white` and `black`. If a file is over 4 MB, lower `-b:v` in steps of 1M.

- [ ] **Step 8: Run the test.** Expected: PASS. Make a GIF for Malik:

```bash
ffmpeg -y -i "$REPO/Resources/MagicMouse/white/intro.mov" -vf "fps=30,scale=480:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse" "$DEV/out/intro-white.gif"
```

- [ ] **Step 9: GATE — send Malik the white and black GIFs** and wait for approval.

- [ ] **Step 10: Commit.**

```bash
git add Resources/MagicMouse/white/intro.mov Resources/MagicMouse/black/intro.mov Tests/CalmMouseAppTests/DeviceModelTests.swift
git commit -m "The Magic Mouse's onboarding intro, rendered in white and black"
```

---

## Phase B — Code

### Task 5: The route

**Files:**
- Create: `Sources/CalmMouseCore/OnboardingRoute.swift`
- Test: `Tests/CalmMouseCoreTests/OnboardingRouteTests.swift`

**Interfaces:**
- Produces:

```swift
public enum DeviceKind: String, CaseIterable, Sendable { case magicMouse, wheelMouse, controller }
public enum OnboardingStep: String, CaseIterable, Sendable {
    case devices, detect, permission, preset, tryIt, wheel, controller, done
}
public enum OnboardingRoute {
    public static func steps(for kinds: Set<DeviceKind>) -> [OnboardingStep]
}
```

- [ ] **Step 1: Write the failing tests.**

```swift
import XCTest
@testable import CalmMouseCore

final class OnboardingRouteTests: XCTestCase {
    private let head: [OnboardingStep] = [.devices, .detect, .permission]

    func testNothingFoundGoesStraightToDone() {
        XCTAssertEqual(OnboardingRoute.steps(for: []), head + [.done])
    }

    func testMagicMouseKeepsPresetAndTryIt() {
        XCTAssertEqual(OnboardingRoute.steps(for: [.magicMouse]), head + [.preset, .tryIt, .done])
    }

    func testWheelMouseGetsItsPage() {
        XCTAssertEqual(OnboardingRoute.steps(for: [.wheelMouse]), head + [.wheel, .done])
    }

    func testControllerGetsItsPage() {
        XCTAssertEqual(OnboardingRoute.steps(for: [.controller]), head + [.controller, .done])
    }

    func testSeveralKindsRunInAFixedOrder() {
        XCTAssertEqual(OnboardingRoute.steps(for: [.controller, .magicMouse]),
                       head + [.preset, .tryIt, .controller, .done])
        XCTAssertEqual(OnboardingRoute.steps(for: Set(DeviceKind.allCases)),
                       head + [.preset, .tryIt, .wheel, .controller, .done])
    }
}
```

- [ ] **Step 2: Run them and check they fail.** Run: `swift test --scratch-path ~/Library/Caches/CalmMouse/build --filter OnboardingRouteTests`. Expected: build error, `OnboardingRoute` not found.

- [ ] **Step 3: Implement.**

```swift
import Foundation

/// What onboarding can find connected. A trackpad is never one of them.
public enum DeviceKind: String, CaseIterable, Sendable {
    case magicMouse, wheelMouse, controller
}

public enum OnboardingStep: String, CaseIterable, Sendable {
    case devices, detect, permission, preset, tryIt, wheel, controller, done
}

/// The onboarding steps for what is connected: the same opening, then a stretch per kind of
/// device in a fixed order, then done. Worked out once, when the person leaves the detection
/// page, so plugging something in later never changes how many steps are left.
public enum OnboardingRoute {
    public static func steps(for kinds: Set<DeviceKind>) -> [OnboardingStep] {
        var steps: [OnboardingStep] = [.devices, .detect, .permission]
        if kinds.contains(.magicMouse) { steps += [.preset, .tryIt] }
        if kinds.contains(.wheelMouse) { steps.append(.wheel) }
        if kinds.contains(.controller) { steps.append(.controller) }
        return steps + [.done]
    }
}
```

- [ ] **Step 4: Run the tests.** Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add Sources/CalmMouseCore/OnboardingRoute.swift Tests/CalmMouseCoreTests/OnboardingRouteTests.swift
git commit -m "Onboarding's steps follow the devices that are connected"
```

### Task 6: `DeviceModel` — the stage draws any of the three

**Files:**
- Modify: `Sources/CalmMouse/MouseShowcase.swift` (`MouseShowcase` lines 9–65, `MouseStageCanvas` 71–86, `MouseFrames` 402–433)
- Test: `Tests/CalmMouseAppTests/DeviceModelTests.swift` (add the `DeviceModelTests` class)

**Interfaces:**
- Produces:

```swift
enum DeviceModel: String, CaseIterable {
    case magicMouse, wheelMouse, controller
    /// The Resources subfolder holding home.webp and turn-00…71.webp.
    func folder(_ finish: MouseFinish) -> String
    /// Stories on the lid, the presence ring and the White/Black menu: Magic Mouse only.
    var isMagicMouse: Bool { self == .magicMouse }
    var accessibilityName: String
    init?(_ kind: DeviceKind)
}
MouseFrames.shared(for model: DeviceModel, finish: MouseFinish) -> MouseFrames
MouseShowcase(model: DeviceModel = .magicMouse, preview:, connected:, presetID:, idle:)
```

- [ ] **Step 1: Write the failing test.**

```swift
@testable import CalmMouse
import CalmMouseCore

final class DeviceModelTests: XCTestCase {
    func testFolders() {
        XCTAssertEqual(DeviceModel.magicMouse.folder(.white), "MagicMouse/white")
        XCTAssertEqual(DeviceModel.magicMouse.folder(.black), "MagicMouse/black")
        // One colour each: the finish doesn't change them.
        XCTAssertEqual(DeviceModel.wheelMouse.folder(.black), "Devices/wheel")
        XCTAssertEqual(DeviceModel.controller.folder(.white), "Devices/controller")
    }

    func testFromDeviceKind() {
        XCTAssertEqual(DeviceModel(.magicMouse), .magicMouse)
        XCTAssertEqual(DeviceModel(.wheelMouse), .wheelMouse)
        XCTAssertEqual(DeviceModel(.controller), .controller)
    }
}
```

- [ ] **Step 2: Run it and check it fails** (`DeviceModel` not found).

- [ ] **Step 3: Implement `DeviceModel`** next to `MouseFinish` in `MouseShowcase.swift`:

```swift
/// The rendered devices the stage can show. Each is framed the same way in its 720 px still
/// (the camera backs off for bigger devices), so the stage geometry is shared.
enum DeviceModel: String, CaseIterable {
    case magicMouse, wheelMouse, controller

    init?(_ kind: DeviceKind) {
        switch kind {
        case .magicMouse: self = .magicMouse
        case .wheelMouse: self = .wheelMouse
        case .controller: self = .controller
        }
    }

    func folder(_ finish: MouseFinish) -> String {
        switch self {
        case .magicMouse: return "MagicMouse/" + finish.rawValue
        case .wheelMouse: return "Devices/wheel"
        case .controller: return "Devices/controller"
        }
    }

    var isMagicMouse: Bool { self == .magicMouse }

    var accessibilityName: String {
        switch self {
        case .magicMouse: return L("Magic Mouse")
        case .wheelMouse: return L("Scroll-wheel mouse")
        case .controller: return L("Game controller")
        }
    }
}
```

- [ ] **Step 4: Key `MouseFrames` by folder.** Replace the cache and initialiser:

```swift
final class MouseFrames {
    private static var byFolder: [String: MouseFrames] = [:]

    static func shared(for model: DeviceModel = .magicMouse, finish: MouseFinish) -> MouseFrames {
        let folder = model.folder(finish)
        if let frames = byFolder[folder] { return frames }
        let frames = MouseFrames(folder: folder)
        byFolder[folder] = frames
        return frames
    }

    private let folder: String
    private let cache = NSCache<NSNumber, CGImage>()
    private lazy var still: CGImage? = load("home")

    private init(folder: String) {
        self.folder = folder
        cache.countLimit = 12
    }
    // rest, turn(_:), load(_:) unchanged
}
```

Existing callers of `MouseFrames.shared(for: finish)` become `MouseFrames.shared(for: .magicMouse, finish: finish)`. Search with `grep -rn "MouseFrames.shared" Sources Tests`.

- [ ] **Step 5: Give `MouseShowcase` a model.** Add `var model: DeviceModel = .magicMouse` as the first stored property. Then:
  - `MouseStageCanvas` gains `let model: DeviceModel`, and `updateNSView` sets `view.frames = MouseFrames.shared(for: model, finish: finish)`.
  - Pass `preview: model.isMagicMouse ? preview : nil` to the canvas, so the other models never play stories.
  - Show `.contextMenu { … }` only `if model.isMagicMouse` (wrap the modifier in a small `@ViewBuilder` helper or use `.contextMenu(model.isMagicMouse ? ContextMenu { … } : nil)`).
  - `.accessibilityLabel(model.accessibilityName)`.
  - `.onAppear { if model.isMagicMouse { model.playRing() } }`. Name clash: the stage model is also called `model`, so rename the `@StateObject` to `stage` throughout the struct.
  - Do the same for the `connected` and `presetID` `onChange` ring triggers.

- [ ] **Step 6: Run all tests.** Run: `swift test --scratch-path ~/Library/Caches/CalmMouse/build`. Expected: all PASS, including the existing `MouseStoryTests`, `MouseStageFadeTests` and `DevicesPaneSizeTests`.

- [ ] **Step 7: Commit.**

```bash
git add Sources/CalmMouse/MouseShowcase.swift Tests/CalmMouseAppTests/DeviceModelTests.swift
git commit -m "The device stage can show the wheel mouse and the controller too"
```

### Task 7: Settings › Devices uses the turntables

**Files:**
- Modify: `Sources/CalmMouse/SettingsWindow.swift:557-575` (`DeviceColumn.showcase`), `:593-596` (`symbol`), `:600-608` (`hero`)
- Modify: `Resources/*.lproj/Localizable.strings` (`"Scroll-wheel mouse"`, `"Game controller"` if missing)

**Interfaces:**
- Consumes: `DeviceModel`, `MouseShowcase(model:…)` from Task 6.

- [ ] **Step 1: Map an entry to a model.** Add to `DeviceColumn`:

```swift
    /// The rendered device for an entry: what it is handled as, so a mouse switched with
    /// "Treat as" shows as what it is treated as. A mouse CalmMouse leaves alone keeps its symbol.
    private func deviceModel(_ device: DeviceEntry) -> DeviceModel? {
        if device.controller != nil { return .controller }
        switch device.kind {
        case .magicMouse: return .magicMouse
        case .wheelMouse: return .wheelMouse
        case nil: return nil
        }
    }
```

- [ ] **Step 2: Replace `showcase`.**

```swift
    @ViewBuilder private var showcase: some View {
        if let selected, let model = deviceModel(selected) {
            if model.isMagicMouse {
                // The rendered mouse plays each setting's story on itself.
                MouseShowcase(preview: focus.preview, connected: selected.connected,
                              presetID: settings.activePreset?.id) { details(selected, turnHint: true) }
            } else {
                // Turns like the Magic Mouse; pointing at a setting shows its sketch instead.
                ZStack(alignment: .top) {
                    MouseShowcase(model: model, preview: nil, connected: selected.connected) {
                        details(selected, turnHint: true)
                    }
                    .opacity(focus.preview == nil ? 1 : 0)
                    .animation(.easeOut(duration: 0.2), value: focus.preview == nil)
                    if let preview = focus.preview {
                        PreviewCanvas(preview: preview)
                            .frame(width: 250, height: 150)
                            .padding(.top, (MouseStage.size.height - 150) / 2)
                    }
                }
            }
        } else if let selected {
            ZStack(alignment: .top) {
                hero(selected).opacity(focus.preview == nil ? 1 : 0)
                if let preview = focus.preview {
                    PreviewCanvas(preview: preview).frame(width: 250, height: 150)
                }
            }
        } else if let preview = focus.preview {
            PreviewCanvas(preview: preview).frame(width: 250, height: 150)
        }
    }
```

`symbol(_:)` now only serves unmanaged mice; delete its `gamecontroller` branch.

- [ ] **Step 3: Strings.** Run `grep -n '"Scroll-wheel mouse"\|"Game controller"' Resources/*/Localizable.strings`. Add any that are missing: en "Scroll-wheel mouse" / zh-Hans "滚轮鼠标" / zh-Hant "滾輪滑鼠"; en "Game controller" / zh-Hans "游戏手柄" / zh-Hant "遊戲控制器". Copy the existing wording if the key is already there.

- [ ] **Step 4: Run all tests.** Expected: PASS. If `DevicesPaneSizeTests` fails because a wheel or controller column is now taller, read the test's comment: Devices opens at about 600 pt. Check the new minimum height still fits and update the expectation only if the spec allows it (it does: the column's minimum height is its natural height).

- [ ] **Step 5: Build and look.** Build with `./build.sh debug`, then run it as described in Task 11 Step 1. Open Settings › Devices with a wheel mouse and the DualSense connected. Drag each to turn it, double-click to send it home, and point at a setting to see the sketch. Confirm CPU at rest with `top -pid $(pgrep -n CalmMouse) -l 3 -stats cpu`; it should read 0.0 after a few seconds idle.

- [ ] **Step 6: Commit.**

```bash
git add Sources/CalmMouse/SettingsWindow.swift Resources/*.lproj/Localizable.strings
git commit -m "Settings › Devices shows the wheel mouse and the controller rendered, and they turn"
```

### Task 8: Detection model

**Files:**
- Create: `Sources/CalmMouse/DeviceDetector.swift`
- Test: `Tests/CalmMouseAppTests/DeviceDetectorTests.swift`

**Interfaces:**
- Consumes: `ConnectedMouse` (`MouseScanner.swift`), `ConnectedController` (`GamepadController.swift`), `MouseEnvironment.shared`, `GamepadController.shared`, `DeviceKind`.
- Produces:

```swift
struct DetectedDevice: Equatable, Identifiable {
    let kind: DeviceKind
    let name: String
    let battery: Int?
    /// More connected devices of the same kind, shown as "+n".
    let more: Int
    var id: DeviceKind { kind }
}
final class DeviceDetector: ObservableObject {
    @Published private(set) var devices: [DetectedDevice]
    var kinds: Set<DeviceKind> { get }
    func start()   // scans now and every second
    func stop()
    static func detected(mice: [ConnectedMouse], controllers: [ConnectedController],
                         batteries: [String: Int]) -> [DetectedDevice]
}
```

- [ ] **Step 1: Write the failing tests.**

```swift
import XCTest
@testable import CalmMouse
import CalmMouseCore

final class DeviceDetectorTests: XCTestCase {
    private func mouse(_ name: String, _ kind: MouseKind?) -> ConnectedMouse {
        ConnectedMouse(name: name, kind: kind,
                       info: .init(product: name, vendorID: 0, productID: 0, transport: "Bluetooth"))
    }

    func testOneOfEachInAFixedOrder() {
        let found = DeviceDetector.detected(
            mice: [mouse("MX Master 3S", .wheelMouse), mouse("Magic Mouse", .magicMouse)],
            controllers: [ConnectedController(name: "DualSense Wireless Controller", hasTouchpad: true, battery: 80)],
            batteries: ["Magic Mouse": 64])
        XCTAssertEqual(found.map(\.kind), [.magicMouse, .wheelMouse, .controller])
        XCTAssertEqual(found[0].battery, 64)
        XCTAssertEqual(found[2].battery, 80)
    }

    func testSameKindTwiceShowsTheFirstAndCountsTheRest() {
        let found = DeviceDetector.detected(
            mice: [mouse("A Mouse", .wheelMouse), mouse("B Mouse", .wheelMouse)], controllers: [], batteries: [:])
        XCTAssertEqual(found, [DetectedDevice(kind: .wheelMouse, name: "A Mouse", battery: nil, more: 1)])
    }

    func testMiceCalmMouseLeavesAloneAreNotFound() {
        XCTAssertEqual(DeviceDetector.detected(mice: [mouse("Plain", nil)], controllers: [], batteries: [:]), [])
    }
}
```

`ConnectedMouse`'s memberwise initialiser is internal, and `DeviceInfo.init(product:vendorID:productID:transport:)` is the one `MouseScanner` uses for the battery fallback. If `DeviceInfo` needs `builtIn:`/`usages:`, those have defaults; check `DeviceIdentifier.swift`.

- [ ] **Step 2: Run them and check they fail.**

- [ ] **Step 3: Implement.**

```swift
import Combine
import Foundation
import CalmMouseCore

struct DetectedDevice: Equatable, Identifiable {
    let kind: DeviceKind
    let name: String
    let battery: Int?
    let more: Int
    var id: DeviceKind { kind }
}

/// What onboarding's detection page shows: the devices CalmMouse can do something for,
/// checked every second while the page is open so plugging one in shows up at once.
final class DeviceDetector: ObservableObject {
    @Published private(set) var devices: [DetectedDevice] = []
    var kinds: Set<DeviceKind> { Set(devices.map(\.kind)) }

    private let mice: MouseEnvironment
    private let gamepad: GamepadController
    private var timer: Timer?

    init(mice: MouseEnvironment = .shared, gamepad: GamepadController = .shared) {
        self.mice = mice
        self.gamepad = gamepad
    }

    func start() {
        guard timer == nil else { return }
        scan()
        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in self?.scan() }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    deinit { timer?.invalidate() }

    private func scan() {
        mice.refresh()
        gamepad.refreshList()
        let found = Self.detected(mice: mice.connected, controllers: gamepad.controllers, batteries: mice.batteries)
        if found != devices { devices = found }
    }

    static func detected(mice: [ConnectedMouse], controllers: [ConnectedController],
                         batteries: [String: Int]) -> [DetectedDevice] {
        func one(_ kind: DeviceKind, _ names: [String], battery: (String) -> Int?) -> DetectedDevice? {
            guard let first = names.first else { return nil }
            return DetectedDevice(kind: kind, name: first, battery: battery(first), more: names.count - 1)
        }
        let magic = mice.filter { $0.kind == .magicMouse }.map(\.name)
        let wheel = mice.filter { $0.kind == .wheelMouse }.map(\.name)
        return [
            one(.magicMouse, magic) { batteries[$0] },
            one(.wheelMouse, wheel) { batteries[$0] },
            one(.controller, controllers.map(\.name)) { name in controllers.first { $0.name == name }?.battery },
        ].compactMap { $0 }
    }
}
```

`refreshList()` must not be `private`. It is currently `func refreshList()` (internal), so this works.

- [ ] **Step 4: Run the tests.** Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add Sources/CalmMouse/DeviceDetector.swift Tests/CalmMouseAppTests/DeviceDetectorTests.swift
git commit -m "Onboarding can see which devices are connected, and keeps looking"
```

### Task 9: The intro player

**Files:**
- Create: `Sources/CalmMouse/IntroVideoView.swift`
- Modify: `Package.swift` (link AVFoundation)

**Interfaces:**
- Produces: `IntroVideo(finish: MouseFinish, startAtEnd: Bool = false, onEnd: () -> Void)`, a SwiftUI view that plays `MagicMouse/<finish>/intro.mov` once, holds the last frame, and calls `onEnd` once. When `startAtEnd` is true, Reduce Motion is on, or the file is missing, it shows the last frame and calls `onEnd` at once.

- [ ] **Step 1: Link the framework.** In `Package.swift`, add `.linkedFramework("AVFoundation"),` to the `CalmMouse` target's `linkerSettings`.

- [ ] **Step 2: Implement.**

```swift
import AVFoundation
import AppKit
import SwiftUI

/// The Magic Mouse's onboarding intro: plays once and holds its last frame, which is the
/// mouse at rest on the desk. Nothing runs once it has ended.
struct IntroVideo: NSViewRepresentable {
    let finish: MouseFinish
    /// Already played in this onboarding: show the mouse at rest straight away.
    var startAtEnd = false
    var onEnd: () -> Void = {}

    func makeNSView(context: Context) -> IntroVideoNSView {
        IntroVideoNSView(finish: finish, startAtEnd: startAtEnd, onEnd: onEnd)
    }

    func updateNSView(_ view: IntroVideoNSView, context: Context) {}

    static func dismantleNSView(_ view: IntroVideoNSView, coordinator: ()) { view.tearDown() }
}

final class IntroVideoNSView: NSView {
    private let player: AVPlayer?
    private let playerLayer = AVPlayerLayer()
    private var endObserver: NSObjectProtocol?
    private var onEnd: (() -> Void)?

    init(finish: MouseFinish, startAtEnd: Bool, onEnd: @escaping () -> Void) {
        let url = Bundle.main.url(forResource: "intro", withExtension: "mov",
                                  subdirectory: DeviceModel.magicMouse.folder(finish))
        player = url.map { AVPlayer(playerItem: AVPlayerItem(url: $0)) }
        self.onEnd = onEnd
        super.init(frame: .zero)
        wantsLayer = true
        layer?.backgroundColor = .clear
        playerLayer.player = player
        playerLayer.videoGravity = .resizeAspect
        playerLayer.pixelBufferAttributes = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA]
        layer?.addSublayer(playerLayer)
        setAccessibilityElement(false)

        guard let player, let item = player.currentItem else { finish_(); return }
        player.isMuted = true
        player.actionAtItemEnd = .pause
        endObserver = NotificationCenter.default.addObserver(
            forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main) { [weak self] _ in self?.finish_() }
        if startAtEnd || NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            // Straight to the mouse at rest.
            item.seek(to: item.asset.duration, toleranceBefore: .zero, toleranceAfter: .zero) { [weak self] _ in
                DispatchQueue.main.async { self?.finish_() }
            }
        } else {
            player.play()
        }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    override func layout() {
        super.layout()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        playerLayer.frame = bounds
        CATransaction.commit()
    }

    private func finish_() {
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
        endObserver = nil
        let callback = onEnd
        onEnd = nil
        callback?()
    }

    func tearDown() {
        player?.pause()
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
        endObserver = nil
        onEnd = nil
    }
}
```

`item.asset.duration` is deprecated on macOS 13 but still works. If the build warns, use `CMTime(value: 89, timescale: 30)`, the last frame's time.

- [ ] **Step 3: Build.** Run: `swift build --scratch-path ~/Library/Caches/CalmMouse/build`. Expected: it builds with no new warnings.

- [ ] **Step 4: Commit.**

```bash
git add Sources/CalmMouse/IntroVideoView.swift Package.swift
git commit -m "A player for the Magic Mouse's intro that stops when it ends"
```

### Task 10: Onboarding pages and navigation

**Files:**
- Create: `Sources/CalmMouse/OnboardingDevices.swift`
- Modify: `Sources/CalmMouse/Onboarding.swift` (window size, `OnboardingStep` removal, navigation, Done page), `Sources/CalmMouse/AppDelegate.swift:476-487`
- Modify: `Resources/{en,zh-Hans,zh-Hant}.lproj/Localizable.strings`

**Interfaces:**
- Consumes: `OnboardingRoute`, `OnboardingStep`, `DeviceKind` (Task 5); `DeviceModel`, `MouseFrames`, `MouseFinish` (Task 6); `DeviceDetector`, `DetectedDevice` (Task 8); `IntroVideo` (Task 9).
- Produces: `SupportedDevicesStep()`, `DetectStep(detector:, introPlayed: Binding<Bool>)`, `WheelStep(settings:)`, `ControllerStep(settings:, hasTouchpad:)`, `DeviceArt(model:, finish:, side:)`.

- [ ] **Step 1: `DeviceArt`, a rendered still as a SwiftUI view.** Put it in `OnboardingDevices.swift`:

```swift
import AppKit
import SwiftUI
import CalmMouseCore

/// A device's rendered still (the frame the stage rests on), `side` points square.
struct DeviceArt: View {
    let model: DeviceModel
    var finish: MouseFinish = .white
    let side: CGFloat

    var body: some View {
        if let image = MouseFrames.shared(for: model, finish: finish).rest {
            Image(decorative: image, scale: 720 / side)   // 720 px still drawn at `side` pt
                .resizable()
                .interpolation(.high)
                .frame(width: side, height: side)
        } else {
            Color.clear.frame(width: side, height: side)
        }
    }
}
```

- [ ] **Step 2: Supported-devices page.**

```swift
struct SupportedDevicesStep: View {
    @AppStorage("magicMouseFinish") private var finishRaw = MouseFinish.white.rawValue
    @State private var shown = false
    private let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion

    private var items: [(DeviceModel, String)] {
        [(.magicMouse, L("Clicks no longer make the page jump")),
         (.wheelMouse, L("Flip the direction, scroll smoothly")),
         (.controller, L("The PS5 touchpad, or any controller's sticks"))]
    }

    var body: some View {
        VStack(spacing: 22) {
            Text(L("CalmMouse makes these work better"))
                .font(.title).fontWeight(.semibold)
            HStack(alignment: .top, spacing: 12) {
                ForEach(Array(items.enumerated()), id: \.offset) { index, item in
                    VStack(spacing: 6) {
                        DeviceArt(model: item.0, finish: MouseFinish(rawValue: finishRaw) ?? .white, side: 190)
                        Text(item.0.accessibilityName).font(.headline)
                        Text(item.1)
                            .font(.callout).foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .frame(width: 190)
                    .opacity(shown ? 1 : 0)
                    .offset(y: shown ? 0 : 10)
                    .animation(reduceMotion ? nil : .easeOut(duration: 0.5).delay(0.12 * Double(index)), value: shown)
                }
            }
            Text(L("Trackpads aren't affected."))
                .font(.caption).foregroundStyle(.secondary)
        }
        .onAppear { shown = true }
    }
}
```

- [ ] **Step 3: Detection page.**

```swift
struct DetectStep: View {
    @ObservedObject var detector: DeviceDetector
    /// Once per onboarding: going back to this page doesn't play the intro again.
    @Binding var introPlayed: Bool
    @AppStorage("magicMouseFinish") private var finishRaw = MouseFinish.white.rawValue
    @State private var introEnded = false
    @State private var breathe = false
    private let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion

    private var finish: MouseFinish { MouseFinish(rawValue: finishRaw) ?? .white }
    private var magic: DetectedDevice? { detector.devices.first { $0.kind == .magicMouse } }
    private var others: [DetectedDevice] { detector.devices.filter { $0.kind != .magicMouse } }
    /// The intro owns the page until it ends; then everything sits side by side. A Magic Mouse
    /// on its own stays in the video, held on its last frame, so nothing jumps when it ends.
    private var playing: Bool { magic != nil && !introEnded && !introPlayed }
    private var magicAlone: Bool { magic != nil && others.isEmpty }

    var body: some View {
        VStack(spacing: 14) {
            Text(detector.devices.isEmpty ? L("Connect your mouse or controller") : L("Your devices"))
                .font(.title).fontWeight(.semibold)
            if detector.devices.isEmpty {
                waiting
            } else if playing || magicAlone {
                IntroVideo(finish: finish, startAtEnd: introPlayed) {
                    withAnimation(.easeInOut(duration: 0.45)) { introEnded = true; introPlayed = true }
                }
                .frame(width: 400, height: 400)
                if let magic, !playing { status(magic).transition(.opacity) }
            } else {
                HStack(alignment: .top, spacing: 16) {
                    ForEach(detector.devices) { device in card(device) }
                }
                .frame(maxHeight: .infinity)
            }
        }
        .onAppear { detector.start() }
        .onDisappear { detector.stop() }
    }

    private var waiting: some View {
        VStack(spacing: 12) {
            DeviceArt(model: .magicMouse, finish: finish, side: 220)
                .saturation(0).opacity(breathe ? 0.35 : 0.15)
                .animation(reduceMotion ? nil : .easeInOut(duration: 1.6).repeatForever(autoreverses: true), value: breathe)
                .onAppear { breathe = true }
            Text(L("Plug one in or turn it on. It shows up here as soon as your Mac sees it."))
                .foregroundStyle(.secondary).multilineTextAlignment(.center)
        }
        .frame(maxHeight: .infinity)
    }

    private func card(_ device: DetectedDevice) -> some View {
        let model = DeviceModel(device.kind)!
        let alone = detector.devices.count == 1
        return VStack(spacing: 6) {
            DeviceArt(model: model, finish: finish, side: alone ? 320 : 190)
            status(device)
        }
        .frame(width: alone ? 320 : 190)
        .transition(reduceMotion ? .opacity : .opacity.combined(with: .offset(y: 10)))
    }

    private func status(_ device: DetectedDevice) -> some View {
        VStack(spacing: 4) {
            Text(device.name).font(.headline).lineLimit(1)
            HStack(spacing: 6) {
                Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
                Text(device.kind == .magicMouse ? L("Connected") : L("Recognised"))
                if let battery = device.battery { Text("· \(battery)%").foregroundStyle(.secondary) }
                if device.more > 0 { Text("· +\(device.more)").foregroundStyle(.secondary) }
            }
            .font(.callout)
        }
    }
}
```

The repeat-forever breathing runs only while nothing is connected and the page is open. It is a SwiftUI animation and stops when the view goes away. That's acceptable under the "nothing moves needlessly" rule because the motion is the message.

- [ ] **Step 4: Wheel page.**

```swift
struct WheelStep: View {
    @ObservedObject var settings: Settings

    var body: some View {
        VStack(spacing: 16) {
            DeviceArt(model: .wheelMouse, side: 150)
            Text(L("Set up your scroll-wheel mouse")).font(.title).fontWeight(.semibold)
            GroupBox {
                VStack(alignment: .leading, spacing: 10) {
                    Toggle(L("Reverse scroll direction"), isOn: $settings.reverseWheelScroll)
                    Divider()
                    Toggle(L("Smooth scrolling"), isOn: $settings.smoothWheelScroll)
                }
                .padding(6)
            }
            .frame(maxWidth: 420)
            Text(L("Each mouse can be set up on its own in Settings › Devices."))
                .font(.caption).foregroundStyle(.secondary)
        }
    }
}
```

Before adding the toggle labels, check `WheelDeviceForm` in `SettingsWindow.swift` for the wording it already uses, and reuse the same keys so the app says it one way.

- [ ] **Step 5: Controller page.**

```swift
struct ControllerStep: View {
    @ObservedObject var settings: Settings
    let hasTouchpad: Bool

    var body: some View {
        VStack(spacing: 14) {
            DeviceArt(model: .controller, side: 150)
            Text(L("Use your Mac with a controller")).font(.title).fontWeight(.semibold)
            GroupBox {
                VStack(alignment: .leading, spacing: 10) {
                    Toggle(L("Use the controller to move the pointer"), isOn: $settings.controllerPointer)
                    Divider()
                    Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 6) {
                        if hasTouchpad { row(L("Touchpad"), L("Move and tap to click")) }
                        row(L("Left stick"), L("Move the pointer"))
                        row(L("Right stick"), L("Scroll"))
                        row(L("Bottom button or press the left stick"), L("Click"))
                        row(L("Right button or press the right stick"), L("Right-click"))
                    }
                    .font(.callout)
                }
                .padding(6)
            }
            .frame(maxWidth: 460)
            Text(L("Holding Options and Create for half a second turns it on or off too (Xbox: ≡ and ⧉). It steps aside while a game is in front."))
                .font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
        }
    }

    private func row(_ input: String, _ action: String) -> some View {
        GridRow {
            Text(input).foregroundStyle(.secondary)
            Text(action)
        }
    }
}
```

`hasTouchpad` comes from `GamepadController.shared.controllers.contains(where: \.hasTouchpad)` at the call site.

- [ ] **Step 6: Route-driven navigation in `Onboarding.swift`.**
  - Delete the private `enum OnboardingStep` (lines 40–42); the Core one replaces it, and `import CalmMouseCore` is already there.
  - Window: `window.setContentSize(NSSize(width: 680, height: 520))`, and `.frame(width: 680, height: 520)` in `OnboardingView`.
  - `OnboardingWindowController.init(settings:blockedCount:)` keeps its signature; `OnboardingView` creates its own `@StateObject private var detector = DeviceDetector()`.
  - State and body:

```swift
    @StateObject private var detector = DeviceDetector()
    @State private var route = OnboardingRoute.steps(for: [])
    @State private var index = 0
    @State private var introPlayed = false
    @ObservedObject private var gamepad = GamepadController.shared

    private var step: OnboardingStep { route[index] }

    // in body's switch:
    case .devices:    SupportedDevicesStep()
    case .detect:     DetectStep(detector: detector, introPlayed: $introPlayed)
    case .permission: PermissionStep()
    case .preset:     PresetStep(settings: settings)
    case .tryIt:      TryItStep(settings: settings, blockedCount: blockedCount)
    case .wheel:      WheelStep(settings: settings)
    case .controller: ControllerStep(settings: settings, hasTouchpad: gamepad.controllers.contains(where: \.hasTouchpad))
    case .done:       DoneStep(hasMagicMouse: route.contains(.preset))
```

  - Footer: the Back button is hidden when `index == 0`; the dots are `ForEach(route.indices, id: \.self)` with the current one filled; the last-step test is `step == .done`.
  - `move`:

```swift
    private func move(_ delta: Int) {
        // Leaving the detection page forwards fixes the route for what was found.
        if step == .detect && delta > 0 {
            route = OnboardingRoute.steps(for: detector.kinds)
        }
        let next = index + delta
        if route.indices.contains(next) { index = next }
    }
```

  - The current `WelcomeStep` is no longer reachable. Delete it. Its strings stay in the `.strings` files only if something else uses them; run `grep` first, and leave unused strings in place (harmless) rather than risk deleting a shared one.

- [ ] **Step 7: Done page.** `DoneStep` gains `let hasMagicMouse: Bool`. Show the "Settings → Magic Mouse" tip only when it is true; otherwise show `tip(symbol: "slider.horizontal.3", text: L("Every device has its own settings in Settings › Devices."))`.

- [ ] **Step 8: Strings.** Add every new `L("…")` key above to the three `.strings` files. The zh-Hans wording follows the spec:

| en | zh-Hans | zh-Hant |
|---|---|---|
| CalmMouse makes these work better | CalmMouse 让这些设备更好用 | CalmMouse 讓這些裝置更好用 |
| Clicks no longer make the page jump | 点按时页面不再乱跳 | 點按時頁面不再亂跳 |
| Flip the direction, scroll smoothly | 反转方向、平滑滚动 | 反轉方向、平滑捲動 |
| The PS5 touchpad, or any controller's sticks | PS5 触控板，或任何手柄的摇杆 | PS5 觸控板，或任何控制器的搖桿 |
| Trackpads aren't affected. | 触控板不受影响。 | 觸控板不受影響。 |
| Your devices | 你的设备 | 你的裝置 |
| Connect your mouse or controller | 连接你的鼠标或手柄 | 連接你的滑鼠或控制器 |
| Plug one in or turn it on. It shows up here as soon as your Mac sees it. | 插上或打开设备，Mac 一识别到就会出现在这里。 | 插上或打開裝置，Mac 一辨識到就會出現在這裡。 |
| Connected | 已连接 | 已連接 |
| Recognised | 已识别 | 已辨識 |
| Set up your scroll-wheel mouse | 调一下你的滚轮鼠标 | 調一下你的滾輪滑鼠 |
| Each mouse can be set up on its own in Settings › Devices. | 每只鼠标都可以在设置 › 设备里单独调。 | 每隻滑鼠都可以在設定 › 裝置裡單獨調整。 |
| Use your Mac with a controller | 用手柄操作 Mac | 用控制器操作 Mac |
| Use the controller to move the pointer | 用手柄控制指针 | 用控制器控制指標 |
| Touchpad | 触控板 | 觸控板 |
| Move and tap to click | 移动、轻点来点按 | 移動、輕點來點按 |
| Left stick | 左摇杆 | 左搖桿 |
| Right stick | 右摇杆 | 右搖桿 |
| Move the pointer | 移动指针 | 移動指標 |
| Scroll | 滚动 | 捲動 |
| Bottom button or press the left stick | 下方键，或按下左摇杆 | 下方鍵，或按下左搖桿 |
| Right button or press the right stick | 右侧键，或按下右摇杆 | 右側鍵，或按下右搖桿 |
| Click | 点按 | 點按 |
| Right-click | 右键点按 | 右鍵點按 |
| Holding Options and Create for half a second turns it on or off too (Xbox: ≡ and ⧉). It steps aside while a game is in front. | 同时按住 Options 和 Create 半秒也能开关（Xbox：≡ 和 ⧉）。游戏在前台时会自动停用。 | 同時按住 Options 和 Create 半秒也能開關（Xbox：≡ 和 ⧉）。遊戲在前景時會自動停用。 |
| Every device has its own settings in Settings › Devices. | 每个设备的设置都在设置 › 设备里。 | 每個裝置的設定都在設定 › 裝置裡。 |

Check each against what the `.strings` files already have for the same idea (for example "Scroll" or "Click" may already exist with a different translation) and reuse the existing translation.

- [ ] **Step 9: Run all tests.** Expected: PASS, including `LocalizationTests`.

- [ ] **Step 10: Commit.**

```bash
git add Sources/CalmMouse/Onboarding.swift Sources/CalmMouse/OnboardingDevices.swift Resources/*.lproj/Localizable.strings
git commit -m "Onboarding starts with the devices CalmMouse supports, recognises yours and only shows the steps for them"
```

### Task 11: Live verification

**Files:** none changed unless a check fails.

- [ ] **Step 1: Run the build.** Back up the preferences, build a signed bundle and open it with `open` (binaries exec'd from the shell never reach the window server):

```bash
defaults export com.calmmouse.app "$DEV/out/prefs-backup.plist"
./build.sh
osascript -e 'quit app "CalmMouse"'
open build/CalmMouse.app
```

- [ ] **Step 2: Re-run onboarding.** Open Settings › General › the welcome-tour button, or post `CalmMouseShowOnboarding`. Walk the whole flow with the Magic Mouse and the DualSense connected. Check: the intro plays once, holds its last frame, then the controller card fades in beside it; Back then Continue doesn't replay the intro; the dots count 7 steps (`devices, detect, permission, preset, tryIt, controller, done`).

- [ ] **Step 3: Other routes.** Turn the Magic Mouse off (or set it to Scroll Wheel in Treat As) and unplug the controller. Re-run onboarding: you should see the waiting state, then Continue → permission → done (4 dots). Plug the DualSense in while on the detection page: the card should appear within a second.

- [ ] **Step 4: Reduce Motion.** Turn on System Settings › Accessibility › Display › Reduce motion, re-run, and confirm the last frame is shown with no playback. Turn it back off afterwards.

- [ ] **Step 5: Screenshots.** Take every page in light and dark mode, using the AX-driven approach in memory `calmmouse-debugging-env` (don't move Malik's real pointer). Record a GIF of the detection page playing. Send them with SendUserFile.

- [ ] **Step 6: CPU and size.** With Settings › Devices open on each of the three devices, idle for 10 s: `top -pid $(pgrep -n CalmMouse) -l 3 -stats cpu` shows 0.0. Then check the size with `du -sh build/CalmMouse.app` and compare it to the 1.3.0 app in `/Applications`; the difference should be about 11 MB.

- [ ] **Step 7: Restore.** Run `defaults import com.calmmouse.app "$DEV/out/prefs-backup.plist"`, quit the test bundle and `open /Applications/CalmMouse.app`.

- [ ] **Step 8: Report to Malik** with the screenshots, the GIF and the size figure. Shipping it (version bump, release, cask) is his call and a separate step.
