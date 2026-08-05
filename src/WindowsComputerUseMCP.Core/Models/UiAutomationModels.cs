namespace WindowsComputerUseMCP.Core.Models;

/// <summary><c>ui_get_tree</c> ツールの要求パラメーター。</summary>
public sealed record UiTreeRequest
{
    public required long WindowHandle { get; init; }
    public int MaxDepth { get; init; } = 8;
    public int MaxElements { get; init; } = 500;
    public bool IncludeOffscreen { get; init; }
}

/// <summary><c>ui_get_tree</c> ツールの戻り値。</summary>
public sealed record UiTreeResult
{
    public required IReadOnlyList<UiElementInfo> Elements { get; init; }

    /// <summary>MaxDepth / MaxElements の上限により結果が打ち切られたかどうか。</summary>
    public required bool Truncated { get; init; }
}

/// <summary><c>ui_find</c> ツールの要求パラメーター。</summary>
public sealed record UiFindRequest
{
    public required long WindowHandle { get; init; }
    public string? Name { get; init; }
    public string? AutomationId { get; init; }
    public string? ControlType { get; init; }
    public string? ClassName { get; init; }
    public MatchMode MatchMode { get; init; } = MatchMode.Contains;
    public int MaxDepth { get; init; } = 12;
    public int MaxElements { get; init; } = 200;
}

/// <summary><c>ui_invoke</c> ツールの戻り値。</summary>
public sealed record UiInvokeResult
{
    public required bool Invoked { get; init; }

    /// <summary>実際に使用されたUI Automationパターン名（例: "Invoke"）。</summary>
    public string? PatternUsed { get; init; }

    /// <summary>未対応だった場合の理由。座標クリックへの自動フォールバックは行わない。</summary>
    public string? Reason { get; init; }
}
