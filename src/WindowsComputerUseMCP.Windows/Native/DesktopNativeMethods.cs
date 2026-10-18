using System.Runtime.InteropServices;
using System.Text;

namespace WindowsComputerUseMCP.Windows.Native;

/// <summary>
/// Windowsの「デスクトップオブジェクト」(HDESK) 関連のP/Invoke。
///
/// Windowsは1つのウィンドウステーション(WinSta0)配下に複数のデスクトップを持てる。既定の対話デスクトップ
/// (Default)とは別に独自のデスクトップを作り、そこでアプリを起動すれば、ユーザーの目の前の画面を
/// 一切奪わずにGUIアプリを操作できる（＝バックグラウンド実行）。SendInputは「呼び出しスレッドが
/// 結び付いているデスクトップ」の入力キューに送られるため、SetThreadDesktopでバックグラウンド
/// デスクトップに結び付けたスレッドから入力すれば、ユーザーのマウス/キーボードとは干渉しない。
/// </summary>
internal static class DesktopNativeMethods
{
    public const uint GENERIC_ALL = 0x10000000;

    public const uint DESKTOP_READOBJECTS = 0x0001;
    public const uint DESKTOP_CREATEWINDOW = 0x0002;
    public const uint DESKTOP_CREATEMENU = 0x0004;
    public const uint DESKTOP_HOOKCONTROL = 0x0008;
    public const uint DESKTOP_JOURNALRECORD = 0x0010;
    public const uint DESKTOP_JOURNALPLAYBACK = 0x0020;
    public const uint DESKTOP_ENUMERATE = 0x0040;
    public const uint DESKTOP_WRITEOBJECTS = 0x0080;
    public const uint DESKTOP_SWITCHDESKTOP = 0x0100;

    public const uint DESKTOP_ALL =
        DESKTOP_READOBJECTS | DESKTOP_CREATEWINDOW | DESKTOP_CREATEMENU | DESKTOP_HOOKCONTROL |
        DESKTOP_JOURNALRECORD | DESKTOP_JOURNALPLAYBACK | DESKTOP_ENUMERATE | DESKTOP_WRITEOBJECTS |
        DESKTOP_SWITCHDESKTOP;

    public const int UOI_NAME = 2;
    public const int WH_KEYBOARD_LL = 13;
    public const int HC_ACTION = 0;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_RECOVERY_TRIGGER = 0x8001;
    public const uint PM_NOREMOVE = 0x0000;
    public const uint VK_ESCAPE = 0x1B;
    public const uint VK_HOME = 0x24;
    public const uint VK_SHIFT = 0x10;
    public const uint VK_CONTROL = 0x11;
    public const uint VK_LWIN = 0x5B;
    public const uint VK_RWIN = 0x5C;
    public const uint STILL_ACTIVE = 259;
    public const uint CREATE_NO_WINDOW = 0x08000000;
    public const ushort SW_HIDE = 0;

    public const uint CREATE_NEW_CONSOLE = 0x00000010;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    public const uint STARTF_USESHOWWINDOW = 0x00000001;
    public const ushort SW_SHOWNORMAL = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateDesktopW(
        string lpszDesktop,
        nint lpszDevice,
        nint pDevmode,
        int dwFlags,
        uint dwDesiredAccess,
        nint lpsa);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint OpenDesktopW(string lpszDesktop, int dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(nint hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetThreadDesktop(nint hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetThreadDesktop(uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SwitchDesktop(nint hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint OpenInputDesktop(int dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetProcessWindowStation();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetUserObjectInformationW(
        nint hObj,
        int nIndex,
        [Out] StringBuilder pvInfo,
        uint nLength,
        out uint lpnLengthNeeded);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumDesktopWindows(nint hDesktop, NativeMethods.EnumWindowsProc lpfn, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool EnumDesktopsW(nint hwinsta, EnumDesktopProc lpEnumFunc, nint lParam);

    public delegate bool EnumDesktopProc([MarshalAs(UnmanagedType.LPWStr)] string lpszDesktop, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder? lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookExW(int idHook, LowLevelKeyboardProc callback, nint hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hook, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetMessageW(out NativeMethods.MSG message, nint hWnd, uint min, uint max);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool PeekMessageW(out NativeMethods.MSG message, nint hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nuint SetTimer(nint hWnd, nuint idEvent, uint elapse, nint timerProc);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool KillTimer(nint hWnd, nuint idEvent);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(nint process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(nint hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    /// <summary>指定したデスクトップハンドルの名前を取得する。</summary>
    public static string GetDesktopName(nint hDesktop)
    {
        var builder = new StringBuilder(256);
        return GetUserObjectInformationW(hDesktop, UOI_NAME, builder, (uint)builder.Capacity, out _)
            ? builder.ToString()
            : string.Empty;
    }

    // ------------------------------------------------------------------
    // メッセージベース入力
    //
    // SendInput はハードウェア入力キューへの注入であり、「入力デスクトップ」
    // （＝ユーザーが実際に見ているデスクトップ）でしか機能しない。
    // 非対話デスクトップ上のアプリを操作するには、ウィンドウメッセージを
    // 直接ポストする必要がある。
    // ------------------------------------------------------------------

    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP = 0x0208;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;

    public const nint MK_LBUTTON = 0x0001;
    public const nint MK_RBUTTON = 0x0002;
    public const nint MK_MBUTTON = 0x0010;

    public const uint MAPVK_VK_TO_VSC = 0;
    public const uint VK_MENU = 0x12;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    /// <summary>lParam に埋め込むクライアント座標を作る。</summary>
    public static nint MakeCoordinateLParam(int x, int y)
        => (nint)((y << 16) | (x & 0xFFFF));

    /// <summary>WM_KEYDOWN / WM_KEYUP 用の lParam を作る。</summary>
    public static nint MakeKeyLParam(ushort virtualKey, bool keyUp)
    {
        var scan = MapVirtualKeyW(virtualKey, MAPVK_VK_TO_VSC) & 0xFF;
        var lParam = (long)(1 | (scan << 16));
        if (keyUp)
        {
            lParam |= (1L << 30) | (1L << 31);
        }

        return (nint)lParam;
    }
}
