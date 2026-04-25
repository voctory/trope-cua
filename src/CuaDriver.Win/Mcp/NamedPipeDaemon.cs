using System.Diagnostics;
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
public sealed record DaemonInstanceRecord(string InstanceId, int Pid, string PipeName, string StartedAt, string ExePath);

public sealed class NamedPipeDaemon
{
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(2);

    private readonly ToolRegistry _registry;
    private readonly ToolContext _context;
    private readonly string _instanceId;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public NamedPipeDaemon(ToolRegistry registry, ToolContext context, string? instanceId = null)
    {
        _registry = registry;
        _context = context;
        _instanceId = NormalizeInstanceId(instanceId ?? Environment.GetEnvironmentVariable("CUA_DRIVER_INSTANCE") ?? "default");
    }

    public static string PipeName => PipeNameFor(Environment.GetEnvironmentVariable("CUA_DRIVER_INSTANCE") ?? "default");

    public string InstancePipeName => PipeNameFor(_instanceId);

    public async Task RunAsync(CancellationToken ct)
    {
        using var mutex = new Mutex(initiallyOwned: false, DaemonMutexNameFor(_instanceId));
        if (!mutex.WaitOne(0))
        {
            Console.Error.WriteLine($"cua-driver-win daemon instance '{_instanceId}' already running on named pipe {InstancePipeName}");
            return;
        }

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
        WriteInstanceRecord();
        Console.Error.WriteLine($"cua-driver-win daemon instance '{_instanceId}' listening on named pipe {InstancePipeName}");
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
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                    // Expected during daemon shutdown.
                }
                catch (Exception ex)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new DaemonResponse(false, null, $"{ex.GetType().Name}: {ex.Message}"), JsonUtil.LineSerializerOptions)).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            RemoveInstanceRecord(_instanceId);
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

    private ToolResult StatusResult()
    {
        var structured = StatusObject();
        var text = $"✅ daemon: running instance={_instanceId} pid={Environment.ProcessId} pipe={InstancePipeName} started_at={_startedAt:O}";
        return ToolResult.Text(text, structured);
    }

    private JsonObject StatusObject() => new()
    {
        ["running"] = true,
        ["instance_id"] = _instanceId,
        ["pid"] = Environment.ProcessId,
        ["pipe_name"] = InstancePipeName,
        ["started_at"] = _startedAt.ToString("O"),
        ["uptime_ms"] = (long)(DateTimeOffset.UtcNow - _startedAt).TotalMilliseconds
    };

    public static string PipeNameFor(string? instanceId) => $"cua-driver-win-{UserKey()}-{NormalizeInstanceId(instanceId ?? "default")}";

    public static ToolResult ListInstances()
    {
        var records = RegisteredInstances();
        var structuredRecords = new JsonArray();
        var lines = new List<string> { $"✅ daemon instances: {records.Count}" };
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
                ["running"] = running
            });
            lines.Add($"- instance={record.InstanceId} pid={record.Pid} running={running} pipe={record.PipeName}");
        }

        return ToolResult.Text(string.Join(Environment.NewLine, lines), new JsonObject
        {
            ["instances"] = structuredRecords
        });
    }

    public static IReadOnlyList<DaemonInstanceRecord> RegisteredInstances() => ReadInstanceRecords();

    private static string UserKey()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        foreach (var ch in Path.GetInvalidFileNameChars())
            sid = sid.Replace(ch, '_');
        return sid;
    }

    private static string DaemonMutexNameFor(string instanceId) => $@"Local\cua-driver-win-{UserKey()}-{NormalizeInstanceId(instanceId)}";

    private static string NormalizeInstanceId(string instanceId)
    {
        var normalized = string.IsNullOrWhiteSpace(instanceId) ? "default" : instanceId.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', ';', ' ']))
            normalized = normalized.Replace(ch, '_');
        return normalized.Length > 64 ? normalized[..64] : normalized;
    }

    private void WriteInstanceRecord()
    {
        Directory.CreateDirectory(DaemonRegistryDirectory);
        var record = new DaemonInstanceRecord(
            _instanceId,
            Environment.ProcessId,
            InstancePipeName,
            _startedAt.ToString("O"),
            Environment.ProcessPath ?? "");
        var path = InstanceRecordPath(_instanceId);
        var tmp = path + $".{Environment.ProcessId}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(record, JsonUtil.SerializerOptions));
        File.Move(tmp, path, overwrite: true);
    }

    public static bool IsInstanceRunning(DaemonInstanceRecord record) => IsProcessRunning(record.Pid, record.ExePath);

    public static bool RemoveInstanceRecord(string instanceId)
    {
        try
        {
            var path = InstanceRecordPath(instanceId);
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch
        {
            // Best-effort cleanup; daemon-list ignores stale dead pids.
            return false;
        }
    }

    private static DaemonInstanceRecord[] ReadInstanceRecords()
    {
        if (!Directory.Exists(DaemonRegistryDirectory))
            return [];

        var records = new List<DaemonInstanceRecord>();
        foreach (var path in Directory.EnumerateFiles(DaemonRegistryDirectory, "*.json"))
        {
            try
            {
                var record = JsonSerializer.Deserialize<DaemonInstanceRecord>(File.ReadAllText(path), JsonUtil.SerializerOptions);
                if (record is not null)
                    records.Add(record);
            }
            catch
            {
                // Ignore corrupt/stale registry files.
            }
        }

        return records.OrderBy(record => record.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsProcessRunning(int pid, string exePath)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            if (string.IsNullOrWhiteSpace(exePath))
                return !process.HasExited;
            return !process.HasExited && string.Equals(process.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string DaemonRegistryDirectory => Path.Combine(DriverConfig.ConfigDirectory, "daemons");

    private static string InstanceRecordPath(string instanceId) => Path.Combine(DaemonRegistryDirectory, $"{NormalizeInstanceId(instanceId)}.json");
}
