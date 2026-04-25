using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Mcp;

internal static class DaemonControl
{
    public static async Task<ToolResult> StopAllAsync(ToolRegistry registry, ToolContext context)
    {
        var records = NamedPipeDaemon.RegisteredInstances();
        var stopped = 0;
        var failed = 0;
        var lines = new List<string> { $"daemon-stop --all: instances={records.Count}" };
        var structured = new JsonArray();

        foreach (var record in records)
        {
            var stale = !NamedPipeDaemon.IsInstanceRunning(record);
            ToolResult? result = null;
            var ok = stale;
            if (stale)
            {
                _ = NamedPipeDaemon.RemoveInstanceRecord(record.InstanceId);
            }
            else
            {
                var daemon = new NamedPipeDaemon(registry, context, record.InstanceId);
                result = await daemon.TryShutdownAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
                ok = result is not null && !result.IsError;
            }

            if (ok)
                stopped++;
            else
                failed++;

            structured.Add(new JsonObject
            {
                ["instance_id"] = record.InstanceId,
                ["pid"] = record.Pid,
                ["ok"] = ok,
                ["stale"] = stale,
                ["message"] = stale ? "stale registry record removed" : result?.ToCliText()
            });
            lines.Add($"- instance={record.InstanceId} pid={record.Pid} ok={ok} stale={stale}");
        }

        lines[0] = (failed == 0 ? ToolText.OkPrefix : ToolText.ErrorPrefix) + lines[0] + $" stopped={stopped} failed={failed}";
        return ToolResult.Text(string.Join(Environment.NewLine, lines), new JsonObject
        {
            ["stopped"] = stopped,
            ["failed"] = failed,
            ["instances"] = structured
        }, failed > 0);
    }
}
