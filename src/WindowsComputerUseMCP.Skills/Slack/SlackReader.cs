using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WindowsComputerUseMCP.Skills.Slack;

/// <summary>
/// Slack Web API の読み取り専用エンドポイント（conversations.list / conversations.history /
/// conversations.replies / team.info）だけを呼び出すクライアント。
///
/// 投稿系（chat.postMessage 等）は意図的に実装しない。読み取り専用スキルの原則
/// （Slackへ書き込み・通知を一切行わない）を型レベルで担保するためである。
///
/// 機能:
/// - カーソルベースのページネーションを自動追従（conversations.list / conversations.history）。
/// - HTTP 429（レート制限）を Retry-After ヘッダーに従って待機・リトライ（既定最大5回）。
/// - 一時的な通信エラー・5xxエラーも指数バックオフでリトライ。
/// - Slackが `ok: false` を返した場合は <see cref="SlackApiException"/> にエラーコードのみを載せてスローする
///   （トークン等の秘匿情報はいかなる例外・ログにも出力しない）。
/// </summary>
public sealed class SlackReader : IDisposable
{
    /// <summary>Bot Token を読み取る環境変数名。</summary>
    public const string BotTokenEnvironmentVariable = "SLACK_BOT_TOKEN";

    private const string BaseUrl = "https://slack.com/api/";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly ILogger _logger;
    private readonly int _maxRetries;
    private bool _disposed;

    /// <param name="token">Slack Bot Token。省略時は <see cref="BotTokenEnvironmentVariable"/> 環境変数から読み取る。</param>
    /// <param name="httpClient">テスト等でHTTP層を差し替えたい場合に指定する。省略時は内部で新規作成し、Disposeで破棄する。</param>
    /// <param name="logger">診断ログ出力先（省略可）。</param>
    /// <param name="maxRetries">レート制限・一時的エラー時の最大リトライ回数（既定5）。</param>
    /// <exception cref="InvalidOperationException">トークンが未指定かつ環境変数も未設定の場合。</exception>
    public SlackReader(string? token = null, HttpClient? httpClient = null, ILogger? logger = null, int maxRetries = 5)
    {
        var resolvedToken = token ?? Environment.GetEnvironmentVariable(BotTokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(resolvedToken))
        {
            throw new InvalidOperationException(
                $"{BotTokenEnvironmentVariable} 環境変数が設定されておらず、トークンも指定されていません。読み取り専用のBot Tokenを用意してください。");
        }

        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resolvedToken);
        _logger = logger ?? NullLogger.Instance;
        _maxRetries = maxRetries;
    }

    /// <summary>
    /// SLACK_BOT_TOKEN が設定されているかどうかだけを確認する（値そのものは一切返さない・ログにも出さない）。
    /// トークンの有無に応じて「実スキャン」と「fixtureによる検証」を切り替える呼び出し元向けのヘルパー。
    /// </summary>
    /// <param name="environmentReader">テスト用に環境変数の取得元を差し替えたい場合に指定する。</param>
    public static bool IsBotTokenConfigured(Func<string, string?>? environmentReader = null)
    {
        var reader = environmentReader ?? Environment.GetEnvironmentVariable;
        return !string.IsNullOrWhiteSpace(reader(BotTokenEnvironmentVariable));
    }

    /// <summary>
    /// ワークスペースの全チャンネル（参加済み・非参加を問わずBotから見える範囲）を列挙する。
    /// カーソルページネーションを自動追従する。
    /// </summary>
    public async Task<IReadOnlyList<SlackChannel>> ListChannelsAsync(
        bool excludeArchived = true,
        string types = "public_channel,private_channel",
        CancellationToken cancellationToken = default)
    {
        var result = new List<SlackChannel>();
        string? cursor = null;

        do
        {
            var query = new Dictionary<string, string?>
            {
                ["limit"] = "200",
                ["types"] = types,
                ["exclude_archived"] = excludeArchived ? "true" : "false",
                ["cursor"] = cursor,
            };

            using var doc = await CallApiAsync("conversations.list", query, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("channels", out var channels))
            {
                foreach (var el in channels.EnumerateArray())
                {
                    result.Add(ParseChannel(el));
                }
            }

            cursor = ReadNextCursor(root);
        } while (!string.IsNullOrEmpty(cursor));

        return result;
    }

    /// <summary>
    /// チャンネルの投稿履歴を取得する。<paramref name="oldestUtc"/>/<paramref name="latestUtc"/> で期間を絞り込め、
    /// カーソルページネーションを自動追従して全件を返す。
    /// </summary>
    public async Task<IReadOnlyList<SlackMessage>> GetChannelHistoryAsync(
        string channelId,
        DateTime? oldestUtc = null,
        DateTime? latestUtc = null,
        int pageLimit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("channelId は必須です。", nameof(channelId));
        }

        var result = new List<SlackMessage>();
        string? cursor = null;
        var oldest = oldestUtc.HasValue ? SlackTimestamp.FromDateTimeOffset(oldestUtc.Value) : null;
        var latest = latestUtc.HasValue ? SlackTimestamp.FromDateTimeOffset(latestUtc.Value) : null;

        do
        {
            var query = new Dictionary<string, string?>
            {
                ["channel"] = channelId,
                ["limit"] = pageLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["oldest"] = oldest,
                ["latest"] = latest,
                ["cursor"] = cursor,
            };

            using var doc = await CallApiAsync("conversations.history", query, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages))
            {
                foreach (var el in messages.EnumerateArray())
                {
                    result.Add(ParseMessage(el));
                }
            }

            cursor = ReadNextCursor(root);
        } while (!string.IsNullOrEmpty(cursor));

        return result;
    }

    /// <summary>スレッド内の返信一覧を取得する（conversations.replies）。ページネーション自動追従。</summary>
    public async Task<IReadOnlyList<SlackMessage>> GetThreadRepliesAsync(
        string channelId,
        string threadTs,
        CancellationToken cancellationToken = default)
    {
        var result = new List<SlackMessage>();
        string? cursor = null;

        do
        {
            var query = new Dictionary<string, string?>
            {
                ["channel"] = channelId,
                ["ts"] = threadTs,
                ["limit"] = "200",
                ["cursor"] = cursor,
            };

            using var doc = await CallApiAsync("conversations.replies", query, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages))
            {
                foreach (var el in messages.EnumerateArray())
                {
                    result.Add(ParseMessage(el));
                }
            }

            cursor = ReadNextCursor(root);
        } while (!string.IsNullOrEmpty(cursor));

        return result;
    }

    /// <summary>
    /// ワークスペース情報（表示名等）を取得する。<c>team:read</c> スコープが無い等で失敗した場合は
    /// 例外を送出せず null を返す（呼び出し元は「取得できなかった」として扱う）。
    /// </summary>
    public async Task<string?> TryGetWorkspaceNameAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = await CallApiAsync("team.info", new Dictionary<string, string?>(), cancellationToken).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("team", out var team) && team.TryGetProperty("name", out var name))
            {
                return name.GetString();
            }
        }
        catch (SlackApiException ex)
        {
            _logger.LogWarning("team.info の取得に失敗しました（続行します）: {ErrorCode}", ex.ErrorCode);
        }

        return null;
    }

    private static SlackChannel ParseChannel(JsonElement el)
    {
        var id = el.GetProperty("id").GetString() ?? string.Empty;
        var name = el.TryGetProperty("name", out var nm) ? nm.GetString() : null;
        var isPrivate = el.TryGetProperty("is_private", out var priv) && priv.ValueKind == JsonValueKind.True;
        var isArchived = el.TryGetProperty("is_archived", out var arch) && arch.ValueKind == JsonValueKind.True;
        string? topic = el.TryGetProperty("topic", out var topicEl) && topicEl.TryGetProperty("value", out var topicVal)
            ? topicVal.GetString()
            : null;
        string? purpose = el.TryGetProperty("purpose", out var purposeEl) && purposeEl.TryGetProperty("value", out var purposeVal)
            ? purposeVal.GetString()
            : null;

        return new SlackChannel
        {
            Id = id,
            Name = name ?? id,
            IsPrivate = isPrivate,
            IsArchived = isArchived,
            Topic = string.IsNullOrEmpty(topic) ? null : topic,
            Purpose = string.IsNullOrEmpty(purpose) ? null : purpose,
        };
    }

    private static SlackMessage ParseMessage(JsonElement el)
    {
        var ts = el.GetProperty("ts").GetString() ?? "0";
        var text = el.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var user = el.TryGetProperty("user", out var u) ? u.GetString() : null;
        var threadTs = el.TryGetProperty("thread_ts", out var tt) ? tt.GetString() : null;

        var reactions = new List<SlackReaction>();
        if (el.TryGetProperty("reactions", out var reactionsEl) && reactionsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in reactionsEl.EnumerateArray())
            {
                var reactionName = r.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                var users = new List<string>();
                if (r.TryGetProperty("users", out var usersEl) && usersEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var uEl in usersEl.EnumerateArray())
                    {
                        var uid = uEl.GetString();
                        if (uid is not null)
                        {
                            users.Add(uid);
                        }
                    }
                }

                var count = r.TryGetProperty("count", out var countEl) && countEl.ValueKind == JsonValueKind.Number
                    ? countEl.GetInt32()
                    : users.Count;

                reactions.Add(new SlackReaction { Name = reactionName, Users = users, Count = count });
            }
        }

        return new SlackMessage
        {
            Ts = ts,
            User = user,
            Text = text,
            ThreadTs = threadTs,
            Reactions = reactions,
        };
    }

    private static string? ReadNextCursor(JsonElement root) =>
        root.TryGetProperty("response_metadata", out var meta) && meta.TryGetProperty("next_cursor", out var nc)
            ? nc.GetString()
            : null;

    private static string BuildUrl(string method, IReadOnlyDictionary<string, string?> query)
    {
        var parts = query
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
        var qs = string.Join("&", parts);
        return qs.Length == 0 ? $"{BaseUrl}{method}" : $"{BaseUrl}{method}?{qs}";
    }

    private static TimeSpan BackoffDelay(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);

    /// <summary>
    /// Slack Web APIを1回呼び出す。HTTP 429（レート制限）・5xx・一時的な通信エラーは
    /// Retry-Afterヘッダー（あれば優先）または指数バックオフで待機してリトライする。
    /// Slackが ok:false を返した場合、または最終的にリトライ上限を超えた場合は例外をスローする。
    /// </summary>
    private async Task<JsonDocument> CallApiAsync(
        string method,
        IReadOnlyDictionary<string, string?> query,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (true)
        {
            attempt++;
            var url = BuildUrl(method, query);
            HttpResponseMessage response;

            try
            {
                response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && attempt <= _maxRetries)
            {
                _logger.LogWarning(ex, "Slack API {Method} の呼び出しに失敗しました。リトライします ({Attempt}/{MaxRetries})。", method, attempt, _maxRetries);
                await DelayAsync(BackoffDelay(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                if (response.StatusCode == (HttpStatusCode)429)
                {
                    if (attempt > _maxRetries)
                    {
                        throw new SlackRateLimitExceededException(_maxRetries);
                    }

                    var retryAfter = ResolveRetryAfter(response) ?? BackoffDelay(attempt);
                    _logger.LogWarning(
                        "Slack APIレート制限（HTTP 429）: {Method}。{Seconds:F1}秒待機してリトライします ({Attempt}/{MaxRetries})。",
                        method, retryAfter.TotalSeconds, attempt, _maxRetries);
                    await DelayAsync(retryAfter, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode >= 500 && attempt <= _maxRetries)
                    {
                        _logger.LogWarning(
                            "Slack API {Method} がHTTP {Status} を返しました。リトライします ({Attempt}/{MaxRetries})。",
                            method, (int)response.StatusCode, attempt, _maxRetries);
                        await DelayAsync(BackoffDelay(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new SlackApiException("http_error", $"Slack API {method} がHTTP {(int)response.StatusCode} を返しました。");
                }

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(body);
                }
                catch (JsonException)
                {
                    throw new SlackApiException("invalid_response", $"Slack API {method} の応答をJSONとして解析できませんでした。");
                }

                if (!doc.RootElement.TryGetProperty("ok", out var okProp) || okProp.ValueKind != JsonValueKind.True)
                {
                    var errorCode = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "unknown_error" : "unknown_error";
                    doc.Dispose();

                    // Slackはまれに ok:false + error:"ratelimited" を200系で返すことがあるため、
                    // HTTPステータスとは別にエラーコードでもリトライ対象を判定する。
                    if (errorCode == "ratelimited" && attempt <= _maxRetries)
                    {
                        var retryAfter = ResolveRetryAfter(response) ?? BackoffDelay(attempt);
                        _logger.LogWarning(
                            "Slack APIレート制限（error=ratelimited）: {Method}。{Seconds:F1}秒待機してリトライします ({Attempt}/{MaxRetries})。",
                            method, retryAfter.TotalSeconds, attempt, _maxRetries);
                        await DelayAsync(retryAfter, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new SlackApiException(errorCode);
                }

                return doc;
            }
        }
    }

    private static TimeSpan? ResolveRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (raw is not null && double.TryParse(raw, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsHttpClient)
        {
            _http.Dispose();
        }

        _disposed = true;
    }
}
