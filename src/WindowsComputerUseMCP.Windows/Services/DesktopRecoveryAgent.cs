using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowsComputerUseMCP.Windows.Native;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>MCPホストから独立し、表示先デスクトップ上で期限付きの復旧キー監視を行う。</summary>
public static class DesktopRecoveryAgent
{
    private const string AgentSwitch = "--wcu-desktop-recovery-agent";
    public const int MaxRecoverySeconds = 120;
    private const nuint TimerId = 1;

    public static bool TryRun(string[] args, out int exitCode)
    {
        if (args.Length == 0 || !string.Equals(args[0], AgentSwitch, StringComparison.Ordinal))
        {
            exitCode = 0;
            return false;
        }

        exitCode = RunAgent(args);
        return true;
    }

    private static int RunAgent(string[] args)
    {
        if (args.Length != 4 ||
            !TemporaryDesktopRecoveryState.IsOrdinaryDesktopName(args[1]) ||
            !int.TryParse(args[3], out var timeoutSeconds) ||
            timeoutSeconds is < 1 or > MaxRecoverySeconds ||
            !args[2].StartsWith(@"Local\WcuRecoveryReady_", StringComparison.Ordinal))
        {
            return 2;
        }

        nint sourceDesktop = nint.Zero;
        nint hook = nint.Zero;
        nuint timer = 0;
        var recovered = false;
        var completedWithoutRecovery = false;
        var recoveryTimer = new Stopwatch();
        var targetName = DesktopNativeMethods.GetDesktopName(
            DesktopNativeMethods.GetThreadDesktop(DesktopNativeMethods.GetCurrentThreadId()));
        if (!TemporaryDesktopRecoveryState.IsOrdinaryDesktopName(targetName) ||
            string.Equals(targetName, args[1], StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Desktop recovery requires a distinct ordinary target desktop.");
            return 2;
        }

        var state = new TemporaryDesktopRecoveryState(args[1], targetName, timeoutSeconds);
        RecoveryKeyStateMachine keyState = new();
        DesktopNativeMethods.LowLevelKeyboardProc hookCallback = (nCode, wParam, lParam) =>
        {
            if (nCode < DesktopNativeMethods.HC_ACTION)
            {
                return DesktopNativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);
            }

            if (wParam == DesktopNativeMethods.WM_KEYDOWN ||
                wParam == DesktopNativeMethods.WM_SYSKEYDOWN ||
                wParam == DesktopNativeMethods.WM_KEYUP ||
                wParam == DesktopNativeMethods.WM_SYSKEYUP)
            {
                var key = Marshal.PtrToStructure<DesktopNativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var keyDown = wParam == DesktopNativeMethods.WM_KEYDOWN || wParam == DesktopNativeMethods.WM_SYSKEYDOWN;
                var action = keyState.Process(key.vkCode, keyDown, IsModifierDown(), (key.flags & 0x12) != 0);
                if (action.ShouldRecover)
                {
                    NativeMethods.PostThreadMessage(
                        DesktopNativeMethods.GetCurrentThreadId(),
                        DesktopNativeMethods.WM_RECOVERY_TRIGGER,
                        0,
                        0);
                }

                if (action.SuppressKey)
                {
                    return 1;
                }
            }

            return DesktopNativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);
        };

        try
        {
            sourceDesktop = DesktopNativeMethods.OpenDesktopW(
                args[1], 0, false, DesktopNativeMethods.DESKTOP_READOBJECTS | DesktopNativeMethods.DESKTOP_SWITCHDESKTOP);
            if (sourceDesktop == nint.Zero)
            {
                return 3;
            }

            _ = DesktopNativeMethods.PeekMessageW(out _, 0, 0, 0, DesktopNativeMethods.PM_NOREMOVE);
            timer = DesktopNativeMethods.SetTimer(0, TimerId, 250, 0);
            if (timer == 0)
            {
                return 4;
            }

            hook = DesktopNativeMethods.SetWindowsHookExW(
                DesktopNativeMethods.WH_KEYBOARD_LL,
                hookCallback,
                DesktopNativeMethods.GetModuleHandleW(null),
                0);
            if (hook == nint.Zero)
            {
                return 5;
            }

            using (var ready = EventWaitHandle.OpenExisting(args[2]))
            {
                ready.Set();
            }

            recoveryTimer.Start();
            while (DesktopNativeMethods.GetMessageW(out var message, 0, 0, 0) > 0)
            {
                if (message.message == NativeMethods.WM_CLOSE)
                {
                    break;
                }

                if (message.message == DesktopNativeMethods.WM_RECOVERY_TRIGGER)
                {
                    var decision = state.Observe(InputDesktopName(), recoveryTimer.Elapsed);
                    if (decision is TemporaryRecoveryDecision.Listen or TemporaryRecoveryDecision.Recover &&
                        TrySwitchBack(sourceDesktop, args[1], targetName))
                    {
                        recovered = true;
                        break;
                    }

                    continue;
                }

                if (message.message == DesktopNativeMethods.WM_TIMER)
                {
                    var decision = state.Observe(InputDesktopName(), recoveryTimer.Elapsed);
                    if (decision == TemporaryRecoveryDecision.Completed)
                    {
                        completedWithoutRecovery = true;
                        break;
                    }

                    if (decision == TemporaryRecoveryDecision.Recover &&
                        TrySwitchBack(sourceDesktop, args[1], targetName))
                    {
                        recovered = true;
                        break;
                    }

                    if (decision == TemporaryRecoveryDecision.Failed)
                    {
                        break;
                    }
                }

                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Desktop recovery agent failed: {exception.Message}");
            return 6;
        }
        finally
        {
            if (hook != nint.Zero)
            {
                DesktopNativeMethods.UnhookWindowsHookEx(hook);
            }

            GC.KeepAlive(hookCallback);
            if (timer != 0)
            {
                DesktopNativeMethods.KillTimer(0, timer);
            }

            if (sourceDesktop != nint.Zero)
            {
                DesktopNativeMethods.CloseDesktop(sourceDesktop);
            }
        }

        if (!recovered && !completedWithoutRecovery)
        {
            Console.Error.WriteLine("Desktop recovery did not complete; input desktop may be inaccessible, locked, or changed.");
            return 7;
        }

        return 0;
    }

    private static bool TrySwitchBack(nint sourceDesktop, string expectedName, string targetName)
    {
        if (!string.Equals(InputDesktopName(), targetName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!DesktopNativeMethods.SwitchDesktop(sourceDesktop))
        {
            return false;
        }

        var verificationTimer = Stopwatch.StartNew();
        do
        {
            var inputDesktop = DesktopNativeMethods.OpenInputDesktop(
                0, false, DesktopNativeMethods.DESKTOP_READOBJECTS);
            if (inputDesktop != nint.Zero)
            {
                try
                {
                    if (string.Equals(
                            DesktopNativeMethods.GetDesktopName(inputDesktop),
                            expectedName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                finally
                {
                    DesktopNativeMethods.CloseDesktop(inputDesktop);
                }
            }

            Thread.Sleep(50);
        }
        while (verificationTimer.Elapsed < TimeSpan.FromSeconds(1));

        return false;
    }

    private static bool IsModifierDown()
        => IsKeyDown(DesktopNativeMethods.VK_SHIFT) ||
           IsKeyDown(DesktopNativeMethods.VK_CONTROL) ||
           IsKeyDown(DesktopNativeMethods.VK_MENU);

    private static bool IsKeyDown(uint virtualKey)
        => (DesktopNativeMethods.GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;

    internal static string? InputDesktopName()
    {
        var input = DesktopNativeMethods.OpenInputDesktop(0, false, DesktopNativeMethods.DESKTOP_READOBJECTS);
        if (input == 0)
        {
            return null;
        }

        try
        {
            var name = DesktopNativeMethods.GetDesktopName(input);
            return TemporaryDesktopRecoveryState.IsOrdinaryDesktopName(name) ? name : null;
        }
        finally
        {
            DesktopNativeMethods.CloseDesktop(input);
        }
    }
}
