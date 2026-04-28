using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Cursor;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Mcp;

internal sealed record DaemonRequest(string Method, string? Name, JsonObject? Args);
internal sealed record DaemonResponse(bool Ok, ToolResult? Result, string? Error);
internal sealed record DaemonInstanceRecord(string InstanceId, int Pid, string PipeName, string StartedAt, string ExePath, string? CursorPalette = null);

internal sealed class NamedPipeDaemon
{
    private readonly ToolRegistry _registry;
    private readonly ToolContext _context;
    private readonly string _instanceId;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();

    public NamedPipeDaemon(ToolRegistry registry, ToolContext context, string? instanceId = null)
    {
        _registry = registry;
        _context = context;
        _instanceId = DriverInstance.Resolve(instanceId);
    }

    public string InstancePipeName => PipeNameFor(_instanceId);

    public async Task RunAsync(CancellationToken ct)
    {
        using var mutex = new Mutex(initiallyOwned: false, DaemonMutexNameFor(_instanceId));
        if (!mutex.WaitOne(0))
        {
            Console.Error.WriteLine($"trope-cua daemon instance '{_instanceId}' already running on named pipe {InstancePipeName}");
            return;
        }

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var paletteName = WriteInstanceRecord();
        _context.State.AgentCursor.SetPalette(AgentCursorPalette.ForNameOrInstance(paletteName, _instanceId));
        Console.Error.WriteLine($"trope-cua daemon instance '{_instanceId}' listening on named pipe {InstancePipeName}");
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(InstancePipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
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
                        await WriteResponseAsync(writer, new DaemonResponse(false, null, "Invalid daemon request")).ConfigureAwait(false);
                        continue;
                    }

                    var response = request.Method switch
                    {
                        "call" when !string.IsNullOrWhiteSpace(request.Name) => await CallAsync(request, shutdown.Token).ConfigureAwait(false),
                        "status" => new DaemonResponse(true, StatusResult(), null),
                        "shutdown" => new DaemonResponse(true, ToolResult.Text(ToolText.OkPrefix + "daemon shutdown requested", StatusObject()), null),
                        _ => new DaemonResponse(false, null, "Invalid daemon request")
                    };

                    await WriteResponseAsync(writer, response).ConfigureAwait(false);
                    if (request.Method == "shutdown")
                        shutdown.Cancel();
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                    // Expected during daemon shutdown.
                }
                catch (Exception ex)
                {
                    await WriteResponseAsync(writer, new DaemonResponse(false, null, $"{ex.GetType().Name}: {ex.Message}")).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            DaemonRegistry.Remove(_instanceId);
        }
    }

    private async Task<DaemonResponse> CallAsync(DaemonRequest request, CancellationToken ct)
    {
        var result = await _registry.InvokeAsync(request.Name!, request.Args ?? new JsonObject(), _context, ct).ConfigureAwait(false);
        return new DaemonResponse(!result.IsError, result, null);
    }

    private static async Task WriteResponseAsync(StreamWriter writer, DaemonResponse response)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonUtil.LineSerializerOptions)).ConfigureAwait(false);
    }

    private ToolResult StatusResult()
    {
        var structured = StatusObject();
        var text = $"{ToolText.OkPrefix}daemon: running instance={_instanceId} pid={Environment.ProcessId} pipe={InstancePipeName} started_at={_startedAt:O}";
        return ToolResult.Text(text, structured);
    }

    private JsonObject StatusObject() => new()
    {
        ["running"] = true,
        ["instance_id"] = _instanceId,
        ["pid"] = Environment.ProcessId,
        ["pipe_name"] = InstancePipeName,
        ["started_at"] = _startedAt.ToString("O"),
        ["cursor_palette"] = _context.State.AgentCursor.Palette.Name,
        ["uptime_ms"] = (long)Stopwatch.GetElapsedTime(_startedTimestamp).TotalMilliseconds
    };

    public static string PipeNameFor(string? instanceId) => $"trope-cua-{UserKey()}-{DriverInstance.Normalize(instanceId)}";

    public static ToolResult ListInstances()
    {
        var records = RegisteredInstances();
        var structuredRecords = new JsonArray();
        var lines = new List<string> { $"{ToolText.OkPrefix}daemon instances: {records.Count}" };
        foreach (var record in records)
        {
            var running = IsInstanceRunning(record);
            structuredRecords.Add(new JsonObject
            {
                ["instance_id"] = record.InstanceId,
                ["pid"] = record.Pid,
                ["pipe_name"] = record.PipeName,
                ["started_at"] = record.StartedAt,
                ["exe_path"] = record.ExePath,
                ["cursor_palette"] = record.CursorPalette,
                ["running"] = running
            });
            lines.Add($"- instance={record.InstanceId} pid={record.Pid} running={running} palette={record.CursorPalette ?? "auto"} pipe={record.PipeName}");
        }

        return ToolResult.Text(string.Join(Environment.NewLine, lines), new JsonObject
        {
            ["instances"] = structuredRecords
        });
    }

    public static IReadOnlyList<DaemonInstanceRecord> RegisteredInstances() => DaemonRegistry.RegisteredInstances();

    private static string UserKey()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        foreach (var ch in Path.GetInvalidFileNameChars())
            sid = sid.Replace(ch, '_');
        return sid;
    }

    private static string DaemonMutexNameFor(string instanceId) => $@"Local\trope-cua-{UserKey()}-{DriverInstance.Normalize(instanceId)}";

    private string WriteInstanceRecord()
    {
        var record = new DaemonInstanceRecord(
            _instanceId,
            Environment.ProcessId,
            InstancePipeName,
            _startedAt.ToString("O"),
            Environment.ProcessPath ?? "");
        return DaemonRegistry.WriteWithAllocatedCursorPalette(record);
    }

    public static bool IsInstanceRunning(DaemonInstanceRecord record) => DaemonRegistry.IsInstanceRunning(record);

    public static bool RemoveInstanceRecord(string instanceId) => DaemonRegistry.Remove(instanceId);
}
