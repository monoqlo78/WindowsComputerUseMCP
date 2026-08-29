using System.Runtime.InteropServices;
using System.Text;

namespace WindowsComputerUseMCP.DesktopLauncher;

/// <summary>
/// 指定したバックグラウンドデスクトップ上で任意のコマンドを起動する薄いラッパー。
///
/// 目的:
///   Playwright MCP のようなブラウザ自動化サーバーは、既定ではユーザーが今見ている
///   デスクトップ (Default) 上にブラウザウィンドウを開くため、操作のたびに前面を奪う。
///   Windows の子プロセスは STARTUPINFO.lpDesktop で指定されたデスクトップを引き継ぐので、
///   MCP サーバー本体をバックグラウンドデスクトップ上で起動すれば、そこから派生する
///   ブラウザプロセスもすべて同じデスクトップに乗る。結果としてユーザーの画面・
///   フォーカス・カーソルを一切奪わずにヘッドあり (headed) のブラウザ操作ができる。
///
/// 重要:
///   デスクトップの割り当てはウィンドウと入力キューにのみ影響し、標準入出力には影響しない。
///   そのため標準ハンドルをそのまま継承させれば JSON-RPC (stdio) は素通しで流れ、
///   ラッパーによるコピーやバッファリングのオーバーヘッドは発生しない。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            // stdout は JSON-RPC 専用なので、診断は必ず stderr に出す。
            Console.Error.WriteLine($"[wcu-desktop-launcher] {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var options = LauncherOptions.Parse(args);
        if (options is null)
        {
            Console.Error.WriteLine(LauncherOptions.Usage);
            return 2;
        }

        // デスクトップは冪等に作成する。既に存在する場合は既存のものが返る。
        var hDesktop = NativeMethods.CreateDesktopW(
            options.DesktopName, nint.Zero, nint.Zero, 0, NativeMethods.GENERIC_ALL, nint.Zero);

        if (hDesktop == nint.Zero)
        {
            throw new InvalidOperationException(
                $"デスクトップ '{options.DesktopName}' を作成できませんでした。" +
                $"Win32Error={Marshal.GetLastWin32Error()}");
        }

        // 子プロセスが終了しなかった場合に確実に道連れにするためのジョブオブジェクト。
        var hJob = CreateKillOnCloseJob();

        try
        {
            var pi = StartOnDesktop(options, hDesktop);

            if (hJob != nint.Zero)
            {
                // 失敗しても致命的ではない（後始末が甘くなるだけ）ので戻り値は無視する。
                NativeMethods.AssignProcessToJobObject(hJob, pi.hProcess);
            }

            Console.Error.WriteLine(
                $"[wcu-desktop-launcher] desktop='{options.DesktopName}' pid={pi.dwProcessId} で起動しました。");

            try
            {
                NativeMethods.WaitForSingleObject(pi.hProcess, NativeMethods.INFINITE);
                return NativeMethods.GetExitCodeProcess(pi.hProcess, out var exitCode) ? (int)exitCode : 0;
            }
            finally
            {
                NativeMethods.CloseHandle(pi.hThread);
                NativeMethods.CloseHandle(pi.hProcess);
            }
        }
        finally
        {
            // ジョブハンドルを閉じた時点で、残っている子プロセスは KILL_ON_JOB_CLOSE で終了する。
            if (hJob != nint.Zero)
            {
                NativeMethods.CloseHandle(hJob);
            }

            NativeMethods.CloseDesktop(hDesktop);
        }
    }

    private static NativeMethods.PROCESS_INFORMATION StartOnDesktop(LauncherOptions options, nint hDesktop)
    {
        _ = hDesktop;

        var stdIn = NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE);
        var stdOut = NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE);
        var stdErr = NativeMethods.GetStdHandle(NativeMethods.STD_ERROR_HANDLE);

        // MCP クライアントから渡されたパイプが継承不可の場合があるため、明示的に継承可能にする。
        MakeInheritable(stdIn);
        MakeInheritable(stdOut);
        MakeInheritable(stdErr);

        var si = new NativeMethods.STARTUPINFO
        {
            cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
            lpDesktop = options.DesktopName,
            dwFlags = NativeMethods.STARTF_USESTDHANDLES,
            hStdInput = stdIn,
            hStdOutput = stdOut,
            hStdError = stdErr,
        };

        var commandLine = new StringBuilder(options.CommandLine);

        var created = NativeMethods.CreateProcessW(
            lpApplicationName: null,
            lpCommandLine: commandLine,
            lpProcessAttributes: nint.Zero,
            lpThreadAttributes: nint.Zero,
            bInheritHandles: true,
            dwCreationFlags: NativeMethods.CREATE_NO_WINDOW | NativeMethods.CREATE_UNICODE_ENVIRONMENT,
            lpEnvironment: nint.Zero,
            lpCurrentDirectory: options.WorkingDirectory,
            lpStartupInfo: ref si,
            lpProcessInformation: out var pi);

        if (!created)
        {
            throw new InvalidOperationException(
                $"コマンドを起動できませんでした: {options.CommandLine} " +
                $"(Win32Error={Marshal.GetLastWin32Error()})");
        }

        return pi;
    }

    private static void MakeInheritable(nint handle)
    {
        if (handle == nint.Zero || handle == -1)
        {
            return;
        }

        NativeMethods.SetHandleInformation(
            handle, NativeMethods.HANDLE_FLAG_INHERIT, NativeMethods.HANDLE_FLAG_INHERIT);
    }

    /// <summary>
    /// ランチャーが終了したら子プロセスツリーごと終了させるジョブオブジェクトを作る。
    /// これが無いと、MCP クライアント側が切断したときにブラウザだけが取り残される。
    /// </summary>
    private static nint CreateKillOnCloseJob()
    {
        var hJob = NativeMethods.CreateJobObjectW(nint.Zero, null);
        if (hJob == nint.Zero)
        {
            return nint.Zero;
        }

        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };

        var size = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!NativeMethods.SetInformationJobObject(
                    hJob, NativeMethods.JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                NativeMethods.CloseHandle(hJob);
                return nint.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return hJob;
    }
}
