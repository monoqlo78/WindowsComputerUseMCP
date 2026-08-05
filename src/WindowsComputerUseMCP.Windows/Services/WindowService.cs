using System.Diagnostics;
using System.Text;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Windows.Native;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>Win32 API を用いたトップレベルウィンドウの列挙・前面化サービス。</summary>
public sealed class WindowService : IWindowService
{
    public Task<IReadOnlyList<WindowInfo>> ListWindowsAsync(CancellationToken cancellationToken = default)
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            // オーナーを持つウィンドウ（ツールチップ等の子的ウィンドウ）はトップレベル操作対象から除外する。
            if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != nint.Zero)
            {
                return true;
            }

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrEmpty(title))
            {
                return true;
            }

            var info = BuildWindowInfo(hWnd, title);
            if (info is not null)
            {
                windows.Add(info);
            }

            return true;
        }, nint.Zero);

        return Task.FromResult<IReadOnlyList<WindowInfo>>(windows);
    }

    public Task<WindowInfo?> GetWindowAsync(long windowHandle, CancellationToken cancellationToken = default)
    {
        var hWnd = (nint)windowHandle;
        if (!NativeMethods.IsWindowVisible(hWnd))
        {
            return Task.FromResult<WindowInfo?>(null);
        }

        var title = GetWindowTitle(hWnd);
        return Task.FromResult(BuildWindowInfo(hWnd, title));
    }

    public Task<WindowInfo?> GetForegroundWindowAsync(CancellationToken cancellationToken = default)
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == nint.Zero)
        {
            return Task.FromResult<WindowInfo?>(null);
        }

        var title = GetWindowTitle(hWnd);
        return Task.FromResult(BuildWindowInfo(hWnd, title));
    }

    public Task<WindowFocusResult> FocusWindowAsync(WindowFocusRequest request, CancellationToken cancellationToken = default)
    {
        nint targetHandle;

        if (request.WindowHandle is { } handleValue)
        {
            targetHandle = (nint)handleValue;
        }
        else if (!string.IsNullOrEmpty(request.Title))
        {
            var candidates = FindByTitle(request.Title, request.TitleMatchMode);
            if (candidates.Count == 0)
            {
                return Task.FromResult(new WindowFocusResult { Focused = false, Window = null });
            }

            if (candidates.Count > 1)
            {
                return Task.FromResult(new WindowFocusResult { Focused = false, Window = null, Candidates = candidates });
            }

            targetHandle = (nint)candidates[0].WindowHandle;
        }
        else
        {
            return Task.FromResult(new WindowFocusResult { Focused = false, Window = null });
        }

        if (!NativeMethods.IsWindowVisible(targetHandle))
        {
            return Task.FromResult(new WindowFocusResult { Focused = false, Window = null });
        }

        if (NativeMethods.IsIconic(targetHandle))
        {
            NativeMethods.ShowWindow(targetHandle, NativeMethods.SW_RESTORE);
        }

        var focused = NativeMethods.SetForegroundWindow(targetHandle);
        var title2 = GetWindowTitle(targetHandle);
        var info = BuildWindowInfo(targetHandle, title2);

        return Task.FromResult(new WindowFocusResult { Focused = focused, Window = info });
    }

    private static List<WindowInfo> FindByTitle(string title, MatchMode matchMode)
    {
        var results = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            var windowTitle = GetWindowTitle(hWnd);
            if (string.IsNullOrEmpty(windowTitle) || !MatchesTitle(windowTitle, title, matchMode))
            {
                return true;
            }

            var info = BuildWindowInfo(hWnd, windowTitle);
            if (info is not null)
            {
                results.Add(info);
            }

            return true;
        }, nint.Zero);

        return results;
    }

    private static bool MatchesTitle(string candidate, string pattern, MatchMode matchMode) => matchMode switch
    {
        MatchMode.Exact => string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase),
        MatchMode.StartsWith => candidate.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
        MatchMode.Regex => System.Text.RegularExpressions.Regex.IsMatch(candidate, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        _ => candidate.Contains(pattern, StringComparison.OrdinalIgnoreCase),
    };

    private static string GetWindowTitle(nint hWnd)
    {
        var length = NativeMethods.GetWindowTextLength(hWnd);
        if (length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassName(nint hWnd)
    {
        var builder = new StringBuilder(256);
        NativeMethods.GetClassName(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static WindowInfo? BuildWindowInfo(nint hWnd, string title)
    {
        if (!NativeMethods.GetWindowRect(hWnd, out var rect))
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);

        string processName = string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (ArgumentException)
        {
            // プロセスが既に終了している場合など。空文字のまま返す。
        }

        return new WindowInfo
        {
            WindowHandle = hWnd,
            Title = title,
            ClassName = GetClassName(hWnd),
            ProcessId = (int)processId,
            ProcessName = processName,
            Bounds = new ScreenRect
            {
                X = rect.Left,
                Y = rect.Top,
                Width = rect.Right - rect.Left,
                Height = rect.Bottom - rect.Top,
            },
            IsVisible = NativeMethods.IsWindowVisible(hWnd),
            IsMinimized = NativeMethods.IsIconic(hWnd),
            IsForeground = hWnd == NativeMethods.GetForegroundWindow(),
        };
    }
}
