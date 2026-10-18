using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Windows.Native;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>
/// Windowsのデスクトップオブジェクト（HDESK）を使って「ユーザーの画面を奪わないバックグラウンド作業領域」を提供する。
///
/// 仕組み:
///  1. <c>CreateDesktopW</c> で通常の Default デスクトップとは別のデスクトップを作る。
///  2. そのデスクトップ専用のワーカースレッドを 1 本作り、<c>SetThreadDesktop</c> で結び付ける。
///  3. <c>SendInput</c> / <c>SetCursorPos</c> / <c>SetForegroundWindow</c> は「呼び出しスレッドのデスクトップ」に
///     作用するため、このワーカースレッドから実行すればユーザーのマウス・キーボード・フォーカスに一切干渉しない。
///  4. アプリは <c>CreateProcessW</c> の <c>STARTUPINFO.lpDesktop</c> にデスクトップ名を指定して起動する。
///
/// 制約: GPUアクセラレーションを多用するアプリ（Electron/Chromium 系、Blenderのビューポート等）は
/// 非対話デスクトップ上で真っ白に描画されたり起動に失敗することがある。README を参照。
/// </summary>
public sealed class BackgroundDesktopService : IBackgroundDesktopService, IDisposable
{
    private const int InlineImageMaxBytes = 4 * 1024 * 1024;
    private const int MoveStepIntervalMs = 10;
    private const int RecoveryAgentReadyTimeoutMs = 5000;

    private static readonly Regex NamePattern = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, DesktopContext> _desktops = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private string? _lastRecoveryDesktopName;
    private bool _disposed;

    public Task<BackgroundDesktopInfo> CreateAsync(BackgroundDesktopCreateRequest request, CancellationToken cancellationToken = default)
    {
        var name = ValidateName(request.Name);
        var context = GetOrCreateContext(name);
        return Task.FromResult(BuildInfo(context));
    }

    public Task<IReadOnlyList<BackgroundDesktopInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var names = new List<string>();
        var station = DesktopNativeMethods.GetProcessWindowStation();
        if (station != nint.Zero)
        {
            DesktopNativeMethods.EnumDesktopsW(station, (desktopName, _) =>
            {
                names.Add(desktopName);
                return true;
            }, nint.Zero);
        }

        var inputDesktopName = GetInputDesktopName();

        var results = new List<BackgroundDesktopInfo>();
        foreach (var name in names)
        {
            if (_desktops.TryGetValue(name, out var managed))
            {
                results.Add(BuildInfo(managed, inputDesktopName));
            }
            else
            {
                results.Add(new BackgroundDesktopInfo
                {
                    Name = name,
                    IsManaged = false,
                    IsInputDesktop = string.Equals(name, inputDesktopName, StringComparison.OrdinalIgnoreCase),
                });
            }
        }

        return Task.FromResult<IReadOnlyList<BackgroundDesktopInfo>>(results);
    }

    public async Task<BackgroundDesktopActionResult> CloseAsync(BackgroundDesktopCloseRequest request, CancellationToken cancellationToken = default)
    {
        var name = ValidateName(request.DesktopName);
        if (!_desktops.TryRemove(name, out var context))
        {
            return new BackgroundDesktopActionResult
            {
                Executed = false,
                DesktopName = name,
                Reason = "このMCPサーバーが管理しているバックグラウンドデスクトップではありません。",
            };
        }

        if (request.TerminateProcesses)
        {
            foreach (var pid in context.SnapshotProcessIds())
            {
                TryKillProcess(pid);
            }

            // プロセス終了の反映を少し待つ（ウィンドウ破棄とデスクトップ解放の競合を避ける）。
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        context.Dispose();

        return new BackgroundDesktopActionResult { Executed = true, DesktopName = name };
    }

    public async Task<BackgroundDesktopLaunchResult> LaunchAsync(BackgroundDesktopLaunchRequest request, CancellationToken cancellationToken = default)
    {
        var name = ValidateName(request.DesktopName);
        var context = GetOrCreateContext(name);

        var commandLine = BuildCommandLine(request.ExecutablePath, request.Arguments);

        // Notepad やストアアプリ、Electron 系はランチャープロセスが別プロセスを起こして
        // 自身は終了することがある。起動前のウィンドウ集合を控えておき、
        // 「起動した PID のウィンドウ」だけでなく「新たに現れたウィンドウ」も検出対象にする。
        var existingHandles = EnumerateWindows(context.Handle)
            .Select(w => w.WindowHandle)
            .ToHashSet();

        var startupInfo = new DesktopNativeMethods.STARTUPINFO
        {
            lpDesktop = $"WinSta0\\{name}",
            dwFlags = DesktopNativeMethods.STARTF_USESHOWWINDOW,
            wShowWindow = DesktopNativeMethods.SW_SHOWNORMAL,
        };
        startupInfo.cb = System.Runtime.InteropServices.Marshal.SizeOf(startupInfo);

        var created = DesktopNativeMethods.CreateProcessW(
            null,
            new StringBuilder(commandLine),
            nint.Zero,
            nint.Zero,
            false,
            0,
            nint.Zero,
            string.IsNullOrWhiteSpace(request.WorkingDirectory) ? null : request.WorkingDirectory,
            ref startupInfo,
            out var processInfo);

        if (!created)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"バックグラウンドデスクトップ '{name}' でのプロセス起動に失敗しました（Win32 error {error}）: {commandLine}");
        }

        DesktopNativeMethods.CloseHandle(processInfo.hThread);
        DesktopNativeMethods.CloseHandle(processInfo.hProcess);

        context.TrackProcess(processInfo.dwProcessId);

        WindowInfo? window = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, request.WaitForWindowMs));
        do
        {
            var windows = EnumerateWindows(context.Handle);
            window = windows.FirstOrDefault(w => w.ProcessId == processInfo.dwProcessId)
                ?? windows.FirstOrDefault(w => !existingHandles.Contains(w.WindowHandle));
            if (window is not null)
            {
                // ランチャー経由で別プロセスが立ち上がった場合も終了対象として追跡する。
                if (window.ProcessId != processInfo.dwProcessId)
                {
                    context.TrackProcess(window.ProcessId);
                }

                break;
            }

            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);

        return new BackgroundDesktopLaunchResult
        {
            DesktopName = name,
            ProcessId = processInfo.dwProcessId,
            WindowAppeared = window is not null,
            Window = window,
            Note = window is null
                ? "プロセスは起動しましたが、待機時間内にウィンドウを検出できませんでした。GPU描画に強く依存するアプリ（Electron/Chromium系など）は非対話デスクトップで起動できない場合があります。"
                : null,
        };
    }

    public Task<IReadOnlyList<WindowInfo>> ListWindowsAsync(string desktopName, CancellationToken cancellationToken = default)
    {
        var context = GetContextOrThrow(desktopName);
        return Task.FromResult(EnumerateWindows(context.Handle));
    }

    public Task<ScreenCaptureResult> CaptureWindowAsync(string desktopName, long windowHandle, bool saveToFile = true, CancellationToken cancellationToken = default)
    {
        var context = GetContextOrThrow(desktopName);

        return context.RunAsync(() =>
        {
            using var bitmap = ScreenCaptureCore.CaptureWindow((nint)windowHandle);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var bytes = stream.ToArray();

            string? filePath = null;
            if (saveToFile)
            {
                UserDataPaths.EnsureDirectoriesExist();
                filePath = Path.Combine(UserDataPaths.ScreenshotsDirectory, $"bgdesktop-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.png");
                File.WriteAllBytes(filePath, bytes);
            }

            return new ScreenCaptureResult
            {
                Width = bitmap.Width,
                Height = bitmap.Height,
                Target = $"BackgroundDesktop:{context.Name}/Window:{windowHandle}",
                MimeType = "image/png",
                FilePath = filePath,
                ImageBase64 = bytes.Length <= InlineImageMaxBytes ? Convert.ToBase64String(bytes) : null,
            };
        }, cancellationToken);
    }

    public Task<BackgroundDesktopActionResult> ClickAsync(BackgroundDesktopClickRequest request, CancellationToken cancellationToken = default)
    {
        var context = GetContextOrThrow(request.DesktopName);

        return context.RunAsync(() =>
        {
            var clicks = Math.Max(1, request.ClickCount);

            if (IsOnInputDesktop(context.Name))
            {
                FocusIfRequested(request.WindowHandle);

                NativeMethods.SetCursorPos(request.X, request.Y);
                Thread.Sleep(MoveStepIntervalMs);

                var (down, up) = GetButtonFlags(request.Button);
                for (var i = 0; i < clicks; i++)
                {
                    SendMouseButtonEvent(down);
                    SendMouseButtonEvent(up);
                    if (i < clicks - 1)
                    {
                        Thread.Sleep(40);
                    }
                }
            }
            else
            {
                PostClick(request.X, request.Y, request.Button, clicks);
            }

            return new BackgroundDesktopActionResult { Executed = true, DesktopName = context.Name };
        }, cancellationToken);
    }

    public Task<BackgroundDesktopActionResult> TypeTextAsync(BackgroundDesktopTypeTextRequest request, CancellationToken cancellationToken = default)
    {
        var context = GetContextOrThrow(request.DesktopName);

        return context.RunAsync(() =>
        {
            var useSendInput = IsOnInputDesktop(context.Name);
            nint focus = 0;

            if (useSendInput)
            {
                FocusIfRequested(request.WindowHandle);
            }
            else
            {
                FocusIfRequested(request.WindowHandle);
                focus = ResolveFocusWindow(ResolveTargetWindow(request.WindowHandle));
            }

            foreach (var c in request.Text)
            {
                if (useSendInput)
                {
                    SendUnicodeChar(c);
                }
                else
                {
                    PostChar(focus, c);
                }

                if (request.IntervalMs is { } interval && interval > 0)
                {
                    Thread.Sleep(interval);
                }
            }

            return new BackgroundDesktopActionResult { Executed = true, DesktopName = context.Name };
        }, cancellationToken);
    }

    public Task<BackgroundDesktopActionResult> SendKeysAsync(BackgroundDesktopKeyRequest request, CancellationToken cancellationToken = default)
    {
        var context = GetContextOrThrow(request.DesktopName);

        return context.RunAsync(() =>
        {
            var useSendInput = IsOnInputDesktop(context.Name);

            FocusIfRequested(request.WindowHandle);
            var focus = useSendInput ? 0 : ResolveFocusWindow(ResolveTargetWindow(request.WindowHandle));

            void Down(ushort vk)
            {
                if (useSendInput)
                {
                    SendKeyEvent(vk, keyUp: false);
                }
                else
                {
                    PostKey(focus, vk, keyUp: false);
                }
            }

            void Up(ushort vk)
            {
                if (useSendInput)
                {
                    SendKeyEvent(vk, keyUp: true);
                }
                else
                {
                    PostKey(focus, vk, keyUp: true);
                }
            }

            if (request.Keys is { Count: > 0 } keys)
            {
                var codes = new ushort[keys.Count];
                for (var i = 0; i < keys.Count; i++)
                {
                    codes[i] = VirtualKeyMap.TryResolve(keys[i])
                        ?? throw new ArgumentException($"未対応のキー名です: {keys[i]}");
                }

                foreach (var code in codes)
                {
                    Down(code);
                }

                for (var i = codes.Length - 1; i >= 0; i--)
                {
                    Up(codes[i]);
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.Key))
            {
                var vk = VirtualKeyMap.TryResolve(request.Key)
                    ?? throw new ArgumentException($"未対応のキー名です: {request.Key}");

                var repeat = Math.Max(1, request.RepeatCount);
                for (var i = 0; i < repeat; i++)
                {
                    Down(vk);
                    Up(vk);
                    if (i < repeat - 1)
                    {
                        Thread.Sleep(20);
                    }
                }
            }
            else
            {
                return new BackgroundDesktopActionResult
                {
                    Executed = false,
                    DesktopName = context.Name,
                    Reason = "key または keys のいずれかを指定してください。",
                };
            }

            return new BackgroundDesktopActionResult { Executed = true, DesktopName = context.Name };
        }, cancellationToken);
    }

    public async Task<BackgroundDesktopActionResult> SwitchAsync(BackgroundDesktopSwitchRequest request, CancellationToken cancellationToken = default)
    {
        var targetName = string.IsNullOrWhiteSpace(request.DesktopName) ? "Default" : request.DesktopName.Trim();
        if (request.AutoReturnSeconds is < 1 or > DesktopRecoveryAgent.MaxRecoverySeconds)
        {
            return new BackgroundDesktopActionResult
            {
                Executed = false,
                DesktopName = targetName,
                Reason = $"autoReturnSeconds は1〜{DesktopRecoveryAgent.MaxRecoverySeconds}の範囲で指定してください。",
            };
        }

        await _switchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        nint previous = nint.Zero;
        nint target = nint.Zero;
        var ownsTarget = false;
        nint recoveryProcess = nint.Zero;
        var terminateAgentOnCleanup = true;
        try
        {
            if (!NamePattern.IsMatch(targetName))
            {
                return FailedSwitch(targetName, "デスクトップ名は英数字・ハイフン・アンダースコアのみ指定できます。");
            }

            previous = DesktopNativeMethods.OpenInputDesktop(
                0, false, DesktopNativeMethods.DESKTOP_READOBJECTS | DesktopNativeMethods.DESKTOP_SWITCHDESKTOP);
            if (previous == nint.Zero)
            {
                return FailedSwitch(targetName, "現在の入力デスクトップを開けません。Windowsのロック画面や認証用secure desktopでは切り替えできません。");
            }

            var previousName = DesktopNativeMethods.GetDesktopName(previous);
            if (string.IsNullOrWhiteSpace(previousName))
            {
                return FailedSwitch(targetName, "現在のデスクトップ名を取得できず、安全な復帰先を確保できませんでした。");
            }

            if (_desktops.TryGetValue(targetName, out var managed))
            {
                target = managed.Handle;
            }
            else
            {
                target = DesktopNativeMethods.OpenDesktopW(targetName, 0, false, DesktopNativeMethods.DESKTOP_SWITCHDESKTOP);
                ownsTarget = true;
            }

            if (target == nint.Zero)
            {
                return FailedSwitch(targetName, $"デスクトップ '{targetName}' を開けませんでした（Win32 error {Marshal.GetLastWin32Error()}）。");
            }

            if (string.Equals(previousName, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return new BackgroundDesktopActionResult
                {
                    Executed = true,
                    DesktopName = targetName,
                    Reason = "指定したデスクトップは既に入力デスクトップです。",
                };
            }

            recoveryProcess = StartRecoveryAgent(targetName, previousName, request.AutoReturnSeconds, cancellationToken, out var agentError);
            if (recoveryProcess == nint.Zero)
            {
                return FailedSwitch(targetName, $"別プロセスの復旧監視を準備できなかったため、デスクトップは切り替えていません。{agentError}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var switched = DesktopNativeMethods.SwitchDesktop(target);
            if (!switched)
            {
                var error = Marshal.GetLastWin32Error();
                DesktopNativeMethods.TerminateProcess(recoveryProcess, 1);
                terminateAgentOnCleanup = false;
                return FailedSwitch(targetName, $"SwitchDesktop に失敗しました（Win32 error {error}）。認証用secure desktopは対象外です。");
            }

            terminateAgentOnCleanup = false;
            _lastRecoveryDesktopName = previousName;
            if (!WaitForInputDesktop(targetName))
            {
                return FailedSwitch(targetName, "SwitchDesktop は成功を返しましたが、入力デスクトップを確認できません。復旧監視は引き続き有効です。");
            }

            return new BackgroundDesktopActionResult
            {
                Executed = true,
                DesktopName = targetName,
                Reason = $"復旧監視を別プロセスで起動しました。{request.AutoReturnSeconds}秒以内にEsc、Home、または単独のWindowsキーを押すと元のデスクトップへの復帰を試みます。secure desktopやWindowsのアクセス制限時は失敗することがあります。",
            };
        }
        finally
        {
            if (recoveryProcess != nint.Zero)
            {
                if (terminateAgentOnCleanup)
                {
                    DesktopNativeMethods.TerminateProcess(recoveryProcess, 1);
                }

                DesktopNativeMethods.CloseHandle(recoveryProcess);
            }

            if (previous != nint.Zero)
            {
                DesktopNativeMethods.CloseDesktop(previous);
            }

            if (ownsTarget && target != nint.Zero)
            {
                DesktopNativeMethods.CloseDesktop(target);
            }

            _switchLock.Release();
        }
    }

    public async Task<BackgroundDesktopActionResult> RecoverAsync(string? desktopName = null, CancellationToken cancellationToken = default)
    {
        await _switchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var targetName = string.IsNullOrWhiteSpace(desktopName)
                ? _lastRecoveryDesktopName ?? "Default"
                : desktopName.Trim();
            if (!NamePattern.IsMatch(targetName))
            {
                return FailedSwitch(targetName, "復旧先デスクトップ名が不正です。");
            }

            var target = DesktopNativeMethods.OpenDesktopW(targetName, 0, false, DesktopNativeMethods.DESKTOP_SWITCHDESKTOP);
            if (target == nint.Zero)
            {
                return FailedSwitch(targetName, $"復旧先デスクトップを開けません（Win32 error {Marshal.GetLastWin32Error()}）。");
            }

            try
            {
                if (!DesktopNativeMethods.SwitchDesktop(target))
                {
                    return FailedSwitch(targetName, $"SwitchDesktop に失敗しました（Win32 error {Marshal.GetLastWin32Error()}）。Windowsのロック画面や認証用secure desktopからの復帰はできません。");
                }

                if (!WaitForInputDesktop(targetName))
                {
                    return FailedSwitch(targetName, "復帰要求後に入力デスクトップを確認できませんでした。成功とは判定していません。");
                }

                _lastRecoveryDesktopName = null;
                return new BackgroundDesktopActionResult { Executed = true, DesktopName = targetName };
            }
            finally
            {
                DesktopNativeMethods.CloseDesktop(target);
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private static BackgroundDesktopActionResult FailedSwitch(string desktopName, string reason)
        => new() { Executed = false, DesktopName = desktopName, Reason = reason };

    private static bool IsInputDesktop(string expectedName)
    {
        var input = DesktopNativeMethods.OpenInputDesktop(0, false, DesktopNativeMethods.DESKTOP_READOBJECTS);
        if (input == nint.Zero)
        {
            return false;
        }

        try
        {
            return string.Equals(DesktopNativeMethods.GetDesktopName(input), expectedName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DesktopNativeMethods.CloseDesktop(input);
        }
    }

    private static bool WaitForInputDesktop(string expectedName)
    {
        var verificationTimer = Stopwatch.StartNew();
        do
        {
            if (IsInputDesktop(expectedName))
            {
                return true;
            }

            Thread.Sleep(50);
        }
        while (verificationTimer.Elapsed < TimeSpan.FromSeconds(1));

        return false;
    }

    private static nint StartRecoveryAgent(
        string targetName,
        string sourceName,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        out string error)
    {
        error = string.Empty;
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            error = "実行中のMCPプロセスのパスを取得できません。";
            return nint.Zero;
        }

        var isDotnetHost = string.Equals(
            Path.GetFileNameWithoutExtension(executablePath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);
        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (isDotnetHost && string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            error = "dotnet実行時にMCPアセンブリのパスを取得できません。";
            return nint.Zero;
        }

        var readyName = $@"Local\WcuRecoveryReady_{Guid.NewGuid():N}";
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
        var arguments = new List<string>();
        if (isDotnetHost)
        {
            arguments.Add(entryAssemblyPath!);
        }

        arguments.Add("--wcu-desktop-recovery-agent");
        arguments.Add(sourceName);
        arguments.Add(readyName);
        arguments.Add(timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var commandLine = new StringBuilder(QuoteWindowsArgument(executablePath));
        foreach (var argument in arguments)
        {
            commandLine.Append(' ').Append(QuoteWindowsArgument(argument));
        }

        var startupInfo = new DesktopNativeMethods.STARTUPINFO
        {
            cb = Marshal.SizeOf<DesktopNativeMethods.STARTUPINFO>(),
            lpDesktop = $@"WinSta0\{targetName}",
            dwFlags = DesktopNativeMethods.STARTF_USESHOWWINDOW,
            wShowWindow = DesktopNativeMethods.SW_HIDE,
        };
        var created = DesktopNativeMethods.CreateProcessW(
            executablePath,
            commandLine,
            nint.Zero,
            nint.Zero,
            false,
            DesktopNativeMethods.CREATE_NO_WINDOW | DesktopNativeMethods.CREATE_UNICODE_ENVIRONMENT,
            nint.Zero,
            Path.GetDirectoryName(executablePath),
            ref startupInfo,
            out var processInfo);
        if (!created)
        {
            error = $"復旧監視プロセスを起動できません（Win32 error {Marshal.GetLastWin32Error()}）。";
            return nint.Zero;
        }

        DesktopNativeMethods.CloseHandle(processInfo.hThread);
        var readySignal = false;
        var readyTimer = Stopwatch.StartNew();
        while (readyTimer.Elapsed < TimeSpan.FromMilliseconds(RecoveryAgentReadyTimeoutMs))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                DesktopNativeMethods.TerminateProcess(processInfo.hProcess, 1);
                DesktopNativeMethods.CloseHandle(processInfo.hProcess);
                throw;
            }

            if (ready.WaitOne(100))
            {
                readySignal = true;
                break;
            }

            if (!DesktopNativeMethods.GetExitCodeProcess(processInfo.hProcess, out var exitCode) ||
                exitCode != DesktopNativeMethods.STILL_ACTIVE)
            {
                error = "復旧監視プロセスが準備完了前に終了しました。";
                DesktopNativeMethods.TerminateProcess(processInfo.hProcess, 1);
                DesktopNativeMethods.CloseHandle(processInfo.hProcess);
                return nint.Zero;
            }
        }

        if (!readySignal)
        {
            DesktopNativeMethods.TerminateProcess(processInfo.hProcess, 1);
            DesktopNativeMethods.CloseHandle(processInfo.hProcess);
            error = "復旧監視プロセスが5秒以内にキー監視を開始できませんでした。";
            return nint.Zero;
        }

        return processInfo.hProcess;
    }

    private static string QuoteWindowsArgument(string argument)
    {
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', (backslashes * 2) + 1).Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }

        result.Append('\\', backslashes * 2).Append('"');
        return result.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var key in _desktops.Keys.ToList())
        {
            if (_desktops.TryRemove(key, out var context))
            {
                context.Dispose();
            }
        }
    }

    private static string ValidateName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (!NamePattern.IsMatch(trimmed))
        {
            throw new ArgumentException("デスクトップ名は英数字・ハイフン・アンダースコアのみ、1〜64文字で指定してください。", nameof(name));
        }

        if (string.Equals(trimmed, "Default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Winlogon", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Screen-saver", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{trimmed}' はシステム予約のデスクトップ名のため、バックグラウンド用途には使用できません。", nameof(name));
        }

        return trimmed;
    }

    private DesktopContext GetOrCreateContext(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _desktops.GetOrAdd(name, static key =>
        {
            var handle = DesktopNativeMethods.CreateDesktopW(
                key,
                nint.Zero,
                nint.Zero,
                0,
                DesktopNativeMethods.GENERIC_ALL,
                nint.Zero);

            if (handle == nint.Zero)
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"バックグラウンドデスクトップ '{key}' の作成に失敗しました（Win32 error {error}）。");
            }

            return new DesktopContext(key, handle);
        });
    }

    private DesktopContext GetContextOrThrow(string desktopName)
    {
        var name = ValidateName(desktopName);
        return _desktops.TryGetValue(name, out var context)
            ? context
            : throw new InvalidOperationException($"バックグラウンドデスクトップ '{name}' は作成されていません。先に background_desktop_create を実行してください。");
    }

    private BackgroundDesktopInfo BuildInfo(DesktopContext context, string? inputDesktopName = null)
    {
        inputDesktopName ??= GetInputDesktopName();

        return new BackgroundDesktopInfo
        {
            Name = context.Name,
            IsManaged = true,
            IsInputDesktop = string.Equals(context.Name, inputDesktopName, StringComparison.OrdinalIgnoreCase),
            WindowCount = EnumerateWindows(context.Handle).Count,
            LaunchedProcessIds = context.SnapshotProcessIds(),
        };
    }

    private static string GetInputDesktopName()
    {
        var handle = DesktopNativeMethods.OpenInputDesktop(0, false, DesktopNativeMethods.DESKTOP_READOBJECTS);
        if (handle == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            return DesktopNativeMethods.GetDesktopName(handle);
        }
        finally
        {
            DesktopNativeMethods.CloseDesktop(handle);
        }
    }

    private static IReadOnlyList<WindowInfo> EnumerateWindows(nint hDesktop)
    {
        var windows = new List<WindowInfo>();

        DesktopNativeMethods.EnumDesktopWindows(hDesktop, (hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

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

        return windows;
    }

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

    private static WindowInfo? BuildWindowInfo(nint hWnd, string title)
    {
        if (!NativeMethods.GetWindowRect(hWnd, out var rect))
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);

        var processName = string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (ArgumentException)
        {
            // 既に終了しているプロセス。空文字のままにする。
        }

        var classNameBuilder = new StringBuilder(256);
        NativeMethods.GetClassName(hWnd, classNameBuilder, classNameBuilder.Capacity);

        return new WindowInfo
        {
            WindowHandle = hWnd,
            Title = title,
            ClassName = classNameBuilder.ToString(),
            ProcessId = (int)processId,
            ProcessName = processName,
            Bounds = new ScreenRect
            {
                X = rect.Left,
                Y = rect.Top,
                Width = rect.Right - rect.Left,
                Height = rect.Bottom - rect.Top,
            },
            IsVisible = true,
            IsMinimized = NativeMethods.IsIconic(hWnd),
            IsForeground = hWnd == NativeMethods.GetForegroundWindow(),
        };
    }

    private static void FocusIfRequested(long? windowHandle)
    {
        if (windowHandle is not { } handle)
        {
            return;
        }

        var hWnd = (nint)handle;
        if (NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        }

        NativeMethods.SetForegroundWindow(hWnd);
        Thread.Sleep(80);
    }

    private static string BuildCommandLine(string executablePath, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("実行ファイルのパスを指定してください。", nameof(executablePath));
        }

        var exe = executablePath.Trim();
        var quoted = exe.StartsWith('"') ? exe : $"\"{exe}\"";
        return string.IsNullOrWhiteSpace(arguments) ? quoted : $"{quoted} {arguments}";
    }

    private static void TryKillProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // 既に終了済み。
        }
        catch (InvalidOperationException)
        {
            // 既に終了済み。
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 権限不足など。無視して続行する。
        }
    }

    private static (uint Down, uint Up) GetButtonFlags(MouseButton button) => button switch
    {
        MouseButton.Right => (NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP),
        MouseButton.Middle => (NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP),
        _ => (NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP),
    };

    private static void SendMouseButtonEvent(uint flags)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION { mi = new NativeMethods.MOUSEINPUT { dwFlags = flags } },
        };

        SendInputChecked([input]);
    }

    private static void SendUnicodeChar(char c)
    {
        var down = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT { wScan = c, dwFlags = NativeMethods.KEYEVENTF_UNICODE },
            },
        };

        var up = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT { wScan = c, dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP },
            },
        };

        SendInputChecked([down, up]);
    }

    private static void SendKeyEvent(ushort vk, bool keyUp)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                },
            },
        };

        SendInputChecked([input]);
    }

    private static void SendInputChecked(NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException($"SendInput が期待した数のイベントを送信できませんでした（{sent}/{inputs.Length}）。");
        }
    }

    // ------------------------------------------------------------------
    // メッセージベース入力（非対話デスクトップ用）
    //
    // SendInput はハードウェア入力キューへの注入であり、入力デスクトップ
    // （ユーザーが実際に見ているデスクトップ）でしか成功しない。
    // バックグラウンドデスクトップでは対象ウィンドウへ直接
    // WM_CHAR / WM_KEYDOWN / WM_LBUTTONDOWN などをポストする。
    // ------------------------------------------------------------------

    /// <summary>現在のスレッドのデスクトップが入力デスクトップかどうかを判定する。</summary>
    private static bool IsOnInputDesktop(string desktopName)
    {
        var hInput = DesktopNativeMethods.OpenInputDesktop(0, false, DesktopNativeMethods.DESKTOP_READOBJECTS);
        if (hInput == 0)
        {
            return false;
        }

        try
        {
            return string.Equals(DesktopNativeMethods.GetDesktopName(hInput), desktopName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DesktopNativeMethods.CloseDesktop(hInput);
        }
    }

    /// <summary>操作対象のトップレベルウィンドウを決める。未指定なら当該デスクトップの前面ウィンドウ。</summary>
    private static nint ResolveTargetWindow(long? windowHandle)
    {
        if (windowHandle is { } handle && handle != 0)
        {
            return (nint)handle;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0)
        {
            throw new InvalidOperationException(
                "操作対象のウィンドウを特定できませんでした。windowHandle を指定してください。");
        }

        return foreground;
    }

    /// <summary>ウィンドウが属するスレッドのフォーカス中コントロールを取得する（取得できなければ元のウィンドウ）。</summary>
    private static nint ResolveFocusWindow(nint hWnd)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
        if (threadId == 0)
        {
            return hWnd;
        }

        var info = new DesktopNativeMethods.GUITHREADINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<DesktopNativeMethods.GUITHREADINFO>(),
        };

        if (DesktopNativeMethods.GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != 0)
        {
            return info.hwndFocus;
        }

        return hWnd;
    }

    private static void PostChar(nint hWnd, char c)
    {
        // 改行は WM_CHAR では CR として扱う。
        var value = c == '\n' ? '\r' : c;
        if (!DesktopNativeMethods.PostMessageW(hWnd, DesktopNativeMethods.WM_CHAR, value, 1))
        {
            throw new InvalidOperationException(
                $"ウィンドウへの文字入力に失敗しました（Win32 エラー {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）。");
        }
    }

    private static void PostKey(nint hWnd, ushort virtualKey, bool keyUp)
    {
        // Alt 系は WM_SYSKEYDOWN / WM_SYSKEYUP を使う。
        var isSystemKey = virtualKey == DesktopNativeMethods.VK_MENU;
        var message = keyUp
            ? (isSystemKey ? DesktopNativeMethods.WM_SYSKEYUP : DesktopNativeMethods.WM_KEYUP)
            : (isSystemKey ? DesktopNativeMethods.WM_SYSKEYDOWN : DesktopNativeMethods.WM_KEYDOWN);

        if (!DesktopNativeMethods.PostMessageW(hWnd, message, virtualKey, DesktopNativeMethods.MakeKeyLParam(virtualKey, keyUp)))
        {
            throw new InvalidOperationException(
                $"ウィンドウへのキー送信に失敗しました（Win32 エラー {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）。");
        }
    }

    private static void PostClick(int x, int y, MouseButton button, int clickCount)
    {
        var point = new DesktopNativeMethods.POINT { X = x, Y = y };
        var target = DesktopNativeMethods.WindowFromPoint(point);
        if (target == 0)
        {
            throw new InvalidOperationException(
                $"座標 ({x}, {y}) にウィンドウが見つかりませんでした。このデスクトップ上の座標を指定してください。");
        }

        var client = point;
        DesktopNativeMethods.ScreenToClient(target, ref client);
        var lParam = DesktopNativeMethods.MakeCoordinateLParam(client.X, client.Y);

        var (downMessage, upMessage, keyFlag) = button switch
        {
            MouseButton.Right => (DesktopNativeMethods.WM_RBUTTONDOWN, DesktopNativeMethods.WM_RBUTTONUP, DesktopNativeMethods.MK_RBUTTON),
            MouseButton.Middle => (DesktopNativeMethods.WM_MBUTTONDOWN, DesktopNativeMethods.WM_MBUTTONUP, DesktopNativeMethods.MK_MBUTTON),
            _ => (DesktopNativeMethods.WM_LBUTTONDOWN, DesktopNativeMethods.WM_LBUTTONUP, DesktopNativeMethods.MK_LBUTTON),
        };

        DesktopNativeMethods.PostMessageW(target, DesktopNativeMethods.WM_MOUSEMOVE, 0, lParam);

        for (var i = 0; i < clickCount; i++)
        {
            if (!DesktopNativeMethods.PostMessageW(target, downMessage, keyFlag, lParam))
            {
                throw new InvalidOperationException(
                    $"ウィンドウへのクリック送信に失敗しました（Win32 エラー {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）。");
            }

            DesktopNativeMethods.PostMessageW(target, upMessage, 0, lParam);

            if (i < clickCount - 1)
            {
                Thread.Sleep(40);
            }
        }
    }

    /// <summary>1つのバックグラウンドデスクトップと、それに結び付いた専用ワーカースレッド。</summary>
    private sealed class DesktopContext : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly List<int> _processIds = [];
        private readonly Thread _worker;
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        public DesktopContext(string name, nint handle)
        {
            Name = name;
            Handle = handle;

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"bg-desktop-{name}",
            };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();

            // SetThreadDesktop の成否をここで確定させる（失敗なら例外が伝播する）。
            _ready.Task.GetAwaiter().GetResult();
        }

        public string Name { get; }

        public nint Handle { get; }

        public void TrackProcess(int processId)
        {
            lock (_processIds)
            {
                _processIds.Add(processId);
            }
        }

        public IReadOnlyList<int> SnapshotProcessIds()
        {
            lock (_processIds)
            {
                return _processIds.ToArray();
            }
        }

        public Task<T> RunAsync<T>(Func<T> func, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try
                {
                    tcs.TrySetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, CancellationToken.None);

            return tcs.Task.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();
            _worker.Join(TimeSpan.FromSeconds(3));
            _queue.Dispose();

            if (Handle != nint.Zero)
            {
                DesktopNativeMethods.CloseDesktop(Handle);
            }
        }

        private void WorkerLoop()
        {
            if (!DesktopNativeMethods.SetThreadDesktop(Handle))
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                _ready.TrySetException(new InvalidOperationException(
                    $"バックグラウンドデスクトップ '{Name}' へのスレッド結び付け（SetThreadDesktop）に失敗しました（Win32 error {error}）。"));
                return;
            }

            _ready.TrySetResult(true);

            foreach (var work in _queue.GetConsumingEnumerable())
            {
                work();
            }
        }
    }
}
