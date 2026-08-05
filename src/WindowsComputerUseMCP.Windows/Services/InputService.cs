using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Windows.Native;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary><see cref="NativeMethods.SendInput"/> を用いたマウス・キーボードの物理入力サービス。</summary>
public sealed class InputService : IInputService
{
    private const int MoveStepIntervalMs = 15;

    private readonly IWindowService _windowService;
    private readonly ILogger<InputService> _logger;

    public InputService(IWindowService windowService, ILogger<InputService> logger)
    {
        _windowService = windowService;
        _logger = logger;
    }

    public async Task MouseMoveAsync(MouseMoveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.DurationMs is > 0)
        {
            await MoveSmoothlyAsync(request.X, request.Y, request.DurationMs.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            NativeMethods.SetCursorPos(request.X, request.Y);
        }
    }

    public async Task<MouseActionResult> MouseClickAsync(MouseClickRequest request, CancellationToken cancellationToken = default)
    {
        NativeMethods.SetCursorPos(request.X, request.Y);
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);

        var (down, up) = GetButtonFlags(request.Button);

        for (var i = 0; i < Math.Max(1, request.ClickCount); i++)
        {
            SendMouseButtonEvent(down);
            SendMouseButtonEvent(up);
            if (i < request.ClickCount - 1)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        var foreground = await _windowService.GetForegroundWindowAsync(cancellationToken).ConfigureAwait(false);
        return new MouseActionResult { Executed = true, ForegroundWindow = foreground };
    }

    public async Task<MouseActionResult> MouseDragAsync(MouseDragRequest request, CancellationToken cancellationToken = default)
    {
        NativeMethods.SetCursorPos(request.StartX, request.StartY);
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);

        var (down, up) = GetButtonFlags(request.Button);
        SendMouseButtonEvent(down);
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);

        await MoveSmoothlyAsync(request.EndX, request.EndY, request.DurationMs ?? 300, cancellationToken).ConfigureAwait(false);

        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        SendMouseButtonEvent(up);

        var foreground = await _windowService.GetForegroundWindowAsync(cancellationToken).ConfigureAwait(false);
        return new MouseActionResult { Executed = true, ForegroundWindow = foreground };
    }

    public Task MouseScrollAsync(MouseScrollRequest request, CancellationToken cancellationToken = default)
    {
        if (request.X is not null && request.Y is not null)
        {
            NativeMethods.SetCursorPos(request.X.Value, request.Y.Value);
        }

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL,
                    mouseData = unchecked((uint)request.Delta),
                },
            },
        };

        SendInputChecked([input]);
        return Task.CompletedTask;
    }

    public async Task KeyboardTypeTextAsync(KeyboardTypeTextRequest request, CancellationToken cancellationToken = default)
    {
        foreach (var c in request.Text)
        {
            SendUnicodeChar(c);
            if (request.IntervalMs is > 0)
            {
                await Task.Delay(request.IntervalMs.Value, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task KeyboardPressAsync(KeyboardPressRequest request, CancellationToken cancellationToken = default)
    {
        var vk = VirtualKeyMap.TryResolve(request.Key);
        if (vk is null)
        {
            _logger.LogWarning("未知のキー名のため無視しました: {Key}", request.Key);
            return;
        }

        for (var i = 0; i < Math.Max(1, request.RepeatCount); i++)
        {
            SendKeyEvent(vk.Value, keyUp: false);
            SendKeyEvent(vk.Value, keyUp: true);
            if (i < request.RepeatCount - 1)
            {
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task KeyboardHotkeyAsync(KeyboardHotkeyRequest request, CancellationToken cancellationToken = default)
    {
        var codes = new List<ushort>();
        foreach (var key in request.Keys)
        {
            var vk = VirtualKeyMap.TryResolve(key);
            if (vk is null)
            {
                _logger.LogWarning("未知のキー名のため、ホットキー全体を中止しました: {Key}", key);
                return Task.CompletedTask;
            }

            codes.Add(vk.Value);
        }

        foreach (var code in codes)
        {
            SendKeyEvent(code, keyUp: false);
        }

        for (var i = codes.Count - 1; i >= 0; i--)
        {
            SendKeyEvent(codes[i], keyUp: true);
        }

        return Task.CompletedTask;
    }

    public ScreenRect GetVirtualScreenBounds() => MonitorEnumerator.GetVirtualScreenBounds();

    private async Task MoveSmoothlyAsync(int targetX, int targetY, int durationMs, CancellationToken cancellationToken)
    {
        NativeMethods.GetCursorPos(out var start);
        var steps = Math.Max(1, durationMs / MoveStepIntervalMs);

        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var x = (int)Math.Round(start.X + ((targetX - start.X) * t));
            var y = (int)Math.Round(start.Y + ((targetY - start.Y) * t));
            NativeMethods.SetCursorPos(x, y);
            await Task.Delay(MoveStepIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        NativeMethods.SetCursorPos(targetX, targetY);
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
}
