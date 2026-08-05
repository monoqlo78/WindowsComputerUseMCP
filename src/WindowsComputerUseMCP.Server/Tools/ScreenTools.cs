using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Core.Results;
using WindowsComputerUseMCP.Server.Hosting;

namespace WindowsComputerUseMCP.Server.Tools;

[McpServerToolType]
public static class ScreenTools
{
    [McpServerTool(Name = "screen_capture")]
    [Description("画面全体、指定モニター、または指定ウィンドウのスクリーンショットを取得する。結果はファイル保存パスおよび/またはBase64画像データとして返る。")]
    public static Task<OperationResult<ScreenCaptureResult>> ScreenCapture(
        IScreenCaptureService screenCaptureService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<ScreenCaptureResult> logger,
        [Description("キャプチャ対象のモニター番号（0始まり）。省略時は画面全体またはウィンドウ指定を優先する。")] int? monitorIndex = null,
        [Description("キャプチャ対象ウィンドウのハンドル（window_list で取得した値）。省略時はモニター/画面全体を対象とする。")] long? windowHandle = null,
        [Description("マウスカーソルを画像に含めるかどうか。既定は false。")] bool includeCursor = false,
        [Description("キャプチャ結果をファイルに保存するかどうか。既定は true。")] bool saveToFile = true,
        CancellationToken cancellationToken = default)
    {
        var request = new ScreenCaptureRequest
        {
            MonitorIndex = monitorIndex,
            WindowHandle = windowHandle,
            IncludeCursor = includeCursor,
            SaveToFile = saveToFile,
        };

        var sanitizedArgs = new Dictionary<string, string?>
        {
            ["monitorIndex"] = monitorIndex?.ToString(),
            ["windowHandle"] = windowHandle?.ToString(),
            ["includeCursor"] = includeCursor.ToString(),
            ["saveToFile"] = saveToFile.ToString(),
        };

        return ToolRunner.RunAsync(
            "screen_capture",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "screen_capture" },
            () => screenCaptureService.CaptureAsync(request, cancellationToken),
            sanitizedArgs);
    }

    [McpServerTool(Name = "wait_for_screen_change")]
    [Description("画面（またはウィンドウ/モニター/指定領域）を一定間隔でポーリングし、直前との差分ピクセル比率が閾値を超えるまで待機する。タイムアウトしても失敗にはならず changed=false / timedOut=true を返す。操作実行直後の画面反映確認に利用する。")]
    public static Task<OperationResult<WaitForScreenChangeResult>> WaitForScreenChange(
        IScreenChangeService screenChangeService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<WaitForScreenChangeResult> logger,
        [Description("最大待機時間（ミリ秒）。既定は5000。")] int timeoutMs = 5000,
        [Description("ポーリング間隔（ミリ秒）。既定は200。")] int pollingIntervalMs = 200,
        [Description("差分ピクセル比率（0.0〜1.0）がこの値を超えたら変化ありと判定する。既定は0.02。")] double differenceThreshold = 0.02,
        [Description("比較対象のモニター番号（0始まり）。windowHandle未指定時のみ使用。")] int? monitorIndex = null,
        [Description("比較対象ウィンドウのハンドル（window_list で取得した値）。")] long? windowHandle = null,
        [Description("比較対象を画面全体ではなく特定領域（キャプチャ座標系）に限定する場合のX座標。")] int? regionX = null,
        [Description("比較領域のY座標。regionX指定時は必須。")] int? regionY = null,
        [Description("比較領域の幅。regionX指定時は必須。")] int? regionWidth = null,
        [Description("比較領域の高さ。regionX指定時は必須。")] int? regionHeight = null,
        CancellationToken cancellationToken = default)
    {
        ScreenRect? region = regionX is { } rx && regionY is { } ry && regionWidth is { } rw && regionHeight is { } rh
            ? new ScreenRect(rx, ry, rw, rh)
            : null;

        var request = new WaitForScreenChangeRequest
        {
            TimeoutMs = timeoutMs,
            PollingIntervalMs = pollingIntervalMs,
            DifferenceThreshold = differenceThreshold,
            Region = region,
            MonitorIndex = monitorIndex,
            WindowHandle = windowHandle,
        };

        var sanitizedArgs = new Dictionary<string, string?>
        {
            ["timeoutMs"] = timeoutMs.ToString(),
            ["pollingIntervalMs"] = pollingIntervalMs.ToString(),
            ["differenceThreshold"] = differenceThreshold.ToString("F4"),
            ["monitorIndex"] = monitorIndex?.ToString(),
            ["windowHandle"] = windowHandle?.ToString(),
        };

        return ToolRunner.RunAsync(
            "wait_for_screen_change",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "wait_for_screen_change" },
            () => screenChangeService.WaitForChangeAsync(request, cancellationToken),
            sanitizedArgs);
    }
}
