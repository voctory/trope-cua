using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CuaDriver.Win.Browser;

internal static class CdpWebSocketClient
{
    private static int _nextId;

    public static async Task SendAsync(ClientWebSocket ws, string method, JsonObject parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var obj = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };
        var payload = Encoding.UTF8.GetBytes(obj.ToJsonString(JsonUtil.SerializerOptions));
        await ws.SendAsync(payload, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

        var buffer = new byte[8192];
        while (true)
        {
            var text = await ReceiveMessageAsync(ws, buffer, ct).ConfigureAwait(false);
            if (text is null || IsResponseForId(text, id))
                return;
        }
    }

    private static async Task<string?> ReceiveMessageAsync(ClientWebSocket ws, byte[] buffer, CancellationToken ct)
    {
        var message = new StringBuilder();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage)
                return message.ToString();
        }
    }

    private static bool IsResponseForId(string message, int id)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("id", out var idNode)
                   && idNode.TryGetInt32(out var responseId)
                   && responseId == id;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
