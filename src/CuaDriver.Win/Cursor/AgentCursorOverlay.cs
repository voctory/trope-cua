using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Forms;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Cursor;

internal sealed class AgentCursorOverlay
{
    private const double RestingHeadingRadians = AgentCursorGeometry.RestingHeadingRadians;
    private const float SurfaceHalfSize = 76f;
    private const double NominalGlideDurationMs = 160;
    private const double MinMoveDurationSeconds = 0.08;
    private const double MaxMoveDurationSeconds = 0.34;
    private const double DurationBaseSeconds = 0.055;
    private const double DurationDistanceDivisor = 1900;
    private const double BezierProgressExponent = 3.157;
    private const double BezierProgressDamping = 0.9;
    private const double IdleBreathPeriodSeconds = 1.8;
    private const double IdleRotationPeriodSeconds = 2.4;
    private const double IdleRotationAmplitudeRadians = 0.10;
    private const double VisualHeadingCatchUpRadiansPerSecond = 18;
    private const double SameTargetTipTolerance = 3.0;
    private const double FadeOutDurationMs = 180;

    private readonly object _gate = new();
    private OverlayForm? _form;
    private Thread? _thread;
    private ManualResetEventSlim? _threadReady;
    private bool _enabled = true;
    private AgentCursorMotion _motion = AgentCursorMotion.Default;
    private readonly string _overlayWindowTitle;

    public AgentCursorOverlay(string? instanceId = null)
    {
        _overlayWindowTitle = AgentCursorWindowing.OverlayWindowTitleFor(instanceId);
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
            next = _motion.WithOverrides(
                startHandle,
                endHandle,
                arcSize,
                arcFlow,
                spring,
                glideDurationMs,
                dwellAfterClickMs,
                idleHideMs,
                pressDurationMs);
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
        double? renderFps = null;
        double? renderMs = null;
        long renderFrameCount = 0;
        var thinking = false;
        long idleAnimationMs = 0;
        double? displayHeadingRadians = null;
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
                renderFps = snapshot.RenderFps;
                renderMs = snapshot.RenderMs;
                renderFrameCount = snapshot.RenderFrameCount;
                thinking = snapshot.Thinking;
                idleAnimationMs = snapshot.IdleAnimationMs;
                displayHeadingRadians = snapshot.DisplayHeadingRadians;
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
            ["render_fps"] = renderFps,
            ["render_ms"] = renderMs,
            ["render_frame_count"] = renderFrameCount,
            ["thinking"] = thinking,
            ["idle_animation_ms"] = idleAnimationMs,
            ["display_heading_radians"] = displayHeadingRadians,
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
        private readonly System.Threading.Timer _timer;
        private readonly Rectangle _virtualBounds;
        private readonly string _overlayWindowTitle;
        private AgentCursorMotion _motion = AgentCursorMotion.Default;
        private PointF _current;
        private long _lastFrameTimestamp = Stopwatch.GetTimestamp();
        private double _heading = RestingHeadingRadians;
        private double _displayHeading = RestingHeadingRadians;
        private PlannedCursorPath? _path;
        private long _glideStartedTimestamp;
        private double _glideDurationSeconds;
        private bool _hasPosition;
        private bool _isGliding;
        private bool _visibleCursor;
        private bool _enabled = true;
        private double _pulseStartedMs;
        private long _lastActivityMs = Environment.TickCount64;
        private long _idleAnimationStartedMs = Environment.TickCount64;
        private TaskCompletionSource? _arrival;
        private IntPtr _pinnedTargetHwnd;
        private long _lastPinAtMs;
        private long _fadeStartedMs;
        private bool _isFadingOut;
        private string _layering = "normal";
        private readonly bool _timerResolutionRaised;
        private long _lastRenderedFrameTimestamp;
        private long _renderFrameCount;
        private double? _renderFps;
        private double? _renderMs;
        private int _animationQueued;
        private LayeredBackBuffer? _backBuffer;

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
            _current = AgentCursorGeometry.InitialPosition(1f);
            _timerResolutionRaised = NativeMethods.timeBeginPeriod(1) == 0;

            _timer = new System.Threading.Timer(_ => QueueAnimationStep(), null, TimeSpan.FromMilliseconds(8), TimeSpan.FromMilliseconds(8));
            FormClosed += (_, _) =>
            {
                _timer.Dispose();
                _backBuffer?.Dispose();
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
                _current = AgentCursorGeometry.VisualPositionForTip(ToLocal(screenX, screenY), DpiScaleForPoint(screenX, screenY));
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
            _glideStartedTimestamp = 0;
            _glideDurationSeconds = 0;
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
                MarkVisibilityKeepAlive();
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
            var target = AgentCursorGeometry.VisualPositionForTip(targetTip, scale);
            if (!_hasPosition)
            {
                _current = AgentCursorGeometry.InitialPosition(scale);
                _heading = RestingHeadingRadians;
                _displayHeading = _heading;
                _hasPosition = true;
            }
            else
            {
                var visiblePose = RenderPose(scale);
                _current = visiblePose.Center;
                _heading = visiblePose.Heading;

                var currentTip = AgentCursorGeometry.TipPointFromVisualPosition(_current, scale, _heading);
                if (Hypot(currentTip.X - targetTip.X, currentTip.Y - targetTip.Y) <= SameTargetTipTolerance * scale)
                {
                    _arrival?.TrySetResult();
                    _arrival = arrival;
                    _current = target;
                    _heading = RestingHeadingRadians;
                    _path = null;
                    _glideStartedTimestamp = 0;
                    _glideDurationSeconds = 0;
                    _isGliding = false;
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
            _path = AgentCursorPathPlanner.Plan(
                _current.X,
                _current.Y,
                _heading + Math.PI,
                target.X,
                target.Y,
                RestingHeadingRadians + Math.PI,
                0,
                RestingHeadingRadians,
                target,
                MovementBounds());
            _glideStartedTimestamp = Stopwatch.GetTimestamp();
            _glideDurationSeconds = ResolveGlideDurationSeconds(_path.Value, _motion);
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
                _glideStartedTimestamp = 0;
                _glideDurationSeconds = 0;
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

        private void QueueAnimationStep()
        {
            if (IsDisposed || !IsHandleCreated || Interlocked.Exchange(ref _animationQueued, 1) == 1)
                return;

            try
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    Interlocked.Exchange(ref _animationQueued, 0);
                    if (!IsDisposed && IsHandleCreated)
                        StepAnimation();
                }));
            }
            catch
            {
                Interlocked.Exchange(ref _animationQueued, 0);
            }
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

            if (_path is { } path)
            {
                var elapsed = _glideStartedTimestamp > 0
                    ? (nowTimestamp - _glideStartedTimestamp) / (double)Stopwatch.Frequency
                    : _glideDurationSeconds;
                var progress = _glideDurationSeconds <= 0 ? 1 : Math.Clamp(elapsed / _glideDurationSeconds, 0, 1);
                var easedProgress = EaseMotion(progress);

                if (progress >= 1)
                {
                    var endState = path.Sample(path.Length);
                    _current = path.TargetPoint;
                    _heading = path.EndVisualHeading;
                    _path = null;
                    _glideStartedTimestamp = 0;
                    _glideDurationSeconds = 0;
                    _isGliding = false;
                    MarkActivity(nowMs);
                    _arrival?.TrySetResult();
                    _arrival = null;
                }
                else
                {
                    var state = path.Sample(easedProgress * path.Length);
                    _current = new PointF((float)state.X, (float)state.Y);
                    _heading = AgentCursorKinematics.RotateToward(_heading, state.Heading + Math.PI, 18 * dt);
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
                MarkActivity(nowMs);
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
                return SnapshotFor(null, null);

            var tip = AgentCursorGeometry.TipPointFromVisualPosition(_current, CurrentDpiScale());
            return SnapshotFor(
                (int)Math.Round(tip.X + _virtualBounds.Left),
                (int)Math.Round(tip.Y + _virtualBounds.Top));
        }

        private CursorSnapshot SnapshotFor(int? screenX, int? screenY) => new(
            _visibleCursor,
            screenX,
            screenY,
            WindowIdFor(_pinnedTargetHwnd),
            _layering,
            _renderFps,
            _renderMs,
            _renderFrameCount,
            IsThinkingIdle(),
            IdleAnimationMs(),
            _displayHeading);

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
            _pinnedTargetHwnd = AgentCursorWindowing.NormalizeTargetHwnd(targetHwnd);
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

            var target = AgentCursorWindowing.NormalizeTargetHwnd(_pinnedTargetHwnd);
            _pinnedTargetHwnd = target;
            if (target == IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, 0, 0, 0, 0, flags);
                _layering = "normal";
                return;
            }

            var targetTopmost = AgentCursorWindowing.IsTopmostWindow(target);
            if (!targetTopmost)
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);

            var insertAfter = AgentCursorWindowing.WindowJustAboveTarget(target, Handle, targetTopmost);
            NativeMethods.SetWindowPos(Handle, insertAfter, 0, 0, 0, 0, flags);
            _layering = targetTopmost ? "target_pinned_topmost" : "target_pinned";
        }

        private static long? WindowIdFor(IntPtr hwnd) => hwnd == IntPtr.Zero ? null : hwnd.ToInt64();

        private void RenderFrame()
        {
            if (!_enabled || !_visibleCursor || !IsHandleCreated || IsDisposed)
                return;

            var renderStarted = Stopwatch.GetTimestamp();
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

            var screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
                return;

            try
            {
                var buffer = BackBuffer(screenDc, width, height);
                if (buffer is null)
                    return;

                using (var g = Graphics.FromImage(buffer.Bitmap))
                {
                    g.Clear(Color.Transparent);
                    AgentCursorRenderer.ConfigureHighQuality(g);
                    AgentCursorRenderer.DrawBloom(g, localCenter, scale, BloomBreath());
                    AgentCursorRenderer.DrawCursor(g, localCenter, renderPose.Heading, scale);
                }

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
                NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, buffer.MemoryDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
                RecordRenderedFrame(renderStarted);
            }
            finally
            {
                _ = NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private LayeredBackBuffer? BackBuffer(IntPtr screenDc, int width, int height)
        {
            if (_backBuffer is { } existing && existing.Width == width && existing.Height == height)
                return existing;

            _backBuffer?.Dispose();
            _backBuffer = LayeredBackBuffer.TryCreate(screenDc, width, height);
            return _backBuffer;
        }

        private sealed class LayeredBackBuffer : IDisposable
        {
            public int Width { get; }
            public int Height { get; }
            public IntPtr MemoryDc { get; }
            public Bitmap Bitmap { get; }

            private readonly IntPtr _hBitmap;
            private readonly IntPtr _oldBitmap;

            private LayeredBackBuffer(int width, int height, IntPtr memoryDc, IntPtr hBitmap, IntPtr oldBitmap, IntPtr bits)
            {
                Width = width;
                Height = height;
                MemoryDc = memoryDc;
                _hBitmap = hBitmap;
                _oldBitmap = oldBitmap;
                Bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
                Bitmap.SetResolution(96, 96);
            }

            public static LayeredBackBuffer? TryCreate(IntPtr screenDc, int width, int height)
            {
                var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
                if (memoryDc == IntPtr.Zero)
                    return null;

                var info = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height,
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = NativeMethods.BI_RGB,
                        biSizeImage = (uint)(width * height * 4)
                    }
                };

                var hBitmap = NativeMethods.CreateDIBSection(
                    screenDc,
                    ref info,
                    NativeMethods.DIB_RGB_COLORS,
                    out var bits,
                    IntPtr.Zero,
                    0);
                if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
                {
                    if (hBitmap != IntPtr.Zero)
                        NativeMethods.DeleteObject(hBitmap);
                    NativeMethods.DeleteDC(memoryDc);
                    return null;
                }

                var oldBitmap = NativeMethods.SelectObject(memoryDc, hBitmap);
                if (oldBitmap == IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                    NativeMethods.DeleteDC(memoryDc);
                    return null;
                }

                return new LayeredBackBuffer(width, height, memoryDc, hBitmap, oldBitmap, bits);
            }

            public void Dispose()
            {
                Bitmap.Dispose();
                if (_oldBitmap != IntPtr.Zero)
                    NativeMethods.SelectObject(MemoryDc, _oldBitmap);
                if (_hBitmap != IntPtr.Zero)
                    NativeMethods.DeleteObject(_hBitmap);
                if (MemoryDc != IntPtr.Zero)
                    NativeMethods.DeleteDC(MemoryDc);
            }
        }

        private void RecordRenderedFrame(long renderStarted)
        {
            var renderedAt = Stopwatch.GetTimestamp();
            var renderMs = Stopwatch.GetElapsedTime(renderStarted, renderedAt).TotalMilliseconds;
            _renderMs = _renderMs is null ? renderMs : _renderMs.Value * 0.85 + renderMs * 0.15;

            if (_lastRenderedFrameTimestamp > 0)
            {
                var elapsed = (renderedAt - _lastRenderedFrameTimestamp) / (double)Stopwatch.Frequency;
                if (elapsed > 0)
                {
                    var fps = 1 / elapsed;
                    _renderFps = _renderFps is null ? fps : _renderFps.Value * 0.85 + fps * 0.15;
                }
            }

            _lastRenderedFrameTimestamp = renderedAt;
            _renderFrameCount++;
        }

        private double BloomBreath()
        {
            if (_path is not null || _pulseStartedMs > 0)
                return 1;

            var seconds = IdleSeconds();
            return 0.5 + 0.5 * Math.Sin(seconds / IdleBreathPeriodSeconds * Math.PI * 2);
        }

        private (PointF Center, double Heading) RenderPose(float scale)
        {
            var anchorHeading = IsThinkingIdle() ? RestingHeadingRadians : _heading;
            var tip = AgentCursorGeometry.TipPointFromVisualPosition(_current, scale, anchorHeading);
            return (AgentCursorGeometry.VisualPositionForTip(tip, scale, _displayHeading), _displayHeading);
        }

        private bool IsThinkingIdle()
        {
            return _enabled
                   && _visibleCursor
                   && _hasPosition
                   && !_isGliding
                   && _path is null
                   && _pulseStartedMs <= 0;
        }

        private double IdleRotation()
        {
            var seconds = IdleSeconds();
            return Math.Sin(seconds / IdleRotationPeriodSeconds * Math.PI * 2) * IdleRotationAmplitudeRadians;
        }

        private void MarkActivity() => MarkActivity(Environment.TickCount64);

        private void MarkActivity(long nowMs)
        {
            _lastActivityMs = nowMs;
            _idleAnimationStartedMs = nowMs;
        }

        private void MarkVisibilityKeepAlive() => _lastActivityMs = Environment.TickCount64;

        private long IdleAnimationMs() => Math.Max(0, Environment.TickCount64 - _idleAnimationStartedMs);

        private double IdleSeconds() => IdleAnimationMs() / 1000.0;

        private bool UpdateDisplayHeading(double dt)
        {
            var previous = _displayHeading;
            _displayHeading = AgentCursorKinematics.RotateToward(
                _displayHeading,
                DesiredDisplayHeading(),
                VisualHeadingCatchUpRadiansPerSecond * dt);
            return Math.Abs(AgentCursorKinematics.AngularDifference(previous, _displayHeading)) > 0.0001;
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

        private float CurrentDpiScale()
        {
            var tip = AgentCursorGeometry.TipPointFromVisualPosition(_current, Math.Max(1f, DeviceDpi / 96f));
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

        private RectangleF MovementBounds()
        {
            var inset = 8f * CurrentDpiScale();
            var width = Math.Max(1, _virtualBounds.Width - inset * 2);
            var height = Math.Max(1, _virtualBounds.Height - inset * 2);
            return new RectangleF(inset, inset, width, height);
        }

        private static double ResolveGlideDurationSeconds(PlannedCursorPath path, AgentCursorMotion motion)
        {
            var adaptive = Math.Clamp(
                DurationBaseSeconds + path.StraightLineDistance / DurationDistanceDivisor,
                MinMoveDurationSeconds,
                MaxMoveDurationSeconds);
            var userScale = Math.Clamp(motion.GlideDurationMs / NominalGlideDurationMs, 0.35, 3.0);
            return Math.Clamp(adaptive * userScale, 0.03, 1.2);
        }

        private static double EaseMotion(double progress)
        {
            var clamped = Math.Clamp(progress, 0, 1);
            var response = 1 - Math.Pow(1 - clamped, BezierProgressExponent);
            return Math.Clamp(
                response * BezierProgressDamping + clamped * (1 - BezierProgressDamping),
                0,
                1);
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

            return AgentCursorWindowing.IsDefaultOverlayTitle(_overlayWindowTitle) &&
                   title.Equals(AgentCursorWindowing.OverlayWindowTitlePrefix, StringComparison.Ordinal);
        }

        private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
    }
}
