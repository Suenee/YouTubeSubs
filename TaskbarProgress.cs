using System.Runtime.InteropServices;

namespace YouTubeSubs;

internal static class TaskbarProgress
{
    private const uint NoProgress = 0x0;
    private const uint Normal = 0x2;
    private const uint Error = 0x4;
    private const uint ClsctxInprocServer = 0x1;
    private static readonly Guid TaskbarListClsid = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private static readonly Guid TaskbarList3Iid = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84");
    private static readonly object Sync = new();
    private static ITaskbarList3? _taskbar;
    private static bool _failureLogged;

    public static void SetProgress(Form owner, double percent)
    {
        var target = ResolveTaskbarWindow(owner);
        if (target is null) return;
        try
        {
            var taskbar = GetTaskbar();
            var handle = target.Handle;
            taskbar.SetProgressState(handle, Normal);
            var value = (ulong)Math.Clamp((int)Math.Round(percent * 10), 0, 1000);
            taskbar.SetProgressValue(handle, value, 1000);
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    public static void SetError(Form owner)
    {
        var target = ResolveTaskbarWindow(owner);
        if (target is null) return;
        try { GetTaskbar().SetProgressState(target.Handle, Error); }
        catch (Exception ex) { LogFailure(ex); }
    }

    public static void Clear(Form owner)
    {
        var target = ResolveTaskbarWindow(owner, requireHandle: true);
        if (target is null) return;
        try { GetTaskbar().SetProgressState(target.Handle, NoProgress); }
        catch (Exception ex) { LogFailure(ex); }
    }

    private static Form? ResolveTaskbarWindow(Form owner, bool requireHandle = false)
    {
        if (!OperatingSystem.IsWindows() || owner.IsDisposed) return null;
        var target = owner;
        while (target.Owner is Form parent && !parent.IsDisposed) target = parent;
        if (!target.ShowInTaskbar)
        {
            target = Application.OpenForms.Cast<Form>().FirstOrDefault(form => !form.IsDisposed && form.ShowInTaskbar) ?? target;
        }
        if (requireHandle && !target.IsHandleCreated) return null;
        return target;
    }

    private static ITaskbarList3 GetTaskbar()
    {
        lock (Sync)
        {
            if (_taskbar is not null) return _taskbar;
            var clsid = TaskbarListClsid;
            var iid = TaskbarList3Iid;
            var hr = CoCreateInstance(ref clsid, nint.Zero, ClsctxInprocServer, ref iid, out var instance);
            Marshal.ThrowExceptionForHR(hr);
            _taskbar = (ITaskbarList3)instance;
            _taskbar.HrInit();
            return _taskbar;
        }
    }

    private static void LogFailure(Exception ex)
    {
        if (_failureLogged) return;
        _failureLogged = true;
        AppLog.Exception("taskbar progress", ex);
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(nint hwnd);
        void DeleteTab(nint hwnd);
        void ActivateTab(nint hwnd);
        void SetActiveAlt(nint hwnd);
        void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(nint hwnd, ulong completed, ulong total);
        void SetProgressState(nint hwnd, uint flags);
    }
}
