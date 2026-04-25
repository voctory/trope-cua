using System.Text.Json.Nodes;

namespace CuaDriver.Win.Tooling;

public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonObject InputSchema,
    bool ReadOnly = false,
    bool Destructive = false,
    bool Idempotent = true,
    bool OpenWorld = false);
