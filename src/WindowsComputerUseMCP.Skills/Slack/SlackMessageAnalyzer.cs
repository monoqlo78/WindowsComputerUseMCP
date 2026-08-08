using System.Globalization;
using System.Text.RegularExpressions;

namespace WindowsComputerUseMCP.Skills.Slack;

/// <summary>チャンネル内で見つかった「手順・手続き」らしきメッセージの候補。</summary>
public sealed record ProcedureItem
{
    public required string MessageTs { get; init; }
    public required string Snippet { get; init; }
    public string? Author { get; init; }
    public IReadOnlyList<string> Links { get; init; } = [];
}

/// <summary>チャンネル内で見つかった「承認」らしきメッセージの候補（リアクションまたはキーワードで検出）。</summary>
public sealed record ApprovalItem
{
    public required string MessageTs { get; init; }
    public required string Snippet { get; init; }
    public IReadOnlyList<string> ApprovedByUserIds { get; init; } = [];
    public IReadOnlyList<string> ReactionNames { get; init; } = [];
}

/// <summary>チャンネル内で見つかった TODO / 依頼らしきメッセージの候補。</summary>
public sealed record TodoItem
{
    public required string MessageTs { get; init; }
    public required string Text { get; init; }
    public string? Assignee { get; init; }
    public string? DueDateRaw { get; init; }
    public required string Status { get; init; } // "open" | "overdue"
}

/// <summary>探索対象キーワード（"締切" "提出" 等）に一致したメッセージの候補。</summary>
public sealed record KeywordHit
{
    public required string Keyword { get; init; }
    public required string MessageTs { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string Snippet { get; init; }
    public string? Author { get; init; }
}

/// <summary>1チャンネル分の解析結果。</summary>
public sealed record SlackChannelAnalysis
{
    public required string ChannelId { get; init; }
    public required string ChannelName { get; init; }
    public required DateTime ScannedFromUtc { get; init; }
    public required DateTime ScannedToUtc { get; init; }
    public required int MessagesAnalyzed { get; init; }
    public required IReadOnlyList<ProcedureItem> Procedures { get; init; }
    public required IReadOnlyList<ApprovalItem> Approvals { get; init; }
    public required IReadOnlyList<TodoItem> Todos { get; init; }
    public required IReadOnlyList<KeywordHit> KeywordHits { get; init; }
    public required IReadOnlyList<string> Summary { get; init; }
}

/// <summary>
/// <see cref="SlackMessage"/> の集合から、手続き・承認・TODO・キーワード一致を抽出する純粋なロジック。
/// HTTP通信を一切行わないため、ユニットテストで固定メッセージ集合を渡して検証できる。
/// </summary>
public static class SlackMessageAnalyzer
{
    /// <summary>ハッカソン運営告知でよく使われる探索キーワードの既定セット。</summary>
    public static readonly IReadOnlyList<string> DefaultSearchKeywords =
    [
        "最終成果物", "提出", "作品", "手順", "手続き", "締切", "期限", "フォーム",
        "First Stage", "2分", "収録", "審査", "URL", "GitHub", "デモ", "承認",
    ];

    private static readonly string[] ProcedureKeywords = ["手順", "手続き", "手順書", "やること", "作業手順", "実施方法"];

    private static readonly Regex TodoPattern = new(@"\b(TODO|todo)\b|やること|お願いします|お願いいたします", RegexOptions.Compiled);
    private static readonly Regex DuePattern = new(@"(?:due[:\s]?|期限[:\s]?|締切[:\s]?|までに[:\s]?)(?<date>[^\n、。]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AssigneePattern = new(@"@(?<user>[A-Za-z0-9._\-]+)", RegexOptions.Compiled);
    private static readonly Regex ApprovePattern = new(@"\b(approve|approved)\b|承認|確認済み|✅", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UrlPattern = new(@"https?://[^\s>)]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] ApprovalReactionNames = ["white_check_mark", "heavy_check_mark", "ok", "+1", "thumbsup"];

    public static SlackChannelAnalysis Analyze(
        SlackChannel channel,
        IReadOnlyList<SlackMessage> messages,
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<string>? searchKeywords = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(messages);

        var keywords = searchKeywords ?? DefaultSearchKeywords;

        var procedures = new List<ProcedureItem>();
        var approvals = new List<ApprovalItem>();
        var todos = new List<TodoItem>();
        var keywordHits = new List<KeywordHit>();

        foreach (var message in messages)
        {
            var text = message.Text ?? string.Empty;

            if (ProcedureKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                procedures.Add(new ProcedureItem
                {
                    MessageTs = message.Ts,
                    Snippet = Snippet(text),
                    Author = message.User,
                    Links = ExtractLinks(text),
                });
            }

            if (TodoPattern.IsMatch(text))
            {
                string? assignee = null;
                var assigneeMatch = AssigneePattern.Match(text);
                if (assigneeMatch.Success)
                {
                    assignee = assigneeMatch.Groups["user"].Value;
                }

                string? dueRaw = null;
                var status = "open";
                var dueMatch = DuePattern.Match(text);
                if (dueMatch.Success)
                {
                    dueRaw = dueMatch.Groups["date"].Value.Trim();
                    if (TryParseLooseDate(dueRaw, message.Timestamp.UtcDateTime, out var dueDate) && dueDate < DateTime.UtcNow)
                    {
                        status = "overdue";
                    }
                }

                todos.Add(new TodoItem
                {
                    MessageTs = message.Ts,
                    Text = Snippet(text),
                    Assignee = assignee,
                    DueDateRaw = dueRaw,
                    Status = status,
                });
            }

            var reactionNames = message.Reactions.Select(r => r.Name).ToList();
            var hasApprovalReaction = reactionNames.Any(r => ApprovalReactionNames.Contains(r, StringComparer.OrdinalIgnoreCase));
            if (ApprovePattern.IsMatch(text) || hasApprovalReaction)
            {
                var approvedBy = message.Reactions
                    .Where(r => ApprovalReactionNames.Contains(r.Name, StringComparer.OrdinalIgnoreCase) || ApprovePattern.IsMatch(text))
                    .SelectMany(r => r.Users)
                    .Distinct()
                    .ToList();

                approvals.Add(new ApprovalItem
                {
                    MessageTs = message.Ts,
                    Snippet = Snippet(text),
                    ApprovedByUserIds = approvedBy,
                    ReactionNames = reactionNames,
                });
            }

            foreach (var keyword in keywords)
            {
                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    keywordHits.Add(new KeywordHit
                    {
                        Keyword = keyword,
                        MessageTs = message.Ts,
                        TimestampUtc = message.Timestamp,
                        Snippet = Snippet(text),
                        Author = message.User,
                    });
                }
            }
        }

        var summary = new List<string>
        {
            $"#{channel.Name} を {fromUtc:yyyy-MM-dd} 〜 {toUtc:yyyy-MM-dd} の範囲で {messages.Count} 件解析しました。",
            $"手続き候補 {procedures.Count} 件、TODO {todos.Count} 件、承認候補 {approvals.Count} 件、キーワード一致 {keywordHits.Count} 件を検出しました。",
        };

        var overdueCount = todos.Count(t => t.Status == "overdue");
        if (overdueCount > 0)
        {
            summary.Add($"期限超過の可能性があるTODOが {overdueCount} 件あります。要確認です。");
        }

        return new SlackChannelAnalysis
        {
            ChannelId = channel.Id,
            ChannelName = channel.Name,
            ScannedFromUtc = fromUtc,
            ScannedToUtc = toUtc,
            MessagesAnalyzed = messages.Count,
            Procedures = procedures,
            Approvals = approvals,
            Todos = todos,
            KeywordHits = keywordHits,
            Summary = summary,
        };
    }

    private static IReadOnlyList<string> ExtractLinks(string text) =>
        UrlPattern.Matches(text).Select(m => m.Value).ToList();

    private static string Snippet(string text, int max = 240) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, max), "...");

    /// <summary>
    /// "8/20", "8月20日", "2026-08-20", "8/20 18:00" 等、日本語の告知でよく使われる緩い日付表記の解析を試みる。
    /// 年が省略されている場合はメッセージ投稿時点の年（またはその翌年、月が過去に戻る場合）を補う。
    /// </summary>
    private static bool TryParseLooseDate(string raw, DateTime messagePostedAtUtc, out DateTime result)
    {
        result = default;
        var cleaned = raw.Trim();

        if (DateTime.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            return true;
        }

        if (DateTime.TryParse(cleaned, CultureInfo.GetCultureInfo("ja-JP"), DateTimeStyles.None, out result))
        {
            return true;
        }

        var match = Regex.Match(cleaned, @"(?<month>\d{1,2})[/月](?<day>\d{1,2})日?(?:\s*(?<hour>\d{1,2}):(?<minute>\d{2}))?");
        if (match.Success)
        {
            var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            var hour = match.Groups["hour"].Success ? int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture) : 23;
            var minute = match.Groups["minute"].Success ? int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture) : 59;

            var year = messagePostedAtUtc.Year;
            try
            {
                result = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        return false;
    }
}
