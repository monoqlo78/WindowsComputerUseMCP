namespace WindowsComputerUseMCP.Core.Models;

/// <summary>Safetyポリシー判定のための入力情報。</summary>
public sealed record SafetyCheckRequest
{
    /// <summary>呼び出されたMCPツール名（例: "mouse_click"）。</summary>
    public required string ToolName { get; init; }

    public string? ProcessName { get; init; }
    public string? WindowTitle { get; init; }

    /// <summary>クリック対象や入力先がパスワード入力欄と判定されている場合 true。</summary>
    public bool IsPasswordField { get; init; }

    /// <summary>UACやセキュアデスクトップ等、操作対象外のウィンドウ/デスクトップである場合 true。</summary>
    public bool IsProtectedSurface { get; init; }

    /// <summary>呼び出し元が明示的に承認済みとして渡してきたか（例: requireConfirmation 引数）。</summary>
    public bool CallerAcknowledgedConfirmation { get; init; }

    /// <summary>判定に使うテキスト（ボタン名、入力テキストの有無等）。機密情報そのものは含めないこと。</summary>
    public IReadOnlyList<string> InspectionTexts { get; init; } = [];
}

/// <summary>Safetyポリシーの判定結果。</summary>
public sealed record SafetyDecision
{
    public required bool Allowed { get; init; }
    public required bool RequiresConfirmation { get; init; }
    public string? Reason { get; init; }
    public string? Category { get; init; }

    public static SafetyDecision Allow(string? category = null) =>
        new() { Allowed = true, RequiresConfirmation = false, Category = category };

    public static SafetyDecision Deny(string reason, string? category = null) =>
        new() { Allowed = false, RequiresConfirmation = false, Reason = reason, Category = category };

    public static SafetyDecision Confirm(string reason, string? category = null) =>
        new() { Allowed = false, RequiresConfirmation = true, Reason = reason, Category = category };
}
