namespace WindowsComputerUseMCP.Core.Results;

/// <summary>
/// すべての MCP ツールが共通で返却する結果モデル。
/// 例外のスタックトレースはここに含めず、診断ログにのみ記録すること。
/// </summary>
/// <typeparam name="TData">操作固有の戻り値データの型。</typeparam>
public sealed class OperationResult<TData>
{
    /// <summary>操作が成功したかどうか。</summary>
    public required bool Success { get; init; }

    /// <summary>この操作を一意に識別するID（監査ログと突き合わせ可能）。</summary>
    public required string OperationId { get; init; }

    /// <summary>人間が読める要約メッセージ。</summary>
    public string? Message { get; init; }

    /// <summary>操作固有の戻り値データ。失敗時は既定で null。</summary>
    public TData? Data { get; init; }

    /// <summary>処理は成功したが利用者に伝えるべき警告。</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>失敗時のエラーコード（<see cref="ErrorCodes"/> 参照）。成功時は null。</summary>
    public string? ErrorCode { get; init; }

    /// <summary>操作開始時刻（UTC）。</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>操作完了時刻（UTC）。</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>所要時間（ミリ秒）。</summary>
    public double DurationMs { get; init; }

    /// <summary>成功結果を生成する。</summary>
    public static OperationResult<TData> Ok(
        string operationId,
        DateTimeOffset startedAt,
        TData? data = default,
        string? message = null,
        IReadOnlyList<string>? warnings = null)
    {
        var completedAt = DateTimeOffset.UtcNow;
        return new OperationResult<TData>
        {
            Success = true,
            OperationId = operationId,
            Message = message,
            Data = data,
            Warnings = warnings ?? [],
            ErrorCode = null,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DurationMs = (completedAt - startedAt).TotalMilliseconds,
        };
    }

    /// <summary>失敗結果を生成する。</summary>
    public static OperationResult<TData> Fail(
        string operationId,
        DateTimeOffset startedAt,
        string errorCode,
        string message,
        TData? data = default,
        IReadOnlyList<string>? warnings = null)
    {
        var completedAt = DateTimeOffset.UtcNow;
        return new OperationResult<TData>
        {
            Success = false,
            OperationId = operationId,
            Message = message,
            Data = data,
            Warnings = warnings ?? [],
            ErrorCode = errorCode,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DurationMs = (completedAt - startedAt).TotalMilliseconds,
        };
    }
}
