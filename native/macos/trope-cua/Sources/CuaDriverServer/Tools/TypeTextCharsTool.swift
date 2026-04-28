import ApplicationServices
import CoreGraphics
import CuaDriverCore
import Foundation
import MCP

/// Character-by-character CGEvent typing, always targeting a specific pid.
/// Previously this posted to the system HID tap (frontmost-routed), which
/// was a footgun when a driver-backgrounded app typed characters into the
/// user's real foreground app. Making pid mandatory and routing via
/// `CGEvent.postToPid` removes the footgun.
public enum TypeTextCharsTool {
    public static let handler = ToolHandler(
        tool: Tool(
            name: "type_text_chars",
            description: """
                Type `text` one character at a time, delivered directly to
                the target pid's event queue via `CGEvent.postToPid`. Each
                character is posted as a synthesized key-down/key-up pair
                whose Unicode payload is set via
                `CGEventKeyboardSetUnicodeString`, bypassing virtual-key
                mapping so accents, symbols, and emoji transmit verbatim.

                Use this when the AX-based `type_text` silently drops
                characters — typical for Chromium / Electron text inputs
                that don't expose `kAXSelectedText`. Optional
                `element_index` + `window_id` focuses that element while
                the characters are posted. When omitted, the tool reuses
                the last text target established by `click` / `type_text`
                when available. The target does NOT need to be frontmost.

                `delay_ms` (0-200) spaces successive characters so
                autocomplete and IME paths can keep up. Default 30.
                """,
            inputSchema: [
                "type": "object",
                "required": ["pid", "text"],
                "properties": [
                    "pid": [
                        "type": "integer",
                        "description": "Target process ID.",
                    ],
                    "text": [
                        "type": "string",
                        "description": "Text to type into the target's focused element.",
                    ],
                    "element_index": [
                        "type": "integer",
                        "description":
                            "Optional element_index from the last get_window_state for the same (pid, window_id). When present, the element is focused before typing. Requires window_id.",
                    ],
                    "window_id": [
                        "type": "integer",
                        "description":
                            "CGWindowID for the window whose get_window_state produced the element_index. Also selects a cached text target when element_index is omitted.",
                    ],
                    "delay_ms": [
                        "type": "integer",
                        "minimum": 0,
                        "maximum": 200,
                        "description":
                            "Milliseconds between successive characters. Default 30.",
                    ],
                ],
                "additionalProperties": false,
            ],
            annotations: .init(
                readOnlyHint: false,
                destructiveHint: true,
                idempotentHint: false,
                openWorldHint: true
            )
        ),
        invoke: { arguments in
            guard let rawPid = arguments?["pid"]?.intValue else {
                return errorResult("Missing required integer field pid.")
            }
            guard let text = arguments?["text"]?.stringValue else {
                return errorResult("Missing required string field text.")
            }
            let delayMs = arguments?["delay_ms"]?.intValue ?? 30
            guard let pid = Int32(exactly: rawPid) else {
                return errorResult(
                    "pid \(rawPid) is outside the supported Int32 range.")
            }
            let elementIndex = arguments?["element_index"]?.intValue
            let rawWindowId = arguments?["window_id"]?.intValue
            if elementIndex != nil && rawWindowId == nil {
                return errorResult(
                    "window_id is required when element_index is used — the "
                    + "element_index cache is scoped per (pid, window_id). Pass "
                    + "the same window_id you used in `get_window_state`.")
            }

            let windowId: UInt32?
            if let rawWindowId {
                guard let checked = UInt32(exactly: rawWindowId) else {
                    return errorResult(
                        "window_id \(rawWindowId) is outside the supported UInt32 range.")
                }
                windowId = checked
            } else {
                windowId = nil
            }

            do {
                if let index = elementIndex, let windowId {
                    let element = try await AppStateRegistry.engine.lookup(
                        pid: pid,
                        windowId: windowId,
                        elementIndex: index)
                    try await typeIntoElement(
                        text,
                        delayMs: delayMs,
                        pid: pid,
                        windowId: windowId,
                        element: element)
                    await AppStateRegistry.textTargets.remember(
                        pid: pid,
                        windowId: windowId,
                        element: element)
                } else if let target = await cachedTextTarget(
                    pid: pid, windowId: windowId)
                {
                    try await typeIntoElement(
                        text,
                        delayMs: delayMs,
                        pid: pid,
                        windowId: target.windowId,
                        element: target.element)
                } else {
                    try KeyboardInput.typeCharacters(
                        text,
                        delayMilliseconds: delayMs,
                        toPid: pid
                    )
                }
                let summary =
                    "✅ Typed \(text.count) character(s) on pid \(rawPid) with \(delayMs)ms delay."
                return CallTool.Result(
                    content: [.text(text: summary, annotations: nil, _meta: nil)]
                )
            } catch let error as AppStateError {
                return errorResult(error.description)
            } catch let error as AXInputError {
                return errorResult(error.description)
            } catch let error as KeyboardError {
                return errorResult(error.description)
            } catch {
                return errorResult("Unexpected error: \(error)")
            }
        }
    )

    struct Result: Codable, Sendable {
        let pid: Int
        let characterCount: Int
        let delayMilliseconds: Int

        private enum CodingKeys: String, CodingKey {
            case pid
            case characterCount = "character_count"
            case delayMilliseconds = "delay_ms"
        }
    }

    private static func errorResult(_ message: String) -> CallTool.Result {
        CallTool.Result(
            content: [.text(text: message, annotations: nil, _meta: nil)],
            isError: true
        )
    }

    private static func cachedTextTarget(
        pid: Int32,
        windowId: UInt32?
    ) async -> (windowId: UInt32, element: AXUIElement)? {
        if let windowId {
            guard let element = await AppStateRegistry.textTargets.lookup(
                pid: pid,
                windowId: windowId)
            else { return nil }
            return (windowId, element)
        }
        return await AppStateRegistry.textTargets.lookup(pid: pid)
    }

    private static func typeIntoElement(
        _ text: String,
        delayMs: Int,
        pid: Int32,
        windowId: UInt32,
        element: AXUIElement
    ) async throws {
        try await AppStateRegistry.focusGuard.withFocusSuppressed(
            pid: pid,
            element: element
        ) {
            _ = FocusWithoutRaise.activateWithoutRaise(
                targetPid: pid,
                targetWid: CGWindowID(windowId))
            try? await Task.sleep(for: .milliseconds(50))
            try? AXInput.setAttribute(
                "AXFocused",
                on: element,
                value: kCFBooleanTrue as CFTypeRef
            )
            try KeyboardInput.typeCharacters(
                text,
                delayMilliseconds: delayMs,
                toPid: pid
            )
        }
    }
}
