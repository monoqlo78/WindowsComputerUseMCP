namespace WindowsComputerUseMCP.Core.Models;

/// <summary>UI Automation で取得したUI要素1つ分の情報。</summary>
public sealed record UiElementInfo
{
    /// <summary>このセッション内で一意な要素ID。ui_invoke 等で参照するために使用する。</summary>
    public required string ElementId { get; init; }

    public string? Name { get; init; }
    public string? AutomationId { get; init; }
    public string? ControlType { get; init; }
    public string? ClassName { get; init; }
    public required ScreenRect Bounds { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool IsOffscreen { get; init; }

    /// <summary>UIAの IsPassword プロパティ。true の場合、既定でSafetyポリシーが入力操作を拒否する。</summary>
    public bool IsPassword { get; init; }


    /// <summary>この要素がサポートするUI Automationパターン名の一覧（例: "Invoke", "Toggle"）。</summary>
    public required IReadOnlyList<string> SupportedPatterns { get; init; }

    /// <summary>親要素のElementId。ルート要素の場合は null。</summary>
    public string? ParentId { get; init; }
}
