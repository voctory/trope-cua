using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.HardCases;

internal sealed record ChildSessionHostOptions(int Width, int Height, int TimeoutMs, bool Visible);

internal sealed record ChildSessionHostStartResult(
    bool Ok,
    string Status,
    int? ChildSessionId,
    bool HostRunning,
    string[] Log);

internal static class ChildSessionHost
{
    private static readonly object Gate = new();
    private static HostInstance? _host;

    public static bool IsRunning
    {
        get
        {
            lock (Gate)
                return _host?.IsRunning == true;
        }
    }

    public static async Task<ChildSessionHostStartResult> StartAsync(ChildSessionHostOptions options, CancellationToken cancellationToken)
    {
        HostInstance host;
        lock (Gate)
        {
            if (_host?.IsRunning == true)
            {
                var child = ChildSessionBroker.GetChildSessionId();
                return new ChildSessionHostStartResult(
                    Ok: child is not null,
                    Status: child is null ? "child_session_host already running; no connected child session is reported yet" : "child_session_host already running",
                    ChildSessionId: child,
                    HostRunning: true,
                    Log: _host.LogSnapshot());
            }

            host = new HostInstance(options);
            _host = host;
        }

        var result = await host.StartAsync(cancellationToken).ConfigureAwait(false);
        if (!result.HostRunning)
        {
            lock (Gate)
            {
                if (ReferenceEquals(_host, host))
                    _host = null;
            }
        }

        return result;
    }

    public static string StatusText()
    {
        lock (Gate)
        {
            var child = ChildSessionBroker.GetChildSessionId();
            var host = _host;
            if (host?.IsRunning == true)
                return $"host_running=true child_session_id={(child?.ToString(CultureInfo.InvariantCulture) ?? "none")} state=\"{host.State}\"";

            return $"host_running=false child_session_id={(child?.ToString(CultureInfo.InvariantCulture) ?? "none")}";
        }
    }

    public static JsonObject StatusObject()
    {
        lock (Gate)
        {
            var child = ChildSessionBroker.GetChildSessionId();
            var host = _host;
            return new JsonObject
            {
                ["host_running"] = host?.IsRunning == true,
                ["child_session_id"] = child,
                ["state"] = host?.IsRunning == true ? host.State : "stopped",
                ["log"] = host is null ? new JsonArray() : JsonArrayFrom(host.LogSnapshot())
            };
        }
    }

    public static bool Stop(out string message)
    {
        HostInstance? host;
        lock (Gate)
        {
            host = _host;
            _host = null;
        }

        if (host is null)
        {
            message = "child_session_host was not running.";
            return true;
        }

        return host.Stop(out message);
    }

    private static JsonArray JsonArrayFrom(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var value in values)
            arr.Add(value);
        return arr;
    }

    private sealed class HostInstance
    {
        private readonly ChildSessionHostOptions _options;
        private readonly TaskCompletionSource<ChildSessionHostStartResult> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _log = [];
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private Thread? _thread;
        private ChildSessionForm? _form;
        private volatile bool _running;

        public HostInstance(ChildSessionHostOptions options)
        {
            _options = options;
        }

        public bool IsRunning => _running;
        public string State { get; private set; } = "created";

        public void SetState(string state) => State = state;

        public string[] LogSnapshot()
        {
            lock (_log)
                return _log.ToArray();
        }

        public async Task<ChildSessionHostStartResult> StartAsync(CancellationToken cancellationToken)
        {
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "cua-driver-win child session host"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _running = true;
            _thread.Start();

            var outerTimeoutMs = Math.Max(2_000, _options.TimeoutMs + 2_000);
            var completed = await Task.WhenAny(_started.Task, Task.Delay(outerTimeoutMs, cancellationToken)).ConfigureAwait(false);
            if (completed == _started.Task)
                return await _started.Task.ConfigureAwait(false);

            AddLog($"outer timeout after {outerTimeoutMs}ms");
            Stop(out _);
            return BuildResult(false, "Timed out waiting for the child-session host thread.", hostRunning: false);
        }

        public bool Stop(out string message)
        {
            try
            {
                var form = _form;
                if (form is not null && !form.IsDisposed)
                    form.BeginInvoke(new System.Windows.Forms.MethodInvoker(form.Close));

                message = "child_session_host stop requested.";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private void ThreadMain()
        {
            try
            {
                State = "starting";
                using var form = new ChildSessionForm(_options, this);
                _form = form;
                Application.Run(form);
            }
            catch (Exception ex)
            {
                AddLog($"{ex.GetType().Name}: {ex.Message}");
                Signal(BuildResult(false, $"child_session_host failed: {ex.Message}", hostRunning: false));
            }
            finally
            {
                State = "stopped";
                _running = false;
            }
        }

        public void AddLog(string message)
        {
            lock (_log)
                _log.Add($"{_stopwatch.ElapsedMilliseconds}ms {message}");
        }

        public void Signal(ChildSessionHostStartResult result)
        {
            _started.TrySetResult(result);
        }

        public ChildSessionHostStartResult BuildResult(bool ok, string status, bool hostRunning)
            => new(ok, status, ChildSessionBroker.GetChildSessionId(), hostRunning, LogSnapshot());
    }

    private sealed class ChildSessionForm : Form
    {
        private readonly ChildSessionHostOptions _options;
        private readonly HostInstance _host;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private RdpActiveXControl? _rdp;
        private System.Windows.Forms.Timer? _pollTimer;
        private bool _signaled;

        public ChildSessionForm(ChildSessionHostOptions options, HostInstance host)
        {
            _options = options;
            _host = host;
            Text = "cua-driver-win child session";
            Width = Math.Max(640, options.Width);
            Height = Math.Max(480, options.Height);
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.Manual;
            Opacity = options.Visible ? 1.0 : 0.01;

            var area = Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1280, 720);
            Left = Math.Max(area.Left, area.Right - Width - 24);
            Top = Math.Max(area.Top, area.Bottom - Height - 24);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            BeginInvoke(new System.Windows.Forms.MethodInvoker(StartConnection));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            TryDisconnect();
            base.OnFormClosing(e);
        }

        private void StartConnection()
        {
            try
            {
                _host.SetState("connecting");
                var clsid = ChildSessionRdpCom.ResolveRdpClientClsid();
                _host.AddLog($"using RDP ActiveX CLSID {clsid}");

                _rdp = new RdpActiveXControl(clsid) { Dock = DockStyle.Fill };
                ((ISupportInitialize)_rdp).BeginInit();
                Controls.Add(_rdp);
                ((ISupportInitialize)_rdp).EndInit();
                _rdp.CreateControl();

                var ocx = _rdp.OcxObject;
                ChildSessionRdpCom.SetProperty(ocx, "Server", "localhost");
                ChildSessionRdpCom.SetProperty(ocx, "DesktopWidth", _options.Width);
                ChildSessionRdpCom.SetProperty(ocx, "DesktopHeight", _options.Height);
                ChildSessionRdpCom.TrySetProperty(ocx, "ColorDepth", 32);
                ChildSessionRdpCom.TryConfigureAdvancedSettings(ocx);

                ChildSessionRdpCom.SetExtendedProperty(ocx, "ConnectToChildSession", true);
                ChildSessionRdpCom.TrySetExtendedProperty(ocx, "EnableFrameBufferRedirection", true);
                ChildSessionRdpCom.TrySetExtendedProperty(ocx, "ManualClipboardSyncEnabled", true);

                ChildSessionRdpCom.InvokeMethod(ocx, "Connect");
                _host.AddLog("RDP ActiveX Connect() returned");

                _pollTimer = new System.Windows.Forms.Timer { Interval = 250 };
                _pollTimer.Tick += PollForChildSession;
                _pollTimer.Start();
            }
            catch (Exception ex)
            {
                _host.AddLog($"{ex.GetType().Name}: {ex.Message}");
                SignalOnce(_host.BuildResult(false, $"child_session_host failed while connecting: {ex.Message}", hostRunning: false));
                Close();
            }
        }

        private void PollForChildSession(object? sender, EventArgs e)
        {
            var childSessionId = ChildSessionBroker.GetChildSessionId();
            if (childSessionId is not null)
            {
                _pollTimer?.Stop();
                _host.SetState("connected");
                _host.AddLog($"WTSGetChildSessionId returned {childSessionId}");
                SignalOnce(_host.BuildResult(true, "child_session_host connected", hostRunning: true));
                return;
            }

            if (_stopwatch.ElapsedMilliseconds <= _options.TimeoutMs)
                return;

            _pollTimer?.Stop();
            _host.AddLog("timed out waiting for WTSGetChildSessionId");
            SignalOnce(_host.BuildResult(false, "Timed out waiting for WTSGetChildSessionId after RDP Connect().", hostRunning: false));
            Close();
        }

        private void SignalOnce(ChildSessionHostStartResult result)
        {
            if (_signaled)
                return;

            _signaled = true;
            _host.Signal(result);
        }

        private void TryDisconnect()
        {
            try
            {
                _pollTimer?.Stop();
                if (_rdp?.OcxObject is { } ocx)
                    ChildSessionRdpCom.InvokeMethod(ocx, "Disconnect");
            }
            catch
            {
                // Best effort during teardown.
            }
        }

    }
}
