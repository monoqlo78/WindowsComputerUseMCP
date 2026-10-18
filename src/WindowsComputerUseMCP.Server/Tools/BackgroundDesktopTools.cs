using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Core.Results;
using WindowsComputerUseMCP.Server.Hosting;

namespace WindowsComputerUseMCP.Server.Tools;

/// <summary>
/// ユーザーの画面を占有せずにGUIアプリを操作するための、バックグラウンドデスクトップ操作ツール群。
/// </summary>
[McpServerToolType]
public static class BackgroundDesktopTools
{
    [McpServerTool(Name = "background_desktop_create")]
    [Description("ユーザーの画面とは別の『バックグラウンドデスクトップ（隠しワークスペース）』を作成する。ここで起動したアプリはユーザーの画面・カーソル・フォーカスを一切奪わずに操作できる。既に同名が存在する場合はそれを再利用する。")]
    public static Task<OperationResult<BackgroundDesktopInfo>> Create(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<BackgroundDesktopInfo> logger,
        [Description("作成するデスクトップ名。英数字・ハイフン・アンダースコアのみ（例: \"work1\"）。")] string name,
        CancellationToken cancellationToken = default)
    {
        return ToolRunner.RunAsync(
            "background_desktop_create",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_create" },
            () => desktopService.CreateAsync(new BackgroundDesktopCreateRequest { Name = name }, cancellationToken),
            new Dictionary<string, string?> { ["name"] = name },
            applyRateLimit: true);
    }

    [McpServerTool(Name = "background_desktop_list")]
    [Description("現在のウィンドウステーション上のデスクトップ一覧を返す。MCPが管理しているバックグラウンドデスクトップにはウィンドウ数と起動済みプロセスIDが含まれる。")]
    public static Task<OperationResult<IReadOnlyList<BackgroundDesktopInfo>>> List(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<BackgroundDesktopInfo> logger,
        CancellationToken cancellationToken = default)
    {
        return ToolRunner.RunAsync(
            "background_desktop_list",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_list" },
            () => desktopService.ListAsync(cancellationToken));
    }

    [McpServerTool(Name = "background_desktop_launch")]
    [Description("バックグラウンドデスクトップ上でアプリを起動する。起動したアプリのウィンドウはユーザーの画面には表示されない。注意: Electron/Chromium系などGPU描画に強く依存するアプリは非対話デスクトップで起動できない場合がある。")]
    public static Task<OperationResult<BackgroundDesktopLaunchResult>> Launch(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<BackgroundDesktopLaunchResult> logger,
        [Description("起動先のバックグラウンドデスクトップ名。")] string desktopName,
        [Description("起動する実行ファイル（例: \"notepad.exe\" や絶対パス）。")] string executablePath,
        [Description("コマンドライン引数。省略可。")] string? arguments = null,
        [Description("作業ディレクトリ。省略可。")] string? workingDirectory = null,
        [Description("ウィンドウ出現を待つ最大ミリ秒。既定 15000。")] int waitForWindowMs = 15000,
        CancellationToken cancellationToken = default)
    {
        var request = new BackgroundDesktopLaunchRequest
        {
            DesktopName = desktopName,
            ExecutablePath = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            WaitForWindowMs = waitForWindowMs,
        };

        return ToolRunner.RunAsync(
            "background_desktop_launch",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_launch" },
            () => desktopService.LaunchAsync(request, cancellationToken),
            new Dictionary<string, string?>
            {
                ["desktopName"] = desktopName,
                ["executablePath"] = executablePath,
                ["arguments"] = arguments,
            },
            applyRateLimit: true);
    }

    [McpServerTool(Name = "background_desktop_window_list")]
    [Description("指定したバックグラウンドデスクトップ上のトップレベルウィンドウを列挙する（通常の window_list はこのデスクトップのウィンドウを返さない）。")]
    public static Task<OperationResult<IReadOnlyList<WindowInfo>>> WindowList(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<WindowInfo> logger,
        [Description("対象のバックグラウンドデスクトップ名。")] string desktopName,
        CancellationToken cancellationToken = default)
    {
        return ToolRunner.RunAsync(
            "background_desktop_window_list",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_window_list" },
            () => desktopService.ListWindowsAsync(desktopName, cancellationToken),
            new Dictionary<string, string?> { ["desktopName"] = desktopName });
    }

    [McpServerTool(Name = "background_desktop_capture")]
    [Description("バックグラウンドデスクトップ上のウィンドウをキャプチャする（PrintWindow を使用するため、ユーザーの画面には何も表示されない）。")]
    public static Task<OperationResult<ScreenCaptureResult>> Capture(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<ScreenCaptureResult> logger,
        [Description("対象のバックグラウンドデスクトップ名。")] string desktopName,
        [Description("キャプチャするウィンドウのハンドル（background_desktop_window_list で取得）。")] long windowHandle,
        [Description("結果をファイルに保存するか。既定 true。")] bool saveToFile = true,
        CancellationToken cancellationToken = default)
    {
        return ToolRunner.RunAsync(
            "background_desktop_capture",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_capture" },
            () => desktopService.CaptureWindowAsync(desktopName, windowHandle, saveToFile, cancellationToken),
            new Dictionary<string, string?>
            {
                ["desktopName"] = desktopName,
                ["windowHandle"] = windowHandle.ToString(),
            });
    }

    [McpServerTool(Name = "background_desktop_click")]
    [Description("バックグラウンドデスクトップ内でマウスクリックする。ユーザーの実際のカーソルは動かず、フォーカスも奪われない。")]
    public static Task<OperationResult<BackgroundDesktopActionResult>> Click(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<BackgroundDesktopActionResult> logger,
        [Description("対象のバックグラウンドデスクトップ名。")] string desktopName,
        [Description("クリックするX座標（そのデスクトップ内の座標）。")] int x,
        [Description("クリックするY座標（そのデスクトップ内の座標）。")] int y,
        [Description("マウスボタン。既定はLeft。")] MouseButton button = MouseButton.Left,
        [Description("クリック回数。既定は1（2でダブルクリック）。")] int clickCount = 1,
        [Description("指定するとクリック前にこのウィンドウをそのデスクトップ内で前面化する。")] long? windowHandle = null,
        [Description("危険操作カテゴリに該当する場合でも実行することを呼び出し元が承認済みであることを示す。")] bool requireConfirmation = false,
        CancellationToken cancellationToken = default)
    {
        var request = new BackgroundDesktopClickRequest
        {
            DesktopName = desktopName,
            X = x,
            Y = y,
            Button = button,
            ClickCount = clickCount,
            WindowHandle = windowHandle,
        };

        return ToolRunner.RunAsync(
            "background_desktop_click",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_click", CallerAcknowledgedConfirmation = requireConfirmation },
            () => desktopService.ClickAsync(request, cancellationToken),
            new Dictionary<string, string?>
            {
                ["desktopName"] = desktopName,
                ["x"] = x.ToString(),
                ["y"] = y.ToString(),
                ["button"] = button.ToString(),
            },
            applyRateLimit: true);
    }

    [McpServerTool(Name = "background_desktop_type_text")]
    [Description("バックグラウンドデスクトップ内でテキストを入力する。ユーザーが今使っているアプリには一切入力されない。")]
    public static Task<OperationResult<BackgroundDesktopActionResult>> TypeText(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<BackgroundDesktopActionResult> logger,
        [Description("対象のバックグラウンドデスクトップ名。")] string desktopName,
        [Description("入力するテキスト。")] string text,
        [Description("指定すると入力前にこのウィンドウをそのデスクトップ内で前面化する。")] long? windowHandle = null,
        [Description("1文字ごとの入力間隔（ミリ秒）。省略時は即時連続入力。")] int? intervalMs = null,
        [Description("危険操作カテゴリに該当する場合でも実行することを呼び出し元が承認済みであることを示す。")] bool requireConfirmation = false,
        CancellationToken cancellationToken = default)
    {
        var request = new BackgroundDesktopTypeTextRequest
        {
            DesktopName = desktopName,
            Text = text,
            WindowHandle = windowHandle,
            IntervalMs = intervalMs,
        };

        return ToolRunner.RunAsync(
            "background_desktop_type_text",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_type_text", CallerAcknowledgedConfirmation = requireConfirmation },
            () => desktopService.TypeTextAsync(request, cancellationToken),
            new Dictionary<string, string?>
            {
                ["desktopName"] = desktopName,
                ["textLength"] = text.Length.ToString(),
            },
            applyRateLimit: true);
    }

    [McpServerTool(Name = "background_desktop_send_keys")]
    [Description("バックグラウンドデスクトップ内でキー押下またはホットキー（同時押し）を送信する。key か keys のいずれかを指定する。")]
    public static Task<OperationResult<BackgroundDesktopActionResult>> SendKeys(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<BackgroundDesktopActionResult> logger,
        [Description("対象のバックグラウンドデスクトップ名。")] string desktopName,
        [Description("単一キー名（例: Enter, Tab, F5）。keys を使う場合は不要。")] string? key = null,
        [Description("同時押しするキー名の配列（例: [\"Ctrl\",\"S\"]）。")] string[]? keys = null,
        [Description("単一キーの連続押下回数。既定は1。")] int repeatCount = 1,
        [Description("指定すると送信前にこのウィンドウをそのデスクトップ内で前面化する。")] long? windowHandle = null,
        [Description("危険操作カテゴリに該当する場合でも実行することを呼び出し元が承認済みであることを示す。")] bool requireConfirmation = false,
        CancellationToken cancellationToken = default)
    {
        var request = new BackgroundDesktopKeyRequest
        {
            DesktopName = desktopName,
            Key = key,
            Keys = keys,
            RepeatCount = repeatCount,
            WindowHandle = windowHandle,
        };

        return ToolRunner.RunAsync(
            "background_desktop_send_keys",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_send_keys", CallerAcknowledgedConfirmation = requireConfirmation },
            () => desktopService.SendKeysAsync(request, cancellationToken),
            new Dictionary<string, string?>
            {
                ["desktopName"] = desktopName,
                ["key"] = key,
                ["keys"] = keys is null ? null : string.Join('+', keys),
            },
            applyRateLimit: true);
    }

    [McpServerTool(Name = "background_desktop_switch")]
    [Description("指定したWin32デスクトップを一時表示する。切り替え前に対象デスクトップ上で独立した復旧監視を起動し、Esc、Home、単独のWindowsキー、または上限120秒の自動復帰で元のWin32デスクトップへの復帰を試みる。認証用secure desktopには対応しない。desktopName を省略または \"Default\" にするとDefaultへ切り替える。")]
    public static Task<OperationResult<BackgroundDesktopActionResult>> Switch(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<BackgroundDesktopActionResult> logger,
        [Description("表示するデスクトップ名。省略または \"Default\" で通常デスクトップへ戻る。")] string? desktopName = null,
        [Description("自動復帰までの秒数（1〜120）。既定15。")] int autoReturnSeconds = 15,
        CancellationToken cancellationToken = default)
    {
        var request = new BackgroundDesktopSwitchRequest
        {
            DesktopName = desktopName,
            AutoReturnSeconds = autoReturnSeconds,
        };

        return ToolRunner.RunAsync(
            "background_desktop_switch",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_switch" },
            () => desktopService.SwitchAsync(request, cancellationToken),
            new Dictionary<string, string?>
            {
                ["desktopName"] = desktopName,
                ["autoReturnSeconds"] = autoReturnSeconds.ToString(),
            },
            applyRateLimit: true);
    }

    [McpServerTool(Name = "background_desktop_recover")]
    [Description("直前の background_desktop_switch の切り替え元へ即時復帰する。サーバーが切り替え元を保持していない場合は Default へ戻る。認証用secure desktopからの復帰には使用できない。")]
    public static Task<OperationResult<BackgroundDesktopActionResult>> Recover(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<BackgroundDesktopActionResult> logger,
        [Description("復帰先のWin32デスクトップ名。省略すると直前の切り替え元、または Default。")] string? desktopName = null,
        CancellationToken cancellationToken = default)
    {
        return ToolRunner.RunAsync(
            "background_desktop_recover",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_recover" },
            () => desktopService.RecoverAsync(desktopName, cancellationToken),
            new Dictionary<string, string?> { ["desktopName"] = desktopName },
            applyRateLimit: true);
    }

    [McpServerTool(Name = "background_desktop_close")]
    [Description("バックグラウンドデスクトップを閉じる。既定ではそのデスクトップ上でMCPが起動したプロセスも終了させる。")]
    public static Task<OperationResult<BackgroundDesktopActionResult>> Close(
        IBackgroundDesktopService desktopService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<BackgroundDesktopActionResult> logger,
        [Description("閉じるバックグラウンドデスクトップ名。")] string desktopName,
        [Description("起動したプロセスを終了させるか。既定 true。")] bool terminateProcesses = true,
        CancellationToken cancellationToken = default)
    {
        var request = new BackgroundDesktopCloseRequest
        {
            DesktopName = desktopName,
            TerminateProcesses = terminateProcesses,
        };

        return ToolRunner.RunAsync(
            "background_desktop_close",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "background_desktop_close" },
            () => desktopService.CloseAsync(request, cancellationToken),
            new Dictionary<string, string?>
            {
                ["desktopName"] = desktopName,
                ["terminateProcesses"] = terminateProcesses.ToString(),
            },
            applyRateLimit: true);
    }
}
