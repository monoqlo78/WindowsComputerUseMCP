using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Diagnostics;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Core.Results;

namespace WindowsComputerUseMCP.Server.Hosting;

/// <summary>
/// すべての MCP ツール実装が共通で行う処理（操作ID発行、Safety判定、実行、監査ログ記録、
/// 例外の <see cref="OperationResult{TData}"/> への変換）を集約するヘルパー。
/// </summary>
public static class ToolRunner
{
    /// <summary>
    /// 読み取り専用ツール向け: Safetyポリシーの危険操作判定・レート制限は行わず、緊急停止時のみ拒否する
    /// （<see cref="ISafetyPolicyService.Evaluate"/> 側でReadOnlyToolsは緊急停止中も許可される）。
    /// </summary>
    public static async Task<OperationResult<TData>> RunAsync<TData>(
        string toolName,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger logger,
        SafetyCheckRequest? safetyRequest,
        Func<Task<TData>> action,
        IReadOnlyDictionary<string, string?>? sanitizedArgs = null,
        bool applyRateLimit = false)
    {
        var operationId = OperationIdGenerator.NewId();
        var startedAt = DateTimeOffset.UtcNow;
        var args = sanitizedArgs ?? new Dictionary<string, string?>();

        var effectiveRequest = safetyRequest ?? new SafetyCheckRequest { ToolName = toolName };
        var decision = safetyPolicy.Evaluate(effectiveRequest);

        if (!decision.Allowed)
        {
            var errorCode = decision.RequiresConfirmation ? ErrorCodes.ConfirmationRequired : ErrorCodes.Denied;
            await LogAsync(auditLog, operationId, toolName, args, decision.RequiresConfirmation ? "ConfirmationRequired" : "Denied", startedAt, decision, effectiveRequest).ConfigureAwait(false);
            return OperationResult<TData>.Fail(operationId, startedAt, errorCode, decision.Reason ?? "操作は許可されませんでした。");
        }

        if (applyRateLimit)
        {
            var rateDecision = safetyPolicy.CheckRateLimit(toolName);
            if (!rateDecision.Allowed)
            {
                await LogAsync(auditLog, operationId, toolName, args, "Denied", startedAt, rateDecision, effectiveRequest).ConfigureAwait(false);
                return OperationResult<TData>.Fail(operationId, startedAt, ErrorCodes.RateLimited, rateDecision.Reason ?? "レート制限により拒否されました。");
            }
        }

        try
        {
            var data = await action().ConfigureAwait(false);
            await LogAsync(auditLog, operationId, toolName, args, "Success", startedAt, decision, effectiveRequest).ConfigureAwait(false);
            return OperationResult<TData>.Ok(operationId, startedAt, data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ツール {ToolName} の実行中にエラーが発生しました。OperationId={OperationId}", toolName, operationId);
            await LogAsync(auditLog, operationId, toolName, args, "Failure", startedAt, decision, effectiveRequest).ConfigureAwait(false);
            return OperationResult<TData>.Fail(operationId, startedAt, ErrorCodes.Unknown, "操作の実行中にエラーが発生しました。詳細はサーバーログを参照してください。");
        }
    }

    private static Task LogAsync(
        IAuditLogService auditLog,
        string operationId,
        string toolName,
        IReadOnlyDictionary<string, string?> args,
        string result,
        DateTimeOffset startedAt,
        SafetyDecision decision,
        SafetyCheckRequest? context = null)
    {
        var entry = new AuditLogEntry
        {
            OperationId = operationId,
            Timestamp = DateTimeOffset.UtcNow,
            ToolName = toolName,
            TargetWindow = context?.WindowTitle,
            TargetProcess = context?.ProcessName,
            SanitizedArguments = args,
            Result = result,
            DurationMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            SafetyDecision = decision.Allowed
                ? "Allowed"
                : decision.RequiresConfirmation
                    ? $"ConfirmationRequired:{decision.Category}"
                    : $"Denied:{decision.Category}",
        };

        return auditLog.LogAsync(entry);
    }
}
