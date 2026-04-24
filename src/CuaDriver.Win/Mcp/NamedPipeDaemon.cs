using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
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

    public NamedPipeDaemon(ToolRegistry registry, ToolContext context)
    {
        _registry = registry;
        _context = context;
    }

    private static string PipeName
    {
        get
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            foreach (var ch in Path.GetInvalidFileNameChars())
                sid = sid.Replace(ch, '_');
            return $"cua-driver-win-{sid}";
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Console.Error.WriteLine($"cua-driver-win daemon listening on named pipe {PipeName}");
        while (!ct.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(pipe);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };

            try
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                    continue;
                var request = JsonSerializer.Deserialize<DaemonRequest>(line, JsonUtil.SerializerOptions);
                if (request is null || request.Method != "call" || string.IsNullOrWhiteSpace(request.Name))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new DaemonResponse(false, null, "Invalid daemon request"), JsonUtil.LineSerializerOptions)).ConfigureAwait(false);
                    continue;
                }

                var result = await _registry.InvokeAsync(request.Name, request.Args ?? new JsonObject(), _context, ct).ConfigureAwait(false);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new DaemonResponse(!result.IsError, result, null), JsonUtil.LineSerializerOptions)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new DaemonResponse(false, null, $"{ex.GetType().Name}: {ex.Message}"), JsonUtil.LineSerializerOptions)).ConfigureAwait(false);
            }
        }
    }

    public async Task<ToolResult?> TryCallAsync(string name, JsonObject args, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };
            using var reader = new StreamReader(pipe);

            var request = new DaemonRequest("call", name, args);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonUtil.LineSerializerOptions)).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            if (line is null)
                return null;

            var response = JsonSerializer.Deserialize<DaemonResponse>(line, JsonUtil.SerializerOptions);
            if (response?.Result is not null)
                return response.Result;
            if (!string.IsNullOrWhiteSpace(response?.Error))
                return ToolResult.Error(response.Error);
            return null;
        }
        catch
        {
            return null;
        }
    }
}
