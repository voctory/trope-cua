using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Browser;

public static class CdpBrowserBridge
{
    private static readonly HttpClient PageDiscoveryClient = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(2),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public static async Task<ActionReceipt?> TryClickAsync(IntPtr hwnd, long windowId, double x, double y, int count, bool rightButton, int? port, CancellationToken ct, IReadOnlyCollection<string>? modifiers = null)
    {
        if (port is null)
            return null;

        using var guard = NoRegressionGuard.Capture();

        try
        {
            var wsUrl = await PageWebSocketUrlAsync(port.Value, windowId, ct).ConfigureAwait(false);
            if (wsUrl is null)
                return null;

            var viewport = WindowPointToViewport(windowId, hwnd, x, y);
            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);

            var button = rightButton ? "right" : "left";
            var modifierMask = CdpModifierMask(modifiers);
            var normalizedCount = Math.Max(1, count);
            for (var i = 1; i <= normalizedCount; i++)
            {
                await SendAsync(client, "Input.dispatchMouseEvent", new JsonObject
                {
                    ["type"] = "mousePressed",
                    ["x"] = viewport.X,
                    ["y"] = viewport.Y,
                    ["button"] = button,
                    ["buttons"] = rightButton ? 2 : 1,
                    ["clickCount"] = i,
                    ["modifiers"] = modifierMask
                }, ct).ConfigureAwait(false);

                await SendAsync(client, "Input.dispatchMouseEvent", new JsonObject
                {
                    ["type"] = "mouseReleased",
                    ["x"] = viewport.X,
                    ["y"] = viewport.Y,
                    ["button"] = button,
                    ["buttons"] = 0,
                    ["clickCount"] = i,
                    ["modifiers"] = modifierMask
                }, ct).ConfigureAwait(false);

                if (i < normalizedCount)
                    await Task.Delay(80, ct).ConfigureAwait(false);
            }

            return guard.Finish(ActionReceipt.Success(rightButton ? "cdp.input.dispatch_mouse.right" : "cdp.input.dispatch_mouse"));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("cdp.input.dispatch_mouse", ex.Message));
        }
    }

    public static async Task<ActionReceipt?> TryTypeTextAsync(int? port, string text, int delayMs, CancellationToken ct)
        => await TryTypeTextAsync(port, null, text, delayMs, ct).ConfigureAwait(false);

    public static async Task<ActionReceipt?> TryTypeTextAsync(int? port, long? windowId, string text, int delayMs, CancellationToken ct)
    {
        if (port is null)
            return null;

        using var guard = NoRegressionGuard.Capture();
        try
        {
            var wsUrl = await PageWebSocketUrlAsync(port.Value, windowId, ct).ConfigureAwait(false);
            if (wsUrl is null)
                return null;

            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
            var units = TextElementSplitter.Split(text);
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

    public static async Task<ActionReceipt> EvaluateUserGestureAsync(int port, string expression, CancellationToken ct)
        => await EvaluateUserGestureAsync(port, null, expression, ct).ConfigureAwait(false);

    public static async Task<ActionReceipt> EvaluateUserGestureAsync(int port, long? windowId, string expression, CancellationToken ct)
    {
        using var guard = NoRegressionGuard.Capture();
        try
        {
            var wsUrl = await PageWebSocketUrlAsync(port, windowId, ct).ConfigureAwait(false)
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

    private static async Task<string?> PageWebSocketUrlAsync(int port, long? windowId, CancellationToken ct)
    {
        var json = await PageDiscoveryClient.GetStringAsync($"http://127.0.0.1:{port}/json", ct).ConfigureAwait(false);
        var arr = JsonNode.Parse(json) as JsonArray;
        if (arr is null)
            return null;

        var pages = arr
            .OfType<JsonObject>()
            .Where(node => node["type"]?.GetValue<string>() == "page"
                           && !string.IsNullOrWhiteSpace(node["webSocketDebuggerUrl"]?.GetValue<string>()))
            .ToArray();
        if (pages.Length == 0)
            return null;
        if (pages.Length == 1 || windowId is null)
            return pages[0]["webSocketDebuggerUrl"]!.GetValue<string>();

        var windowTitle = WindowEnumerator.Find(windowId.Value)?.Title;
        var normalizedWindowTitle = NormalizeBrowserTitle(windowTitle);
        if (!string.IsNullOrWhiteSpace(normalizedWindowTitle))
        {
            var best = pages
                .Select(page => new
                {
                    Page = page,
                    Score = TargetScore(normalizedWindowTitle, page["title"]?.GetValue<string>(), page["url"]?.GetValue<string>())
                })
                .OrderByDescending(item => item.Score)
                .FirstOrDefault();
            if (best is not null && best.Score > 0)
                return best.Page["webSocketDebuggerUrl"]!.GetValue<string>();
        }

        return pages[0]["webSocketDebuggerUrl"]!.GetValue<string>();
    }

    private static int TargetScore(string normalizedWindowTitle, string? pageTitle, string? pageUrl)
    {
        var title = NormalizeTitle(pageTitle);
        var url = NormalizeTitle(pageUrl);
        if (!string.IsNullOrWhiteSpace(title))
        {
            if (normalizedWindowTitle.Equals(title, StringComparison.OrdinalIgnoreCase))
                return 100;
            if (normalizedWindowTitle.Contains(title, StringComparison.OrdinalIgnoreCase))
                return 80;
            if (title.Contains(normalizedWindowTitle, StringComparison.OrdinalIgnoreCase))
                return 60;
        }

        if (!string.IsNullOrWhiteSpace(url) && normalizedWindowTitle.Contains(url, StringComparison.OrdinalIgnoreCase))
            return 20;

        return 0;
    }

    private static string NormalizeBrowserTitle(string? title)
    {
        var normalized = NormalizeTitle(title);
        if (string.IsNullOrWhiteSpace(normalized))
            return normalized;

        string[] suffixes =
        [
            " - Google Chrome",
            " - Microsoft Edge",
            " - Brave",
            " - Opera",
            " - Vivaldi"
        ];

        foreach (var suffix in suffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return normalized[..^suffix.Length].Trim();
        }

        return normalized;
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static (double X, double Y) WindowPointToViewport(long windowId, IntPtr hwnd, double x, double y)
    {
        var screen = WindowMessageInput.WindowLocalToScreen(hwnd, x, y);
        var doc = UiAutomationTree.FindFirstDocumentBounds(windowId);
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

    private static int _nextId;

}
