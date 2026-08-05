using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Core.Results;
using WindowsComputerUseMCP.Server.Hosting;

namespace WindowsComputerUseMCP.Server.Tools;

[McpServerToolType]
public static class UiAutomationTools
{
    [McpServerTool(Name = "ui_get_tree")]
    [Description("指定ウィンドウのUI Automation要素ツリーを取得する。各要素にはelementId・名前・コントロール種別・位置・対応パターンが含まれる。")]
    public static Task<OperationResult<UiTreeResult>> UiGetTree(
        IUiAutomationService uiAutomationService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<UiTreeResult> logger,
        [Description("対象ウィンドウのハンドル（window_list で取得した値）。")] long windowHandle,
        [Description("探索する最大の深さ。既定は8。")] int maxDepth = 8,
        [Description("取得する要素数の上限。既定は500。")] int maxElements = 500,
        [Description("画面外（Offscreen）の要素も含めるかどうか。既定は false。")] bool includeOffscreen = false,
        CancellationToken cancellationToken = default)
    {
        var request = new UiTreeRequest
        {
            WindowHandle = windowHandle,
            MaxDepth = maxDepth,
            MaxElements = maxElements,
            IncludeOffscreen = includeOffscreen,
        };

        var sanitizedArgs = new Dictionary<string, string?>
        {
            ["windowHandle"] = windowHandle.ToString(),
            ["maxDepth"] = maxDepth.ToString(),
            ["maxElements"] = maxElements.ToString(),
        };

        return ToolRunner.RunAsync(
            "ui_get_tree",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "ui_get_tree" },
            () => uiAutomationService.GetTreeAsync(request, cancellationToken),
            sanitizedArgs);
    }

    [McpServerTool(Name = "ui_find")]
    [Description("指定ウィンドウ内から、名前・AutomationId・コントロール種別・クラス名などの条件に一致するUI要素を検索する。")]
    public static Task<OperationResult<IReadOnlyList<UiElementInfo>>> UiFind(
        IUiAutomationService uiAutomationService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<UiElementInfo> logger,
        [Description("対象ウィンドウのハンドル（window_list で取得した値）。")] long windowHandle,
        [Description("要素名（部分一致等はmatchModeに従う）。")] string? name = null,
        [Description("AutomationId。")] string? automationId = null,
        [Description("コントロール種別（例: Button, Edit）。")] string? controlType = null,
        [Description("クラス名。")] string? className = null,
        [Description("一致方法: Exact | Contains | StartsWith | Regex。既定はContains。")] MatchMode matchMode = MatchMode.Contains,
        [Description("探索する最大の深さ。既定は12。")] int maxDepth = 12,
        [Description("取得する要素数の上限。既定は200。")] int maxElements = 200,
        CancellationToken cancellationToken = default)
    {
        var request = new UiFindRequest
        {
            WindowHandle = windowHandle,
            Name = name,
            AutomationId = automationId,
            ControlType = controlType,
            ClassName = className,
            MatchMode = matchMode,
            MaxDepth = maxDepth,
            MaxElements = maxElements,
        };

        var sanitizedArgs = new Dictionary<string, string?>
        {
            ["windowHandle"] = windowHandle.ToString(),
            ["name"] = name,
            ["automationId"] = automationId,
            ["controlType"] = controlType,
            ["className"] = className,
            ["matchMode"] = matchMode.ToString(),
        };

        return ToolRunner.RunAsync(
            "ui_find",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "ui_find" },
            () => uiAutomationService.FindAsync(request, cancellationToken),
            sanitizedArgs);
    }

    [McpServerTool(Name = "ui_invoke")]
    [Description("ui_get_tree/ui_find で取得したelementIdの要素に対し、対応するUI Automationパターン（Invoke/Toggle/SelectionItem/ExpandCollapse）を実行する。対応パターンが無い場合は座標クリックへ自動フォールバックしない。パスワード入力欄は既定で拒否される。")]
    public static async Task<OperationResult<UiInvokeResult>> UiInvoke(
        IUiAutomationService uiAutomationService,
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<UiInvokeResult> logger,
        [Description("操作対象の要素ID（ui_get_tree/ui_find のElementId）。")] string elementId,
        [Description("危険操作カテゴリに該当する場合でも実行することを呼び出し元が承認済みであることを示す。")] bool requireConfirmation = false,
        CancellationToken cancellationToken = default)
    {
        var elementInfo = await uiAutomationService.GetElementInfoAsync(elementId, cancellationToken).ConfigureAwait(false);

        var inspectionTexts = new List<string>();
        if (elementInfo?.Name is not null)
        {
            inspectionTexts.Add(elementInfo.Name);
        }

        if (elementInfo?.AutomationId is not null)
        {
            inspectionTexts.Add(elementInfo.AutomationId);
        }

        var safetyRequest = await SafetyContextResolver.BuildAsync(
            windowService,
            "ui_invoke",
            inspectionTexts: inspectionTexts,
            isPasswordField: elementInfo?.IsPassword ?? false,
            requireConfirmation: requireConfirmation,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ToolRunner.RunAsync(
            "ui_invoke",
            safetyPolicy,
            auditLog,
            logger,
            safetyRequest,
            () => uiAutomationService.InvokeAsync(elementId, cancellationToken),
            new Dictionary<string, string?> { ["elementId"] = elementId, ["elementName"] = elementInfo?.Name },
            applyRateLimit: true).ConfigureAwait(false);
    }
}
