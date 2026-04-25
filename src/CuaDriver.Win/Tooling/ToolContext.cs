namespace CuaDriver.Win.Tooling;

internal sealed class ToolContext
{
    public required DriverState State { get; init; }
    public required ToolRegistry Registry { get; init; }
}
