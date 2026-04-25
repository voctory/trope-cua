using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows.Forms;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Cursor;

public sealed record AgentCursorMotion
{
    [JsonPropertyName("start_handle")]
    public double StartHandle { get; init; } = 0.3;

    [JsonPropertyName("end_handle")]
    public double EndHandle { get; init; } = 0.3;

    [JsonPropertyName("arc_size")]
    public double ArcSize { get; init; } = 0.25;

    [JsonPropertyName("arc_flow")]
    public double ArcFlow { get; init; } = 0.0;

    [JsonPropertyName("spring")]
    public double Spring { get; init; } = 0.72;

    [JsonPropertyName("glide_duration_ms")]
    public double GlideDurationMs { get; init; } = 750;

    [JsonPropertyName("dwell_after_click_ms")]
    public double DwellAfterClickMs { get; init; } = 400;

    [JsonPropertyName("idle_hide_ms")]
    public double IdleHideMs { get; init; } = 20000;

    [JsonPropertyName("press_duration_ms")]
    public double PressDurationMs { get; init; } = 650;

    public static AgentCursorMotion Default { get; } = new();

    public JsonObject ToJsonObject() => JsonUtil.ToJsonObject(this);
}

public sealed record CursorSnapshot(bool Visible, int? ScreenX, int? ScreenY, long? TargetWindowId, string Layering);

public sealed class AgentCursorOverlay
{
    private const string OverlayWindowTitlePrefix = "CuaDriverWin.AgentCursorOverlay";
    private const double RestingHeadingRadians = Math.PI / 4;
    private const float CursorTipOffset = 16f;
    private const float SurfaceHalfSize = 76f;
    private const int Supersample = 3;
    private const double TurnRadius = 80;
    private const double PeakSpeed = 900;
    private const double MinStartSpeed = 300;
    private const double MinEndSpeed = 200;
    private const double SpringStiffness = 400;
    private const double SpringOvershoot = 0.8;
    private const double IdleBreathPeriodSeconds = 1.8;
    private const double IdleRotationPeriodSeconds = 2.4;
    private const double IdleRotationAmplitudeRadians = 0.10;
    private const double VisualHeadingCatchUpRadiansPerSecond = 18;
    private const double SameTargetTipTolerance = 3.0;
    private const double DefaultGlideDurationMs = 750;
    private const double FadeOutDurationMs = 180;
    private const float InitialOffscreenPosition = -200f;

    private readonly object _gate = new();
    private OverlayForm? _form;
    private Thread? _thread;
    private ManualResetEventSlim? _threadReady;
    private bool _enabled = true;
    private AgentCursorMotion _motion = AgentCursorMotion.Default;
    private readonly string _overlayWindowTitle;

    public AgentCursorOverlay(string? instanceId = null)
    {
        _overlayWindowTitle = OverlayWindowTitleFor(instanceId);
    }

    public bool Enabled
    {
        get
        {
            lock (_gate) return _enabled;
        }
    }

    public AgentCursorMotion Motion
    {
        get
        {
            lock (_gate) return _motion;
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            _enabled = enabled;
        }

        if (enabled)
        {
            Post(form => form.SetEnabled(true));
            return;
        }

        Post(form =>
        {
            form.SetEnabled(false);
            form.HideCursor();
        });
    }

    public AgentCursorMotion UpdateMotion(
        double? startHandle,
        double? endHandle,
        double? arcSize,
        double? arcFlow,
        double? spring,
        double? glideDurationMs,
        double? dwellAfterClickMs,
        double? idleHideMs,
        double? pressDurationMs)
    {
        AgentCursorMotion next;
        lock (_gate)
        {
            next = _motion with
            {
                StartHandle = Clamp(startHandle ?? _motion.StartHandle, 0, 1),
                EndHandle = Clamp(endHandle ?? _motion.EndHandle, 0, 1),
                ArcSize = Clamp(arcSize ?? _motion.ArcSize, 0, 1),
                ArcFlow = Clamp(arcFlow ?? _motion.ArcFlow, -1, 1),
                Spring = Clamp(spring ?? _motion.Spring, 0.3, 1),
                GlideDurationMs = Clamp(glideDurationMs ?? _motion.GlideDurationMs, 50, 5000),
                DwellAfterClickMs = Clamp(dwellAfterClickMs ?? _motion.DwellAfterClickMs, 0, 5000),
                IdleHideMs = Clamp(idleHideMs ?? _motion.IdleHideMs, 0, 60000),
                PressDurationMs = Clamp(pressDurationMs ?? _motion.PressDurationMs, 0, 5000),
            };
            _motion = next;
        }

        Post(form => form.SetMotion(next));
        return next;
    }

    public Task MoveToAsync(POINT screenPoint, CancellationToken ct) => MoveToAsync(screenPoint, null, ct);

    public async Task MoveToAsync(POINT screenPoint, IntPtr? targetHwnd, CancellationToken ct)
    {
        if (!Enabled)
            return;

        if (!EnsureThread())
            return;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Post(form => form.GlideTo(screenPoint.X, screenPoint.Y, targetHwnd ?? IntPtr.Zero, Motion, completion)))
            return;

        await completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public Task ClickPulseAsync(POINT screenPoint, CancellationToken ct) => ClickPulseAsync(screenPoint, null, ct);

    public async Task ClickPulseAsync(POINT screenPoint, IntPtr? targetHwnd, CancellationToken ct)
    {
        if (!Enabled)
            return;

        await MoveToAsync(screenPoint, targetHwnd, ct).ConfigureAwait(false);
        var motion = Motion;
        if (!Post(form => form.StartPress(motion)))
            return;

        var delayMs = Math.Max(0, motion.PressDurationMs + motion.DwellAfterClickMs);
        if (delayMs > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct).ConfigureAwait(false);
    }

    public JsonObject StateObject()
    {
        bool enabled;
        var formReady = false;
        var visible = false;
        int? screenX = null;
        int? screenY = null;
        long? targetWindowId = null;
        var layering = "uninitialized";
        AgentCursorMotion motion;
        lock (_gate)
        {
            enabled = _enabled;
            motion = _motion;
            formReady = _form is not null && !_form.IsDisposed;
            if (formReady)
            {
                var snapshot = _form!.InvokeRequired
                    ? (CursorSnapshot)_form.Invoke(new Func<CursorSnapshot>(_form.Snapshot))
                    : _form.Snapshot();
                visible = snapshot.Visible;
                screenX = snapshot.ScreenX;
                screenY = snapshot.ScreenY;
                targetWindowId = snapshot.TargetWindowId;
                layering = snapshot.Layering;
            }
        }

        return new JsonObject
        {
            ["enabled"] = enabled,
            ["route"] = "winforms.click_through_overlay",
            ["ready"] = formReady,
            ["visible"] = visible,
            ["screen_x"] = screenX,
            ["screen_y"] = screenY,
            ["target_window_id"] = targetWindowId,
            ["layering"] = layering,
            ["persistent"] = motion.IdleHideMs <= 0,
            ["motion"] = motion.ToJsonObject()
        };
    }

    public void KeepAlive()
    {
        Post(form => form.KeepAlive());
    }

    private bool EnsureThread()
    {
        ManualResetEventSlim ready;
        lock (_gate)
        {
            if (_thread is { IsAlive: true })
            {
                ready = _threadReady ?? new ManualResetEventSlim(true);
            }
            else
            {
                ready = new ManualResetEventSlim(false);
                _threadReady = ready;
                _thread = new Thread(() =>
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var form = new OverlayForm(_overlayWindowTitle);
                    form.SetMotion(Motion);
                    _ = form.Handle;
                    lock (_gate)
                    {
                        _form = form;
                    }
                    ready.Set();
                    Application.Run(new ApplicationContext());
                })
                {
                    IsBackground = true,
                    Name = "cua-driver-agent-cursor",
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }

        return ready.IsSet || SpinWait.SpinUntil(() => ready.IsSet, TimeSpan.FromSeconds(2));
    }

    private bool Post(Action<OverlayForm> action)
    {
        OverlayForm? form;
        lock (_gate)
        {
            form = _form;
        }

        if (form is null || form.IsDisposed || !form.IsHandleCreated)
            return false;

        try
        {
            if (form.InvokeRequired)
                form.BeginInvoke((MethodInvoker)(() => action(form)));
            else
                action(form);
            return true;
        }
        catch
        {
            // Overlay is a best-effort trust signal; input routes must not fail because it closed.
            return false;
        }
    }

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));

    private static bool IsDefaultOverlayTitle(string title) =>
        title.Equals(OverlayWindowTitleFor(DriverInstance.DefaultId), StringComparison.Ordinal);

    private static string OverlayWindowTitleFor(string? instanceId) =>
        $"{OverlayWindowTitlePrefix}.{DriverInstance.Resolve(instanceId)}";

    private static POINT CurrentCursorPosition()
    {
        if (NativeMethods.GetCursorPos(out var point))
            return point;

        var left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        return new POINT(left + width / 2, top + height / 2);
    }

    private sealed class OverlayForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Rectangle _virtualBounds;
        private readonly string _overlayWindowTitle;
        private AgentCursorMotion _motion = AgentCursorMotion.Default;
        private PointF _current;
        private long _lastFrameTimestamp = Stopwatch.GetTimestamp();
        private double _heading = RestingHeadingRadians;
        private double _displayHeading = RestingHeadingRadians;
        private PlannedPath? _path;
        private Trip? _trip;
        private SpringState? _spring;
        private SpringTarget? _springTarget;
        private double _distanceSoFar;
        private bool _hasPosition;
        private bool _isGliding;
        private bool _visibleCursor;
        private bool _enabled = true;
        private double _pulseStartedMs;
        private long _lastActivityMs = Environment.TickCount64;
        private TaskCompletionSource? _arrival;
        private IntPtr _pinnedTargetHwnd;
        private long _lastPinAtMs;
        private long _fadeStartedMs;
        private bool _isFadingOut;
        private string _layering = "normal";
        private readonly bool _timerResolutionRaised;

        public OverlayForm(string overlayWindowTitle)
        {
            _overlayWindowTitle = overlayWindowTitle;
            _virtualBounds = new Rectangle(
                NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

            Bounds = _virtualBounds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = false;
            Text = _overlayWindowTitle;
            StartPosition = FormStartPosition.Manual;
            _current = new PointF(InitialOffscreenPosition, InitialOffscreenPosition);
            _timerResolutionRaised = NativeMethods.timeBeginPeriod(1) == 0;

            _timer = new System.Windows.Forms.Timer { Interval = 8 };
            _timer.Tick += (_, _) =>
            {
                StepAnimation();
            };
            _timer.Start();
            FormClosed += (_, _) =>
            {
                if (_timerResolutionRaised)
                    _ = NativeMethods.timeEndPeriod(1);
                Application.ExitThread();
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT
                              | NativeMethods.WS_EX_LAYERED
                              | NativeMethods.WS_EX_NOACTIVATE
                              | NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void SetMotion(AgentCursorMotion motion) => _motion = motion;

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled)
                HideCursor();
        }

        public void EnsureVisibleAt(int screenX, int screenY, IntPtr targetHwnd, AgentCursorMotion motion)
        {
            if (!_enabled)
                return;

            _motion = motion;
            if (!PinToTarget(targetHwnd))
                return;
            if (!_hasPosition)
            {
                _current = VisualPositionForTip(ToLocal(screenX, screenY), DpiScaleForPoint(screenX, screenY));
                _heading = RestingHeadingRadians;
                _displayHeading = _heading;
                _hasPosition = true;
            }

            CancelFadeOut();
            _visibleCursor = true;
            MarkActivity();
            ShowOverlay();
        }

        public void HideCursor()
        {
            CancelFadeOut();
            _visibleCursor = false;
            _isGliding = false;
            _path = null;
            _trip = null;
            _spring = null;
            _springTarget = null;
            _pinnedTargetHwnd = IntPtr.Zero;
            _layering = "hidden";
            _arrival?.TrySetResult();
            _arrival = null;
            Hide();
        }

        public void KeepAlive()
        {
            if (_enabled && _visibleCursor)
            {
                CancelFadeOut();
                MarkActivity();
                ShowOverlay();
            }
        }

        public void GlideTo(int screenX, int screenY, IntPtr targetHwnd, AgentCursorMotion motion, TaskCompletionSource arrival)
        {
            if (!_enabled)
            {
                arrival.TrySetResult();
                return;
            }

            _motion = motion;
            CancelFadeOut();
            if (!PinToTarget(targetHwnd))
            {
                arrival.TrySetResult();
                return;
            }
            var scale = DpiScaleForPoint(screenX, screenY);
            var targetTip = ToLocal(screenX, screenY);
            var target = VisualPositionForTip(targetTip, scale);
            if (!_hasPosition)
            {
                _current = InitialPosition(scale);
                _heading = RestingHeadingRadians;
                _displayHeading = _heading;
                _hasPosition = true;
            }
            else
            {
                var visiblePose = RenderPose(scale);
                _current = visiblePose.Center;
                _heading = visiblePose.Heading;

                var currentTip = TipPointFromVisualPosition(_current, scale, _heading);
                if (Hypot(currentTip.X - targetTip.X, currentTip.Y - targetTip.Y) <= SameTargetTipTolerance * scale)
                {
                    _arrival?.TrySetResult();
                    _arrival = arrival;
                    _current = target;
                    _heading = RestingHeadingRadians;
                    _path = null;
                    _trip = null;
                    _spring = null;
                    _springTarget = null;
                    _isGliding = false;
                    _distanceSoFar = 0;
                    MarkActivity();
                    _lastFrameTimestamp = Stopwatch.GetTimestamp();
                    _visibleCursor = true;
                    _pulseStartedMs = 0;
                    arrival.TrySetResult();
                    _arrival = null;
                    ShowOverlay();
                    return;
                }
            }

            _arrival?.TrySetResult();
            _arrival = arrival;
            _path = PlanPath(
                _current.X,
                _current.Y,
                _heading + Math.PI,
                target.X,
                target.Y,
                RestingHeadingRadians + Math.PI,
                Math.Max(1, TurnRadius * scale),
                RestingHeadingRadians,
                target);
            _trip = TripFor(_motion.GlideDurationMs, scale);
            _spring = null;
            _springTarget = null;
            _distanceSoFar = 0;
            _lastFrameTimestamp = Stopwatch.GetTimestamp();
            MarkActivity();
            _visibleCursor = true;
            _pulseStartedMs = 0;

            if (_path.Value.Length < 1)
            {
                _current = target;
                _heading = RestingHeadingRadians;
                _isGliding = false;
                _path = null;
                _trip = null;
                _arrival?.TrySetResult();
                _arrival = null;
            }
            else
            {
                _isGliding = true;
            }

            ShowOverlay();
        }

        public void StartPress(AgentCursorMotion motion)
        {
            if (!_enabled)
                return;

            _motion = motion;
            CancelFadeOut();
            MarkActivity();
            _pulseStartedMs = Environment.TickCount64;
            _visibleCursor = true;
            ShowOverlay();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Rendering is handled by UpdateLayeredWindow so the glow gets real per-pixel alpha.
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Suppress the normal WinForms background paint; this window is a per-pixel alpha surface.
        }

        private void StepAnimation()
        {
            var changed = false;
            if (!_visibleCursor)
                return;

            ReapplyWindowLayer(force: false);

            var nowTimestamp = Stopwatch.GetTimestamp();
            var dt = Math.Min(0.05, Math.Max(0, (nowTimestamp - _lastFrameTimestamp) / (double)Stopwatch.Frequency));
            _lastFrameTimestamp = nowTimestamp;
            var nowMs = Environment.TickCount64;

            if (_path is { } path && _trip is { } trip)
            {
                var u = Math.Min(1.0, _distanceSoFar / Math.Max(path.Length, 1));
                var profileValue = SmootherSpeedProfile(u);
                var floorSpeed = u < 0.5 ? trip.MinStart : trip.MinEnd;
                var currentSpeed = floorSpeed + (trip.Peak - floorSpeed) * profileValue;
                _distanceSoFar += currentSpeed * dt;

                if (_distanceSoFar >= path.Length)
                {
                    var endState = path.Sample(path.Length);
                    _spring = new SpringState(
                        0,
                        0,
                        Math.Cos(endState.Heading) * currentSpeed * SpringOvershoot,
                        Math.Sin(endState.Heading) * currentSpeed * SpringOvershoot);
                    _springTarget = new SpringTarget(path.TargetPoint, path.EndVisualHeading);
                    _current = path.TargetPoint;
                    _heading = path.EndVisualHeading;
                    _path = null;
                    _trip = null;
                    _distanceSoFar = 0;
                    _isGliding = false;
                    _lastActivityMs = nowMs;
                    _arrival?.TrySetResult();
                    _arrival = null;
                }
                else
                {
                    var state = path.Sample(_distanceSoFar);
                    _current = new PointF((float)state.X, (float)state.Y);
                    _heading = RotateToward(_heading, state.Heading + Math.PI, 14 * dt);
                }

                changed = true;
            }
            else if (_spring is { } spring && _springTarget is { } springTarget)
            {
                var damping = Math.Max(0.3, _motion.Spring) * 24;
                var substeps = 4;
                var sdt = dt / substeps;
                for (var i = 0; i < substeps; i++)
                {
                    spring.Vx += (-SpringStiffness * spring.Ox - damping * spring.Vx) * sdt;
                    spring.Vy += (-SpringStiffness * spring.Oy - damping * spring.Vy) * sdt;
                    spring.Ox += spring.Vx * sdt;
                    spring.Oy += spring.Vy * sdt;
                }

                _current = new PointF(
                    (float)(springTarget.Point.X + spring.Ox),
                    (float)(springTarget.Point.Y + spring.Oy));
                _heading = springTarget.Heading;
                if (Hypot(spring.Ox, spring.Oy) < 0.3 && Hypot(spring.Vx, spring.Vy) < 2)
                {
                    _current = springTarget.Point;
                    _spring = null;
                    _springTarget = null;
                }
                else
                {
                    _spring = spring;
                }

                changed = true;
            }

            changed = UpdateDisplayHeading(dt) || changed;

            if (_isFadingOut)
            {
                if (Environment.TickCount64 - _fadeStartedMs >= FadeOutDurationMs)
                {
                    HideCursor();
                    return;
                }

                changed = true;
            }

            if (_pulseStartedMs > 0 && Environment.TickCount64 - _pulseStartedMs > Math.Max(1, _motion.PressDurationMs))
            {
                _pulseStartedMs = 0;
                _lastActivityMs = nowMs;
                changed = true;
            }
            else if (_pulseStartedMs > 0)
            {
                changed = true;
            }

            if (!_isGliding && _pulseStartedMs <= 0 && !_isFadingOut)
            {
                var idleMs = Math.Max(0, nowMs - _lastActivityMs);
                if (_motion.IdleHideMs > 0 && idleMs >= _motion.IdleHideMs)
                {
                    BeginFadeOut();
                    changed = true;
                }
                else
                    changed = true;
            }

            if (changed)
                RenderFrame();
        }

        public CursorSnapshot Snapshot()
        {
            if (!_hasPosition)
                return new CursorSnapshot(_visibleCursor, null, null, WindowIdFor(_pinnedTargetHwnd), _layering);

            var tip = TipPointFromVisualPosition(_current, CurrentDpiScale());
            return new CursorSnapshot(
                _visibleCursor,
                (int)Math.Round(tip.X + _virtualBounds.Left),
                (int)Math.Round(tip.Y + _virtualBounds.Top),
                WindowIdFor(_pinnedTargetHwnd),
                _layering);
        }

        private PointF ToLocal(int screenX, int screenY) => new(screenX - _virtualBounds.Left, screenY - _virtualBounds.Top);

        private void ShowOverlay()
        {
            CloseSiblingOverlayWindows();
            if (!Visible)
                Show();
            RenderFrame();
            ReapplyWindowLayer(force: true);
        }

        private bool PinToTarget(IntPtr targetHwnd)
        {
            var requestedTarget = targetHwnd != IntPtr.Zero;
            _pinnedTargetHwnd = NormalizeTargetHwnd(targetHwnd);
            _lastPinAtMs = 0;
            if (requestedTarget && _pinnedTargetHwnd == IntPtr.Zero)
            {
                _layering = "target_missing";
                BeginFadeOut();
                return false;
            }

            _layering = _pinnedTargetHwnd == IntPtr.Zero ? "normal" : "target_pinned";
            return true;
        }

        private void ReapplyWindowLayer(bool force)
        {
            if (!IsHandleCreated || IsDisposed)
                return;

            var now = Environment.TickCount64;
            if (!force && now - _lastPinAtMs < 80)
                return;

            _lastPinAtMs = now;
            var flags = NativeMethods.SWP_NOMOVE
                        | NativeMethods.SWP_NOSIZE
                        | NativeMethods.SWP_NOACTIVATE
                        | NativeMethods.SWP_NOOWNERZORDER
                        | NativeMethods.SWP_NOSENDCHANGING
                        | NativeMethods.SWP_SHOWWINDOW;

            var target = NormalizeTargetHwnd(_pinnedTargetHwnd);
            _pinnedTargetHwnd = target;
            if (target == IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, 0, 0, 0, 0, flags);
                _layering = "normal";
                return;
            }

            var targetTopmost = IsTopmostWindow(target);
            if (!targetTopmost)
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);

            var insertAfter = WindowJustAboveTarget(target, Handle, targetTopmost);
            NativeMethods.SetWindowPos(Handle, insertAfter, 0, 0, 0, 0, flags);
            _layering = targetTopmost ? "target_pinned_topmost" : "target_pinned";
        }

        private static IntPtr WindowJustAboveTarget(IntPtr target, IntPtr overlay, bool targetTopmost)
        {
            var above = NativeMethods.GetWindow(target, NativeMethods.GW_HWNDPREV);
            while (above != IntPtr.Zero)
            {
                if (above != overlay && !IsAgentCursorOverlayWindow(above))
                {
                    if (!targetTopmost && IsTopmostWindow(above))
                        return NativeMethods.HWND_TOP;

                    return above;
                }

                above = NativeMethods.GetWindow(above, NativeMethods.GW_HWNDPREV);
            }

            return targetTopmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP;
        }

        private static IntPtr NormalizeTargetHwnd(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return IntPtr.Zero;

            var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            hwnd = root == IntPtr.Zero ? hwnd : root;
            return NativeMethods.IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd)
                ? hwnd
                : IntPtr.Zero;
        }

        private static bool IsTopmostWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            return (exStyle & NativeMethods.WS_EX_TOPMOST) != 0;
        }

        private static long? WindowIdFor(IntPtr hwnd) => hwnd == IntPtr.Zero ? null : hwnd.ToInt64();

        private void RenderFrame()
        {
            if (!_enabled || !_visibleCursor || !IsHandleCreated || IsDisposed)
                return;

            var opacity = FadeOpacity();
            if (opacity <= 0)
                return;

            var scale = CurrentDpiScale();
            var renderPose = RenderPose(scale);
            var screenCenter = new PointF(renderPose.Center.X + _virtualBounds.Left, renderPose.Center.Y + _virtualBounds.Top);
            var halfSize = Math.Max(32, (int)Math.Ceiling(SurfaceHalfSize * scale));
            var width = halfSize * 2;
            var height = halfSize * 2;
            var left = (int)Math.Floor(screenCenter.X - halfSize);
            var top = (int)Math.Floor(screenCenter.Y - halfSize);
            var localCenter = new PointF(screenCenter.X - left, screenCenter.Y - top);

            using var high = new Bitmap(width * Supersample, height * Supersample, PixelFormat.Format32bppPArgb);
            high.SetResolution(96 * Supersample, 96 * Supersample);
            using (var g = Graphics.FromImage(high))
            {
                g.Clear(Color.Transparent);
                ConfigureHighQuality(g);
                g.ScaleTransform(Supersample, Supersample);
                DrawBloom(g, localCenter, scale, BloomBreath());
                DrawCursor(g, localCenter, renderPose.Heading, scale);
            }

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            bitmap.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                ConfigureHighQuality(g);
                g.DrawImage(high, new Rectangle(0, 0, width, height), 0, 0, high.Width, high.Height, GraphicsUnit.Pixel);
            }

            var screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
                return;

            var memDc = IntPtr.Zero;
            var hBitmap = IntPtr.Zero;
            var oldBitmap = IntPtr.Zero;
            try
            {
                memDc = NativeMethods.CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero)
                    return;

                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);
                var dst = new POINT(left, top);
                var size = new SIZE(width, height);
                var src = new POINT(0, 0);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = (byte)Math.Clamp((int)Math.Round(255 * opacity), 0, 255),
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };
                NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero)
                    NativeMethods.SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero)
                    NativeMethods.DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero)
                    NativeMethods.DeleteDC(memDc);
                _ = NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static PointF InitialPosition(float scale) =>
            new(InitialOffscreenPosition * scale, InitialOffscreenPosition * scale);

        private static void DrawBloom(Graphics g, PointF p, float scale, double breath)
        {
            DrawGaussianGlow(
                g,
                p,
                (float)((64 + 3 * breath) * scale),
                (float)((19.5 + breath) * scale),
                Color.FromArgb(188, 232, 252),
                centerAlpha: (int)Math.Round(70 + 16 * breath));

            DrawGaussianGlow(
                g,
                p,
                (float)((30 + breath) * scale),
                (float)((8.5 + 0.5 * breath) * scale),
                Color.FromArgb(238, 248, 255),
                centerAlpha: (int)Math.Round(42 + 10 * breath));
        }

        private static void DrawGaussianGlow(
            Graphics g,
            PointF p,
            float radius,
            float sigma,
            Color color,
            int centerAlpha)
        {
            if (radius <= 0 || sigma <= 0 || centerAlpha <= 0)
                return;

            var renderScale = Supersample;
            var diameter = Math.Max(1, (int)Math.Ceiling(radius * 2 * renderScale));
            var center = (diameter - 1) / 2.0;
            var maxRadius = radius * renderScale;
            var sigmaPixels = sigma * renderScale;
            var maxRadiusSquared = maxRadius * maxRadius;
            var sigmaDenominator = 2 * sigmaPixels * sigmaPixels;

            using var bitmap = new Bitmap(diameter, diameter, PixelFormat.Format32bppPArgb);
            var rect = new Rectangle(0, 0, diameter, diameter);
            var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var stride = data.Stride;
                var strideAbs = Math.Abs(stride);
                var buffer = new byte[strideAbs * diameter];

                for (var y = 0; y < diameter; y++)
                {
                    var dy = y - center;
                    var row = stride >= 0 ? y * stride : (diameter - 1 - y) * strideAbs;
                    for (var x = 0; x < diameter; x++)
                    {
                        var dx = x - center;
                        var r2 = dx * dx + dy * dy;
                        if (r2 > maxRadiusSquared)
                            continue;

                        var alpha = (int)Math.Round(centerAlpha * Math.Exp(-r2 / sigmaDenominator));
                        if (alpha <= 0)
                            continue;

                        var index = row + x * 4;
                        buffer[index] = (byte)(color.B * alpha / 255);
                        buffer[index + 1] = (byte)(color.G * alpha / 255);
                        buffer[index + 2] = (byte)(color.R * alpha / 255);
                        buffer[index + 3] = (byte)alpha;
                    }
                }

                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            var dest = new RectangleF(p.X - radius, p.Y - radius, radius * 2, radius * 2);
            g.DrawImage(bitmap, dest);
        }

        private static void DrawCursor(Graphics g, PointF p, double heading, float scale)
        {
            using var path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(14 * scale, 0),
                new PointF(-8 * scale, -9 * scale),
                new PointF(-3 * scale, 0),
                new PointF(-8 * scale, 9 * scale),
            });

            using var matrix = new Matrix();
            matrix.Rotate((float)((heading + Math.PI) * 180 / Math.PI));
            matrix.Translate(p.X, p.Y, MatrixOrder.Append);
            path.Transform(matrix);

            var bounds = path.GetBounds();
            if (bounds.Width < 1 || bounds.Height < 1)
                return;

            using var brush = new LinearGradientBrush(bounds, Color.White, Color.White, 135f)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors =
                    [
                        Color.FromArgb(219, 238, 255),
                        Color.FromArgb(94, 192, 232),
                        Color.FromArgb(84, 205, 160)
                    ],
                    Positions = [0.0f, 0.53f, 1.0f]
                }
            };
            using var outline = new Pen(Color.White, 1.5f * scale) { LineJoin = LineJoin.Round };
            g.FillPath(brush, path);
            g.DrawPath(outline, path);
        }

        private static double SmootherSpeedProfile(double u) => (30 * u * u * (1 - u) * (1 - u)) / 1.875;

        private double BloomBreath()
        {
            if (_path is not null || _spring is not null || _pulseStartedMs > 0)
                return 1;

            var seconds = IdleSeconds();
            return 0.5 + 0.5 * Math.Sin(seconds / IdleBreathPeriodSeconds * Math.PI * 2);
        }

        private (PointF Center, double Heading) RenderPose(float scale)
        {
            var anchorHeading = IsThinkingIdle() ? RestingHeadingRadians : _heading;
            var tip = TipPointFromVisualPosition(_current, scale, anchorHeading);
            return (VisualPositionForTip(tip, scale, _displayHeading), _displayHeading);
        }

        private bool IsThinkingIdle()
        {
            return _enabled
                   && _visibleCursor
                   && _hasPosition
                   && !_isGliding
                   && _path is null
                   && _spring is null
                   && _pulseStartedMs <= 0;
        }

        private double IdleRotation()
        {
            var seconds = IdleSeconds();
            return Math.Sin(seconds / IdleRotationPeriodSeconds * Math.PI * 2) * IdleRotationAmplitudeRadians;
        }

        private void MarkActivity() => _lastActivityMs = Environment.TickCount64;

        private double IdleSeconds() => Math.Max(0, Environment.TickCount64 - _lastActivityMs) / 1000.0;

        private bool UpdateDisplayHeading(double dt)
        {
            var previous = _displayHeading;
            _displayHeading = RotateToward(
                _displayHeading,
                DesiredDisplayHeading(),
                VisualHeadingCatchUpRadiansPerSecond * dt);
            return Math.Abs(AngularDifference(previous, _displayHeading)) > 0.0001;
        }

        private double DesiredDisplayHeading()
        {
            return IsThinkingIdle() ? RestingHeadingRadians + IdleRotation() : _heading;
        }

        private void BeginFadeOut()
        {
            if (_isFadingOut || !_visibleCursor)
                return;

            _isFadingOut = true;
            _fadeStartedMs = Environment.TickCount64;
            _lastFrameTimestamp = Stopwatch.GetTimestamp();
        }

        private void CancelFadeOut()
        {
            _isFadingOut = false;
            _fadeStartedMs = 0;
        }

        private double FadeOpacity()
        {
            if (!_isFadingOut || _fadeStartedMs <= 0)
                return 1;

            var u = Math.Clamp((Environment.TickCount64 - _fadeStartedMs) / FadeOutDurationMs, 0, 1);
            var eased = u * u * (3 - 2 * u);
            return 1 - eased;
        }

        private static double RotateToward(double current, double desired, double maxStep)
        {
            var diff = AngularDifference(current, desired);
            return current + Math.Max(-maxStep, Math.Min(maxStep, diff));
        }

        private static double AngularDifference(double current, double desired)
        {
            var diff = desired - current;
            while (diff > Math.PI) diff -= 2 * Math.PI;
            while (diff < -Math.PI) diff += 2 * Math.PI;
            return diff;
        }

        private float CurrentDpiScale()
        {
            var tip = TipPointFromVisualPosition(_current, Math.Max(1f, DeviceDpi / 96f));
            return DpiScaleForPoint(
                (int)Math.Round(tip.X + _virtualBounds.Left),
                (int)Math.Round(tip.Y + _virtualBounds.Top));
        }

        private float DpiScaleForPoint(int screenX, int screenY)
        {
            try
            {
                var monitor = NativeMethods.MonitorFromPoint(new POINT(screenX, screenY), NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero &&
                    NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 &&
                    dpiX > 0)
                {
                    return Math.Clamp(dpiX / 96f, 1f, 2.5f);
                }
            }
            catch
            {
                // Fall through to DeviceDpi for older Windows builds or unavailable shcore.
            }

            return Math.Clamp(DeviceDpi / 96f, 1f, 2.5f);
        }

        private static PointF VisualPositionForTip(PointF tip, float scale)
        {
            return VisualPositionForTip(tip, scale, RestingHeadingRadians);
        }

        private static PointF VisualPositionForTip(PointF tip, float scale, double heading)
        {
            var offset = CursorTipOffset * scale;
            return new PointF(
                tip.X + (float)(Math.Cos(heading) * offset),
                tip.Y + (float)(Math.Sin(heading) * offset));
        }

        private static PointF TipPointFromVisualPosition(PointF visualPosition, float scale)
        {
            return TipPointFromVisualPosition(visualPosition, scale, RestingHeadingRadians);
        }

        private static PointF TipPointFromVisualPosition(PointF visualPosition, float scale, double heading)
        {
            var offset = CursorTipOffset * scale;
            return new PointF(
                visualPosition.X - (float)(Math.Cos(heading) * offset),
                visualPosition.Y - (float)(Math.Sin(heading) * offset));
        }

        private static void ConfigureHighQuality(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        private static Trip TripFor(double glideDurationMs, float scale)
        {
            var speedScale = DefaultGlideDurationMs / Math.Clamp(glideDurationMs, 50, 5000);
            return new Trip(
                PeakSpeed * scale * speedScale,
                MinStartSpeed * scale * speedScale,
                MinEndSpeed * scale * speedScale);
        }

        private static PlannedPath PlanPath(
            double x0,
            double y0,
            double th0,
            double x1,
            double y1,
            double th1,
            double radius,
            double endVisualHeading,
            PointF targetPoint)
        {
            return PlanDubins(x0, y0, th0, x1, y1, th1, radius, endVisualHeading, targetPoint)
                   ?? PlannedPath.Linear(x0, y0, th0, x1, y1, th1, radius, endVisualHeading, targetPoint);
        }

        private static PlannedPath? PlanDubins(
            double x0,
            double y0,
            double th0,
            double x1,
            double y1,
            double th1,
            double radius,
            double endVisualHeading,
            PointF targetPoint)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;
            var distance = Hypot(dx, dy);
            if (distance <= 0.5)
                return null;

            var d = distance / radius;
            var theta = Mod2Pi(Math.Atan2(dy, dx));
            var a = Mod2Pi(th0 - theta);
            var b = Mod2Pi(th1 - theta);
            DubinsSolution? best = null;
            var bestLength = double.PositiveInfinity;

            foreach (var solver in DubinsSolvers)
            {
                var solution = solver(d, a, b);
                if (solution is not { } s || !double.IsFinite(s.Length) || s.Length < 0 || s.Length >= bestLength)
                    continue;

                best = s;
                bestLength = s.Length;
            }

            return best is { } chosen
                ? PlannedPath.Dubins(x0, y0, th0, radius, chosen, endVisualHeading, targetPoint, x1, y1, th1)
                : null;
        }

        private static readonly Func<double, double, double, DubinsSolution?>[] DubinsSolvers =
        [
            DubinsLsl,
            DubinsRsr,
            DubinsLsr,
            DubinsRsl,
            DubinsRlr,
            DubinsLrl
        ];

        private static DubinsSolution? DubinsLsl(double d, double a, double b)
        {
            var tmp0 = d + Math.Sin(a) - Math.Sin(b);
            var p2 = 2 + d * d - 2 * Math.Cos(a - b) + 2 * d * (Math.Sin(a) - Math.Sin(b));
            if (p2 < 0)
                return null;
            var tmp1 = Math.Atan2(Math.Cos(b) - Math.Cos(a), tmp0);
            return new DubinsSolution(Mod2Pi(-a + tmp1), Math.Sqrt(p2), Mod2Pi(b - tmp1), ['L', 'S', 'L']);
        }

        private static DubinsSolution? DubinsRsr(double d, double a, double b)
        {
            var tmp0 = d - Math.Sin(a) + Math.Sin(b);
            var p2 = 2 + d * d - 2 * Math.Cos(a - b) + 2 * d * (Math.Sin(b) - Math.Sin(a));
            if (p2 < 0)
                return null;
            var tmp1 = Math.Atan2(Math.Cos(a) - Math.Cos(b), tmp0);
            return new DubinsSolution(Mod2Pi(a - tmp1), Math.Sqrt(p2), Mod2Pi(-b + tmp1), ['R', 'S', 'R']);
        }

        private static DubinsSolution? DubinsLsr(double d, double a, double b)
        {
            var p2 = -2 + d * d + 2 * Math.Cos(a - b) + 2 * d * (Math.Sin(a) + Math.Sin(b));
            if (p2 < 0)
                return null;
            var p = Math.Sqrt(p2);
            var tmp1 = Math.Atan2(-Math.Cos(a) - Math.Cos(b), d + Math.Sin(a) + Math.Sin(b)) - Math.Atan2(-2, p);
            return new DubinsSolution(Mod2Pi(-a + tmp1), p, Mod2Pi(-Mod2Pi(b) + tmp1), ['L', 'S', 'R']);
        }

        private static DubinsSolution? DubinsRsl(double d, double a, double b)
        {
            var p2 = d * d - 2 + 2 * Math.Cos(a - b) - 2 * d * (Math.Sin(a) + Math.Sin(b));
            if (p2 < 0)
                return null;
            var p = Math.Sqrt(p2);
            var tmp1 = Math.Atan2(Math.Cos(a) + Math.Cos(b), d - Math.Sin(a) - Math.Sin(b)) - Math.Atan2(2, p);
            return new DubinsSolution(Mod2Pi(a - tmp1), p, Mod2Pi(b - tmp1), ['R', 'S', 'L']);
        }

        private static DubinsSolution? DubinsRlr(double d, double a, double b)
        {
            var tmp = (6 - d * d + 2 * Math.Cos(a - b) + 2 * d * (Math.Sin(a) - Math.Sin(b))) / 8;
            if (Math.Abs(tmp) > 1)
                return null;
            var p = Mod2Pi(2 * Math.PI - Math.Acos(tmp));
            var t = Mod2Pi(a - Math.Atan2(Math.Cos(a) - Math.Cos(b), d - Math.Sin(a) + Math.Sin(b)) + p / 2);
            return new DubinsSolution(t, p, Mod2Pi(a - b - t + p), ['R', 'L', 'R']);
        }

        private static DubinsSolution? DubinsLrl(double d, double a, double b)
        {
            var tmp = (6 - d * d + 2 * Math.Cos(a - b) + 2 * d * (Math.Sin(b) - Math.Sin(a))) / 8;
            if (Math.Abs(tmp) > 1)
                return null;
            var p = Mod2Pi(2 * Math.PI - Math.Acos(tmp));
            var t = Mod2Pi(-a + Math.Atan2(-Math.Cos(a) + Math.Cos(b), d + Math.Sin(a) - Math.Sin(b)) + p / 2);
            return new DubinsSolution(t, p, Mod2Pi(Mod2Pi(b) - a - t + p), ['L', 'R', 'L']);
        }

        private static double Mod2Pi(double value)
        {
            var tau = 2 * Math.PI;
            var result = value - tau * Math.Floor(value / tau);
            return result < 0 ? result + tau : result;
        }

        private readonly record struct Trip(double Peak, double MinStart, double MinEnd);

        private struct SpringState(double ox, double oy, double vx, double vy)
        {
            public double Ox = ox;
            public double Oy = oy;
            public double Vx = vx;
            public double Vy = vy;
        }

        private readonly record struct SpringTarget(PointF Point, double Heading);

        private readonly record struct PathState(double X, double Y, double Heading);

        private readonly record struct DubinsSolution(double T, double P, double Q, char[] Types)
        {
            public double Length => T + P + Q;
        }

        private readonly record struct PlannedPath(
            bool IsDubins,
            double Length,
            double EndVisualHeading,
            PointF TargetPoint,
            double X0,
            double Y0,
            double Th0,
            double Radius,
            double Seg1,
            double Seg2,
            double Seg3,
            char[] Types,
            double X1,
            double Y1,
            double Th1)
        {
            public static PlannedPath Linear(
                double x0,
                double y0,
                double th0,
                double x1,
                double y1,
                double th1,
                double radius,
                double endVisualHeading,
                PointF targetPoint)
            {
                return new PlannedPath(false, Math.Max(1, Hypot(x1 - x0, y1 - y0)), endVisualHeading, targetPoint, x0, y0, th0, radius, 0, 0, 0, [], x1, y1, th1);
            }

            public static PlannedPath Dubins(
                double x0,
                double y0,
                double th0,
                double radius,
                DubinsSolution solution,
                double endVisualHeading,
                PointF targetPoint,
                double x1,
                double y1,
                double th1)
            {
                return new PlannedPath(true, solution.Length * radius, endVisualHeading, targetPoint, x0, y0, th0, radius, solution.T, solution.P, solution.Q, solution.Types, x1, y1, th1);
            }

            public PathState Sample(double distance)
            {
                return IsDubins ? SampleDubins(distance) : SampleLinear(distance);
            }

            private PathState SampleLinear(double distance)
            {
                var u = Math.Clamp(distance / Length, 0, 1);
                var diff = Th1 - Th0;
                while (diff > Math.PI) diff -= 2 * Math.PI;
                while (diff < -Math.PI) diff += 2 * Math.PI;
                return new PathState(X0 + (X1 - X0) * u, Y0 + (Y1 - Y0) * u, Th0 + diff * u);
            }

            private PathState SampleDubins(double inputDistance)
            {
                if (inputDistance <= 0)
                    return new PathState(X0, Y0, Th0);

                var l1 = Seg1 * Radius;
                var l2 = Seg2 * Radius;
                var l3 = Seg3 * Radius;
                var radius = Radius;
                var distance = Math.Min(inputDistance, l1 + l2 + l3);
                var x = X0;
                var y = Y0;
                var heading = Th0;

                void Advance(double length, char type)
                {
                    if (type == 'S')
                    {
                        x += Math.Cos(heading) * length;
                        y += Math.Sin(heading) * length;
                        return;
                    }

                    var deltaHeading = length / radius * (type == 'L' ? 1 : -1);
                    var perpendicular = type == 'L' ? Math.PI / 2 : -Math.PI / 2;
                    var centerX = x + Math.Cos(heading + perpendicular) * radius;
                    var centerY = y + Math.Sin(heading + perpendicular) * radius;
                    var angle = Math.Atan2(y - centerY, x - centerX);
                    x = centerX + Math.Cos(angle + deltaHeading) * radius;
                    y = centerY + Math.Sin(angle + deltaHeading) * radius;
                    heading += deltaHeading;
                }

                if (distance <= l1)
                {
                    Advance(distance, Types[0]);
                    return new PathState(x, y, heading);
                }

                Advance(l1, Types[0]);
                if (distance <= l1 + l2)
                {
                    Advance(distance - l1, Types[1]);
                    return new PathState(x, y, heading);
                }

                Advance(l2, Types[1]);
                Advance(distance - l1 - l2, Types[2]);
                return new PathState(x, y, heading);
            }
        }

        private void CloseSiblingOverlayWindows()
        {
            var currentPid = (uint)Environment.ProcessId;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (threadId == 0 || pid == 0 || pid == currentPid)
                    return true;

                if (IsSiblingOverlayWindow(hwnd))
                    NativeMethods.PostMessageW(hwnd, NativeMethods.WM_CLOSE, UIntPtr.Zero, IntPtr.Zero);

                return true;
            }, IntPtr.Zero);
        }

        private bool IsSiblingOverlayWindow(IntPtr hwnd)
        {
            var title = NativeMethods.GetWindowText(hwnd);
            if (title.Equals(_overlayWindowTitle, StringComparison.Ordinal))
                return true;

            return IsDefaultOverlayTitle(_overlayWindowTitle) &&
                   title.Equals(OverlayWindowTitlePrefix, StringComparison.Ordinal);
        }

        private static bool IsAgentCursorOverlayWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            var title = NativeMethods.GetWindowText(hwnd);
            return title.Equals(OverlayWindowTitlePrefix, StringComparison.Ordinal) ||
                   title.StartsWith(OverlayWindowTitlePrefix + ".", StringComparison.Ordinal);
        }

        private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
    }
}
