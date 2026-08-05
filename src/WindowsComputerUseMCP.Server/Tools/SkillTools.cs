using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Core.Results;
using WindowsComputerUseMCP.Server.Hosting;
using WindowsComputerUseMCP.Skills;
using WindowsComputerUseMCP.Skills.Abstractions;

namespace WindowsComputerUseMCP.Server.Tools;

/// <summary>
/// 「アプリスキルパック」フレームワークのMCPツール表面。
/// 各アプリ固有の詳細（Blenderの TCP ブリッジ、Adobe/Clipchamp向けの UI Automation 操作等）は
/// <see cref="ISkillPack"/> 実装側に隠蔽し、ここでは共通の一覧・実行ディスパッチのみを行う。
/// </summary>
[McpServerToolType]
public static class SkillTools
{
    [McpServerTool(Name = "skill_list_apps")]
    [Description("スキルパック（アプリ固有の操作機能）が登録されている全アプリの一覧を返す（appId, 表示名, 対象プロセス名を含む）。")]
    public static Task<OperationResult<IReadOnlyList<SkillAppSummary>>> SkillListApps(
        SkillRegistry registry,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<SkillRegistry> logger,
        CancellationToken cancellationToken = default)
    {
        return ToolRunner.RunAsync(
            "skill_list_apps",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "skill_list_apps" },
            () => Task.FromResult<IReadOnlyList<SkillAppSummary>>(
                registry.ListPacks()
                    .Select(p => new SkillAppSummary { AppId = p.AppId, DisplayName = p.DisplayName, ProcessNames = p.ProcessNames })
                    .ToList()));
    }

    [McpServerTool(Name = "skill_list_actions")]
    [Description("指定したアプリ（skill_list_appsで返るappId）が提供するアクション一覧を返す（名前・説明・パラメーター定義を含む）。")]
    public static Task<OperationResult<IReadOnlyList<SkillActionDescriptor>>> SkillListActions(
        SkillRegistry registry,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<SkillRegistry> logger,
        [Description("対象アプリのID（例: \"blender\", \"clipchamp\", \"photoshop\"）。")] string appId,
        CancellationToken cancellationToken = default)
    {
        return ToolRunner.RunAsync(
            "skill_list_actions",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "skill_list_actions" },
            () =>
            {
                var pack = registry.Find(appId) ?? throw new InvalidOperationException($"未登録のappIdです: {appId}");
                return Task.FromResult(pack.ListActions());
            },
            new Dictionary<string, string?> { ["appId"] = appId });
    }

    [McpServerTool(Name = "skill_run_action")]
    [Description(
        "指定したアプリのスキルアクションを実行する。実行内容はアプリの公式API/スクリプト連携（例: Blenderならソケットブリッジ経由でのシーン操作）を" +
        "最優先し、それが無いアプリ（Adobe製品・Clipchamp等）ではUI Automationおよび画面操作（スクリーンショット・クリック等）を使う。" +
        "argumentsJson はアクション固有パラメーターのJSONオブジェクト文字列（例: '{\"code\": \"...\"}'）。省略時は空引数として扱う。")]
    public static async Task<OperationResult<SkillActionOutcome>> SkillRunAction(
        SkillRegistry registry,
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<SkillRegistry> logger,
        [Description("対象アプリのID（例: \"blender\", \"clipchamp\", \"photoshop\"）。")] string appId,
        [Description("実行するアクション名（skill_list_actionsで確認できる）。")] string actionName,
        [Description("アクション固有パラメーターのJSONオブジェクト文字列。省略可。")] string? argumentsJson = null,
        CancellationToken cancellationToken = default)
    {
        var pack = registry.Find(appId);
        if (pack is null)
        {
            var operationId = Core.Diagnostics.OperationIdGenerator.NewId();
            return OperationResult<SkillActionOutcome>.Fail(
                operationId, DateTimeOffset.UtcNow, ErrorCodes.InvalidArgument, $"未登録のappIdです: {appId}");
        }

        IReadOnlyDictionary<string, object?> arguments;
        try
        {
            arguments = ParseArguments(argumentsJson);
        }
        catch (JsonException ex)
        {
            var operationId = Core.Diagnostics.OperationIdGenerator.NewId();
            return OperationResult<SkillActionOutcome>.Fail(
                operationId, DateTimeOffset.UtcNow, ErrorCodes.InvalidArgument, $"argumentsJson の解析に失敗しました: {ex.Message}");
        }

        var safetyRequest = await SafetyContextResolver.BuildAsync(
            windowService,
            $"skill_run_action:{appId}.{actionName}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ToolRunner.RunAsync(
            "skill_run_action",
            safetyPolicy,
            auditLog,
            logger,
            safetyRequest,
            async () =>
            {
                try
                {
                    return await pack.InvokeAsync(actionName, arguments, cancellationToken).ConfigureAwait(false);
                }
                catch (SkillArgumentException ex)
                {
                    return SkillActionOutcome.Fail(ex.Message);
                }
            },
            new Dictionary<string, string?> { ["appId"] = appId, ["actionName"] = actionName },
            applyRateLimit: true).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, object?> ParseArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new Dictionary<string, object?>();
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson);
        return parsed ?? new Dictionary<string, object?>();
    }
}

/// <summary>skill_list_apps の戻り値1件分。</summary>
public sealed record SkillAppSummary
{
    public required string AppId { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<string> ProcessNames { get; init; }
}
