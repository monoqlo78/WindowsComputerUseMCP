namespace WindowsComputerUseMCP.Core.Diagnostics;

/// <summary>
/// 操作監査ログ1行分のエントリ（JSON Lines形式で保存される）。
/// 入力文字列の全文やパスワード等の機密情報は含めないこと。
/// </summary>
public sealed record AuditLogEntry
{
    public required string OperationId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string ToolName { get; init; }
    public string? TargetWindow { get; init; }
    public string? TargetProcess { get; init; }

    /// <summary>引数のサニタイズ済み表現（機密値はマスク/ハッシュ化済み）。</summary>
    public required IReadOnlyDictionary<string, string?> SanitizedArguments { get; init; }

    /// <summary>"Success" | "Failure" | "Denied" | "ConfirmationRequired" 等。</summary>
    public required string Result { get; init; }

    public required double DurationMs { get; init; }

    /// <summary>Safetyポリシーの判定内容（許可/拒否/承認要求とカテゴリ）。</summary>
    public required string SafetyDecision { get; init; }
}
