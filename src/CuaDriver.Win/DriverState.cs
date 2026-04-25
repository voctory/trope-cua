using System.Collections.Concurrent;
using System.Windows.Automation;
using CuaDriver.Win.Cursor;
using CuaDriver.Win.Recording;

namespace CuaDriver.Win;

public sealed class DriverState
{
    public DriverConfig Config { get; set; } = DriverConfig.Load();
    public Uia.UiAutomationTree UiaTree { get; } = new();
    public ConcurrentDictionary<(int Pid, long WindowId), double> ImageResizeRatio { get; } = new();
    public ConcurrentDictionary<int, ZoomContext> ZoomContexts { get; } = new();
    public ConcurrentDictionary<(int Pid, long WindowId), IntPtr> LastTargetHwnd { get; } = new();
    public ConcurrentDictionary<(int Pid, long WindowId), AutomationElement> LastUiaTextTarget { get; } = new();
    public AgentCursorOverlay AgentCursor { get; }
    public RecordingSession Recording { get; } = new();

    public DriverState(string? instanceId = null)
    {
        AgentCursor = new AgentCursorOverlay(instanceId);
        ApplyLiveConfig();
    }

    public void SaveConfig(DriverConfig config, string? changedKey = null)
    {
        var next = config.Normalize();
        next.Save();
        Config = next;
        ApplyLiveConfig(changedKey);
    }

    private void ApplyLiveConfig(string? changedKey = null)
    {
        if (changedKey is null || changedKey.Equals("agent_cursor.enabled", StringComparison.OrdinalIgnoreCase))
            AgentCursor.SetEnabled(Config.AgentCursor.Enabled);

        if (changedKey is null ||
            changedKey.Equals("agent_cursor.motion", StringComparison.OrdinalIgnoreCase) ||
            changedKey.StartsWith("agent_cursor.motion.", StringComparison.OrdinalIgnoreCase))
            ApplyAgentCursorMotion();
    }

    private void ApplyAgentCursorMotion()
    {
        var motion = Config.AgentCursor.Motion;
        AgentCursor.UpdateMotion(
            motion.StartHandle,
            motion.EndHandle,
            motion.ArcSize,
            motion.ArcFlow,
            motion.Spring,
            motion.GlideDurationMs,
            motion.DwellAfterClickMs,
            motion.IdleHideMs,
            motion.PressDurationMs);
    }
}

public sealed record ZoomContext(int OriginX, int OriginY, int Width, int Height, double Ratio, long WindowId);
