using System.Runtime.InteropServices;
using Accessibility;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Uia;

internal static class Ia2ComQuery
{
    public static readonly Guid Accessible2 = new("E89F726E-C4F4-4C19-BB19-B647D7FA8478");
    public static readonly Guid AccessibleAction = new("B70D9F59-3B5A-4DBA-AB9E-22012F607DF5");
    public static readonly Guid AccessibleEditableText = new("A59AA09A-7011-4b65-939D-32B1FB5547E3");
    public static readonly Guid AccessibleText = new("24FD2FFB-3AAD-4a08-8335-A3AD89C0FB4B");

    public static IAccessible? AsAccessible(object? value)
    {
        if (value is null)
            return null;

        if (value is IAccessible accessible)
            return accessible;

        if (!Marshal.IsComObject(value))
            return null;

        return QueryInterface<IAccessible>(value, NativeMethods.IID_IAccessible);
    }

    public static T? AsIa2Service<T>(object? value, Guid iid)
        where T : class
    {
        if (value is null)
            return null;

        if (value is T typed)
            return typed;

        if (!Marshal.IsComObject(value))
            return null;

        var result = QueryInterface<T>(value, iid);
        if (result is not null)
            return result;

        if (value is not IServiceProvider serviceProvider)
            return null;

        var service = Accessible2;
        var requested = iid;
        if (serviceProvider.QueryService(ref service, ref requested, out var ptr) != 0 || ptr == IntPtr.Zero)
            return null;

        return ObjectForPointer<T>(ptr);
    }

    private static T? QueryInterface<T>(object value, Guid iid)
        where T : class
    {
        IntPtr unknown = IntPtr.Zero;
        IntPtr ptr = IntPtr.Zero;
        try
        {
            unknown = Marshal.GetIUnknownForObject(value);
            if (Marshal.QueryInterface(unknown, in iid, out ptr) != 0 || ptr == IntPtr.Zero)
                return null;

            var result = ObjectForPointer<T>(ptr);
            ptr = IntPtr.Zero;
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (ptr != IntPtr.Zero)
                Marshal.Release(ptr);
            if (unknown != IntPtr.Zero)
                Marshal.Release(unknown);
        }
    }

    private static T? ObjectForPointer<T>(IntPtr ptr)
        where T : class
    {
        try
        {
            return Marshal.GetObjectForIUnknown(ptr) as T;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }
}
