using System.Security.Principal;
using System.Text.Json.Nodes;
using CuaDriver.Win.Capture;
using CuaDriver.Win.HardCases;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal sealed class CheckPermissionsTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "check_permissions",
        "Report Windows automation/capture capability status for this process.",
        JsonArgs.Schema(),
        ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        var admin = principal.IsInRole(WindowsBuiltInRole.Administrator);
        var wgcSupported = WgcCaptureSeam.IsSupportedByOs();
        var childSessionId = ChildSessionBroker.GetChildSessionId();
        var childSessionsEnabled = ChildSessionBroker.IsEnabled();
        var appBroadcastStatus = AppBroadcastInputInjector.Status();
        var lines = new List<string>
        {
            ToolText.OkPrefix + "Windows permission/capability probe",
            $"admin={admin}",
            $"wgc_supported_by_os={wgcSupported}",
            $"wgc_status=\"{WgcCaptureSeam.Status()}\"",
            $"appbroadcast_inputinjector=\"{appBroadcastStatus}\"",
            $"parent_session_id={ChildSessionBroker.CurrentProcessSessionId()}",
            $"active_console_session_id={ChildSessionBroker.ActiveConsoleSessionId()}",
            $"child_sessions_supported_by_os={ChildSessionBroker.IsSupportedByOs()}",
            $"child_sessions_enabled={childSessionsEnabled}",
            $"child_session=\"{ChildSessionBroker.Status()}\"",
            $"child_session_host=\"{ChildSessionHost.StatusText()}\"",
            $"allow_parent_sendinput={context.State.Config.AllowParentSendInput}",
            "uia_available=true",
            "note=\"The default lane refuses parent-session SendInput and reports route receipts for every mutating action.\""
        };
        return Task.FromResult(ToolResult.Text(string.Join(Environment.NewLine, lines), new JsonObject
        {
            ["admin"] = admin,
            ["wgc_supported_by_os"] = wgcSupported,
            ["wgc_status"] = WgcCaptureSeam.Status(),
            ["appbroadcast_inputinjector"] = appBroadcastStatus,
            ["parent_session_id"] = ChildSessionBroker.CurrentProcessSessionId(),
            ["active_console_session_id"] = ChildSessionBroker.ActiveConsoleSessionId(),
            ["child_sessions_supported_by_os"] = ChildSessionBroker.IsSupportedByOs(),
            ["child_sessions_enabled"] = childSessionsEnabled,
            ["child_session_id"] = childSessionId,
            ["child_session_connected"] = childSessionId is not null,
            ["child_session_host"] = ChildSessionHost.StatusObject(),
            ["allow_parent_sendinput"] = context.State.Config.AllowParentSendInput,
            ["uia_available"] = true
        }));
    }
}
