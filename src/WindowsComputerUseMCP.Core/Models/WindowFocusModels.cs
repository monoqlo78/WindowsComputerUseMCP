namespace WindowsComputerUseMCP.Core.Models;

/// <summary><c>window_focus</c> ツールの要求パラメーター。</summary>
public sealed record WindowFocusRequest
{
    public long? WindowHandle { get; init; }
    public string? Title { get; init; }
    public MatchMode TitleMatchMode { get; init; } = MatchMode.Contains;
}

/// <summary><c>window_focus</c> ツールの戻り値。</summary>
public sealed record WindowFocusResult
{
    public required bool Focused { get; init; }
    public WindowInfo? Window { get; init; }

    /// <summary>タイトル部分一致等で複数候補が見つかった場合の候補一覧。</summary>
    public IReadOnlyList<WindowInfo> Candidates { get; init; } = [];
}
