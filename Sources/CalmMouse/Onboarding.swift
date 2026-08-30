import AppKit
import SwiftUI
import CalmMouseCore

extension Notification.Name {
    /// Posted by the settings window's "welcome tour" button; AppDelegate listens.
    static let calmMouseShowOnboarding = Notification.Name("CalmMouseShowOnboarding")
}

// MARK: - Window

final class OnboardingWindowController: NSWindowController, NSWindowDelegate {
    /// Live count of scrolls CalmMouse has swallowed — the "try it" step shows it ticking.
    init(settings: Settings, blockedCount: @escaping () -> Int) {
        let root = OnboardingView(settings: settings, blockedCount: blockedCount)
        let hosting = NSHostingController(rootView: root)
        let window = NSWindow(contentViewController: hosting)
        window.title = L("Welcome to CalmMouse")
        window.styleMask = [.titled, .closable]
        window.setContentSize(NSSize(width: 560, height: 500))
        window.center()
        window.isReleasedWhenClosed = false
        super.init(window: window)
        window.delegate = self
        hosting.rootView = OnboardingView(settings: settings, blockedCount: blockedCount,
                                          finish: { [weak self] in self?.close() })
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    /// Closing the window — by the Finish button or the red dot — counts as done.
    /// The tour must never nag someone who already dismissed it.
    func windowWillClose(_ notification: Notification) {
        Settings.shared.onboardingCompleted = true
    }
}

// MARK: - Steps

private enum OnboardingStep: Int, CaseIterable {
    case welcome, permission, preset, tryIt, done
}

private struct OnboardingView: View {
    @ObservedObject var settings: Settings
    let blockedCount: () -> Int
    var finish: () -> Void = {}

    @State private var step: OnboardingStep = .welcome

    var body: some View {
        VStack(spacing: 0) {
            Group {
                switch step {
                case .welcome:    WelcomeStep()
                case .permission: PermissionStep()
                case .preset:     PresetStep(settings: settings)
                case .tryIt:      TryItStep(settings: settings, blockedCount: blockedCount)
                case .done:       DoneStep()
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .padding(.horizontal, 36)
            .padding(.top, 28)

            footer
        }
        .frame(width: 560, height: 500)
    }

    private var footer: some View {
        HStack {
            Button("Back") { move(-1) }
                .opacity(step == .welcome ? 0 : 1)

            Spacer()
            HStack(spacing: 7) {
                ForEach(OnboardingStep.allCases, id: \.rawValue) { s in
                    Circle()
                        .fill(s == step ? Color.accentColor : Color.primary.opacity(0.18))
                        .frame(width: 7, height: 7)
                }
            }
            Spacer()

            if step == .done {
                Button("Start Using CalmMouse") { finish() }
                    .keyboardShortcut(.defaultAction)
            } else {
                Button("Continue") { move(1) }
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .background(.bar)
    }

    private func move(_ delta: Int) {
        if let next = OnboardingStep(rawValue: step.rawValue + delta) {
            step = next
        }
    }
}

// MARK: Shared bits

private struct StepTitle: View {
    let title: String
    let subtitle: String

    var body: some View {
        VStack(spacing: 6) {
            Text(title).font(.title).fontWeight(.semibold)
            Text(subtitle)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity)
    }
}

// MARK: Step 1 — Welcome

private struct WelcomeStep: View {
    var body: some View {
        VStack(spacing: 18) {
            Image(nsImage: NSApp.applicationIconImage)
                .resizable().frame(width: 76, height: 76)
            StepTitle(title: L("Welcome to CalmMouse"),
                      subtitle: L("Your Magic Mouse's whole top is a touch surface — and it stays live while you click, so pages jump around. CalmMouse fixes that."))
            PreviewCanvas(preview: .blockWhileClick)
                .frame(width: 280, height: 168)
            Text("The page holds still while you click. That's the main fix — the next steps set it up.")
                .font(.caption).foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
    }
}

// MARK: Step 2 — Permission

private struct PermissionStep: View {
    @State private var trusted = AXIsProcessTrusted()
    private let timer = Timer.publish(every: 1.5, on: .main, in: .common).autoconnect()

    var body: some View {
        VStack(spacing: 18) {
            Image(systemName: "hand.raised.circle")
                .font(.system(size: 56)).foregroundStyle(Color.accentColor)
            StepTitle(title: L("Allow CalmMouse to see mouse events"),
                      subtitle: L("macOS calls this Accessibility access. It's how CalmMouse can tell a click from a scroll — it never sees keystrokes and never touches the network."))

            GroupBox {
                HStack(spacing: 10) {
                    Image(systemName: trusted ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                        .font(.title2)
                        .foregroundStyle(trusted ? .green : .orange)
                    Text(trusted ? L("Access granted — you're all set.")
                                 : L("Turn on CalmMouse in System Settings → Privacy & Security → Accessibility."))
                        .fixedSize(horizontal: false, vertical: true)
                    Spacer()
                    if !trusted {
                        Button("Open System Settings…") { SettingsHelpers.openAccessibilityPane() }
                    }
                }
                .padding(6)
            }

            if !trusted {
                Text("You can also do this later — CalmMouse waits in the menu bar and starts working within two seconds of being allowed.")
                    .font(.caption).foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
            }
        }
        .onReceive(timer) { _ in trusted = AXIsProcessTrusted() }
    }
}

// MARK: Step 3 — Pick a preset

private struct PresetStep: View {
    @ObservedObject var settings: Settings

    var body: some View {
        VStack(spacing: 14) {
            StepTitle(title: L("Pick a starting point"),
                      subtitle: L("One click sets everything up. You can tweak any single setting afterwards — and save your own presets in Settings → Presets."))

            VStack(spacing: 8) {
                ForEach(Preset.builtIn) { preset in
                    ChoiceCard(preset: preset,
                               selected: settings.currentPresetValues == preset.values,
                               choose: { settings.apply(preset) })
                }
            }
        }
    }
}

private struct ChoiceCard: View {
    let preset: Preset
    let selected: Bool
    let choose: () -> Void

    var body: some View {
        Button(action: choose) {
            HStack(spacing: 12) {
                Image(systemName: preset.symbolName)
                    .font(.title2)
                    .foregroundStyle(selected ? Color.white : Color.accentColor)
                    .frame(width: 30)
                VStack(alignment: .leading, spacing: 2) {
                    Text(LocalizedStringKey(preset.name)).fontWeight(.semibold)
                    Text(LocalizedStringKey(preset.summary))
                        .font(.caption)
                        .foregroundStyle(selected ? Color.white.opacity(0.85) : Color.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                        .multilineTextAlignment(.leading)
                }
                Spacer()
                Image(systemName: selected ? "checkmark.circle.fill" : "circle")
                    .font(.title3)
                    .foregroundStyle(selected ? Color.white : Color.secondary.opacity(0.5))
            }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: 10)
                    .fill(selected ? Color.accentColor : Color.primary.opacity(0.04)))
            .overlay(
                RoundedRectangle(cornerRadius: 10)
                    .strokeBorder(selected ? Color.clear : Color.primary.opacity(0.12)))
            .contentShape(RoundedRectangle(cornerRadius: 10))
        }
        .buttonStyle(.plain)
        .foregroundStyle(selected ? Color.white : Color.primary)
    }
}

// MARK: Step 4 — Try it

private struct TryItStep: View {
    @ObservedObject var settings: Settings
    let blockedCount: () -> Int

    @State private var trusted = AXIsProcessTrusted()
    @State private var count = 0
    @State private var startCount: Int?
    private let timer = Timer.publish(every: 0.5, on: .main, in: .common).autoconnect()

    private var blockedHere: Int { max(0, count - (startCount ?? count)) }

    var body: some View {
        VStack(spacing: 18) {
            StepTitle(title: L("Try it"),
                      subtitle: L("Grab your Magic Mouse and put the cursor over any page you can scroll — this window's background works too."))

            VStack(alignment: .leading, spacing: 10) {
                tryRow(number: "1", text: L("Click and hold the mouse button."))
                tryRow(number: "2", text: L("Slide your finger along the surface, like a scroll."))
                tryRow(number: "3", text: L("The page stays put — that scroll was swallowed."))
                if settings.tapToClick {
                    tryRow(number: "4", text: L("Also try a light tap — no press needed. That's tap to click."))
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            GroupBox {
                HStack(spacing: 10) {
                    if !trusted {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .foregroundStyle(.orange)
                        Text("Accessibility access isn't granted yet, so nothing is blocked. Go back one step to turn it on.")
                            .font(.callout)
                            .fixedSize(horizontal: false, vertical: true)
                    } else if blockedHere > 0 {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.title2).foregroundStyle(.green)
                        // LocalizedStringKey so the ** markdown still renders after translation.
                        Text(LocalizedStringKey(blockedHere == 1
                            ? L("It works! **1** accidental scroll calmed just now.")
                            : L("It works! **%ld** accidental scrolls calmed just now.", blockedHere)))
                            .font(.callout)
                    } else {
                        Image(systemName: "ellipsis.circle")
                            .font(.title2).foregroundStyle(.secondary)
                        Text("Waiting for a click-and-slide on the Magic Mouse…")
                            .font(.callout).foregroundStyle(.secondary)
                    }
                    Spacer()
                }
                .padding(6)
            }
        }
        .onReceive(timer) { _ in
            trusted = AXIsProcessTrusted()
            count = blockedCount()
            if startCount == nil { startCount = count }
        }
    }

    private func tryRow(number: String, text: String) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text(number)
                .font(.caption).bold().monospacedDigit()
                .frame(width: 18, height: 18)
                .background(Circle().fill(Color.accentColor.opacity(0.18)))
            Text(text)
        }
    }
}

// MARK: Step 5 — Done

private struct DoneStep: View {
    var body: some View {
        VStack(spacing: 18) {
            Image(systemName: "checkmark.seal.fill")
                .font(.system(size: 56)).foregroundStyle(.green)
            StepTitle(title: L("That's it"),
                      subtitle: L("CalmMouse lives in your menu bar and keeps working in the background."))

            VStack(alignment: .leading, spacing: 14) {
                tip(symbol: "magicmouse.fill",
                    text: L("Look for the mouse icon in the menu bar — the everyday switches live there. Orange means it needs attention, dimmed means paused."))
                tip(symbol: "info.circle",
                    text: L("In Settings, hover any option for a little animated preview of what it does."))
                tip(symbol: "slider.horizontal.3",
                    text: L("Changed your mind about the feel? Settings → Presets switches the whole setup in one click, or saves your own."))
            }
            .padding(.horizontal, 8)
        }
    }

    private func tip(symbol: String, text: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: symbol)
                .font(.title3)
                .foregroundStyle(Color.accentColor)
                .frame(width: 26)
            Text(text)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
