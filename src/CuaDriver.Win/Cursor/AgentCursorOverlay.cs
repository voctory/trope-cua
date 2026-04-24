using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Cursor;

public sealed class AgentCursorOverlay
{
    private readonly object _gate = new();
    private OverlayForm? _form;
    private Thread? _thread;
    private bool _enabled = true;

    public bool Enabled
    {
        get
        {
            lock (_gate) return _enabled;
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            _enabled = enabled;
        }

        EnsureThread();
        Post(form =>
        {
            form.SetEnabled(enabled);
            if (!enabled)
                form.HideCursor();
        });
    }

    public async Task MoveToAsync(POINT screenPoint, CancellationToken ct)
    {
        if (!Enabled)
            return;

        EnsureThread();
        Post(form => form.MoveTo(screenPoint.X, screenPoint.Y));
        await Task.Delay(220, ct).ConfigureAwait(false);
    }

    public async Task ClickPulseAsync(POINT screenPoint, CancellationToken ct)
    {
        if (!Enabled)
            return;

        EnsureThread();
        Post(form => form.ClickPulse(screenPoint.X, screenPoint.Y));
        await Task.Delay(140, ct).ConfigureAwait(false);
    }

    public string StateJson()
    {
        var formReady = false;
        lock (_gate)
        {
            formReady = _form is not null && !_form.IsDisposed;
        }

        return $"{{\"enabled\":{Enabled.ToString().ToLowerInvariant()},\"route\":\"winforms.click_through_overlay\",\"ready\":{formReady.ToString().ToLowerInvariant()}}}";
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

    private void Post(Action<OverlayForm> action)
    {
        OverlayForm? form;
        lock (_gate)
        {
            form = _form;
        }

        if (form is null || form.IsDisposed)
            return;

        try
        {
            if (form.InvokeRequired)
                form.BeginInvoke(action, form);
            else
                action(form);
        }
        catch
        {
            // Overlay is a best-effort trust signal; input routes must not fail because it closed.
        }
    }

    private sealed class OverlayForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Rectangle _virtualBounds;
        private PointF _current;
        private PointF _start;
        private PointF _target;
        private DateTime _startedAt;
        private bool _visibleCursor;
        private bool _enabled = true;
        private double _pulseStartedMs;

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
            _current = new PointF(_virtualBounds.Left + _virtualBounds.Width / 2f, _virtualBounds.Top + _virtualBounds.Height / 2f);
            _start = _current;
            _target = _current;

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

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled)
                HideCursor();
        }

        public void HideCursor()
        {
            _visibleCursor = false;
            Hide();
        }

        public void MoveTo(int screenX, int screenY)
        {
            if (!_enabled)
                return;

            var target = ToLocal(screenX, screenY);
            if (!_visibleCursor)
            {
                _current = target;
                _start = target;
            }
            else
            {
                _start = _current;
            }

            _target = target;
            _startedAt = DateTime.UtcNow;
            _visibleCursor = true;
            if (!Visible)
                Show();
            TopMost = true;
            Invalidate();
        }

        public void ClickPulse(int screenX, int screenY)
        {
            if (!_enabled)
                return;

            MoveTo(screenX, screenY);
            _pulseStartedMs = Environment.TickCount64;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_enabled || !_visibleCursor)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            DrawPulse(e.Graphics);
            DrawCursor(e.Graphics, _current);
        }

        private void StepAnimation()
        {
            if (!_visibleCursor)
                return;

            var elapsed = (DateTime.UtcNow - _startedAt).TotalMilliseconds;
            var t = Math.Clamp(elapsed / 180.0, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3);
            _current = new PointF(
                (float)(_start.X + (_target.X - _start.X) * eased),
                (float)(_start.Y + (_target.Y - _start.Y) * eased));
        }

        private PointF ToLocal(int screenX, int screenY) => new(screenX - _virtualBounds.Left, screenY - _virtualBounds.Top);

        private void DrawPulse(Graphics g)
        {
            if (_pulseStartedMs <= 0)
                return;

            var age = Environment.TickCount64 - _pulseStartedMs;
            if (age > 520)
            {
                _pulseStartedMs = 0;
                return;
            }

            var t = (float)Math.Clamp(age / 520.0, 0.0, 1.0);
            var radius = 10f + 30f * t;
            var alpha = (int)(150f * (1f - t));
            using var pen = new Pen(Color.FromArgb(alpha, 94, 192, 232), 3);
            g.DrawEllipse(pen, _current.X - radius, _current.Y - radius, radius * 2f, radius * 2f);
        }

        private static void DrawCursor(Graphics g, PointF p)
        {
            using var path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(p.X, p.Y),
                new PointF(p.X + 4, p.Y + 28),
                new PointF(p.X + 11, p.Y + 20),
                new PointF(p.X + 21, p.Y + 41),
                new PointF(p.X + 28, p.Y + 38),
                new PointF(p.X + 18, p.Y + 17),
                new PointF(p.X + 29, p.Y + 17),
            });

            using var glow = new SolidBrush(Color.FromArgb(75, 94, 192, 232));
            g.FillEllipse(glow, p.X - 15, p.Y - 13, 58, 58);

            using var brush = new LinearGradientBrush(
                new RectangleF(p.X, p.Y, 30, 42),
                Color.FromArgb(219, 238, 255),
                Color.FromArgb(84, 205, 160),
                135f);
            using var outline = new Pen(Color.White, 2.4f) { LineJoin = LineJoin.Round };
            g.FillPath(brush, path);
            g.DrawPath(outline, path);
        }
    }
}
