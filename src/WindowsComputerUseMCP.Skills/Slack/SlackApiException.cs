namespace WindowsComputerUseMCP.Skills.Slack;

/// <summary>
/// Slack Web API が <c>ok: false</c> を返した場合、または通信自体に失敗した場合にスローする例外。
/// エラーメッセージにはSlackのエラーコード（例: "channel_not_found", "invalid_auth"）のみを含め、
/// トークン等の秘匿情報は一切含めない。
/// </summary>
public class SlackApiException : Exception
{
    public SlackApiException(string errorCode, string? message = null)
        : base(message ?? $"Slack APIがエラーを返しました: {errorCode}")
    {
        ErrorCode = errorCode;
    }

    /// <summary>Slackが返したエラーコード（"ok": false 時の "error" フィールド）。</summary>
    public string ErrorCode { get; }
}

/// <summary>
/// レート制限（HTTP 429 / Retry-Afterヘッダー）への対応でリトライを重ねたが、
/// 上限回数に達しても成功しなかった場合にスローする。
/// </summary>
public sealed class SlackRateLimitExceededException(int maxRetries)
    : SlackApiException("rate_limited", $"Slack APIのレート制限（HTTP 429）が続いたため、リトライ上限（{maxRetries}回）を超えて処理を中断しました。");
