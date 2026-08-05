using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Windows.Native;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>ディスプレイモニターの列挙・仮想スクリーン範囲の取得を行うヘルパー。</summary>
public static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<(nint Handle, NativeMethods.RECT Rect)>();

        NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, (nint hMonitor, nint _, ref NativeMethods.RECT rect, nint _) =>
        {
            monitors.Add((hMonitor, rect));
            return true;
        }, nint.Zero);

        var results = new List<MonitorInfo>(monitors.Count);
        for (var i = 0; i < monitors.Count; i++)
        {
            var (handle, rect) = monitors[i];
            var info = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            var isPrimary = false;
            if (NativeMethods.GetMonitorInfo(handle, ref info))
            {
                isPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
            }

            var dpiScale = 1.0;
            if (NativeMethods.GetDpiForMonitor(handle, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                dpiScale = dpiX / 96.0;
            }

            results.Add(new MonitorInfo
            {
                Index = i,
                Bounds = ToScreenRect(rect),
                WorkArea = ToScreenRect(info.rcWork),
                IsPrimary = isPrimary,
                DpiScale = dpiScale,
            });
        }

        return results;
    }

    public static ScreenRect GetVirtualScreenBounds() => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

    private static ScreenRect ToScreenRect(NativeMethods.RECT rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}
