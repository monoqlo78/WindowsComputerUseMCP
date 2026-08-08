using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Skills.Abstractions;

namespace WindowsComputerUseMCP.Skills.Slack;

/// <summary>
/// Slack用スキルパック。読み取り専用のWeb API（conversations.list / conversations.history 等）のみを
/// 呼び出し、投稿・リアクション・編集・アップロードといった書き込み系アクションは一切提供しない。
///
/// SLACK_BOT_TOKEN 環境変数からBot Tokenを読み取る。未設定の場合はDI構築時ではなく、
/// 各アクション呼び出し時に分かりやすい失敗メッセージを返す（サーバー起動自体は妨げない）。
/// </summary>
public sealed class SlackSkillPack(ILogger<SlackSkillPack> logger) : ISkillPack
{
    public string AppId => "slack";

    public string DisplayName => "Slack (read-only)";

    public IReadOnlyList<string> ProcessNames => [];

    public IReadOnlyList<SkillActionDescriptor> ListActions() =>
    [
        new SkillActionDescriptor
        {
            Name = "is_token_configured",
            Description = "SLACK_BOT_TOKEN 環境変数が設定されているかどうかだけを確認する（値は返さない）。",
        },
        new SkillActionDescriptor
        {
            Name = "list_channels",
            Description = "ワークスペースの全チャンネル一覧を取得する（読み取り専用）。",
        },
        new SkillActionDescriptor
        {
            Name = "find_relevant_channels",
            Description = "運営告知・提出・審査等に関連しそうなチャンネルをチャンネル名/トピック/目的から推測して返す。",
            Parameters = [new SkillParameterDescriptor { Name = "nameFilters", Type = "string[]", Required = false, Description = "チャンネル名フィルターのキーワード一覧（省略時は既定セット）" }],
        },
        new SkillActionDescriptor
        {
            Name = "scan_channel",
            Description = "指定した1チャンネルの履歴を取得し、手続き/承認/TODO/キーワード一致を解析してJSON/Markdown artifactを書き出す（読み取り専用）。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "channel", Type = "string", Required = true, Description = "チャンネルID(C...)またはチャンネル名(#name)" },
                new SkillParameterDescriptor { Name = "days", Type = "int", Required = false, Description = "遡る日数（既定14）" },
                new SkillParameterDescriptor { Name = "outputDirectory", Type = "string", Required = false, Description = "artifact出力先ディレクトリ（既定 ./artifacts）" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "scan_workspace",
            Description = "関連チャンネルを自動探索し、まとめて履歴取得・解析してJSON/Markdown artifactを書き出す（読み取り専用）。一致0件の場合は自動的に遡及期間を延長する。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "outputDirectory", Type = "string", Required = false, Description = "artifact出力先ディレクトリ（既定 ./artifacts）" },
                new SkillParameterDescriptor { Name = "initialLookbackDays", Type = "int", Required = false, Description = "初期の遡及日数（既定14）" },
                new SkillParameterDescriptor { Name = "extendedLookbackDays", Type = "int", Required = false, Description = "一致0件時に延長する遡及日数（既定90）" },
            ],
        },
    ];

    public async Task<SkillActionOutcome> InvokeAsync(
        string actionName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return actionName switch
            {
                "is_token_configured" => IsTokenConfigured(),
                "list_channels" => await ListChannelsAsync(cancellationToken).ConfigureAwait(false),
                "find_relevant_channels" => await FindRelevantChannelsAsync(arguments, cancellationToken).ConfigureAwait(false),
                "scan_channel" => await ScanChannelAsync(arguments, cancellationToken).ConfigureAwait(false),
                "scan_workspace" => await ScanWorkspaceAsync(arguments, cancellationToken).ConfigureAwait(false),
                _ => SkillActionOutcome.Fail($"未知のアクションです: {actionName}"),
            };
        }
        catch (SkillArgumentException ex)
        {
            return SkillActionOutcome.Fail(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // SlackReaderのコンストラクターがトークン未設定時にスローする例外。
            return SkillActionOutcome.Fail(ex.Message);
        }
        catch (SlackApiException ex)
        {
            return SkillActionOutcome.Fail($"Slack APIエラー: {ex.ErrorCode}");
        }
    }

    private static SkillActionOutcome IsTokenConfigured()
    {
        var configured = SlackReader.IsBotTokenConfigured();
        return SkillActionOutcome.Ok(new { configured },
            configured ? "SLACK_BOT_TOKEN が設定されています。" : "SLACK_BOT_TOKEN が未設定です。実スキャンは実行できません。");
    }

    private SlackReader CreateReader() => new(logger: logger);

    private async Task<SkillActionOutcome> ListChannelsAsync(CancellationToken cancellationToken)
    {
        using var reader = CreateReader();
        var channels = await reader.ListChannelsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return SkillActionOutcome.Ok(channels, $"{channels.Count} 件のチャンネルを取得しました。");
    }

    private async Task<SkillActionOutcome> FindRelevantChannelsAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        using var reader = CreateReader();
        var scanner = new SlackWorkspaceScanner(reader, logger);
        var filters = arguments.GetStringList("nameFilters");
        var candidates = await scanner.FindRelevantChannelsAsync(filters.Count > 0 ? filters : null, cancellationToken).ConfigureAwait(false);
        return SkillActionOutcome.Ok(candidates, $"{candidates.Count} 件の関連候補チャンネルが見つかりました。");
    }

    private async Task<SkillActionOutcome> ScanChannelAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var channelArg = arguments.GetRequiredString("channel");
        var days = arguments.GetInt("days", 14)!.Value;
        var outputDirectory = arguments.GetString("outputDirectory", "artifacts")!;

        using var reader = CreateReader();
        var channels = await reader.ListChannelsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var target = channels.FirstOrDefault(ch =>
            ch.Id.Equals(channelArg, StringComparison.OrdinalIgnoreCase)
            || (channelArg.StartsWith('#') && ("#" + ch.Name).Equals(channelArg, StringComparison.OrdinalIgnoreCase))
            || ch.Name.Equals(channelArg.TrimStart('#'), StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            return SkillActionOutcome.Fail($"チャンネル '{channelArg}' が見つかりませんでした。候補: {string.Join(',', channels.Select(c => c.Name).Take(10))}");
        }

        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-days);
        var messages = await reader.GetChannelHistoryAsync(target.Id, fromUtc, toUtc, cancellationToken: cancellationToken).ConfigureAwait(false);
        var analysis = SlackMessageAnalyzer.Analyze(target, messages, fromUtc, toUtc);

        var report = new SlackWorkspaceReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Mode = "live",
            WorkspaceName = await reader.TryGetWorkspaceNameAsync(cancellationToken).ConfigureAwait(false),
            ChannelNameFilters = [channelArg],
            CandidateChannels = [new CandidateChannelInfo { Id = target.Id, Name = target.Name, IsPrivate = target.IsPrivate, MatchedBy = "指定チャンネル" }],
            ChannelAnalyses = [analysis],
            Notes = [],
        };

        var (jsonPath, mdPath) = await SlackWorkspaceScanner.WriteArtifactsAsync(report, outputDirectory, cancellationToken).ConfigureAwait(false);
        return SkillActionOutcome.Ok(new { json = jsonPath, markdown = mdPath, summary = analysis.Summary },
            $"チャンネル #{target.Name} を {messages.Count} 件解析しました。Artifacts: {jsonPath}, {mdPath}");
    }

    private async Task<SkillActionOutcome> ScanWorkspaceAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var outputDirectory = arguments.GetString("outputDirectory", "artifacts")!;
        var initialLookbackDays = arguments.GetInt("initialLookbackDays", 14)!.Value;
        var extendedLookbackDays = arguments.GetInt("extendedLookbackDays", 90)!.Value;

        using var reader = CreateReader();
        var scanner = new SlackWorkspaceScanner(reader, logger);
        var report = await scanner.ScanWorkspaceAsync(
            initialLookbackDays: initialLookbackDays,
            extendedLookbackDays: extendedLookbackDays,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var (jsonPath, mdPath) = await SlackWorkspaceScanner.WriteArtifactsAsync(report, outputDirectory, cancellationToken).ConfigureAwait(false);
        return SkillActionOutcome.Ok(new { json = jsonPath, markdown = mdPath, channelsAnalyzed = report.ChannelAnalyses.Count },
            $"{report.CandidateChannels.Count} 件の候補チャンネルを解析しました。Artifacts: {jsonPath}, {mdPath}");
    }
}
