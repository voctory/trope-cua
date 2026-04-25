using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Text.Json;
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
    public double IdleHideMs { get; init; } = 0;

    [JsonPropertyName("press_duration_ms")]
    public double PressDurationMs { get; init; } = 650;

    public static AgentCursorMotion Default { get; } = new();
}

public sealed record CursorSnapshot(bool Visible, int? ScreenX, int? ScreenY);

public sealed class AgentCursorOverlay
{
    private const string OverlayWindowTitle = "CuaDriverWin.AgentCursorOverlay";
    private const double RestingHeadingRadians = Math.PI / 4;
    private const float CursorTipOffset = 16f;
    private const float SurfaceHalfSize = 38f;
    private const int Supersample = 3;

    private readonly object _gate = new();
    private OverlayForm? _form;
    private Thread? _thread;
    private ManualResetEventSlim? _threadReady;
    private bool _enabled = true;
    private AgentCursorMotion _motion = AgentCursorMotion.Default;

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
            EnsureThread();
            var fallback = CurrentCursorPosition();
            Post(form =>
            {
                form.SetEnabled(true);
                form.EnsureVisibleAt(fallback.X, fallback.Y, Motion);
            });
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
        double? idleHideMs)
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
            };
            _motion = next;
        }

        var enabled = Enabled;
        var fallback = CurrentCursorPosition();
        Post(form =>
        {
            form.SetMotion(next);
            if (enabled)
                form.EnsureVisibleAt(fallback.X, fallback.Y, next);
        });
        return next;
    }

    public async Task MoveToAsync(POINT screenPoint, CancellationToken ct)
    {
        if (!Enabled)
            return;

        EnsureThread();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Post(form => form.GlideTo(screenPoint.X, screenPoint.Y, Motion, completion)))
            return;

        await completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public async Task ClickPulseAsync(POINT screenPoint, CancellationToken ct)
    {
        if (!Enabled)
            return;

        await MoveToAsync(screenPoint, ct).ConfigureAwait(false);
        var motion = Motion;
        if (!Post(form => form.StartPress(motion)))
            return;

        var delayMs = Math.Max(0, motion.PressDurationMs + motion.DwellAfterClickMs);
        if (delayMs > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct).ConfigureAwait(false);
    }

    public string StateJson()
    {
        var formReady = false;
        var visible = false;
        int? screenX = null;
        int? screenY = null;
        lock (_gate)
        {
            formReady = _form is not null && !_form.IsDisposed;
            if (formReady)
            {
                var snapshot = _form!.Snapshot();
                visible = snapshot.Visible;
                screenX = snapshot.ScreenX;
                screenY = snapshot.ScreenY;
            }
        }

        return JsonSerializer.Serialize(new
        {
            enabled = Enabled,
            route = "winforms.click_through_overlay",
            ready = formReady,
            visible,
            screen_x = screenX,
            screen_y = screenY,
            persistent = Motion.IdleHideMs <= 0,
            motion = Motion
        }, JsonUtil.SerializerOptions);
    }

    private void EnsureThread()
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
                    var form = new OverlayForm();
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

        ready.Wait(TimeSpan.FromSeconds(2));
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
        private AgentCursorMotion _motion = AgentCursorMotion.Default;
        private PointF _current;
        private PointF _start;
        private PointF _target;
        private PointF _control1;
        private PointF _control2;
        private DateTime _glideStartedAt;
        private DateTime _lastFrameAt = DateTime.UtcNow;
        private double _heading = Math.PI / 4;
        private bool _hasPosition;
        private bool _isGliding;
        private bool _visibleCursor;
        private bool _enabled = true;
        private double _pulseStartedMs;
        private DateTime _lastActivityAt = DateTime.UtcNow;
        private TaskCompletionSource? _arrival;

        public OverlayForm()
        {
            _virtualBounds = new Rectangle(
                NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

            Bounds = _virtualBounds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            Text = OverlayWindowTitle;
            StartPosition = FormStartPosition.Manual;
            _current = new PointF(-180, -180);
            _start = _current;
            _target = _current;
            _control1 = _current;
            _control2 = _current;

            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += (_, _) =>
            {
                StepAnimation();
            };
            _timer.Start();
            FormClosed += (_, _) => Application.ExitThread();
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

        public void EnsureVisibleAt(int screenX, int screenY, AgentCursorMotion motion)
        {
            if (!_enabled)
                return;

            _motion = motion;
            if (!_hasPosition)
            {
                _current = VisualPositionForTip(ToLocal(screenX, screenY), DpiScaleForPoint(screenX, screenY));
                _start = _current;
                _target = _current;
                _control1 = _current;
                _control2 = _current;
                _hasPosition = true;
            }

            _visibleCursor = true;
            _lastActivityAt = DateTime.UtcNow;
            ShowOverlay();
        }

        public void HideCursor()
        {
            _visibleCursor = false;
            _isGliding = false;
            _arrival?.TrySetResult();
            _arrival = null;
            Hide();
        }

        public void GlideTo(int screenX, int screenY, AgentCursorMotion motion, TaskCompletionSource arrival)
        {
            if (!_enabled)
            {
                arrival.TrySetResult();
                return;
            }

            _motion = motion;
            var target = VisualPositionForTip(ToLocal(screenX, screenY), DpiScaleForPoint(screenX, screenY));
            if (!_hasPosition)
            {
                _current = InitialPosition(target);
                _hasPosition = true;
            }

            _arrival?.TrySetResult();
            _arrival = arrival;
            _start = _current;
            _target = target;
            BuildBezier(_start, _target, _motion, out _control1, out _control2);
            _glideStartedAt = DateTime.UtcNow;
            _lastActivityAt = _glideStartedAt;
            _visibleCursor = true;
            _pulseStartedMs = 0;

            if (Distance(_start, _target) < 1)
            {
                _current = _target;
                _isGliding = false;
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
            _lastActivityAt = DateTime.UtcNow;
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

            var now = DateTime.UtcNow;
            var dt = Math.Min(0.05, Math.Max(0.001, (now - _lastFrameAt).TotalSeconds));
            _lastFrameAt = now;

            if (_isGliding)
            {
                var elapsed = (now - _glideStartedAt).TotalMilliseconds;
                var t = Math.Clamp(elapsed / Math.Max(1, _motion.GlideDurationMs), 0, 1);
                var eased = SmootherStep(t);
                _current = Bezier(_start, _control1, _control2, _target, eased);
                var tangent = BezierTangentRadians(_start, _control1, _control2, _target, eased);
                _heading = RotateToward(_heading, tangent + Math.PI, 14 * dt);
                changed = true;
                if (t >= 1)
                {
                    _current = _target;
                    _heading = RestingHeadingRadians;
                    _isGliding = false;
                    _arrival?.TrySetResult();
                    _arrival = null;
                }
            }

            if (_pulseStartedMs > 0 && Environment.TickCount64 - _pulseStartedMs > Math.Max(1, _motion.PressDurationMs))
            {
                _pulseStartedMs = 0;
                _lastActivityAt = now;
            }

            if (!_isGliding && _pulseStartedMs <= 0)
            {
                var idleMs = (now - _lastActivityAt).TotalMilliseconds;
                if (_motion.IdleHideMs > 0 && idleMs >= _motion.IdleHideMs)
                    HideCursor();
            }

            if (changed)
                RenderFrame();
        }

        public CursorSnapshot Snapshot()
        {
            if (!_hasPosition)
                return new CursorSnapshot(_visibleCursor, null, null);

            var tip = TipPointFromVisualPosition(_current, CurrentDpiScale());
            return new CursorSnapshot(
                _visibleCursor,
                (int)Math.Round(tip.X + _virtualBounds.Left),
                (int)Math.Round(tip.Y + _virtualBounds.Top));
        }

        private PointF ToLocal(int screenX, int screenY) => new(screenX - _virtualBounds.Left, screenY - _virtualBounds.Top);

        private void ShowOverlay()
        {
            CloseSiblingOverlayWindows();
            if (!Visible)
                Show();
            TopMost = true;
            RenderFrame();
        }

        private void RenderFrame()
        {
            if (!_enabled || !_visibleCursor || !IsHandleCreated || IsDisposed)
                return;

            var scale = CurrentDpiScale();
            var screenCenter = new PointF(_current.X + _virtualBounds.Left, _current.Y + _virtualBounds.Top);
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
                DrawBloom(g, localCenter, scale);
                DrawCursor(g, localCenter, _heading, scale);
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
                    SourceConstantAlpha = 255,
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
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static PointF InitialPosition(PointF target)
        {
            var x = Math.Min(-140, target.X - 260);
            var y = Math.Min(-140, target.Y - 220);
            return new PointF(x, y);
        }

        private static void BuildBezier(PointF start, PointF end, AgentCursorMotion motion, out PointF control1, out PointF control2)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            var perpX = -dy / length;
            var perpY = dx / length;
            var deflection = length * motion.ArcSize;
            var flowBias = (motion.ArcFlow + 1) / 2;
            var c1Deflect = deflection * (1 - 0.5 * flowBias);
            var c2Deflect = deflection * (1 - 0.5 * (1 - flowBias));

            var c1BaseX = start.X + dx * motion.StartHandle;
            var c1BaseY = start.Y + dy * motion.StartHandle;
            var c2BaseX = end.X - dx * motion.EndHandle;
            var c2BaseY = end.Y - dy * motion.EndHandle;

            control1 = new PointF((float)(c1BaseX + perpX * c1Deflect), (float)(c1BaseY + perpY * c1Deflect));
            control2 = new PointF((float)(c2BaseX + perpX * c2Deflect), (float)(c2BaseY + perpY * c2Deflect));
        }

        private static void DrawBloom(Graphics g, PointF p, float scale)
        {
            var radius = 22f * scale;
            var bounds = new RectangleF(p.X - radius, p.Y - radius, radius * 2, radius * 2);
            using var path = new GraphicsPath();
            path.AddEllipse(bounds);
            using var brush = new PathGradientBrush(path)
            {
                CenterPoint = p,
                CenterColor = Color.FromArgb(140, 94, 192, 232),
                SurroundColors = [Color.FromArgb(0, 94, 192, 232)]
            };
            brush.Blend = new Blend
            {
                Factors = [1.0f, 0.27f, 0.0f],
                Positions = [0.0f, 0.5f, 1.0f]
            };
            g.FillPath(brush, path);
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
            using var outline = new Pen(Color.White, 2f * scale) { LineJoin = LineJoin.Round };
            g.FillPath(brush, path);
            g.DrawPath(outline, path);
        }

        private static PointF Bezier(PointF a, PointF b, PointF c, PointF d, double t)
        {
            var u = 1 - t;
            var uu = u * u;
            var uuu = uu * u;
            var tt = t * t;
            var ttt = tt * t;
            return new PointF(
                (float)(uuu * a.X + 3 * uu * t * b.X + 3 * u * tt * c.X + ttt * d.X),
                (float)(uuu * a.Y + 3 * uu * t * b.Y + 3 * u * tt * c.Y + ttt * d.Y));
        }

        private static double SmootherStep(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        private static double BezierTangentRadians(PointF a, PointF b, PointF c, PointF d, double t)
        {
            var u = 1 - t;
            var dx = 3 * u * u * (b.X - a.X) + 6 * u * t * (c.X - b.X) + 3 * t * t * (d.X - c.X);
            var dy = 3 * u * u * (b.Y - a.Y) + 6 * u * t * (c.Y - b.Y) + 3 * t * t * (d.Y - c.Y);
            return Math.Atan2(dy, dx);
        }

        private static double RotateToward(double current, double desired, double maxStep)
        {
            var diff = desired - current;
            while (diff > Math.PI) diff -= 2 * Math.PI;
            while (diff < -Math.PI) diff += 2 * Math.PI;
            return current + Math.Max(-maxStep, Math.Min(maxStep, diff));
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
            var offset = CursorTipOffset * scale;
            return new PointF(
                tip.X + (float)(Math.Cos(RestingHeadingRadians) * offset),
                tip.Y + (float)(Math.Sin(RestingHeadingRadians) * offset));
        }

        private static PointF TipPointFromVisualPosition(PointF visualPosition, float scale)
        {
            var offset = CursorTipOffset * scale;
            return new PointF(
                visualPosition.X - (float)(Math.Cos(RestingHeadingRadians) * offset),
                visualPosition.Y - (float)(Math.Sin(RestingHeadingRadians) * offset));
        }

        private static void ConfigureHighQuality(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        private static void CloseSiblingOverlayWindows()
        {
            var currentPid = (uint)Environment.ProcessId;
            var currentProcessName = Process.GetCurrentProcess().ProcessName;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0 || pid == currentPid)
                    return true;

                if (IsSiblingOverlayWindow(hwnd, pid, currentProcessName))
                    NativeMethods.PostMessageW(hwnd, NativeMethods.WM_CLOSE, UIntPtr.Zero, IntPtr.Zero);

                return true;
            }, IntPtr.Zero);
        }

        private static bool IsSiblingOverlayWindow(IntPtr hwnd, uint pid, string currentProcessName)
        {
            if (NativeMethods.GetWindowText(hwnd).Equals(OverlayWindowTitle, StringComparison.Ordinal))
                return true;

            var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            var overlayStyle = NativeMethods.WS_EX_TRANSPARENT
                               | NativeMethods.WS_EX_LAYERED
                               | NativeMethods.WS_EX_NOACTIVATE
                               | NativeMethods.WS_EX_TOOLWINDOW;
            if ((exStyle & overlayStyle) != overlayStyle)
                return false;

            try
            {
                using var process = Process.GetProcessById((int)pid);
                return process.ProcessName.Equals(currentProcessName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static double Distance(PointF a, PointF b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
