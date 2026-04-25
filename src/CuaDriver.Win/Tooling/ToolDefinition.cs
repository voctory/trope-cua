using System.Text.Json.Nodes;

namespace CuaDriver.Win.Tooling;

internal sealed record ToolDefinition(
    string Name,
    string Description,
    JsonObject InputSchema,
    bool ReadOnly = false,
    bool Destructive = false,
    bool Idempotent = true,
    bool OpenWorld = false);
