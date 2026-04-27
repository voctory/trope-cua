using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Win32;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Media.AppBroadcasting;
using Windows.Media.Capture;
using Windows.UI.Input.Preview.Injection;

namespace CuaDriver.Win.HardCases;

/// <summary>
/// Probe and integration seam for Windows.UI.Input.Preview.Injection.
/// The API can be present/callable on some desktop builds, but it injects
/// parent-session input and is not a pid-addressed background lane by itself.
/// </summary>
internal static class AppBroadcastInputInjector
{
    internal sealed record ProbeResult(string Text, JsonObject StructuredContent, bool IsError);

    public static string Status()
    {
        try
        {
            var typePresent = ApiInformation.IsTypePresent("Windows.UI.Input.Preview.Injection.InputInjector");
            if (!typePresent)
                return "InputInjector WinRT type is not present on this OS.";

            var injector = InputInjector.TryCreate();
            var appBroadcastInjector = InputInjector.TryCreateForAppBroadcastOnly();
            var appBroadcastServicesPresent = ApiInformation.IsTypePresent("Windows.Media.Capture.AppBroadcastServices");
            var appBroadcastPlugInManagerPresent = ApiInformation.IsTypePresent("Windows.Media.Capture.AppBroadcastPlugInManager");
            var gameBarServicesManagerPresent = ApiInformation.IsTypePresent("Windows.Media.Capture.GameBarServicesManager");
            var appBroadcastingUiPresent = ApiInformation.IsTypePresent("Windows.Media.AppBroadcasting.AppBroadcastingUI");
            return $"type_present=true try_create={(injector is not null)} try_create_appbroadcast={(appBroadcastInjector is not null)} appbroadcast_services_type_present={appBroadcastServicesPresent} appbroadcast_plugin_manager_type_present={appBroadcastPlugInManagerPresent} gamebar_services_manager_type_present={gameBarServicesManagerPresent} appbroadcasting_ui_type_present={appBroadcastingUiPresent} generic_lane=parent_session_global appbroadcast_lane=requires_active_broadcast_validation";
        }
        catch (Exception ex)
        {
            return $"probe_failed type={ex.GetType().Name} hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"";
        }
    }

    public static ProbeResult ProbeKeyboardKey(bool appBroadcastOnly, string key)
    {
        var route = appBroadcastOnly ? "inputinjector.trycreateforappbroadcastonly.keyboard" : "inputinjector.trycreate.keyboard";
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Input.Preview.Injection.InputInjector"))
                return Failure(appBroadcastOnly, route, "InputInjector WinRT type is not present on this OS.");

            var vk = WindowMessageKeyMapping.VirtualKey(key);
            if (vk == 0)
                return Failure(appBroadcastOnly, route, $"Unsupported key '{key}'.");

            var injector = appBroadcastOnly
                ? InputInjector.TryCreateForAppBroadcastOnly()
                : InputInjector.TryCreate();
            if (injector is null)
                return Failure(appBroadcastOnly, route, "InputInjector factory returned null.");

            if (!NativeMethods.GetCursorPos(out var cursorBefore))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed before probe injection.");
            var foregroundBefore = NativeMethods.GetForegroundWindow();

            var down = new InjectedInputKeyboardInfo
            {
                VirtualKey = (ushort)vk,
                KeyOptions = InjectedInputKeyOptions.None
            };
            var up = new InjectedInputKeyboardInfo
            {
                VirtualKey = (ushort)vk,
                KeyOptions = InjectedInputKeyOptions.KeyUp
            };
            injector.InjectKeyboardInput([down, up]);

            Thread.Sleep(50);

            if (!NativeMethods.GetCursorPos(out var cursorAfter))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed after probe injection.");
            var foregroundAfter = NativeMethods.GetForegroundWindow();
            var cursorMoved = cursorBefore.X != cursorAfter.X || cursorBefore.Y != cursorAfter.Y;
            var foregroundChanged = foregroundBefore != foregroundAfter;

            var text = string.Join(Environment.NewLine, [
                "ok=true",
                $"appbroadcast_only={appBroadcastOnly}",
                $"key={key}",
                $"virtual_key=0x{vk:X2}",
                $"cursor_before={cursorBefore}",
                $"cursor_after={cursorAfter}",
                $"foreground_before=0x{foregroundBefore.ToInt64():X}",
                $"foreground_after=0x{foregroundAfter.ToInt64():X}",
                $"cursor_moved={cursorMoved}",
                $"foreground_changed={foregroundChanged}",
                $"background_safe={!cursorMoved && !foregroundChanged}",
                $"route={route}"
            ]);
            return new ProbeResult(text, new JsonObject
            {
                ["ok"] = true,
                ["appbroadcast_only"] = appBroadcastOnly,
                ["route"] = route,
                ["key"] = key,
                ["virtual_key"] = $"0x{vk:X2}",
                ["cursor_before"] = PointObject(cursorBefore),
                ["cursor_after"] = PointObject(cursorAfter),
                ["foreground_before"] = $"0x{foregroundBefore.ToInt64():X}",
                ["foreground_after"] = $"0x{foregroundAfter.ToInt64():X}",
                ["cursor_moved"] = cursorMoved,
                ["foreground_changed"] = foregroundChanged,
                ["background_safe"] = !cursorMoved && !foregroundChanged
            }, IsError: false);
        }
        catch (Exception ex)
        {
            var message = $"{ex.GetType().Name}: {ex.Message}";
            return new ProbeResult(
                $"ok=false appbroadcast_only={appBroadcastOnly} route=\"{route}\" hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"",
                new JsonObject
                {
                    ["ok"] = false,
                    ["appbroadcast_only"] = appBroadcastOnly,
                    ["route"] = route,
                    ["hresult"] = $"0x{Marshal.GetHRForException(ex):X8}",
                    ["reason"] = message
                },
                IsError: true);
        }
    }

    public static ProbeResult ProbeKeyboardText(bool appBroadcastOnly, string text, bool pressEnter)
    {
        var route = appBroadcastOnly ? "inputinjector.trycreateforappbroadcastonly.text" : "inputinjector.trycreate.text";
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Input.Preview.Injection.InputInjector"))
                return Failure(appBroadcastOnly, route, "InputInjector WinRT type is not present on this OS.");

            var injector = appBroadcastOnly
                ? InputInjector.TryCreateForAppBroadcastOnly()
                : InputInjector.TryCreate();
            if (injector is null)
                return Failure(appBroadcastOnly, route, "InputInjector factory returned null.");

            if (!NativeMethods.GetCursorPos(out var cursorBefore))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed before probe injection.");
            var foregroundBefore = NativeMethods.GetForegroundWindow();

            var keys = new List<InjectedInputKeyboardInfo>();
            foreach (var ch in text)
            {
                keys.Add(new InjectedInputKeyboardInfo
                {
                    ScanCode = ch,
                    KeyOptions = InjectedInputKeyOptions.Unicode
                });
                keys.Add(new InjectedInputKeyboardInfo
                {
                    ScanCode = ch,
                    KeyOptions = InjectedInputKeyOptions.Unicode | InjectedInputKeyOptions.KeyUp
                });
            }

            if (pressEnter)
            {
                keys.Add(new InjectedInputKeyboardInfo
                {
                    VirtualKey = 0x0D,
                    KeyOptions = InjectedInputKeyOptions.None
                });
                keys.Add(new InjectedInputKeyboardInfo
                {
                    VirtualKey = 0x0D,
                    KeyOptions = InjectedInputKeyOptions.KeyUp
                });
            }

            injector.InjectKeyboardInput(keys);

            Thread.Sleep(250);

            if (!NativeMethods.GetCursorPos(out var cursorAfter))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed after probe injection.");
            var foregroundAfter = NativeMethods.GetForegroundWindow();
            var cursorMoved = cursorBefore.X != cursorAfter.X || cursorBefore.Y != cursorAfter.Y;
            var foregroundChanged = foregroundBefore != foregroundAfter;

            var textResult = string.Join(Environment.NewLine, [
                "ok=true",
                $"appbroadcast_only={appBroadcastOnly}",
                $"text_length={text.Length}",
                $"press_enter={pressEnter}",
                $"cursor_before={cursorBefore}",
                $"cursor_after={cursorAfter}",
                $"foreground_before=0x{foregroundBefore.ToInt64():X}",
                $"foreground_after=0x{foregroundAfter.ToInt64():X}",
                $"cursor_moved={cursorMoved}",
                $"foreground_changed={foregroundChanged}",
                $"background_safe={!cursorMoved && !foregroundChanged}",
                $"route={route}"
            ]);
            return new ProbeResult(textResult, new JsonObject
            {
                ["ok"] = true,
                ["appbroadcast_only"] = appBroadcastOnly,
                ["route"] = route,
                ["text_length"] = text.Length,
                ["press_enter"] = pressEnter,
                ["cursor_before"] = PointObject(cursorBefore),
                ["cursor_after"] = PointObject(cursorAfter),
                ["foreground_before"] = $"0x{foregroundBefore.ToInt64():X}",
                ["foreground_after"] = $"0x{foregroundAfter.ToInt64():X}",
                ["cursor_moved"] = cursorMoved,
                ["foreground_changed"] = foregroundChanged,
                ["background_safe"] = !cursorMoved && !foregroundChanged
            }, IsError: false);
        }
        catch (Exception ex)
        {
            var message = $"{ex.GetType().Name}: {ex.Message}";
            return new ProbeResult(
                $"ok=false appbroadcast_only={appBroadcastOnly} route=\"{route}\" hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"",
                new JsonObject
                {
                    ["ok"] = false,
                    ["appbroadcast_only"] = appBroadcastOnly,
                    ["route"] = route,
                    ["hresult"] = $"0x{Marshal.GetHRForException(ex):X8}",
                    ["reason"] = message
                },
                IsError: true);
        }
    }

    public static ProbeResult ProbeMouseClick(bool appBroadcastOnly, int x, int y)
    {
        var route = appBroadcastOnly ? "inputinjector.trycreateforappbroadcastonly.click" : "inputinjector.trycreate.click";
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Input.Preview.Injection.InputInjector"))
                return Failure(appBroadcastOnly, route, "InputInjector WinRT type is not present on this OS.");

            var injector = appBroadcastOnly
                ? InputInjector.TryCreateForAppBroadcastOnly()
                : InputInjector.TryCreate();
            if (injector is null)
                return Failure(appBroadcastOnly, route, "InputInjector factory returned null.");

            if (!NativeMethods.GetCursorPos(out var cursorBefore))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed before probe injection.");
            var foregroundBefore = NativeMethods.GetForegroundWindow();

            var virtualX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            var virtualY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            var virtualWidth = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN));
            var virtualHeight = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
            var normalizedX = (int)Math.Clamp(Math.Round((x - virtualX) * 65535.0 / Math.Max(1, virtualWidth - 1)), 0, 65535);
            var normalizedY = (int)Math.Clamp(Math.Round((y - virtualY) * 65535.0 / Math.Max(1, virtualHeight - 1)), 0, 65535);
            const InjectedInputMouseOptions baseOptions = InjectedInputMouseOptions.Absolute | InjectedInputMouseOptions.VirtualDesk;
            injector.InjectMouseInput([
                new InjectedInputMouseInfo
                {
                    MouseOptions = baseOptions | InjectedInputMouseOptions.Move,
                    DeltaX = normalizedX,
                    DeltaY = normalizedY
                },
                new InjectedInputMouseInfo
                {
                    MouseOptions = baseOptions | InjectedInputMouseOptions.LeftDown,
                    DeltaX = normalizedX,
                    DeltaY = normalizedY
                },
                new InjectedInputMouseInfo
                {
                    MouseOptions = baseOptions | InjectedInputMouseOptions.LeftUp,
                    DeltaX = normalizedX,
                    DeltaY = normalizedY
                }
            ]);

            Thread.Sleep(150);

            if (!NativeMethods.GetCursorPos(out var cursorAfter))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed after probe injection.");
            var foregroundAfter = NativeMethods.GetForegroundWindow();
            var cursorMoved = cursorBefore.X != cursorAfter.X || cursorBefore.Y != cursorAfter.Y;
            var foregroundChanged = foregroundBefore != foregroundAfter;

            var text = string.Join(Environment.NewLine, [
                "ok=true",
                $"appbroadcast_only={appBroadcastOnly}",
                $"x={x}",
                $"y={y}",
                $"normalized_x={normalizedX}",
                $"normalized_y={normalizedY}",
                $"cursor_before={cursorBefore}",
                $"cursor_after={cursorAfter}",
                $"foreground_before=0x{foregroundBefore.ToInt64():X}",
                $"foreground_after=0x{foregroundAfter.ToInt64():X}",
                $"cursor_moved={cursorMoved}",
                $"foreground_changed={foregroundChanged}",
                $"background_safe={!cursorMoved && !foregroundChanged}",
                $"route={route}"
            ]);
            return new ProbeResult(text, new JsonObject
            {
                ["ok"] = true,
                ["appbroadcast_only"] = appBroadcastOnly,
                ["route"] = route,
                ["x"] = x,
                ["y"] = y,
                ["normalized_x"] = normalizedX,
                ["normalized_y"] = normalizedY,
                ["cursor_before"] = PointObject(cursorBefore),
                ["cursor_after"] = PointObject(cursorAfter),
                ["foreground_before"] = $"0x{foregroundBefore.ToInt64():X}",
                ["foreground_after"] = $"0x{foregroundAfter.ToInt64():X}",
                ["cursor_moved"] = cursorMoved,
                ["foreground_changed"] = foregroundChanged,
                ["background_safe"] = !cursorMoved && !foregroundChanged
            }, IsError: false);
        }
        catch (Exception ex)
        {
            var message = $"{ex.GetType().Name}: {ex.Message}";
            return new ProbeResult(
                $"ok=false appbroadcast_only={appBroadcastOnly} route=\"{route}\" hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"",
                new JsonObject
                {
                    ["ok"] = false,
                    ["appbroadcast_only"] = appBroadcastOnly,
                    ["route"] = route,
                    ["hresult"] = $"0x{Marshal.GetHRForException(ex):X8}",
                    ["reason"] = message
                },
                IsError: true);
        }
    }

    public static ProbeResult ProbeBroadcastStatus()
    {
        const string route = "appbroadcasting.ui.getstatus";
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.Media.AppBroadcasting.AppBroadcastingUI"))
                return Failure(true, route, "AppBroadcastingUI WinRT type is not present on this OS.");

            var ui = AppBroadcastingUI.GetDefault();
            var status = ui.GetStatus();
            var details = status.Details;
            var text = string.Join(Environment.NewLine, [
                "ok=true",
                $"route={route}",
                $"can_start_broadcast={status.CanStartBroadcast}",
                $"is_any_app_broadcasting={details.IsAnyAppBroadcasting}",
                $"is_app_inactive={details.IsAppInactive}",
                $"is_blocked_for_app={details.IsBlockedForApp}",
                $"is_capture_resource_unavailable={details.IsCaptureResourceUnavailable}",
                $"is_disabled_by_system={details.IsDisabledBySystem}",
                $"is_disabled_by_user={details.IsDisabledByUser}",
                $"is_game_stream_in_progress={details.IsGameStreamInProgress}",
                $"is_gpu_constrained={details.IsGpuConstrained}"
            ]);
            return new ProbeResult(text, new JsonObject
            {
                ["ok"] = true,
                ["route"] = route,
                ["can_start_broadcast"] = status.CanStartBroadcast,
                ["details"] = new JsonObject
                {
                    ["is_any_app_broadcasting"] = details.IsAnyAppBroadcasting,
                    ["is_app_inactive"] = details.IsAppInactive,
                    ["is_blocked_for_app"] = details.IsBlockedForApp,
                    ["is_capture_resource_unavailable"] = details.IsCaptureResourceUnavailable,
                    ["is_disabled_by_system"] = details.IsDisabledBySystem,
                    ["is_disabled_by_user"] = details.IsDisabledByUser,
                    ["is_game_stream_in_progress"] = details.IsGameStreamInProgress,
                    ["is_gpu_constrained"] = details.IsGpuConstrained
                }
            }, IsError: false);
        }
        catch (Exception ex)
        {
            var message = $"{ex.GetType().Name}: {ex.Message}";
            return new ProbeResult(
                $"ok=false route=\"{route}\" hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"",
                new JsonObject
                {
                    ["ok"] = false,
                    ["route"] = route,
                    ["hresult"] = $"0x{Marshal.GetHRForException(ex):X8}",
                    ["reason"] = message
                },
                IsError: true);
        }
    }

    public static ProbeResult ProbeBroadcastPlugins()
    {
        const string route = "appbroadcast.plugin_manager";
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.Media.Capture.AppBroadcastPlugInManager"))
                return Failure(true, route, "AppBroadcastPlugInManager WinRT type is not present on this OS.");

            var manager = AppBroadcastPlugInManager.GetDefault();
            if (manager is null)
                return Failure(true, route, "AppBroadcastPlugInManager.GetDefault returned null.");

            var plugIns = new JsonArray();
            var list = SafeObject(() => manager.PlugInList);
            if (list is not null)
            {
                foreach (var plugIn in list)
                    plugIns.Add(DescribeAppBroadcastPlugIn(plugIn));
            }

            var defaultPlugIn = SafeObject(() => manager.DefaultPlugIn);
            var providerAvailable = SafeBool(() => manager.IsBroadcastProviderAvailable);
            var text = string.Join(Environment.NewLine, [
                "ok=true",
                $"route={route}",
                $"is_broadcast_provider_available={providerAvailable}",
                $"default_plugin_present={defaultPlugIn is not null}",
                $"plugin_count={plugIns.Count}"
            ]);
            return new ProbeResult(text, new JsonObject
            {
                ["ok"] = true,
                ["route"] = route,
                ["is_broadcast_provider_available"] = providerAvailable,
                ["default_plugin"] = defaultPlugIn is null ? null : DescribeAppBroadcastPlugIn(defaultPlugIn),
                ["plugin_count"] = plugIns.Count,
                ["plugins"] = plugIns
            }, IsError: false);
        }
        catch (Exception ex)
        {
            var message = $"{ex.GetType().Name}: {ex.Message}";
            return new ProbeResult(
                $"ok=false route=\"{route}\" hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"",
                new JsonObject
                {
                    ["ok"] = false,
                    ["route"] = route,
                    ["hresult"] = $"0x{Marshal.GetHRForException(ex):X8}",
                    ["reason"] = message
                },
                IsError: true);
        }
    }

    public static ProbeResult ProbeApiContext()
    {
        const string route = "appbroadcast.api_context";
        try
        {
            var packageFullName = TryGetCurrentPackageFullName(out var packageFullNameError);
            var aumid = TryGetCurrentApplicationUserModelId(out var aumidError);
            var types = new JsonObject
            {
                ["input_injector"] = SafeBool(() => ApiInformation.IsTypePresent("Windows.UI.Input.Preview.Injection.InputInjector")),
                ["appbroadcast_services"] = SafeBool(() => ApiInformation.IsTypePresent("Windows.Media.Capture.AppBroadcastServices")),
                ["appbroadcast_plugin_manager"] = SafeBool(() => ApiInformation.IsTypePresent("Windows.Media.Capture.AppBroadcastPlugInManager")),
                ["appbroadcasting_ui"] = SafeBool(() => ApiInformation.IsTypePresent("Windows.Media.AppBroadcasting.AppBroadcastingUI")),
                ["gamebar_services_manager"] = SafeBool(() => ApiInformation.IsTypePresent("Windows.Media.Capture.GameBarServicesManager")),
                ["xbox_gamebar_widget"] = SafeBool(() => ApiInformation.IsTypePresent("Microsoft.Gaming.XboxGameBar.XboxGameBarWidget")),
                ["xbox_gamebar_app_target_tracker"] = SafeBool(() => ApiInformation.IsTypePresent("Microsoft.Gaming.XboxGameBar.XboxGameBarAppTargetTracker")),
                ["xbox_gamebar_app_target"] = SafeBool(() => ApiInformation.IsTypePresent("Microsoft.Gaming.XboxGameBar.XboxGameBarAppTarget")),
                ["xbox_gamebar_ft_factory"] = SafeBool(() => ApiInformation.IsTypePresent("XboxGameBarFT.GbftFactory"))
            };
            var properties = new JsonObject
            {
                ["xbox_gamebar_app_target_hwnd"] = SafeBool(() => ApiInformation.IsPropertyPresent("Microsoft.Gaming.XboxGameBar.XboxGameBarAppTarget", "Hwnd"))
            };
            var contracts = new JsonObject
            {
                ["appbroadcast_contract_v1"] = SafeBool(() => ApiInformation.IsApiContractPresent("Windows.Media.Capture.AppBroadcastContract", 1)),
                ["universal_contract_v5"] = SafeBool(() => ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 5))
            };

            var text = string.Join(Environment.NewLine, [
                "ok=true",
                $"route={route}",
                $"packaged={packageFullName is not null}",
                $"package_full_name={(packageFullName ?? packageFullNameError)}",
                $"aumid={(aumid ?? aumidError)}",
                $"xbox_gamebar_widget_type_present={types["xbox_gamebar_widget"]}",
                $"xbox_gamebar_app_target_hwnd_property_present={properties["xbox_gamebar_app_target_hwnd"]}"
            ]);
            return new ProbeResult(text, new JsonObject
            {
                ["ok"] = true,
                ["route"] = route,
                ["packaged"] = packageFullName is not null,
                ["package_full_name"] = packageFullName,
                ["package_full_name_error"] = packageFullNameError,
                ["aumid"] = aumid,
                ["aumid_error"] = aumidError,
                ["types"] = types,
                ["properties"] = properties,
                ["contracts"] = contracts
            }, IsError: false);
        }
        catch (Exception ex)
        {
            var message = $"{ex.GetType().Name}: {ex.Message}";
            return new ProbeResult(
                $"ok=false route=\"{route}\" hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"",
                new JsonObject
                {
                    ["ok"] = false,
                    ["route"] = route,
                    ["hresult"] = $"0x{Marshal.GetHRForException(ex):X8}",
                    ["reason"] = message
                },
                IsError: true);
        }
    }

    public static ProbeResult ProbeGameBarServices(int timeoutMs)
    {
        const string route = "gamebarservicesmanager.servicescreated";
        var startedAt = DateTimeOffset.UtcNow;
        timeoutMs = Math.Clamp(timeoutMs, 100, 120_000);
        var stage = "start";

        try
        {
            stage = "type_check";
            if (!ApiInformation.IsTypePresent("Windows.Media.Capture.GameBarServicesManager"))
                return Failure(true, route, "GameBarServicesManager WinRT type is not present on this OS.");

            var events = new JsonArray();
            using var ready = new ManualResetEventSlim(false);
            stage = "get_default";
            var manager = GameBarServicesManager.GetDefault();
            if (manager is null)
                return Failure(true, route, "GameBarServicesManager.GetDefault returned null.");

            var managerCreated = true;
            TypedEventHandler<GameBarServicesManager, GameBarServicesManagerGameBarServicesCreatedEventArgs>? handler = null;
            handler = (_, args) =>
            {
                events.Add(DescribeGameBarServices(args.GameBarServices));
                ready.Set();
            };

            stage = "subscribe";
            manager.GameBarServicesCreated += handler;
            try
            {
                stage = "wait";
                ready.Wait(timeoutMs);
            }
            finally
            {
                stage = "unsubscribe";
                manager.GameBarServicesCreated -= handler;
            }

            stage = "complete";
            var elapsedMs = (int)Math.Round((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            var observed = events.Count > 0;
            var text = string.Join(Environment.NewLine, [
                $"ok={observed.ToString().ToLowerInvariant()}",
                $"route={route}",
                $"manager_created={managerCreated}",
                $"event_count={events.Count}",
                $"timeout_ms={timeoutMs}",
                $"elapsed_ms={elapsedMs}"
            ]);
            return new ProbeResult(text, new JsonObject
            {
                ["ok"] = observed,
                ["route"] = route,
                ["manager_created"] = managerCreated,
                ["event_count"] = events.Count,
                ["events"] = events,
                ["timeout_ms"] = timeoutMs,
                ["elapsed_ms"] = elapsedMs
            }, IsError: !observed);
        }
        catch (Exception ex)
        {
            var message = $"{ex.GetType().Name}: {ex.Message}";
            return new ProbeResult(
                $"ok=false route=\"{route}\" hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"",
                new JsonObject
                {
                    ["ok"] = false,
                    ["route"] = route,
                    ["stage"] = stage,
                    ["hresult"] = $"0x{Marshal.GetHRForException(ex):X8}",
                    ["reason"] = message,
                    ["timeout_ms"] = timeoutMs
                },
                IsError: true);
        }
    }

    public static ProbeResult ProbeMouseDelta(bool appBroadcastOnly)
    {
        var route = appBroadcastOnly ? "inputinjector.trycreateforappbroadcastonly" : "inputinjector.trycreate";
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Input.Preview.Injection.InputInjector"))
                return Failure(appBroadcastOnly, route, "InputInjector WinRT type is not present on this OS.");

            var injector = appBroadcastOnly
                ? InputInjector.TryCreateForAppBroadcastOnly()
                : InputInjector.TryCreate();
            if (injector is null)
                return Failure(appBroadcastOnly, route, "InputInjector factory returned null.");

            if (!NativeMethods.GetCursorPos(out var before))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed before probe injection.");

            var forward = new InjectedInputMouseInfo
            {
                MouseOptions = InjectedInputMouseOptions.Move,
                DeltaX = 8,
                DeltaY = 0
            };
            injector.InjectMouseInput([forward]);
            if (!TryWaitForCursor(point => point.X != before.X || point.Y != before.Y, TimeSpan.FromMilliseconds(50), out var afterForward))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed after forward probe injection.");

            var back = new InjectedInputMouseInfo
            {
                MouseOptions = InjectedInputMouseOptions.Move,
                DeltaX = -8,
                DeltaY = 0
            };
            injector.InjectMouseInput([back]);
            if (!TryWaitForCursor(point => point.X == before.X && point.Y == before.Y, TimeSpan.FromMilliseconds(50), out var afterBack))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed after restore probe injection.");

            var movedOnForward = before.X != afterForward.X || before.Y != afterForward.Y;
            var restoredByInjector = before.X == afterBack.X && before.Y == afterBack.Y;
            var restoredByProbe = false;
            if (!restoredByInjector)
            {
                var restoreRequested = NativeMethods.SetCursorPos(before.X, before.Y);
                if (!TryWaitForCursor(point => point.X == before.X && point.Y == before.Y, TimeSpan.FromMilliseconds(50), out afterBack))
                    return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed after parent cursor restoration attempt.");
                restoredByProbe = before.X == afterBack.X && before.Y == afterBack.Y;
                if (!restoreRequested || !restoredByProbe)
                {
                    var restoreFailureText = $"ok=false appbroadcast_only={appBroadcastOnly} route=\"{route}\" reason=\"Probe could not restore the parent cursor.\" cursor_before={before} cursor_after_back={afterBack}";
                    return new ProbeResult(restoreFailureText, new JsonObject
                    {
                        ["ok"] = false,
                        ["appbroadcast_only"] = appBroadcastOnly,
                        ["route"] = route,
                        ["reason"] = "Probe could not restore the parent cursor.",
                        ["cursor_before"] = PointObject(before),
                        ["cursor_after_back"] = PointObject(afterBack),
                        ["cursor_restored_by_probe"] = restoredByProbe
                    }, IsError: true);
                }
            }

            var text = string.Join(Environment.NewLine, [
                "ok=true",
                $"appbroadcast_only={appBroadcastOnly}",
                $"cursor_before={before}",
                $"cursor_after_forward={afterForward}",
                $"cursor_after_back={afterBack}",
                $"cursor_moved_on_forward={movedOnForward}",
                $"cursor_restored_by_injector={restoredByInjector}",
                $"cursor_restored_by_probe={restoredByProbe}",
                $"background_safe={!movedOnForward}",
                $"route={route}"
            ]);
            return new ProbeResult(text, new JsonObject
            {
                ["ok"] = true,
                ["appbroadcast_only"] = appBroadcastOnly,
                ["route"] = route,
                ["cursor_before"] = PointObject(before),
                ["cursor_after_forward"] = PointObject(afterForward),
                ["cursor_after_back"] = PointObject(afterBack),
                ["cursor_position_verified"] = true,
                ["cursor_moved_on_forward"] = movedOnForward,
                ["cursor_restored_by_injector"] = restoredByInjector,
                ["cursor_restored_by_probe"] = restoredByProbe,
                ["background_safe"] = !movedOnForward
            }, IsError: false);
        }
        catch (Exception ex)
        {
            var message = $"{ex.GetType().Name}: {ex.Message}";
            return new ProbeResult(
                $"ok=false appbroadcast_only={appBroadcastOnly} route=\"{route}\" hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"",
                new JsonObject
                {
                    ["ok"] = false,
                    ["appbroadcast_only"] = appBroadcastOnly,
                    ["route"] = route,
                    ["hresult"] = $"0x{Marshal.GetHRForException(ex):X8}",
                    ["reason"] = message
                },
                IsError: true);
        }
    }

    private static ProbeResult Failure(bool appBroadcastOnly, string route, string reason)
    {
        var text = $"ok=false appbroadcast_only={appBroadcastOnly} route=\"{route}\" reason=\"{reason}\"";
        return new ProbeResult(text, new JsonObject
        {
            ["ok"] = false,
            ["appbroadcast_only"] = appBroadcastOnly,
            ["route"] = route,
            ["reason"] = reason
        }, IsError: true);
    }

    private static bool TryWaitForCursor(Func<POINT, bool> predicate, TimeSpan timeout, out POINT current)
    {
        if (!NativeMethods.GetCursorPos(out current))
            return false;

        var observed = current;
        SpinWait.SpinUntil(() =>
        {
            if (!NativeMethods.GetCursorPos(out observed))
                return false;
            return predicate(observed);
        }, timeout);
        current = observed;
        return true;
    }

    private static JsonObject PointObject(POINT point) => new()
    {
        ["x"] = point.X,
        ["y"] = point.Y
    };

    private static JsonObject DescribeAppBroadcastPlugIn(AppBroadcastPlugIn plugIn)
    {
        return new JsonObject
        {
            ["app_id"] = SafeString(() => plugIn.AppId),
            ["display_name"] = SafeString(() => plugIn.DisplayName),
            ["provider_settings_present"] = SafeObject(() => plugIn.ProviderSettings) is not null
        };
    }

    private static JsonObject DescribeGameBarServices(GameBarServices services)
    {
        var result = new JsonObject
        {
            ["session_id"] = SafeString(() => services.SessionId),
            ["target_capture_policy"] = SafeString(() => services.TargetCapturePolicy.ToString())
        };

        var target = SafeObject(() => services.TargetInfo);
        if (target is not null)
        {
            result["target_info"] = new JsonObject
            {
                ["app_id"] = SafeString(() => target.AppId),
                ["display_mode"] = SafeString(() => target.DisplayMode.ToString()),
                ["display_name"] = SafeString(() => target.DisplayName),
                ["title_id"] = SafeString(() => target.TitleId)
            };
        }

        var broadcast = SafeObject(() => services.AppBroadcastServices);
        if (broadcast is not null)
        {
            result["appbroadcast"] = new JsonObject
            {
                ["broadcast_language"] = SafeString(() => broadcast.BroadcastLanguage),
                ["broadcast_title"] = SafeString(() => broadcast.BroadcastTitle),
                ["can_capture"] = SafeBool(() => broadcast.CanCapture),
                ["capture_target_type"] = SafeString(() => broadcast.CaptureTargetType.ToString()),
                ["state"] = DescribeAppBroadcastState(SafeObject(() => broadcast.State)),
                ["user_name"] = SafeString(() => broadcast.UserName)
            };
        }

        var capture = SafeObject(() => services.AppCaptureServices);
        if (capture is not null)
        {
            var state = SafeObject(() => capture.State);
            result["appcapture"] = new JsonObject
            {
                ["state"] = state is null
                    ? null
                    : new JsonObject
                    {
                        ["is_historical_capture_enabled"] = SafeBool(() => state.IsHistoricalCaptureEnabled),
                        ["is_target_running"] = SafeBool(() => state.IsTargetRunning),
                        ["microphone_capture_state"] = SafeString(() => state.MicrophoneCaptureState.ToString()),
                        ["should_capture_microphone"] = SafeBool(() => state.ShouldCaptureMicrophone)
                    }
            };
        }

        return result;
    }

    private static JsonObject? DescribeAppBroadcastState(AppBroadcastState? state)
    {
        if (state is null)
            return null;

        return new JsonObject
        {
            ["microphone_capture_state"] = SafeString(() => state.MicrophoneCaptureState.ToString()),
            ["plug_in_state"] = SafeString(() => state.PlugInState.ToString()),
            ["should_capture_camera"] = SafeBool(() => state.ShouldCaptureCamera),
            ["should_capture_microphone"] = SafeBool(() => state.ShouldCaptureMicrophone),
            ["sign_in_state"] = SafeString(() => state.SignInState.ToString()),
            ["stream_state"] = SafeString(() => state.StreamState.ToString()),
            ["termination_reason"] = SafeString(() => state.TerminationReason.ToString()),
            ["viewer_count"] = SafeUInt(() => state.ViewerCount)
        };
    }

    private static T? SafeObject<T>(Func<T> read) where T : class
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeString(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            return $"<error 0x{Marshal.GetHRForException(ex):X8} {ex.GetType().Name}>";
        }
    }

    private static bool? SafeBool(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    private static int? SafeInt(Func<int> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    private static uint? SafeUInt(Func<uint> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetCurrentPackageFullName(out string? error)
    {
        error = null;
        var length = 0;
        var rc = GetCurrentPackageFullName(ref length, null);
        if (rc != ErrorInsufficientBuffer)
        {
            error = $"win32_error={rc}";
            return null;
        }

        var buffer = new char[length];
        rc = GetCurrentPackageFullName(ref length, buffer);
        if (rc != 0)
        {
            error = $"win32_error={rc}";
            return null;
        }

        return new string(buffer, 0, Math.Max(0, length - 1));
    }

    private static string? TryGetCurrentApplicationUserModelId(out string? error)
    {
        error = null;
        var length = 0;
        var rc = GetCurrentApplicationUserModelId(ref length, null);
        if (rc != ErrorInsufficientBuffer)
        {
            error = $"win32_error={rc}";
            return null;
        }

        var buffer = new char[length];
        rc = GetCurrentApplicationUserModelId(ref length, buffer);
        if (rc != 0)
        {
            error = $"win32_error={rc}";
            return null;
        }

        return new string(buffer, 0, Math.Max(0, length - 1));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, [Out] char[]? packageFullName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentApplicationUserModelId(ref int applicationUserModelIdLength, [Out] char[]? applicationUserModelId);

    private const int ErrorInsufficientBuffer = 122;

#if CUA_ENABLE_APPBROADCAST
    // Intentionally left as an integration seam. The provisioned broker should
    // return ActionReceipt(route: "appbroadcast.inputinjector", lane: "appbroadcast").
#endif
}
