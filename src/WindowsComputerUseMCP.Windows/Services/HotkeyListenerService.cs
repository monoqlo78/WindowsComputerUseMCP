using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Windows.Native;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>
/// appsettings.json の <c>Safety.EmergencyStopHotkey</c>（既定 "Ctrl+Shift+F12"）をOSレベルのグローバルホットキーとして
/// 登録し、押下時に <see cref="IEmergencyStopService.Activate"/> を呼び出すホストサービス。
/// メッセージポンプが必要な RegisterHotKey/WM_HOTKEY のため、専用スレッド上にメッセージ専用ウィンドウ
/// (HWND_MESSAGE) を作成してGetMessageループを回す。ControlPanelが起動していなくても、Serverプロセスが
/// 生存している限りホットキーは機能する。
/// </summary>
public sealed class HotkeyListenerService(
    IEmergencyStopService emergencyStop,
    IOptionsMonitor<WindowsComputerUseMcpOptions> options,
    ILogger<HotkeyListenerService> logger) : BackgroundService
{
    private const int HotkeyId = 1;
    private const string WindowClassName = "WindowsComputerUseMCP_HotkeyWindow";

    private Thread? _thread;
    private uint _threadId;

    // GC対策: デリゲートをフィールドに保持してGCされないようにする。
    private readonly NativeMethods.WndProc _wndProcDelegate = NativeMethods.DefWindowProc;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _thread = new Thread(() => RunMessageLoop(stoppingToken))
        {
            IsBackground = true,
            Name = "WCUMCP-HotkeyListener",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        stoppingToken.Register(() =>
        {
            if (_threadId != 0)
            {
                NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_CLOSE, (nint)0, (nint)0);
            }
        });

        return Task.CompletedTask;
    }

    private void RunMessageLoop(CancellationToken stoppingToken)
    {
        _threadId = NativeMethods.GetCurrentThreadId();

        var moduleHandle = NativeMethods.GetModuleHandle(null);

        var wndClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _wndProcDelegate,
            lpszClassName = WindowClassName,
            hInstance = moduleHandle,
        };

        if (NativeMethods.RegisterClassEx(ref wndClass) == 0)
        {
            var classErr = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            logger.LogError("ホットキー用ウィンドウクラスの登録に失敗しました（エラーコード: {Error}）。緊急停止ホットキーは無効です。", classErr);
            return;
        }

        var hwnd = NativeMethods.CreateWindowEx(0, WindowClassName, null, 0, 0, 0, 0, 0, NativeMethods.HWND_MESSAGE, 0, moduleHandle, 0);
        if (hwnd == 0)
        {
            var createErr = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            logger.LogError("ホットキー用メッセージウィンドウの作成に失敗しました（エラーコード: {Error}）。緊急停止ホットキーは無効です。", createErr);
            return;
        }

        var hotkeyText = options.CurrentValue.Safety.EmergencyStopHotkey;
        var registered = TryRegisterHotkey(hwnd, hotkeyText);
        if (!registered)
        {
            logger.LogWarning("緊急停止ホットキー '{Hotkey}' の登録に失敗しました。他のアプリと競合している可能性があります。", hotkeyText);
        }
        else
        {
            logger.LogInformation("緊急停止ホットキー '{Hotkey}' を登録しました。", hotkeyText);
        }

        while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == NativeMethods.WM_HOTKEY && msg.wParam == HotkeyId)
            {
                logger.LogWarning("緊急停止ホットキーが押下されました。緊急停止を有効化します。");
                emergencyStop.Activate("グローバルホットキー");
                continue;
            }

            if (msg.message == NativeMethods.WM_CLOSE)
            {
                break;
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (registered)
        {
            NativeMethods.UnregisterHotKey(hwnd, HotkeyId);
        }

        NativeMethods.DestroyWindow(hwnd);
    }

    private static bool TryRegisterHotkey(nint hwnd, string hotkeyText)
    {
        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            return false;
        }

        uint modifiers = NativeMethods.MOD_NOREPEAT;
        ushort? vk = null;

        foreach (var rawPart in hotkeyText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    vk = VirtualKeyMap.TryResolve(rawPart);
                    break;
            }
        }

        if (vk is null)
        {
            return false;
        }

        return NativeMethods.RegisterHotKey(hwnd, HotkeyId, modifiers, vk.Value);
    }
}
