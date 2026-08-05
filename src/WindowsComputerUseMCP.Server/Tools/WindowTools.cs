using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Core.Results;
using WindowsComputerUseMCP.Server.Hosting;

namespace WindowsComputerUseMCP.Server.Tools;

[McpServerToolType]
public static class WindowTools
{
    [McpServerTool(Name = "window_list")]
    [Description("現在デスクトップ上に存在する、ユーザーが操作可能なトップレベルウィンドウの一覧を返す（ハンドル・タイトル・プロセス名・位置・状態を含む）。")]
    public static Task<OperationResult<IReadOnlyList<WindowInfo>>> WindowList(
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<WindowInfo> logger,
        CancellationToken cancellationToken = default)
    {
        return ToolRunner.RunAsync(
            "window_list",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "window_list" },
            () => windowService.ListWindowsAsync(cancellationToken));
    }

    [McpServerTool(Name = "window_focus")]
    [Description("指定したハンドルまたはタイトル（一致方法指定可）のウィンドウを前面化する（最小化されている場合は復元する）。タイトルが複数一致した場合は候補一覧を返す。")]
    public static async Task<OperationResult<WindowFocusResult>> WindowFocus(
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<WindowInfo> logger,
        [Description("前面化するウィンドウのハンドル（window_list で取得した値）。titleと同時指定時はこちらを優先。")] long? windowHandle = null,
        [Description("前面化するウィンドウのタイトル（部分一致等はtitleMatchModeに従う）。")] string? title = null,
        [Description("タイトルの一致方法: Exact | Contains | StartsWith | Regex。既定はContains。")] MatchMode titleMatchMode = MatchMode.Contains,
        CancellationToken cancellationToken = default)
    {
        var request = new WindowFocusRequest { WindowHandle = windowHandle, Title = title, TitleMatchMode = titleMatchMode };
        var safetyRequest = await SafetyContextResolver.BuildAsync(windowService, "window_focus", cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ToolRunner.RunAsync(
            "window_focus",
            safetyPolicy,
            auditLog,
            logger,
            safetyRequest,
            () => windowService.FocusWindowAsync(request, cancellationToken),
            new Dictionary<string, string?> { ["windowHandle"] = windowHandle?.ToString(), ["title"] = title },
            applyRateLimit: true).ConfigureAwait(false);
    }
}
