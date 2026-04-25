using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Mcp;

internal sealed class NamedPipeDaemonClient
{
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(2);

    public NamedPipeDaemonClient(string? instanceId = null)
    {
        InstancePipeName = NamedPipeDaemon.PipeNameFor(instanceId);
    }

    public string InstancePipeName { get; }

    public async Task<ToolResult?> TryCallAsync(string name, JsonObject args, TimeSpan timeout, CancellationToken ct)
    {
        var response = await TryRequestAsync(new DaemonRequest("call", name, args), timeout, ct).ConfigureAwait(false);
        return ResultFromResponse(response);
    }

    public async Task<ToolResult?> TryStatusAsync(TimeSpan timeout, CancellationToken ct)
    {
        var response = await TryRequestAsync(new DaemonRequest("status", null, null), timeout, ct).ConfigureAwait(false);
        return ResultFromResponse(response);
    }

    public async Task<ToolResult?> TryShutdownAsync(TimeSpan timeout, CancellationToken ct)
    {
        var response = await TryRequestAsync(new DaemonRequest("shutdown", null, null), timeout, ct).ConfigureAwait(false);
        return ResultFromResponse(response);
    }

    private static ToolResult? ResultFromResponse(DaemonResponse? response)
    {
        if (response is null)
            return null;
        if (response.Result is not null)
            return response.Result;
        return string.IsNullOrWhiteSpace(response.Error) ? null : ToolResult.Error(response.Error);
    }

    private async Task<DaemonResponse?> TryRequestAsync(DaemonRequest request, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await using var pipe = new NamedPipeClientStream(".", InstancePipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            connectCts.CancelAfter(Min(timeout, PipeConnectTimeout));
            await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonUtil.LineSerializerOptions)).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            if (line is null)
                return null;

            return JsonSerializer.Deserialize<DaemonResponse>(line, JsonUtil.SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
}
