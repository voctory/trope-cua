# Tool output format

CLI text output is human-readable by default. Set `TROPE_CUA_JSON=1` to print the raw `ToolResult` JSON.

MCP responses follow the normal `tools/call` shape:

```json
{
  "content": [
    {"type": "text", "text": "..."},
    {"type": "image", "data": "base64...", "mimeType": "image/jpeg"}
  ],
  "isError": false,
  "structuredContent": {}
}
```

Tools should return `structuredContent` whenever the caller needs to make a routing or safety decision. Mutating actions expose the same action receipt in text and structured form:

```json
{
  "ok": true,
  "route": "uia.invoke",
  "lane": "same_session",
  "background_safe": true,
  "cursor_moved": false,
  "foreground_changed": false,
  "session": "parent"
}
```

`ok=true` means the requested route completed. It does not by itself mean the route was safe for background automation. Treat a mutating action as background-safe only when `background_safe=true`, `cursor_moved=false`, and `foreground_changed=false`.

When a tool cannot safely act, `structuredContent.route` should name the missing or refused lane, such as `requires_cdp_or_child_session`, `requires_child_session_or_appbroadcast`, or `requires_background_launch_lane`.
