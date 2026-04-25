using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Browser;

internal static class CdpBrowserBridge
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
                await CdpWebSocketClient.SendAsync(client, "Input.dispatchMouseEvent", new JsonObject
                {
                    ["type"] = "mousePressed",
                    ["x"] = viewport.X,
                    ["y"] = viewport.Y,
                    ["button"] = button,
                    ["buttons"] = rightButton ? 2 : 1,
                    ["clickCount"] = i,
                    ["modifiers"] = modifierMask
                }, ct).ConfigureAwait(false);

                await CdpWebSocketClient.SendAsync(client, "Input.dispatchMouseEvent", new JsonObject
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
                await CdpWebSocketClient.SendAsync(client, "Input.insertText", new JsonObject { ["text"] = text }, ct).ConfigureAwait(false);
                return guard.Finish(ActionReceipt.Success("cdp.input.insert_text"));
            }

            for (var i = 0; i < units.Count; i++)
            {
                await CdpWebSocketClient.SendAsync(client, "Input.insertText", new JsonObject { ["text"] = units[i] }, ct).ConfigureAwait(false);
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
            await CdpWebSocketClient.SendAsync(client, "Runtime.evaluate", new JsonObject
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

        var matched = CdpPageMatcher.BestPageForWindowTitle(pages, WindowEnumerator.Find(windowId.Value)?.Title);
        if (matched is not null)
            return matched["webSocketDebuggerUrl"]!.GetValue<string>();

        return pages[0]["webSocketDebuggerUrl"]!.GetValue<string>();
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
            switch (ModifierKeys.Normalize(modifier))
            {
                case ModifierKey.Alt:
                    mask |= 1;
                    break;
                case ModifierKey.Control:
                    mask |= 2;
                    break;
                case ModifierKey.Meta:
                    mask |= 4;
                    break;
                case ModifierKey.Shift:
                    mask |= 8;
                    break;
            }
        }

        return mask;
    }

}
