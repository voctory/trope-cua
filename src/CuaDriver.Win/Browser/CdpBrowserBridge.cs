using System.Net.Http;
using System.Net.WebSockets;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Browser;

public sealed class CdpBrowserBridge
{
    private readonly UiAutomationTree _uia;

    public CdpBrowserBridge(UiAutomationTree uia)
    {
        _uia = uia;
    }

    public async Task<ActionReceipt?> TryClickAsync(IntPtr hwnd, long windowId, double x, double y, int count, bool rightButton, int? port, CancellationToken ct, IReadOnlyCollection<string>? modifiers = null)
    {
        if (port is null)
            return null;

        var guard = NoRegressionGuard.Capture();

        try
        {
            var wsUrl = await FirstPageWebSocketUrlAsync(port.Value, ct).ConfigureAwait(false);
            if (wsUrl is null)
                return null;

            var viewport = WindowPointToViewport(windowId, hwnd, x, y);
            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);

            var button = rightButton ? "right" : "left";
            var modifierMask = CdpModifierMask(modifiers);
            await SendAsync(client, "Input.dispatchMouseEvent", new JsonObject
            {
                ["type"] = "mousePressed",
                ["x"] = viewport.X,
                ["y"] = viewport.Y,
                ["button"] = button,
                ["buttons"] = rightButton ? 2 : 1,
                ["clickCount"] = Math.Max(1, count),
                ["modifiers"] = modifierMask
            }, ct).ConfigureAwait(false);

            await SendAsync(client, "Input.dispatchMouseEvent", new JsonObject
            {
                ["type"] = "mouseReleased",
                ["x"] = viewport.X,
                ["y"] = viewport.Y,
                ["button"] = button,
                ["buttons"] = 0,
                ["clickCount"] = Math.Max(1, count),
                ["modifiers"] = modifierMask
            }, ct).ConfigureAwait(false);

            return guard.Finish(ActionReceipt.Success(rightButton ? "cdp.input.dispatch_mouse.right" : "cdp.input.dispatch_mouse"));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("cdp.input.dispatch_mouse", ex.Message));
        }
    }

    public async Task<ActionReceipt?> TryTypeTextAsync(int? port, string text, int delayMs, CancellationToken ct)
    {
        if (port is null)
            return null;

        var guard = NoRegressionGuard.Capture();
        try
        {
            var wsUrl = await FirstPageWebSocketUrlAsync(port.Value, ct).ConfigureAwait(false);
            if (wsUrl is null)
                return null;

            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
            var units = TextElements(text);
            if (delayMs <= 0 || units.Count <= 1)
            {
                await SendAsync(client, "Input.insertText", new JsonObject { ["text"] = text }, ct).ConfigureAwait(false);
                return guard.Finish(ActionReceipt.Success("cdp.input.insert_text"));
            }

            for (var i = 0; i < units.Count; i++)
            {
                await SendAsync(client, "Input.insertText", new JsonObject { ["text"] = units[i] }, ct).ConfigureAwait(false);
                if (i + 1 < units.Count)
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }

            return guard.Finish(ActionReceipt.Success("cdp.input.insert_text.stream"));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("cdp.input.insert_text", ex.Message));
        }
    }

    public async Task<ActionReceipt> EvaluateUserGestureAsync(int port, string expression, CancellationToken ct)
    {
        var guard = NoRegressionGuard.Capture();
        try
        {
            var wsUrl = await FirstPageWebSocketUrlAsync(port, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"No page tab found on CDP port {port}.");

            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
            await SendAsync(client, "Runtime.evaluate", new JsonObject
            {
                ["expression"] = expression,
                ["userGesture"] = true,
                ["awaitPromise"] = true
            }, ct).ConfigureAwait(false);
            return guard.Finish(ActionReceipt.Success("cdp.runtime.evaluate.user_gesture"));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("cdp.runtime.evaluate.user_gesture", ex.Message));
        }
    }

    private async Task<string?> FirstPageWebSocketUrlAsync(int port, CancellationToken ct)
    {
        using var http = new HttpClient();
        var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json", ct).ConfigureAwait(false);
        var arr = JsonNode.Parse(json) as JsonArray;
        if (arr is null)
            return null;

        foreach (var node in arr.OfType<JsonObject>())
        {
            var type = node["type"]?.GetValue<string>();
            var ws = node["webSocketDebuggerUrl"]?.GetValue<string>();
            if (type == "page" && !string.IsNullOrWhiteSpace(ws))
                return ws;
        }
        return null;
    }

    private (double X, double Y) WindowPointToViewport(long windowId, IntPtr hwnd, double x, double y)
    {
        var screen = WindowMessageInput.WindowLocalToScreen(hwnd, x, y);
        var doc = _uia.FindFirstDocumentBounds(windowId);
        if (doc is { } rect && !rect.IsEmpty)
            return (screen.X - rect.X, screen.Y - rect.Y);

        // Fallback: assume caller's pixel coordinate is already content-relative.
        return (x, y);
    }

    private static int CdpModifierMask(IReadOnlyCollection<string>? modifiers)
    {
        if (modifiers is null || modifiers.Count == 0)
            return 0;

        var mask = 0;
        foreach (var modifier in modifiers)
        {
            switch (modifier.Trim().ToLowerInvariant())
            {
                case "alt":
                case "option":
                    mask |= 1;
                    break;
                case "ctrl":
                case "control":
                    mask |= 2;
                    break;
                case "cmd":
                case "meta":
                case "win":
                    mask |= 4;
                    break;
                case "shift":
                    mask |= 8;
                    break;
            }
        }

        return mask;
    }

    private static async Task SendAsync(ClientWebSocket ws, string method, JsonObject parameters, CancellationToken ct)
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

        // Drain the response for protocol hygiene; callers currently don't need the payload.
        var buffer = new byte[8192];
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return;
            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (text.Contains($"\"id\":{id}", StringComparison.Ordinal) || text.Contains($"\"id\": {id}", StringComparison.Ordinal))
                return;
            if (result.EndOfMessage)
                return;
        }
    }

    private static int _nextId;

    private static IReadOnlyList<string> TextElements(string text)
    {
        var result = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            result.Add(enumerator.GetTextElement());
        return result;
    }
}
