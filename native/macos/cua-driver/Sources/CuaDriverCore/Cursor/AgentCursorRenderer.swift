import CoreGraphics
import Foundation
import Observation
import QuartzCore

@Observable
@MainActor
public final class AgentCursorRenderer {
    public static let shared = AgentCursorRenderer()

    private static let restingHeading = Double.pi / 4
    fileprivate static let directDistanceThreshold = 18.0
    fileprivate static let shortMoveDirectDistanceThreshold = 72.0
    private static let nominalGlideDuration = 0.16
    private static let minMoveDuration = 0.08
    private static let maxMoveDuration = 0.34
    private static let durationBase = 0.055
    private static let durationDistanceDivisor = 1900.0
    private static let progressExponent = 3.157
    private static let progressDamping = 0.9
    private static let visualHeadingCatchUp = 18.0

    public var palette: AgentCursorPalette = .defaultBlue
    public private(set) var position = CGPoint(x: -200, y: -200)
    public private(set) var heading = restingHeading
    public private(set) var opacity = 1.0
    public var focusRect: CGRect? = nil

    private var path: PlannedCursorPath?
    private var glideStartedAt: CFTimeInterval = 0
    private var glideDuration: CFTimeInterval = 0
    private var lastFrameTime: CFTimeInterval?
    private var lastGlideHeading = restingHeading
    private var arrivalContinuation: CheckedContinuation<Void, Never>?
    private var fadeStartedAt: CFTimeInterval?
    private var fadeDuration: CFTimeInterval = 0

    public init() {}

    public func moveTo(
        point: CGPoint,
        endAngleDegrees: Double,
        duration: CFTimeInterval,
        options: CursorMotionPath.Options
    ) {
        moveTo(
            point: point,
            endAngleRadians: endAngleDegrees * .pi / 180,
            duration: duration,
            options: options
        )
    }

    public func moveTo(
        point targetPoint: CGPoint,
        endAngleRadians endAngle: Double,
        duration: CFTimeInterval,
        options: CursorMotionPath.Options
    ) {
        let previous = arrivalContinuation
        arrivalContinuation = nil
        previous?.resume()
        cancelFadeOut()

        if position.x < -100 {
            position = CGPoint(x: targetPoint.x - 160, y: targetPoint.y + 120)
            heading = Self.restingHeading
            lastGlideHeading = heading
        }

        let target = CGPoint(
            x: targetPoint.x + CGFloat(cos(endAngle) * 16),
            y: targetPoint.y + CGFloat(sin(endAngle) * 16)
        )
        let planned = PlannedCursorPath(
            start: position,
            end: target,
            startHeading: heading + .pi,
            endHeading: endAngle + .pi,
            endVisualHeading: endAngle,
            targetPoint: target,
            options: options
        )

        path = planned
        glideStartedAt = CACurrentMediaTime()
        glideDuration = resolvedDuration(path: planned, requested: duration)
        lastFrameTime = glideStartedAt
        lastGlideHeading = heading

        if planned.length < 1 {
            finishPath(planned)
        }
    }

    public func setInitialPosition(_ point: CGPoint, heading h: Double? = nil) {
        position = point
        heading = h ?? self.heading
        path = nil
        lastFrameTime = nil
        lastGlideHeading = heading
        cancelFadeOut()
    }

    public func waitForArrival() async {
        guard path != nil else { return }
        await withCheckedContinuation { continuation in
            let previous = arrivalContinuation
            arrivalContinuation = continuation
            previous?.resume()
        }
    }

    public var estimatedTravelSeconds: Double {
        guard let path else { return 0 }
        return glideDuration * max(0, 1 - progress(now: CACurrentMediaTime()))
    }

    public func beginFadeOut(duration: CFTimeInterval) {
        fadeStartedAt = CACurrentMediaTime()
        fadeDuration = max(0.01, duration)
    }

    public func cancelFadeOut() {
        fadeStartedAt = nil
        opacity = 1
    }

    public func tick(now: CFTimeInterval) {
        let previousTime = lastFrameTime ?? now
        let dt = min(0.05, max(0, now - previousTime))
        lastFrameTime = now

        if let path {
            let u = progress(now: now)
            if u >= 1 {
                finishPath(path)
            } else {
                let state = path.sample(distance: easedProgress(u) * path.length)
                position = CGPoint(x: state.x, y: state.y)
                let desired = continueAngle(
                    reference: lastGlideHeading,
                    angle: state.heading + .pi
                )
                lastGlideHeading = desired
                heading = moveTowardContinuous(
                    current: heading,
                    desired: desired,
                    maxStep: Self.visualHeadingCatchUp * dt
                )
            }
        }

        updateFade(now: now)
    }

    private func finishPath(_ planned: PlannedCursorPath) {
        position = planned.targetPoint
        heading = continueAngle(reference: lastGlideHeading, angle: planned.endVisualHeading)
        lastGlideHeading = heading
        path = nil
        glideStartedAt = 0
        glideDuration = 0
        let continuation = arrivalContinuation
        arrivalContinuation = nil
        continuation?.resume()
    }

    private func progress(now: CFTimeInterval) -> Double {
        if glideDuration <= 0 { return 1 }
        return min(1, max(0, (now - glideStartedAt) / glideDuration))
    }

    private func resolvedDuration(path: PlannedCursorPath, requested: CFTimeInterval) -> CFTimeInterval {
        let adaptive = min(
            Self.maxMoveDuration,
            max(Self.minMoveDuration, Self.durationBase + path.straightLineDistance / Self.durationDistanceDivisor)
        )
        let userScale = min(3.0, max(0.35, requested / Self.nominalGlideDuration))
        return min(1.2, max(0.03, adaptive * userScale))
    }

    private func easedProgress(_ value: Double) -> Double {
        let clamped = min(1, max(0, value))
        let response = 1 - pow(1 - clamped, Self.progressExponent)
        return min(1, max(0, response * Self.progressDamping + clamped * (1 - Self.progressDamping)))
    }

    private func updateFade(now: CFTimeInterval) {
        guard let fadeStartedAt else {
            opacity = 1
            return
        }

        let u = min(1, max(0, (now - fadeStartedAt) / fadeDuration))
        let eased = u * u * (3 - 2 * u)
        opacity = 1 - eased
    }
}

private struct PlannedCursorPath {
    let length: Double
    let straightLineDistance: Double
    let endVisualHeading: Double
    let targetPoint: CGPoint
    let segments: [CursorMotionSegment]

    init(
        start: CGPoint,
        end: CGPoint,
        startHeading: Double,
        endHeading: Double,
        endVisualHeading: Double,
        targetPoint: CGPoint,
        options: CursorMotionPath.Options
    ) {
        let dx = end.x - start.x
        let dy = end.y - start.y
        let distance = sqrt(dx * dx + dy * dy)
        let segments = Self.buildSegments(
            start: start,
            end: end,
            dx: dx,
            dy: dy,
            distance: distance,
            startHeading: startHeading,
            endHeading: endHeading,
            options: options
        )
        self.length = max(1, segments.reduce(0) { $0 + $1.length })
        self.straightLineDistance = distance
        self.endVisualHeading = endVisualHeading
        self.targetPoint = targetPoint
        self.segments = segments
    }

    func sample(distance: Double) -> CursorPathState {
        guard !segments.isEmpty else {
            return CursorPathState(x: targetPoint.x, y: targetPoint.y, heading: endVisualHeading)
        }

        var accumulated = 0.0
        let target = min(distance, length)
        for index in segments.indices {
            let segment = segments[index]
            let segmentLength = max(0.001, segment.length)
            if target <= accumulated + segmentLength || index == segments.count - 1 {
                return segment.state(at: (target - accumulated) / segmentLength)
            }
            accumulated += segmentLength
        }
        return segments[segments.count - 1].state(at: 1)
    }

    private static func buildSegments(
        start: CGPoint,
        end: CGPoint,
        dx: CGFloat,
        dy: CGFloat,
        distance: CGFloat,
        startHeading: Double,
        endHeading: Double,
        options: CursorMotionPath.Options
    ) -> [CursorMotionSegment] {
        if distance < 4 {
            return directSegments(
                start: start,
                end: end,
                distance: distance,
                startHeading: startHeading,
                endHeading: endHeading,
                options: options
            )
        }

        let direct = directSegments(
            start: start,
            end: end,
            distance: distance,
            startHeading: startHeading,
            endHeading: endHeading,
            options: options
        )
        if Double(distance) <= AgentCursorRenderer.directDistanceThreshold {
            return direct
        }

        let unit = CGPoint(x: dx / distance, y: dy / distance)
        let normal = CGPoint(x: -unit.y, y: unit.x)
        let startUnit = unitFor(startHeading)
        let endUnit = unitFor(endHeading)
        let baseControl = min(
            min(640, Double(distance) * 0.9),
            max(min(48, Double(distance) * 0.33), Double(distance) * 0.41960295031576633)
        )
        let baseArc = min(
            min(440, Double(distance) * 0.65),
            max(min(18, Double(distance) * 0.18), Double(distance) * 0.2765523188064277)
        )
        let arcScaleFromOptions = max(0.2, min(2.0, Double(options.arcSize / 0.25)))
        let preferredSign = dx >= 0 ? 1.0 : -1.0
        var candidates: [([CursorMotionSegment], CursorMotionMeasurement, Double)] = []

        if Double(distance) <= AgentCursorRenderer.shortMoveDirectDistanceThreshold {
            let measured = measure(direct)
            candidates.append((direct, measured, score(measured)))
        }

        for sign in [preferredSign, -preferredSign] {
            for controlScale in [0.55, 0.8, 1.05] {
                for arcScale in [0.65, 1.0, 1.35] {
                    let startControl = min(
                        baseControl * controlScale * max(0.25, Double(options.startHandle + 0.2)),
                        Double(distance) * 0.47
                    )
                    let endControl = min(
                        baseControl * controlScale * max(0.25, Double(options.endHandle + 0.2)),
                        Double(distance) * 0.47
                    )
                    let midControl = min(baseControl * 0.65, Double(distance) * 0.38)
                    let arc = baseArc * arcScale * arcScaleFromOptions * sign
                    let arcOffset = CGFloat(arc)
                    let midControlOffset = CGFloat(midControl)
                    let startControlOffset = CGFloat(startControl)
                    let endControlOffset = CGFloat(endControl)
                    let flow = max(-1.0, min(1.0, Double(options.arcFlow)))
                    let mid = CGPoint(
                        x: start.x + dx * CGFloat(0.5 + flow * 0.12) + normal.x * arcOffset,
                        y: start.y + dy * CGFloat(0.5 + flow * 0.12) + normal.y * arcOffset
                    )
                    let segments = [
                        CursorMotionSegment(
                            start: start,
                            control1: CGPoint(
                                x: start.x + startUnit.x * startControlOffset,
                                y: start.y + startUnit.y * startControlOffset
                            ),
                            control2: CGPoint(
                                x: mid.x - unit.x * midControlOffset,
                                y: mid.y - unit.y * midControlOffset
                            ),
                            end: mid
                        ),
                        CursorMotionSegment(
                            start: mid,
                            control1: CGPoint(
                                x: mid.x + unit.x * midControlOffset,
                                y: mid.y + unit.y * midControlOffset
                            ),
                            control2: CGPoint(
                                x: end.x - endUnit.x * endControlOffset,
                                y: end.y - endUnit.y * endControlOffset
                            ),
                            end: end
                        ),
                    ]
                    let measured = measure(segments)
                    candidates.append((segments, measured, score(measured)))
                }
            }
        }

        return candidates.min { $0.2 < $1.2 }?.0 ?? direct
    }

    private static func directSegments(
        start: CGPoint,
        end: CGPoint,
        distance: CGFloat,
        startHeading: Double,
        endHeading: Double,
        options: CursorMotionPath.Options
    ) -> [CursorMotionSegment] {
        if distance <= 0 {
            return [CursorMotionSegment(start: start, control1: start, control2: end, end: end)]
        }

        let baseControl = min(
            min(min(640, Double(distance) * 0.9), max(min(48, Double(distance) * 0.33), Double(distance) * 0.41960295031576633)),
            Double(distance) * 0.45
        )
        let startControl = baseControl * max(0.25, Double(options.startHandle + 0.2))
        let endControl = baseControl * max(0.25, Double(options.endHandle + 0.2))
        let startUnit = unitFor(startHeading)
        let endUnit = unitFor(endHeading)
        let startControlOffset = CGFloat(startControl)
        let endControlOffset = CGFloat(endControl)
        return [
            CursorMotionSegment(
                start: start,
                control1: CGPoint(x: start.x + startUnit.x * startControlOffset, y: start.y + startUnit.y * startControlOffset),
                control2: CGPoint(x: end.x - endUnit.x * endControlOffset, y: end.y - endUnit.y * endControlOffset),
                end: end
            )
        ]
    }
}

private struct CursorMotionSegment {
    let start: CGPoint
    let control1: CGPoint
    let control2: CGPoint
    let end: CGPoint
    let length: Double

    init(start: CGPoint, control1: CGPoint, control2: CGPoint, end: CGPoint) {
        self.start = start
        self.control1 = control1
        self.control2 = control2
        self.end = end
        self.length = Self.measure(start: start, control1: control1, control2: control2, end: end)
    }

    func point(at progress: Double) -> CGPoint {
        let t = min(1, max(0, progress))
        let u = 1 - t
        return CGPoint(
            x: u * u * u * start.x + 3 * u * u * t * control1.x + 3 * u * t * t * control2.x + t * t * t * end.x,
            y: u * u * u * start.y + 3 * u * u * t * control1.y + 3 * u * t * t * control2.y + t * t * t * end.y
        )
    }

    func tangent(at progress: Double) -> CGPoint {
        let t = min(1, max(0, progress))
        let u = 1 - t
        return CGPoint(
            x: 3 * u * u * (control1.x - start.x) + 6 * u * t * (control2.x - control1.x) + 3 * t * t * (end.x - control2.x),
            y: 3 * u * u * (control1.y - start.y) + 6 * u * t * (control2.y - control1.y) + 3 * t * t * (end.y - control2.y)
        )
    }

    func state(at progress: Double) -> CursorPathState {
        let point = point(at: progress)
        let tangent = tangent(at: progress)
        return CursorPathState(x: point.x, y: point.y, heading: atan2(tangent.y, tangent.x))
    }

    private static func measure(start: CGPoint, control1: CGPoint, control2: CGPoint, end: CGPoint) -> Double {
        let segment = CursorMotionSegment.Unmeasured(start: start, control1: control1, control2: control2, end: end)
        var length = 0.0
        var previous = start
        for index in 1...32 {
            let point = segment.point(at: Double(index) / 32.0)
            let dx = point.x - previous.x
            let dy = point.y - previous.y
            length += Double(sqrt(dx * dx + dy * dy))
            previous = point
        }
        return length
    }

    private struct Unmeasured {
        let start: CGPoint
        let control1: CGPoint
        let control2: CGPoint
        let end: CGPoint

        func point(at progress: Double) -> CGPoint {
            let t = min(1, max(0, progress))
            let u = 1 - t
            return CGPoint(
                x: u * u * u * start.x + 3 * u * u * t * control1.x + 3 * u * t * t * control2.x + t * t * t * end.x,
                y: u * u * u * start.y + 3 * u * u * t * control1.y + 3 * u * t * t * control2.y + t * t * t * end.y
            )
        }
    }
}

private struct CursorPathState {
    let x: CGFloat
    let y: CGFloat
    let heading: Double
}

private struct CursorMotionMeasurement {
    let length: Double
    let angleChangeEnergy: Double
    let maxAngleChange: Double
    let totalTurn: Double
}

private func measure(_ segments: [CursorMotionSegment]) -> CursorMotionMeasurement {
    let sampleCount = 72
    let totalLength = max(1, segments.reduce(0) { $0 + $1.length })
    let path = PlannedPathSampler(segments: segments, length: totalLength)
    var samples: [CGPoint] = []
    for index in 0..<sampleCount {
        samples.append(path.sample(distance: Double(index) / Double(sampleCount - 1) * totalLength))
    }

    var length = 0.0
    var angleChangeEnergy = 0.0
    var maxAngleChange = 0.0
    var totalTurn = 0.0
    var previousAngle: Double?
    for index in 1..<samples.count {
        let previous = samples[index - 1]
        let sample = samples[index]
        let dx = sample.x - previous.x
        let dy = sample.y - previous.y
        let sampleDistance = sqrt(dx * dx + dy * dy)
        length += Double(sampleDistance)
        guard sampleDistance > 0.001 else { continue }
        let angle = atan2(dy, dx)
        if let prior = previousAngle {
            let change = abs(normalizedAngleDelta(angle - prior))
            angleChangeEnergy += change * change
            maxAngleChange = max(maxAngleChange, change)
            totalTurn += change
        }
        previousAngle = angle
    }

    return CursorMotionMeasurement(
        length: length,
        angleChangeEnergy: angleChangeEnergy,
        maxAngleChange: maxAngleChange,
        totalTurn: totalTurn
    )
}

private func score(_ measurement: CursorMotionMeasurement) -> Double {
    measurement.length
        + measurement.angleChangeEnergy * 320
        + measurement.maxAngleChange * 140
        + measurement.totalTurn * 180
}

private struct PlannedPathSampler {
    let segments: [CursorMotionSegment]
    let length: Double

    func sample(distance: Double) -> CGPoint {
        var accumulated = 0.0
        let target = min(distance, length)
        for index in segments.indices {
            let segment = segments[index]
            let segmentLength = max(0.001, segment.length)
            if target <= accumulated + segmentLength || index == segments.count - 1 {
                return segment.point(at: (target - accumulated) / segmentLength)
            }
            accumulated += segmentLength
        }
        return segments[segments.count - 1].end
    }
}

private func unitFor(_ heading: Double) -> CGPoint {
    CGPoint(x: CGFloat(cos(heading)), y: CGFloat(sin(heading)))
}

private func continueAngle(reference: Double, angle: Double) -> Double {
    var value = angle
    let tau = Double.pi * 2
    while value - reference > .pi { value -= tau }
    while value - reference < -.pi { value += tau }
    return value
}

private func moveTowardContinuous(current: Double, desired: Double, maxStep: Double) -> Double {
    let delta = desired - current
    return current + max(-maxStep, min(maxStep, delta))
}

private func normalizedAngleDelta(_ value: Double) -> Double {
    var delta = value
    while delta > .pi { delta -= 2 * .pi }
    while delta < -.pi { delta += 2 * .pi }
    return delta
}
