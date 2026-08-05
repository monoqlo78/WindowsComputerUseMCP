using System.Threading;
using Microsoft.Extensions.Options;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Safety;

/// <summary>
/// SECURITY.md のSafetyポリシーを実装する。危険操作キーワード判定、パスワード欄拒否、
/// UAC/セキュアデスクトップ拒否、許可/拒否プロセスリスト、連続操作回数制限、緊急停止ゲートを担う。
/// </summary>
public sealed class SafetyPolicyService : ISafetyPolicyService
{
    private readonly IOptionsMonitor<WindowsComputerUseMcpOptions> _options;
    private readonly IEmergencyStopService _emergencyStop;
    private readonly Lock _rateLimitLock = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _recentOperations = new();
    private DateTimeOffset _lastOperationAt = DateTimeOffset.MinValue;

    /// <summary>緊急停止が有効な間も引き続き許可する読み取り専用ツール名。</summary>
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "system_get_capabilities",
        "window_list",
        "screen_capture",
        "ui_get_tree",
        "ui_find",
    };

    public SafetyPolicyService(IOptionsMonitor<WindowsComputerUseMcpOptions> options, IEmergencyStopService emergencyStop)
    {
        _options = options;
        _emergencyStop = emergencyStop;
    }

    public SafetyDecision Evaluate(SafetyCheckRequest request)
    {
        var settings = _options.CurrentValue.Safety;

        // 1. 緊急停止: 読み取り専用ツール以外はすべて拒否する。
        if (_emergencyStop.IsActive && !ReadOnlyTools.Contains(request.ToolName))
        {
            return SafetyDecision.Deny("緊急停止が有効なため操作を拒否しました。", "EmergencyStop");
        }

        // 2. UAC・セキュアデスクトップ等、操作対象外のサーフェス。
        if (request.IsProtectedSurface)
        {
            return SafetyDecision.Deny("UACダイアログまたはセキュアデスクトップは操作対象外です。", "ProtectedSurface");
        }

        // 3. パスワード入力欄。
        if (request.IsPasswordField && !settings.AllowPasswordFieldInput)
        {
            return SafetyDecision.Deny("パスワード入力欄への自動入力は既定で拒否されています。", "PasswordField");
        }

        // 4. プロセス許可/拒否リスト。
        if (!string.IsNullOrEmpty(request.ProcessName))
        {
            if (settings.DeniedProcesses.Contains(request.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                return SafetyDecision.Deny($"プロセス '{request.ProcessName}' は拒否リストに含まれています。", "DeniedProcess");
            }

            if (settings.AllowedProcesses.Count > 0 &&
                !settings.AllowedProcesses.Contains(request.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                return SafetyDecision.Deny($"プロセス '{request.ProcessName}' は許可リストに含まれていません。", "NotAllowedProcess");
            }
        }

        // 5. マウス/キーボード操作の大域スイッチ。
        if (IsMouseTool(request.ToolName) && !settings.AllowMouseClicks)
        {
            return SafetyDecision.Deny("マウスクリック操作は設定で無効化されています。", "MouseDisabled");
        }

        if (IsKeyboardTool(request.ToolName) && !settings.AllowKeyboardInput)
        {
            return SafetyDecision.Deny("キーボード入力操作は設定で無効化されています。", "KeyboardDisabled");
        }

        // 6. 危険操作カテゴリのキーワード検出。
        var category = DangerousActionKeywords.DetectCategory(request.InspectionTexts);
        if (category is not null && settings.RequireConfirmationForDangerousActions && !request.CallerAcknowledgedConfirmation)
        {
            return SafetyDecision.Confirm($"危険操作カテゴリ '{category}' に該当するため、実行前に承認が必要です。", category);
        }

        return SafetyDecision.Allow(category);
    }

    public SafetyDecision CheckRateLimit(string toolName)
    {
        var settings = _options.CurrentValue.Safety;
        var now = DateTimeOffset.UtcNow;

        lock (_rateLimitLock)
        {
            if (settings.MinOperationIntervalMs > 0 && _lastOperationAt != DateTimeOffset.MinValue)
            {
                var elapsedMs = (now - _lastOperationAt).TotalMilliseconds;
                if (elapsedMs < settings.MinOperationIntervalMs)
                {
                    return SafetyDecision.Deny(
                        $"操作間隔が短すぎます（最小 {settings.MinOperationIntervalMs}ms）。",
                        "RateLimitInterval");
                }
            }

            if (!_recentOperations.TryGetValue(toolName, out var queue))
            {
                queue = new Queue<DateTimeOffset>();
                _recentOperations[toolName] = queue;
            }

            var windowStart = now - TimeSpan.FromSeconds(settings.RateLimitWindowSeconds);
            while (queue.Count > 0 && queue.Peek() < windowStart)
            {
                queue.Dequeue();
            }

            if (queue.Count >= settings.MaxConsecutiveOperations)
            {
                return SafetyDecision.Deny(
                    $"直近{settings.RateLimitWindowSeconds}秒間の連続操作回数が上限（{settings.MaxConsecutiveOperations}回）を超えました。",
                    "RateLimitCount");
            }

            queue.Enqueue(now);
            _lastOperationAt = now;
        }

        return SafetyDecision.Allow();
    }

    private static bool IsMouseTool(string toolName) =>
        toolName.StartsWith("mouse_", StringComparison.OrdinalIgnoreCase);

    private static bool IsKeyboardTool(string toolName) =>
        toolName.StartsWith("keyboard_", StringComparison.OrdinalIgnoreCase);
}
