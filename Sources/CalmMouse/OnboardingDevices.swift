import AppKit
import SwiftUI
import CalmMouseCore

/// A device's rendered still (the frame the stage rests on), `side` points square, in the
/// colour that device is set to: each mouse keeps its own; the controller has one.
struct DeviceArt: View {
    let model: DeviceModel
    let side: CGFloat

    @AppStorage("magicMouseFinish") private var magicMouseFinish = MouseFinish.white.rawValue
    @AppStorage("wheelMouseFinish") private var wheelMouseFinish = MouseFinish.white.rawValue

    private var finish: MouseFinish {
        let raw = model.finishKey == "wheelMouseFinish" ? wheelMouseFinish : magicMouseFinish
        return MouseFinish(rawValue: raw) ?? .white
    }

    var body: some View {
        if let image = MouseFrames.shared(for: model, finish: finish).rest {
            Image(decorative: image, scale: CGFloat(image.width) / side)
                .resizable()
                .interpolation(.high)
                .frame(width: side, height: side)
        } else {
            Color.clear.frame(width: side, height: side)
        }
    }
}

// MARK: - The devices CalmMouse supports

struct SupportedDevicesStep: View {
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
                ForEach(items, id: \.0) { item in
                    VStack(spacing: 6) {
                        DeviceArt(model: item.0, side: 190)
                        Text(item.0.accessibilityName).font(.headline)
                        Text(item.1)
                            .font(.callout).foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .frame(width: 190)
                    // Malik (2026-11-18): the tour's entrances fade, nothing flies in.
                    .opacity(shown || reduceMotion ? 1 : 0)
                    .animation(reduceMotion ? nil : .easeInOut(duration: 0.4), value: shown)
                }
            }
            Text(L("Trackpads aren't affected."))
                .font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxHeight: .infinity)
        .onAppear { shown = true }
    }
}

// MARK: - The devices that are connected

struct DetectStep: View {
    @ObservedObject var detector: DeviceDetector
    /// Once per onboarding: going back to this page doesn't play the intro again.
    @Binding var introPlayed: Bool
    @AppStorage("magicMouseFinish") private var magicMouseFinish = MouseFinish.white.rawValue
    @State private var introEnded = false
    @State private var breathe = false
    private let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion

    /// The intro's last frame is the Magic Mouse's still, so the video and a lone card share a size.
    private static let stageSide: CGFloat = 300

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
            Group {
                if detector.devices.isEmpty {
                    waiting
                } else if let magic, playing || magicAlone {
                    VStack(spacing: 6) {
                        IntroVideo(finish: MouseFinish(rawValue: magicMouseFinish) ?? .white, startAtEnd: introPlayed) {
                            withAnimation(.easeInOut(duration: 0.45)) { introEnded = true; introPlayed = true }
                        }
                        .frame(width: Self.stageSide, height: Self.stageSide)
                        .accessibilityHidden(true)
                        // Its place is kept while the intro plays, so the mouse doesn't move when it fades in.
                        status(magic)
                            .opacity(playing ? 0 : 1)
                            .accessibilityHidden(playing)
                    }
                } else {
                    HStack(alignment: .top, spacing: 16) {
                        ForEach(detector.devices) { device in card(device) }
                    }
                }
            }
            .frame(maxHeight: .infinity)
        }
        .onAppear { detector.start() }
        .onDisappear { detector.stop() }
    }

    private var waiting: some View {
        VStack(spacing: 12) {
            DeviceArt(model: .magicMouse, side: 220)
                .saturation(0).opacity(breathe || reduceMotion ? 0.35 : 0.15)
                .animation(reduceMotion ? nil : .easeInOut(duration: 1.6).repeatForever(autoreverses: true), value: breathe)
                .onAppear { breathe = true }
                .accessibilityHidden(true)
            Text(L("Plug one in or turn it on. It shows up here as soon as your Mac sees it."))
                .foregroundStyle(.secondary).multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func card(_ device: DetectedDevice) -> some View {
        let model = DeviceModel(device.kind)!
        let side = detector.devices.count == 1 ? Self.stageSide : 190
        return VStack(spacing: 6) {
            DeviceArt(model: model, side: side)
            status(device)
        }
        .frame(width: side)
        .transition(.opacity)
    }

    private func status(_ device: DetectedDevice) -> some View {
        VStack(spacing: 4) {
            Text(device.name).font(.headline)
                .lineLimit(2).multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 6) {
                Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
                Text(device.kind == .magicMouse ? L("Connected") : L("Recognised"))
                if let battery = device.battery { Text("· \(battery)%").foregroundStyle(.secondary) }
                if device.more > 0 { Text("· +\(device.more)").foregroundStyle(.secondary) }
            }
            .font(.callout)
        }
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Scroll-wheel mouse

struct WheelStep: View {
    @ObservedObject var settings: Settings

    var body: some View {
        VStack(spacing: 16) {
            DeviceArt(model: .wheelMouse, side: 150)
            Text(L("Set up your scroll-wheel mouse")).font(.title).fontWeight(.semibold)
            GroupBox {
                VStack(alignment: .leading, spacing: 10) {
                    // The same words as Settings › Devices, so the app says it one way.
                    OnboardingSwitch(title: L("Reverse direction"),
                                     caption: L("Flips only the wheel's direction.\nOther devices stay as they are."),
                                     isOn: $settings.reverseWheelScroll)
                    Divider()
                    OnboardingSwitch(title: L("Smooth scrolling"),
                                     caption: L("The page glides instead of jumping.\nZooming stays one step per notch."),
                                     isOn: $settings.smoothWheelScroll)
                }
                .padding(6)
            }
            .frame(maxWidth: 420)
            Text(L("Each mouse can be set up on its own in Settings → Devices."))
                .font(.caption).foregroundStyle(.secondary)
        }
    }
}

// MARK: - Game controller

struct ControllerStep: View {
    @ObservedObject var settings: Settings
    let hasTouchpad: Bool

    var body: some View {
        VStack(spacing: 12) {
            DeviceArt(model: .controller, side: 130)
            Text(L("Use your Mac with a controller")).font(.title).fontWeight(.semibold)
            GroupBox {
                VStack(alignment: .leading, spacing: 10) {
                    OnboardingSwitch(title: L("Use controller as mouse"), caption: nil,
                                     isOn: $settings.controllerPointer)
                    Divider()
                    Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 5) {
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
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: 460)
        }
    }

    private func row(_ input: String, _ action: String) -> some View {
        GridRow {
            Text(input).foregroundStyle(.secondary)
            Text(action)
        }
    }
}

/// A setting as Settings shows it: the name (and what it does, small) on the left, a switch on the right.
private struct OnboardingSwitch: View {
    let title: String
    let caption: String?
    @Binding var isOn: Bool

    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                if let caption {
                    Text(caption)
                        .font(.caption).foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 0)
            Toggle(title, isOn: $isOn)
                .labelsHidden()
                .toggleStyle(.switch)
        }
    }
}
