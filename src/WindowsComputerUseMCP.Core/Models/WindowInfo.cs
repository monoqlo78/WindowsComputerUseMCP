namespace WindowsComputerUseMCP.Core.Models;

/// <summary>トップレベルウィンドウ1つ分の情報。</summary>
public sealed record WindowInfo
{
    /// <summary>ウィンドウハンドル（HWND）。JSON表現の都合上 64bit 整数として表す。</summary>
    public required long WindowHandle { get; init; }

    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string Title { get; init; }
    public string? ClassName { get; init; }
    public required ScreenRect Bounds { get; init; }
    public required bool IsVisible { get; init; }
    public required bool IsMinimized { get; init; }
    public required bool IsForeground { get; init; }
}
