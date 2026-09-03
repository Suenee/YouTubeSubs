using System.Runtime.InteropServices;

namespace YouTubeSubs;

internal static class TaskbarProgress
{
    private const uint NoProgress = 0x0;
    private const uint Indeterminate = 0x1;
    private const uint Normal = 0x2;
    private const uint Error = 0x4;

    private static readonly object Sync = new();
    private static ITaskbarList3? _taskbar;

    public static void SetProgress(Form owner, double percent)
    {
        if (!OperatingSystem.IsWindows() || owner.IsDisposed) return;
        try
        {
            var taskbar = GetTaskbar();
            var handle = owner.Handle;
            taskbar.SetProgressState(handle, Normal);
            var value = (ulong)Math.Clamp((int)Math.Round(percent * 10), 0, 1000);
            taskbar.SetProgressValue(handle, value, 1000);
        }
        catch { }
    }

    public static void SetIndeterminate(Form owner)
    {
        if (!OperatingSystem.IsWindows() || owner.IsDisposed) return;
        try { GetTaskbar().SetProgressState(owner.Handle, Indeterminate); } catch { }
    }

    public static void SetError(Form owner)
    {
        if (!OperatingSystem.IsWindows() || owner.IsDisposed) return;
        try { GetTaskbar().SetProgressState(owner.Handle, Error); } catch { }
    }

    public static void Clear(Form owner)
    {
        if (!OperatingSystem.IsWindows() || owner.IsDisposed || !owner.IsHandleCreated) return;
        try { GetTaskbar().SetProgressState(owner.Handle, NoProgress); } catch { }
    }

    private static ITaskbarList3 GetTaskbar()
    {
        lock (Sync)
        {
            if (_taskbar is not null) return _taskbar;
            _taskbar = (ITaskbarList3)new CTaskbarList();
            _taskbar.HrInit();
            return _taskbar;
        }
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class CTaskbarList { }

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
