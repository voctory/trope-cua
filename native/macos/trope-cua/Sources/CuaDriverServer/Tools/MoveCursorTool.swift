import CoreGraphics
import CuaDriverCore
import Foundation
import MCP

public enum MoveCursorTool {
    public static let handler = ToolHandler(
        tool: Tool(
            name: "move_cursor",
            description: """
                Move the visual agent cursor overlay to (x, y) in screen
                points. This is for displaying agent intent only and should
                not be used as an input route. It never moves the user's
                real cursor.
                """,
            inputSchema: [
                "type": "object",
                "required": ["x", "y"],
                "properties": [
                    "x": ["type": "integer", "description": "X in screen points."],
                    "y": ["type": "integer", "description": "Y in screen points."],
                ],
                "additionalProperties": false,
            ],
            annotations: .init(
                readOnlyHint: false,
                destructiveHint: false,
                idempotentHint: true,
                openWorldHint: false
            )
        ),
        invoke: { arguments in
            guard
                let x = arguments?["x"]?.intValue,
                let y = arguments?["y"]?.intValue
            else {
                return CallTool.Result(
                    content: [
                        .text(
                            text: "Missing required integer fields x and y.",
                            annotations: nil,
                            _meta: nil
                        )
                    ],
                    isError: true
                )
            }

            let point = CGPoint(x: x, y: y)
            await AgentCursor.shared.animateAndWait(to: point)
            await MainActor.run {
                AgentCursor.shared.finishMove()
            }

            return CallTool.Result(
                content: [
                    .text(
                        text: "✅ Moved visual agent cursor to (\(x), \(y)) via agent_cursor.visual_move.",
                        annotations: nil,
                        _meta: nil
                    )
                ]
            )
        }
    )
}
