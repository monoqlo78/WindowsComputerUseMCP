namespace WindowsComputerUseMCP.Core.Models;

/// <summary>ControlPanel → Server へ送るIPCコマンドの種類。</summary>
public enum IpcCommand
{
    /// <summary>現在の緊急停止状態を取得する。</summary>
    Status,

    /// <summary>緊急停止を有効化する。</summary>
    Activate,

    /// <summary>緊急停止を解除する。</summary>
    Deactivate,
}

/// <summary>ControlPanel から Server への名前付きパイプ経由のリクエスト（1行1 JSON）。</summary>
public sealed class IpcRequest
{
    public IpcCommand Command { get; set; }

    /// <summary>Activate 時の理由（監査ログ・UI表示用）。</summary>
    public string? Reason { get; set; }
}

/// <summary>Server から ControlPanel への名前付きパイプ経由のレスポンス（1行1 JSON）。</summary>
public sealed class IpcResponse
{
    public bool Success { get; set; }

    public bool EmergencyStopActive { get; set; }

    public string? Message { get; set; }
}
