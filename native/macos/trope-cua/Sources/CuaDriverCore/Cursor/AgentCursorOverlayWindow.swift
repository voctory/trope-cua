import AppKit
import CoreGraphics

/// Transparent, click-through, borderless window used to host the agent
/// cursor overlay. Shares the classic "floating HUD" recipe: no window
/// chrome, no shadow, clear background, ignores mouse events so the user
/// keeps using the machine normally, installed at `.statusBar + 1` so
/// it stays above ordinary windows but below system menus and modals.
///
/// `canBecomeKey` / `canBecomeMain` are overridden to `false` so the
/// window never becomes the focus target — critical to preserve the
/// driver's "never steal focus" contract.
///
/// One instance is created per connected screen. The window frame uses
/// AppKit's `NSScreen.frame`, while `screenBounds` stores the matching
/// CoreGraphics display bounds. Cursor target points arrive in the
/// CG/AX top-left coordinate space, so `AgentCursorView` subtracts
/// `screenBounds.origin` before drawing into this window.
public final class AgentCursorOverlayWindow: NSWindow {
    public let screenBounds: CGRect

    public override var canBecomeKey: Bool { false }
    public override var canBecomeMain: Bool { false }

    public init(screen: NSScreen) {
        let frame = screen.frame
        self.screenBounds = AgentCursorOverlayWindow.coreGraphicsBounds(for: screen)
        super.init(
            contentRect: frame,
            styleMask: .borderless,
            backing: .buffered,
            defer: false
        )
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        ignoresMouseEvents = true
        // `.normal` level so the overlay is sandwiched in the regular
        // window stack. `AgentCursor.pinAbove(pid:)` calls
        // `order(.above, relativeTo: targetWindowId)` to place the
        // overlay exactly one slot above the target window — windows
        // that were already above the target remain above the overlay.
        // This produces the ordering: [target, overlay, fg-windows].
        level = .normal
        collectionBehavior = [
            .canJoinAllSpaces, .fullScreenAuxiliary, .stationary,
        ]
        isReleasedWhenClosed = false
        // The overlay must stay visible even when `trope-cua` itself
        // isn't the active application. Without this, the window would
        // disappear any time the driver loses focus (which is every
        // tool call, given our no-focus-steal contract).
        hidesOnDeactivate = false
    }

    public static func screenSignature() -> [String] {
        NSScreen.screens.map { screen in
            let frame = screen.frame
            let bounds = coreGraphicsBounds(for: screen)
            return "\(frame.origin.x),\(frame.origin.y),\(frame.width),\(frame.height):"
                + "\(bounds.origin.x),\(bounds.origin.y),\(bounds.width),\(bounds.height):"
                + "\(screen.backingScaleFactor)"
        }
    }

    private static func coreGraphicsBounds(for screen: NSScreen) -> CGRect {
        let key = NSDeviceDescriptionKey("NSScreenNumber")
        if let number = screen.deviceDescription[key] as? NSNumber {
            let displayId = CGDirectDisplayID(number.uint32Value)
            let bounds = CGDisplayBounds(displayId)
            if !bounds.isNull && !bounds.isEmpty { return bounds }
        }
        return CGRect(
            x: screen.frame.origin.x,
            y: screen.frame.origin.y,
            width: screen.frame.width,
            height: screen.frame.height
        )
    }
}
