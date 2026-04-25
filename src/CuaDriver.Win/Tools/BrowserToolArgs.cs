using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal static class BrowserToolArgs
{
    public static int? CdpPort(JsonObject args, ToolContext context)
    {
        var explicitPort = JsonArgs.OptionalInt(args, "cdp_port");
        return explicitPort is null
            ? context.State.Config.ChromiumDebuggingPort
            : DriverConfig.ValidateTcpPort(explicitPort.Value);
    }
}
