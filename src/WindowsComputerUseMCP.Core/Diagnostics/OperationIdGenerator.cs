namespace WindowsComputerUseMCP.Core.Diagnostics;

/// <summary>
/// 操作ID (operationId) を採番するユーティリティ。
/// 監査ログとMCPツール応答を突き合わせるためのキーとして使用する。
/// </summary>
public static class OperationIdGenerator
{
    /// <summary>
    /// ソート可能な時刻プレフィックスとGUIDを組み合わせた一意な操作IDを生成する。
    /// </summary>
    public static string NewId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        var unique = Guid.NewGuid().ToString("N")[..12];
        return $"op_{timestamp}_{unique}";
    }
}
