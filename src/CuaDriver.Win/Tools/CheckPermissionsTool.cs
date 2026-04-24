using System.Security.Principal;
using System.Text.Json.Nodes;
using CuaDriver.Win.Capture;
using CuaDriver.Win.HardCases;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class CheckPermissionsTool : IDriverTool
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
        var lines = new List<string>
        {
            "✅ Windows permission/capability probe",
            $"admin={admin}",
            $"wgc_supported_by_os={WgcCaptureSeam.IsSupportedByOs()}",
            $"wgc_status=\"{WgcCaptureSeam.Status()}\"",
            $"appbroadcast_inputinjector=\"{AppBroadcastInputInjector.Status()}\"",
            $"child_session=\"{ChildSessionBroker.Status()}\"",
            $"allow_parent_sendinput={context.State.Config.AllowParentSendInput}",
            "uia_available=true",
            "note=\"The default lane refuses parent-session SendInput and reports route receipts for every mutating action.\""
        };
        return Task.FromResult(ToolResult.Text(string.Join(Environment.NewLine, lines)));
    }
}
