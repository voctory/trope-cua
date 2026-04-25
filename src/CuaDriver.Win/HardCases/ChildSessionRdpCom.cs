using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CuaDriver.Win.HardCases;

internal static class ChildSessionRdpCom
{
    private const int DispatchPropertyPut = -3;
    private const ushort DispatchPropertyPutFlag = 4;

    public static string ResolveRdpClientClsid()
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

    public static void TryConfigureAdvancedSettings(object ocx)
    {
        foreach (var propertyName in new[] { "AdvancedSettings9", "AdvancedSettings8", "AdvancedSettings7", "AdvancedSettings6", "AdvancedSettings5", "AdvancedSettings4", "AdvancedSettings3", "AdvancedSettings2", "AdvancedSettings" })
        {
            var settings = TryGetProperty(ocx, propertyName);
            if (settings is null)
                continue;

            TrySetProperty(settings, "EnableCredSspSupport", true);
            TrySetProperty(settings, "SmartSizing", true);
            TrySetProperty(settings, "RedirectClipboard", false);
            TrySetProperty(settings, "RedirectDrives", false);
            return;
        }
    }

    public static void SetProperty(object target, string name, object value)
        => target.GetType().InvokeMember(
            name,
            BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
            null,
            target,
            [value],
            CultureInfo.InvariantCulture);

    public static void TrySetProperty(object target, string name, object value)
    {
        try
        {
            SetProperty(target, name, value);
        }
        catch
        {
            // Optional RDP ActiveX settings vary by installed control version.
        }
    }

    public static void InvokeMethod(object target, string name)
        => target.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
            null,
            target,
            null,
            CultureInfo.InvariantCulture);

    public static void SetExtendedProperty(object ocx, string name, object value)
    {
        if (!TrySetExtendedProperty(ocx, name, value))
            throw new InvalidOperationException($"RDP ActiveX control does not expose IMsRdpExtendedSettings.Property({name}).");
    }

    public static bool TrySetExtendedProperty(object ocx, string name, object value)
    {
        var iid = new Guid("302D8188-0052-4807-806A-362B628F9AC5");
        var unknown = Marshal.GetIUnknownForObject(ocx);
        try
        {
            var hr = Marshal.QueryInterface(unknown, in iid, out var extended);
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

    private static object? TryGetProperty(object target, string name)
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
                _ = VariantClear(args);
                _ = VariantClear(IntPtr.Add(args, variantSize));
                Marshal.FreeCoTaskMem(args);
                Marshal.FreeCoTaskMem(namedArgs);
            }
        }
        catch
        {
            return false;
        }
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

    [DllImport("oleaut32.dll")]
    private static extern int VariantClear(IntPtr pvarg);
}
