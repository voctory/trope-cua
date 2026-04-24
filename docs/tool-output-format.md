# Tool output format

CLI text output is human-readable by default. Set `CUA_DRIVER_JSON=1` to print the raw `ToolResult` JSON.

MCP responses follow the normal `tools/call` shape:

```json
{
  "content": [
    {"type": "text", "text": "..."},
    {"type": "image", "data": "base64...", "mimeType": "image/jpeg"}
  ],
  "isError": false
}
```

Mutating actions include an action receipt in the first text block.
