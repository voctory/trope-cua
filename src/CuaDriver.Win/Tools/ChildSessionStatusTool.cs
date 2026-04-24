using System.Text.Json.Nodes;
using CuaDriver.Win.HardCases;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class ChildSessionStatusTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "child_session_status",
        "Report child-session/PiP lane status and optionally call WTSEnableChildSessions(TRUE).",
        JsonArgs.Schema(("enable", JsonArgs.Prop("boolean", "Call WTSEnableChildSessions(TRUE)."))),
        ReadOnly: false,
        Idempotent: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        if (JsonArgs.OptionalBool(args, "enable"))
        {
            var ok = ChildSessionBroker.TryEnableChildSessions(out var message);
            lines.Add((ok ? "✅ " : "❌ ") + message);
        }
        lines.Add(ChildSessionBroker.Status());
        return Task.FromResult(ToolResult.Text(string.Join(Environment.NewLine, lines)));
    }
}
