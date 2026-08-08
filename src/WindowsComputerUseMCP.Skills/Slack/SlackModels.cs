using System.Globalization;

namespace WindowsComputerUseMCP.Skills.Slack;

/// <summary>Slackチャンネル1件分の情報。</summary>
public sealed record SlackChannel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsPrivate { get; init; }
    public bool IsArchived { get; init; }
    public string? Topic { get; init; }
    public string? Purpose { get; init; }
}

/// <summary>メッセージに付いたリアクション1件分（絵文字名 + 押したユーザーID一覧）。</summary>
public sealed record SlackReaction
{
    public required string Name { get; init; }
    public IReadOnlyList<string> Users { get; init; } = [];
    public int Count { get; init; }
}

/// <summary>
/// conversations.history / conversations.replies で取得できるメッセージ1件分。
/// 生のJSONは保持せず、解析に必要なフィールドのみを保持する（不要な情報をartifactに含めないため）。
/// </summary>
public sealed record SlackMessage
{
    /// <summary>Slackのメッセージタイムスタンプ（例: "1700000000.000100"）。メッセージの一意IDも兼ねる。</summary>
    public required string Ts { get; init; }

    public string? User { get; init; }

    public required string Text { get; init; }

    /// <summary>スレッド返信の場合、親メッセージのts。スレッド先頭メッセージ自身の場合はTsと同じ値になることがある。</summary>
    public string? ThreadTs { get; init; }

    public IReadOnlyList<SlackReaction> Reactions { get; init; } = [];

    /// <summary>Tsを実時刻（UTC）に変換したもの。</summary>
    public DateTimeOffset Timestamp => SlackTimestamp.ToDateTimeOffset(Ts);
}

/// <summary>Slackの ts 文字列（Unix epoch秒 + マイクロ秒）とDateTimeOffsetの相互変換ヘルパー。</summary>
public static class SlackTimestamp
{
    public static DateTimeOffset ToDateTimeOffset(string ts)
    {
        if (double.TryParse(ts, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
        }

        return DateTimeOffset.UnixEpoch;
    }

    public static string FromDateTimeOffset(DateTimeOffset value)
    {
        var seconds = value.ToUnixTimeMilliseconds() / 1000.0;
        return seconds.ToString("F6", CultureInfo.InvariantCulture);
    }
}
