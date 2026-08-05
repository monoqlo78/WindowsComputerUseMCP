using System.Diagnostics;
using System.Drawing;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>
/// 画面（またはウィンドウ/モニター/指定領域）を一定間隔でキャプチャし、
/// 直前のキャプチャとの差分ピクセル比率が閾値を超えるまでポーリングする実装。
/// </summary>
public sealed class ScreenChangeService : IScreenChangeService
{
    private readonly ILogger<ScreenChangeService> _logger;

    public ScreenChangeService(ILogger<ScreenChangeService> logger)
    {
        _logger = logger;
    }

    public async Task<WaitForScreenChangeResult> WaitForChangeAsync(WaitForScreenChangeRequest request, CancellationToken cancellationToken = default)
    {
        var timeoutMs = Math.Max(request.TimeoutMs, 0);
        var pollingIntervalMs = Math.Max(request.PollingIntervalMs, 10);
        var threshold = Math.Clamp(request.DifferenceThreshold, 0.0, 1.0);

        var stopwatch = Stopwatch.StartNew();

        using var baseline = ScreenCaptureCore.Capture(request.WindowHandle, request.MonitorIndex, request.Region);

        double observedRatio = 0.0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stopwatch.ElapsedMilliseconds >= timeoutMs)
            {
                stopwatch.Stop();
                return new WaitForScreenChangeResult
                {
                    Changed = false,
                    ObservedDifferenceRatio = observedRatio,
                    ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                    TimedOut = true,
                };
            }

            var remainingMs = timeoutMs - stopwatch.ElapsedMilliseconds;
            var delayMs = (int)Math.Min(pollingIntervalMs, Math.Max(remainingMs, 0));
            if (delayMs > 0)
            {
                try
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }

            Bitmap current;
            try
            {
                current = ScreenCaptureCore.Capture(request.WindowHandle, request.MonitorIndex, request.Region);
            }
            catch (Exception ex)
            {
                // 対象ウィンドウが閉じられた等でキャプチャ失敗した場合は「変化あり」として扱う
                // （呼び出し元が閉じる操作を行った直後の典型的なケースを想定）。
                _logger.LogWarning(ex, "wait_for_screen_change: キャプチャに失敗したため変化ありとみなします。");
                stopwatch.Stop();
                return new WaitForScreenChangeResult
                {
                    Changed = true,
                    ObservedDifferenceRatio = 1.0,
                    ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                    TimedOut = false,
                };
            }

            using (current)
            {
                observedRatio = ScreenCaptureCore.ComputeDifferenceRatio(baseline, current);
            }

            if (observedRatio > threshold)
            {
                stopwatch.Stop();
                return new WaitForScreenChangeResult
                {
                    Changed = true,
                    ObservedDifferenceRatio = observedRatio,
                    ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                    TimedOut = false,
                };
            }
        }
    }
}
