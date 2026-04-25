using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Mcp;

public sealed record DaemonRequest(string Method, string? Name, JsonObject? Args);
public sealed record DaemonResponse(bool Ok, ToolResult? Result, string? Error);

public sealed class NamedPipeDaemon
{
    private readonly ToolRegistry _registry;
    private readonly ToolContext _context;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public NamedPipeDaemon(ToolRegistry registry, ToolContext context)
    {
        _registry = registry;
        _context = context;
    }

    public static string PipeName
    {
        get
        {
            return $"cua-driver-win-{UserKey()}";
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var mutex = new Mutex(initiallyOwned: false, DaemonMutexName);
        if (!mutex.WaitOne(0))
        {
            Console.Error.WriteLine($"cua-driver-win daemon already running on named pipe {PipeName}");
            return;
        }

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Console.Error.WriteLine($"cua-driver-win daemon listening on named pipe {PipeName}");
        while (!shutdown.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(shutdown.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };

            try
            {
                var line = await reader.ReadLineAsync(shutdown.Token).ConfigureAwait(false);
                if (line is null)
                    continue;
                var request = JsonSerializer.Deserialize<DaemonRequest>(line, JsonUtil.SerializerOptions);
                if (request is null)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new DaemonResponse(false, null, "Invalid daemon request"), JsonUtil.LineSerializerOptions)).ConfigureAwait(false);
                    continue;
                }

                var response = request.Method switch
                {
                    "call" when !string.IsNullOrWhiteSpace(request.Name) => await CallAsync(request, shutdown.Token).ConfigureAwait(false),
                    "status" => new DaemonResponse(true, StatusResult(), null),
                    "shutdown" => new DaemonResponse(true, ToolResult.Text("✅ daemon shutdown requested", StatusObject()), null),
                    _ => new DaemonResponse(false, null, "Invalid daemon request")
                };

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonUtil.LineSerializerOptions)).ConfigureAwait(false);
                if (request.Method == "shutdown")
                    shutdown.Cancel();
            }
            catch (Exception ex)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new DaemonResponse(false, null, $"{ex.GetType().Name}: {ex.Message}"), JsonUtil.LineSerializerOptions)).ConfigureAwait(false);
            }
        }
    }

    private async Task<DaemonResponse> CallAsync(DaemonRequest request, CancellationToken ct)
    {
        var result = await _registry.InvokeAsync(request.Name!, request.Args ?? new JsonObject(), _context, ct).ConfigureAwait(false);
        return new DaemonResponse(!result.IsError, result, null);
    }

    public async Task<ToolResult?> TryCallAsync(string name, JsonObject args, TimeSpan timeout, CancellationToken ct)
    {
        var response = await TryRequestAsync(new DaemonRequest("call", name, args), timeout, ct).ConfigureAwait(false);
        if (response is null)
            return null;
        if (response.Result is not null)
            return response.Result;
        if (!string.IsNullOrWhiteSpace(response.Error))
            return ToolResult.Error(response.Error);
        return null;
    }

    public async Task<ToolResult?> TryStatusAsync(TimeSpan timeout, CancellationToken ct)
    {
        var response = await TryRequestAsync(new DaemonRequest("status", null, null), timeout, ct).ConfigureAwait(false);
        if (response is null)
            return null;
        if (response.Result is not null)
            return response.Result;
        return string.IsNullOrWhiteSpace(response.Error) ? null : ToolResult.Error(response.Error);
    }

    public async Task<ToolResult?> TryShutdownAsync(TimeSpan timeout, CancellationToken ct)
    {
        var response = await TryRequestAsync(new DaemonRequest("shutdown", null, null), timeout, ct).ConfigureAwait(false);
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
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
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

    private ToolResult StatusResult()
    {
        var structured = StatusObject();
        var text = $"✅ daemon: running pid={Environment.ProcessId} pipe={PipeName} started_at={_startedAt:O}";
        return ToolResult.Text(text, structured);
    }

    private JsonObject StatusObject() => new()
    {
        ["running"] = true,
        ["pid"] = Environment.ProcessId,
        ["pipe_name"] = PipeName,
        ["started_at"] = _startedAt.ToString("O"),
        ["uptime_ms"] = (long)(DateTimeOffset.UtcNow - _startedAt).TotalMilliseconds
    };

    private static string UserKey()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        foreach (var ch in Path.GetInvalidFileNameChars())
            sid = sid.Replace(ch, '_');
        return sid;
    }

    private static string DaemonMutexName => $@"Local\cua-driver-win-{UserKey()}";
}
