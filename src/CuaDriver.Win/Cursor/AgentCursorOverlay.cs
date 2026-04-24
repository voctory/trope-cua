using System.Drawing;
using System.Drawing.Drawing2D;
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
    public double IdleHideMs { get; init; } = 8000;

    [JsonPropertyName("press_duration_ms")]
    public double PressDurationMs { get; init; } = 650;

    public static AgentCursorMotion Default { get; } = new();
}

public sealed class AgentCursorOverlay
{
    private readonly object _gate = new();
    private OverlayForm? _form;
    private Thread? _thread;
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
        var shouldPost = false;
        lock (_gate)
        {
            if (_enabled == enabled && _form is null)
                return;
            _enabled = enabled;
            shouldPost = _form is not null;
        }

        if (!shouldPost)
            return;

        Post(form =>
        {
            form.SetEnabled(enabled);
            if (!enabled)
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
                IdleHideMs = Clamp(idleHideMs ?? _motion.IdleHideMs, 100, 60000),
            };
            _motion = next;
        }

        Post(form => form.SetMotion(next));
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
        lock (_gate)
        {
            formReady = _form is not null && !_form.IsDisposed;
        }

        return JsonSerializer.Serialize(new
        {
            enabled = Enabled,
            route = "winforms.click_through_overlay",
            ready = formReady,
            motion = Motion
        }, JsonUtil.SerializerOptions);
    }

    private void EnsureThread()
    {
        lock (_gate)
        {
            if (_thread is { IsAlive: true })
                return;

            using var ready = new ManualResetEventSlim(false);
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
            ready.Wait(TimeSpan.FromSeconds(2));
        }
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
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
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
                Invalidate();
            };
            _timer.Start();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int wsExTransparent = 0x00000020;
                const int wsExLayered = 0x00080000;
                const int wsExNoActivate = 0x08000000;
                const int wsExToolWindow = 0x00000080;
                var cp = base.CreateParams;
                cp.ExStyle |= wsExTransparent | wsExLayered | wsExNoActivate | wsExToolWindow;
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
            var target = ToLocal(screenX, screenY);
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

            if (!Visible)
                Show();
            TopMost = true;
            Invalidate();
        }

        public void StartPress(AgentCursorMotion motion)
        {
            if (!_enabled)
                return;

            _motion = motion;
            _lastActivityAt = DateTime.UtcNow;
            _pulseStartedMs = Environment.TickCount64;
            _visibleCursor = true;
            if (!Visible)
                Show();
            TopMost = true;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_enabled || !_visibleCursor)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = CursorScale(e.Graphics);
            DrawCursor(e.Graphics, _current, _heading, scale);
            DrawBloom(e.Graphics, _current, scale);
        }

        private void StepAnimation()
        {
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
                if (t >= 1)
                {
                    _current = _target;
                    _heading = Math.PI / 4;
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
                if (idleMs >= _motion.IdleHideMs)
                    HideCursor();
            }
        }

        private PointF ToLocal(int screenX, int screenY) => new(screenX - _virtualBounds.Left, screenY - _virtualBounds.Top);

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
                CenterColor = Color.FromArgb(115, 94, 192, 232),
                SurroundColors = [Color.FromArgb(0, 94, 192, 232)]
            };
            brush.Blend = new Blend
            {
                Factors = [1.0f, 0.22f, 0.0f],
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
            using var outline = new Pen(Color.White, 1.5f * scale) { LineJoin = LineJoin.Round };
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

        private static float CursorScale(Graphics g) => Math.Clamp(g.DpiX / 96f, 1f, 2.5f);

        private static double Distance(PointF a, PointF b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
