using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WindowsComputerUseMCP.Skills.Slack;

/// <summary>
/// 候補チャンネルの検出結果1件（解析対象になったかどうかも含む）。
/// </summary>
public sealed record CandidateChannelInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsPrivate { get; init; }
    public required string MatchedBy { get; init; }
}

/// <summary>
/// ワークスペース全体のスキャン結果（複数チャンネルの解析結果 + メタ情報）。
/// JSON/Markdown両方の出力元になる、シリアライズ可能な最終レポート。
/// </summary>
public sealed record SlackWorkspaceReport
{
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>"live"（実際にSlack APIへ接続して取得） または "fixture-no-token"（トークン未設定のためfixtureで検証）。</summary>
    public required string Mode { get; init; }

    public string? WorkspaceName { get; init; }

    public required IReadOnlyList<string> ChannelNameFilters { get; init; }

    public required IReadOnlyList<CandidateChannelInfo> CandidateChannels { get; init; }

    public required IReadOnlyList<SlackChannelAnalysis> ChannelAnalyses { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }
}

/// <summary>
/// 関連チャンネルの自動探索 → 各チャンネルの履歴取得・解析 → JSON/Markdown artifact生成までを
/// 一括で行うオーケストレーター。CLI (<c>WindowsComputerUseMCP.SlackReaderCli</c>) と
/// <see cref="SlackSkillPack"/> の両方から共有される、読み取り専用のロジック本体。
/// </summary>
public sealed class SlackWorkspaceScanner(SlackReader reader, ILogger? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>運営告知系チャンネルをチャンネル名から推測するための既定キーワード（大文字小文字区別なし）。</summary>
    public static readonly IReadOnlyList<string> DefaultChannelNameFilters =
    [
        "announce", "announcement", "general", "info", "notice", "staff", "admin", "operation",
        "運営", "案内", "告知", "事務局", "お知らせ",
        "提出", "submit", "submission", "final",
        "審査", "review", "judge", "judging",
        "hackathon", "ハッカソン",
    ];

    /// <summary>
    /// 全チャンネルを列挙し、名前・トピック・目的のいずれかがキーワードに一致するものを候補として返す。
    /// アーカイブ済みチャンネルは除外する。
    /// </summary>
    public async Task<IReadOnlyList<CandidateChannelInfo>> FindRelevantChannelsAsync(
        IReadOnlyList<string>? nameFilters = null,
        CancellationToken cancellationToken = default)
    {
        var filters = nameFilters ?? DefaultChannelNameFilters;
        var channels = await reader.ListChannelsAsync(excludeArchived: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var candidates = new List<CandidateChannelInfo>();
        foreach (var channel in channels)
        {
            var matched = filters.FirstOrDefault(f =>
                channel.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (channel.Topic?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false)
                || (channel.Purpose?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false));

            if (matched is not null)
            {
                candidates.Add(new CandidateChannelInfo
                {
                    Id = channel.Id,
                    Name = channel.Name,
                    IsPrivate = channel.IsPrivate,
                    MatchedBy = matched,
                });
            }
        }

        return candidates;
    }

    /// <summary>
    /// 関連チャンネルを自動探索し、各チャンネルの履歴を取得・解析してレポートを組み立てる。
    /// 既定の遡及期間（<paramref name="initialLookbackDays"/>）でキーワード一致が0件だった場合、
    /// 自動的に <paramref name="extendedLookbackDays"/> まで遡って再取得する（読み取りのみ・投稿は一切行わない）。
    /// </summary>
    public async Task<SlackWorkspaceReport> ScanWorkspaceAsync(
        IReadOnlyList<string>? channelNameFilters = null,
        IReadOnlyList<string>? searchKeywords = null,
        int initialLookbackDays = 14,
        int extendedLookbackDays = 90,
        CancellationToken cancellationToken = default)
    {
        var filters = channelNameFilters ?? DefaultChannelNameFilters;
        var keywords = searchKeywords ?? SlackMessageAnalyzer.DefaultSearchKeywords;
        var notes = new List<string>();

        var workspaceName = await reader.TryGetWorkspaceNameAsync(cancellationToken).ConfigureAwait(false);
        if (workspaceName is null)
        {
            notes.Add("team.info からワークスペース名を取得できませんでした（team:read スコープ不足の可能性）。");
        }

        var candidates = await FindRelevantChannelsAsync(filters, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            notes.Add("チャンネル名フィルターに一致する候補が見つかりませんでした。フィルター条件の見直しが必要かもしれません。");
        }

        var toUtc = DateTime.UtcNow;
        var analyses = new List<SlackChannelAnalysis>();

        foreach (var candidate in candidates)
        {
            var fromUtc = toUtc.AddDays(-initialLookbackDays);
            var messages = await reader.GetChannelHistoryAsync(candidate.Id, fromUtc, toUtc, cancellationToken: cancellationToken).ConfigureAwait(false);
            var channel = new SlackChannel { Id = candidate.Id, Name = candidate.Name, IsPrivate = candidate.IsPrivate };
            var analysis = SlackMessageAnalyzer.Analyze(channel, messages, fromUtc, toUtc, keywords);

            if (analysis.KeywordHits.Count == 0 && extendedLookbackDays > initialLookbackDays)
            {
                var extendedFromUtc = toUtc.AddDays(-extendedLookbackDays);
                var extendedMessages = await reader.GetChannelHistoryAsync(candidate.Id, extendedFromUtc, toUtc, cancellationToken: cancellationToken).ConfigureAwait(false);
                var extendedAnalysis = SlackMessageAnalyzer.Analyze(channel, extendedMessages, extendedFromUtc, toUtc, keywords);

                if (extendedAnalysis.KeywordHits.Count > 0)
                {
                    notes.Add($"#{candidate.Name}: 直近{initialLookbackDays}日では一致なしのため{extendedLookbackDays}日まで遡って再取得しました。");
                    analysis = extendedAnalysis;
                }
            }

            analyses.Add(analysis);
        }

        return new SlackWorkspaceReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Mode = "live",
            WorkspaceName = workspaceName,
            ChannelNameFilters = filters,
            CandidateChannels = candidates,
            ChannelAnalyses = analyses,
            Notes = notes,
        };
    }

    /// <summary>JSON/Markdown artifactをディレクトリへ書き出す。ファイル名にはタイムスタンプを含める。</summary>
    public static async Task<(string JsonPath, string MarkdownPath)> WriteArtifactsAsync(
        SlackWorkspaceReport report,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var suffix = report.GeneratedAtUtc.ToString("yyyyMMdd-HHmmss");
        var modeTag = report.Mode == "live" ? string.Empty : "-FIXTURE";
        var jsonPath = Path.Combine(outputDirectory, $"slack-workspace-scan{modeTag}-{suffix}.json");
        var mdPath = Path.Combine(outputDirectory, $"slack-workspace-scan{modeTag}-{suffix}.md");

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(report, options);
        await File.WriteAllTextAsync(jsonPath, json, cancellationToken).ConfigureAwait(false);

        var md = BuildMarkdown(report);
        await File.WriteAllTextAsync(mdPath, md, cancellationToken).ConfigureAwait(false);

        return (jsonPath, mdPath);
    }

    private static string BuildMarkdown(SlackWorkspaceReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Slack ワークスペース スキャン結果");
        sb.AppendLine();

        if (report.Mode != "live")
        {
            sb.AppendLine("> ⚠️ **このレポートはfixture（サンプル）データによる検証結果であり、実際のSlackデータではありません。**");
            sb.AppendLine("> SLACK_BOT_TOKEN が未設定のため、実スキャンは実行していません。");
            sb.AppendLine();
        }

        sb.AppendLine($"- 生成日時（UTC）: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- ワークスペース名: {report.WorkspaceName ?? "(不明)"}");
        sb.AppendLine($"- チャンネル名フィルター: {string.Join(", ", report.ChannelNameFilters)}");
        sb.AppendLine($"- 候補チャンネル数: {report.CandidateChannels.Count}");
        sb.AppendLine();

        if (report.Notes.Count > 0)
        {
            sb.AppendLine("## 注意事項");
            foreach (var note in report.Notes)
            {
                sb.AppendLine($"- {note}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## 候補チャンネル");
        foreach (var c in report.CandidateChannels)
        {
            sb.AppendLine($"- #{c.Name} (id: {c.Id}, private: {c.IsPrivate}, matched by: \"{c.MatchedBy}\")");
        }

        sb.AppendLine();

        foreach (var analysis in report.ChannelAnalyses)
        {
            sb.AppendLine($"## #{analysis.ChannelName}");
            sb.AppendLine();
            foreach (var s in analysis.Summary)
            {
                sb.AppendLine($"- {s}");
            }

            sb.AppendLine();

            if (analysis.KeywordHits.Count > 0)
            {
                sb.AppendLine("### キーワード一致");
                foreach (var hit in analysis.KeywordHits.OrderBy(h => h.TimestampUtc).Take(50))
                {
                    sb.AppendLine($"- [{hit.TimestampUtc:yyyy-MM-dd HH:mm}] `{hit.Keyword}`: {hit.Snippet} (author: {hit.Author ?? "(unknown)"}, ts: {hit.MessageTs})");
                }

                sb.AppendLine();
            }

            if (analysis.Procedures.Count > 0)
            {
                sb.AppendLine("### 手続き候補");
                foreach (var p in analysis.Procedures.Take(20))
                {
                    sb.AppendLine($"- {p.Snippet} (ts: {p.MessageTs}, author: {p.Author ?? "(unknown)"})");
                }

                sb.AppendLine();
            }

            if (analysis.Todos.Count > 0)
            {
                sb.AppendLine("### TODO");
                foreach (var t in analysis.Todos.Take(20))
                {
                    sb.AppendLine($"- [{t.Status.ToUpperInvariant()}] {t.Text} (assignee: {t.Assignee ?? "(none)"}, due: {t.DueDateRaw ?? "(none)"})");
                }

                sb.AppendLine();
            }

            if (analysis.Approvals.Count > 0)
            {
                sb.AppendLine("### 承認候補");
                foreach (var a in analysis.Approvals.Take(20))
                {
                    sb.AppendLine($"- {a.Snippet} (ts: {a.MessageTs}, reactions: {string.Join(',', a.ReactionNames)})");
                }

                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine("Generated by WindowsComputerUseMCP SlackReader (read-only)");
        return sb.ToString();
    }
}
