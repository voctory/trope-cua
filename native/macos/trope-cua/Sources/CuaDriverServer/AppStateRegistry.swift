import ApplicationServices
import CuaDriverCore

/// Shared actor instances used by server-side tools.
///
/// - ``engine`` owns the per-(pid, window_id) element-index map refreshed
///   by `get_window_state(pid, window_id)`.
/// - ``focusGuard`` wraps element-indexed AX actions in layers 1 and 2 of
///   the focus-suppression stack (enablement + synthetic focus).
/// - ``systemFocusStealPreventer`` is layer 3 — a reactive
///   `NSWorkspace.didActivateApplicationNotification` observer that
///   re-activates the previously-frontmost app when a launch target
///   self-activates. `LaunchAppTool` arms it around the launch window.
///
/// The ``FocusGuard`` is constructed off the same ``AXEnablementAssertion``
/// the engine uses, so the "which pids have we already activated?" cache
/// is shared across snapshot calls and per-action focus suppression. That
/// avoids redundant attribute writes on every tool call.
public enum AppStateRegistry {
    public static let enablement = AXEnablementAssertion()
    public static let engine = AppStateEngine(enablement: enablement)
    public static let focusGuard = FocusGuard(
        enablement: enablement,
        enforcer: SyntheticAppFocusEnforcer(),
        systemPreventer: systemFocusStealPreventer
    )
    public static let systemFocusStealPreventer = SystemFocusStealPreventer()
    public static let textTargets = TextTargetRegistry()
}

public actor TextTargetRegistry {
    private var targets: [TextTargetKey: AXUIElement] = [:]

    public init() {}

    public func remember(pid: Int32, windowId: UInt32, element: AXUIElement) {
        targets[TextTargetKey(pid: pid, windowId: windowId)] = element
    }

    public func lookup(pid: Int32, windowId: UInt32) -> AXUIElement? {
        targets[TextTargetKey(pid: pid, windowId: windowId)]
    }

    public func lookup(pid: Int32) -> (windowId: UInt32, element: AXUIElement)? {
        if let frontmost = WindowEnumerator.frontmostWindowID(forPid: pid),
           let windowId = UInt32(exactly: frontmost),
           let element = targets[TextTargetKey(pid: pid, windowId: windowId)]
        {
            return (windowId, element)
        }

        let matches = targets.filter { $0.key.pid == pid }
        guard matches.count == 1, let match = matches.first else { return nil }
        return (match.key.windowId, match.value)
    }
}

private struct TextTargetKey: Hashable, Sendable {
    let pid: Int32
    let windowId: UInt32
}
