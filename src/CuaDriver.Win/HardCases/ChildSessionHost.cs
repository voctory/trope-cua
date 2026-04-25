using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CuaDriver.Win.Win32;
using Microsoft.Win32;
using System.Text.Json.Nodes;

namespace CuaDriver.Win.HardCases;

public sealed record ChildSessionHostOptions(int Width, int Height, int TimeoutMs, bool Visible);

public sealed record ChildSessionHostStartResult(
    bool Ok,
    string Status,
    int? ChildSessionId,
    bool HostRunning,
    string[] Log);

public static class ChildSessionHost
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
                var clsid = ResolveRdpClientClsid();
                _host.AddLog($"using RDP ActiveX CLSID {clsid}");

                _rdp = new RdpActiveXControl(clsid) { Dock = DockStyle.Fill };
                ((ISupportInitialize)_rdp).BeginInit();
                Controls.Add(_rdp);
                ((ISupportInitialize)_rdp).EndInit();
                _rdp.CreateControl();

                var ocx = _rdp.OcxObject;
                SetComProperty(ocx, "Server", "localhost");
                SetComProperty(ocx, "DesktopWidth", _options.Width);
                SetComProperty(ocx, "DesktopHeight", _options.Height);
                TrySetComProperty(ocx, "ColorDepth", 32);
                TryConfigureAdvancedSettings(ocx);

                SetExtendedProperty(ocx, "ConnectToChildSession", true);
                TrySetExtendedProperty(ocx, "EnableFrameBufferRedirection", true);
                TrySetExtendedProperty(ocx, "ManualClipboardSyncEnabled", true);

                InvokeComMethod(ocx, "Connect");
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
                    InvokeComMethod(ocx, "Disconnect");
            }
            catch
            {
                // Best effort during teardown.
            }
        }

        private static string ResolveRdpClientClsid()
        {
            using var curVer = Registry.ClassesRoot.OpenSubKey(@"MsTscAx.MsTscAx\CurVer");
            var progId = curVer?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(progId))
            {
                using var clsid = Registry.ClassesRoot.OpenSubKey($@"{progId}\CLSID");
                if (clsid?.GetValue(null) is string registered && !string.IsNullOrWhiteSpace(registered))
                    return registered;
            }

            return "{8B918B82-7985-4C24-89DF-C33AD2BBFBCD}";
        }

        private static void TryConfigureAdvancedSettings(object ocx)
        {
            foreach (var propertyName in new[] { "AdvancedSettings9", "AdvancedSettings8", "AdvancedSettings7", "AdvancedSettings6", "AdvancedSettings5", "AdvancedSettings4", "AdvancedSettings3", "AdvancedSettings2", "AdvancedSettings" })
            {
                var settings = TryGetComProperty(ocx, propertyName);
                if (settings is null)
                    continue;

                TrySetComProperty(settings, "EnableCredSspSupport", true);
                TrySetComProperty(settings, "SmartSizing", true);
                TrySetComProperty(settings, "RedirectClipboard", false);
                TrySetComProperty(settings, "RedirectDrives", false);
                return;
            }
        }

        private static object? TryGetComProperty(object target, string name)
        {
            try
            {
                return target.GetType().InvokeMember(
                    name,
                    BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                    null,
                    target,
                    null,
                    CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static void SetComProperty(object target, string name, object value)
            => target.GetType().InvokeMember(
                name,
                BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                null,
                target,
                [value],
                CultureInfo.InvariantCulture);

        private static void TrySetComProperty(object target, string name, object value)
        {
            try
            {
                SetComProperty(target, name, value);
            }
            catch
            {
                // Optional RDP ActiveX settings vary by installed control version.
            }
        }

        private static void InvokeComMethod(object target, string name)
            => target.GetType().InvokeMember(
                name,
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                null,
                target,
                null,
                CultureInfo.InvariantCulture);

        private static void SetExtendedProperty(object ocx, string name, object value)
        {
            if (!TrySetExtendedProperty(ocx, name, value))
                throw new InvalidOperationException($"RDP ActiveX control does not expose IMsRdpExtendedSettings.Property({name}).");
        }

        private static bool TrySetExtendedProperty(object ocx, string name, object value)
        {
            var iid = new Guid("302D8188-0052-4807-806A-362B628F9AC5");
            var unknown = Marshal.GetIUnknownForObject(ocx);
            try
            {
                var hr = Marshal.QueryInterface(unknown, ref iid, out var extended);
                if (hr != 0 || extended == IntPtr.Zero)
                    return false;

                try
                {
                    var extendedObject = Marshal.GetObjectForIUnknown(extended);
                    try
                    {
                        extendedObject.GetType().InvokeMember(
                            "Property",
                            BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                            null,
                            extendedObject,
                            [name, value],
                            CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch
                    {
                        return TrySetDispatchIndexedProperty(extended, "Property", name, value);
                    }
                }
                finally
                {
                    Marshal.Release(extended);
                }
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        private static bool TrySetDispatchIndexedProperty(IntPtr dispatchPointer, string propertyName, string key, object value)
        {
            try
            {
                var dispatch = (IDispatchRaw)Marshal.GetTypedObjectForIUnknown(dispatchPointer, typeof(IDispatchRaw));
                var iidNull = Guid.Empty;
                var dispIds = new int[1];
                var names = new[] { propertyName };
                var hr = dispatch.GetIDsOfNames(ref iidNull, names, 1, 0, dispIds);
                if (hr != 0)
                    return false;

                const int variantSize = 16;
                var args = Marshal.AllocCoTaskMem(variantSize * 2);
                var namedArgs = Marshal.AllocCoTaskMem(sizeof(int));
                try
                {
                    Marshal.GetNativeVariantForObject(value, args);
                    Marshal.GetNativeVariantForObject(key, IntPtr.Add(args, variantSize));
                    Marshal.WriteInt32(namedArgs, DispatchPropertyPut);

                    var dispParams = new DISPPARAMS
                    {
                        rgvarg = args,
                        rgdispidNamedArgs = namedArgs,
                        cArgs = 2,
                        cNamedArgs = 1
                    };
                    hr = dispatch.Invoke(dispIds[0], ref iidNull, 0, DispatchPropertyPutFlag, ref dispParams, IntPtr.Zero, IntPtr.Zero, out _);
                    return hr == 0;
                }
                finally
                {
                    VariantClear(args);
                    VariantClear(IntPtr.Add(args, variantSize));
                    Marshal.FreeCoTaskMem(args);
                    Marshal.FreeCoTaskMem(namedArgs);
                }
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class RdpActiveXControl : AxHost
    {
        public RdpActiveXControl(string clsid) : base(clsid)
        {
        }

        public object OcxObject => GetOcx();
    }

    [ComImport]
    [Guid("00020400-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDispatchRaw
    {
        [PreserveSig]
        int GetTypeInfoCount(out uint pctinfo);

        [PreserveSig]
        int GetTypeInfo(uint iTInfo, uint lcid, out IntPtr ppTInfo);

        [PreserveSig]
        int GetIDsOfNames(
            ref Guid riid,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] rgszNames,
            uint cNames,
            uint lcid,
            [Out] int[] rgDispId);

        [PreserveSig]
        int Invoke(
            int dispIdMember,
            ref Guid riid,
            uint lcid,
            ushort wFlags,
            ref DISPPARAMS pDispParams,
            IntPtr pVarResult,
            IntPtr pExcepInfo,
            out uint puArgErr);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPPARAMS
    {
        public IntPtr rgvarg;
        public IntPtr rgdispidNamedArgs;
        public uint cArgs;
        public uint cNamedArgs;
    }

    private const int DispatchPropertyPut = -3;
    private const ushort DispatchPropertyPutFlag = 4;

    [DllImport("oleaut32.dll")]
    private static extern int VariantClear(IntPtr pvarg);
}
