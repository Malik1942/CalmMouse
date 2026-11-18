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

        guard let player, let item = player.currentItem else { ended(); return }
        player.isMuted = true
        player.actionAtItemEnd = .pause
        endObserver = NotificationCenter.default.addObserver(
            forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main) { [weak self] _ in self?.ended() }
        if startAtEnd || NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            // Straight to the mouse at rest: the last frame of the 3 s, 30 fps intro.
            item.seek(to: CMTime(value: 89, timescale: 30), toleranceBefore: .zero, toleranceAfter: .zero) { [weak self] _ in
                DispatchQueue.main.async { self?.ended() }
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

    private func ended() {
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
