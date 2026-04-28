import AppKit
import QuartzCore
import SwiftUI

/// SwiftUI overlay view that drives `AgentCursorRenderer.shared` every
/// display frame and draws the cursor arrow via `Canvas`. Hosted inside
/// `AgentCursorOverlayWindow` via an `NSHostingView`.
///
/// The cursor tip points in the direction of `renderer.heading`. Shape
/// matches the existing gradient-arrow design: a classic pointer with
/// the tip at upper-left, scaled for legibility at any display density.
public struct AgentCursorView: View {
    @Bindable var renderer: AgentCursorRenderer
    private let screenBounds: CGRect

    public init(
        renderer: AgentCursorRenderer = .shared,
        screenBounds: CGRect
    ) {
        self.renderer = renderer
        self.screenBounds = screenBounds
    }

    public var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 120.0)) { _ in
            Canvas { gctx, _ in
                // Keep the renderer on Core Animation's monotonic clock;
                // animation starts, fade timing, and progress sampling all
                // use CACurrentMediaTime().
                renderer.tick(now: CACurrentMediaTime())
                drawCursor(in: gctx)
            }
            .opacity(renderer.opacity)
            .ignoresSafeArea()
            .allowsHitTesting(false)
        }
    }

    /// Draw the cursor arrow centered on `renderer.position`, rotated to
    /// `renderer.heading`. The shape is a 4-vertex pointer arrow with
    /// the tip along +x before rotation, which the caller rotates by
    /// `heading + π` (so the visible tip trails opposite the motion
    /// vector — matching macOS cursor convention).
    private func drawCursor(in ctx: GraphicsContext) {
        guard renderer.hasPosition else { return }
        let global = renderer.position
        let padded = screenBounds.insetBy(dx: -80, dy: -80)
        guard padded.contains(global) else { return }
        let p = CGPoint(
            x: global.x - screenBounds.minX,
            y: global.y - screenBounds.minY
        )

        // Arrow path — tip at (14, 0), tail extends to the left.
        var shape = Path()
        shape.move(to: CGPoint(x: 14, y: 0))
        shape.addLine(to: CGPoint(x: -8, y: -9))
        shape.addLine(to: CGPoint(x: -3, y: 0))
        shape.addLine(to: CGPoint(x: -8, y: 9))
        shape.closeSubpath()

        // `renderer.heading` is the visual heading = motion_direction + π.
        // The arrow path has its tip at +x, so we rotate by heading + π
        // to make the tip point in the motion direction (standard cursor
        // convention where the pointer leads rather than trails).
        let transform = CGAffineTransform(translationX: p.x, y: p.y)
            .rotated(by: CGFloat(renderer.heading + .pi))

        let transformed = shape.applying(transform)

        // Ice-blue gradient fill (matches existing agent-cursor palette).
        ctx.fill(
            transformed,
            with: .linearGradient(
                Gradient(colors: [
                    Color(nsColor: renderer.palette.cursorStart),
                    Color(nsColor: renderer.palette.cursorMid),
                    Color(nsColor: renderer.palette.cursorEnd),
                ]),
                startPoint: CGPoint(x: p.x + 14, y: p.y - 9),
                endPoint: CGPoint(x: p.x - 8, y: p.y + 9)
            )
        )
        // White outline for legibility on any background.
        ctx.stroke(transformed, with: .color(.white), lineWidth: 1.5)

        // Cyan bloom halo — radial glow that reads as agent presence.
        let bloomR: CGFloat = 22
        let bloomRect = CGRect(x: p.x - bloomR, y: p.y - bloomR,
                               width: bloomR * 2, height: bloomR * 2)
        ctx.fill(
            Path(ellipseIn: bloomRect),
            with: .radialGradient(
                Gradient(colors: [
                    Color(nsColor: renderer.palette.bloomInner).opacity(0.45),
                    Color(nsColor: renderer.palette.bloomOuter).opacity(0.10),
                    Color(nsColor: renderer.palette.bloomOuter).opacity(0.0),
                ]),
                center: p,
                startRadius: 0,
                endRadius: bloomR
            )
        )
    }
}
