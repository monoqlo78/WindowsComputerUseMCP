namespace WindowsComputerUseMCP.Core.Models;

/// <summary><c>screen_capture</c> ツールの要求パラメーター。</summary>
public sealed record ScreenCaptureRequest
{
    public int? MonitorIndex { get; init; }
    public long? WindowHandle { get; init; }
    public bool IncludeCursor { get; init; }
    public bool SaveToFile { get; init; }
}

/// <summary><c>screen_capture</c> ツールの戻り値。</summary>
public sealed record ScreenCaptureResult
{
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>キャプチャ対象の説明（例: "Monitor:0", "Window:12345", "VirtualScreen"）。</summary>
    public required string Target { get; init; }

    public required string MimeType { get; init; }

    /// <summary><see cref="ScreenCaptureRequest.SaveToFile"/> が true の場合の保存先パス。</summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// MCPクライアントが直接画像を扱えない場合のための Base64 エンコード画像データ。
    /// サイズが大きすぎる場合は省略されることがある。
    /// </summary>
    public string? ImageBase64 { get; init; }
}
