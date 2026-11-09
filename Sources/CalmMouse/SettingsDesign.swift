import SwiftUI

/// The settings window's design system. Every pane takes its type, spacing and sizes from here,
/// so they cannot drift apart. It deliberately adds very little: rows, section headers, switches
/// and pop-ups are the system's own grouped-form controls, at the system's own metrics.
enum Theme {
    /// macOS text styles. Row titles and section headers are the form's own.
    enum Text {
        /// 11 pt, always in the secondary colour.
        static let detail = Font.subheadline
        /// 15 pt. A symbol leading a two-line row.
        static let rowSymbol = Font.title3
    }

    /// A 4-point grid.
    enum Space {
        static let small: CGFloat = 4
        static let medium: CGFloat = 8
        static let large: CGFloat = 12
    }

    enum Size {
        static let paneWidth: CGFloat = 480
        /// App icons and other artwork that leads a row.
        static let rowIcon: CGFloat = 20
        /// Every slider track is this long, so a column of them lines up.
        static let sliderTrack: CGFloat = 150
        /// Column to the right of a slider that shows its value.
        static let sliderReading: CGFloat = 44
    }
}

extension View {
    /// Secondary text: a preview's caption, a slider's end label.
    func detailText() -> some View {
        font(Theme.Text.detail).foregroundStyle(.secondary)
    }
}

/// A slider row: title on the left, then the track, then either the current value or, with
/// `from:to:`, the words for each end of the scale.
///
/// A `step` snaps the value without drawing tick marks. A form slider's ticks are one mark per
/// step, which at 40 steps is a solid bar.
struct SliderRow: View {
    private let title: LocalizedStringKey
    @Binding private var value: Double
    private let range: ClosedRange<Double>
    private let step: Double?
    private let reading: String?
    private let ends: (LocalizedStringKey, LocalizedStringKey)?

    init(_ title: LocalizedStringKey, value: Binding<Double>, in range: ClosedRange<Double>,
         step: Double? = nil, reading: String) {
        self.title = title
        self._value = value
        self.range = range
        self.step = step
        self.reading = reading
        self.ends = nil
    }

    init(_ title: LocalizedStringKey, value: Binding<Double>, in range: ClosedRange<Double>,
         from low: LocalizedStringKey, to high: LocalizedStringKey) {
        self.title = title
        self._value = value
        self.range = range
        self.step = nil
        self.reading = nil
        self.ends = (low, high)
    }

    private var snapped: Binding<Double> {
        Binding(get: { value },
                set: { new in value = step.map { (new / $0).rounded() * $0 } ?? new })
    }

    var body: some View {
        HStack(spacing: Theme.Space.medium) {
            Text(title)
            Spacer(minLength: Theme.Space.large)
            if let ends { Text(ends.0).detailText() }
            Slider(value: snapped, in: range)
                .labelsHidden()
                .frame(width: Theme.Size.sliderTrack)
            if let ends {
                Text(ends.1).detailText()
            } else if let reading {
                Text(reading)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
                    .frame(width: Theme.Size.sliderReading, alignment: .trailing)
            }
        }
    }
}

/// A row that folds the rows after it away. `DisclosureGroup` inside a grouped form draws its
/// children as one undivided block; a header row plus ordinary rows keeps the separators.
struct DisclosureHeader<Label: View>: View {
    @Binding var isExpanded: Bool
    @ViewBuilder let label: () -> Label

    var body: some View {
        Button {
            withAnimation(.easeInOut(duration: 0.15)) { isExpanded.toggle() }
        } label: {
            HStack(spacing: Theme.Space.medium) {
                label()
                Spacer()
                Image(systemName: "chevron.right")
                    .font(Theme.Text.detail.weight(.semibold))
                    .foregroundStyle(.tertiary)
                    .rotationEffect(.degrees(isExpanded ? 90 : 0))
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}
