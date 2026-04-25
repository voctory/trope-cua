namespace CuaDriver.Win.Tooling;

public sealed class ToolContext
{
    public required DriverState State { get; init; }
    public required ToolRegistry Registry { get; init; }
}
