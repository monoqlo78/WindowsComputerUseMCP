using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Skills.Slack;
using WindowsComputerUseMCP.SlackReaderCli.Fixtures;

// SlackReaderCli: 読み取り専用のSlackワークスペーススキャンCLI。
// - 投稿・リアクション・編集・アップロード等の書き込み系操作は一切行わない/実装しない。
// - SLACK_BOT_TOKEN 環境変数の「有無」だけを確認する。値そのものはコンソール・ログ・artifactの
//   いずれにも一切出力しない。
// - トークンが無い場合は実スキャンを明確にスキップし、組み込みfixtureデータで
//   JSON/Markdown artifact生成パイプラインの動作のみを検証する。

var outputDirectory = "artifacts";
var initialLookbackDays = 14;
var extendedLookbackDays = 90;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--output" or "-o" when i + 1 < args.Length:
            outputDirectory = args[++i];
            break;
        case "--days" when i + 1 < args.Length && int.TryParse(args[i + 1], out var d):
            initialLookbackDays = d;
            i++;
            break;
        case "--extended-days" when i + 1 < args.Length && int.TryParse(args[i + 1], out var ed):
            extendedLookbackDays = ed;
            i++;
            break;
    }
}

using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
}).SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("SlackReaderCli");

var tokenConfigured = SlackReader.IsBotTokenConfigured();
logger.LogInformation(
    "SLACK_BOT_TOKEN: {Status}（値は出力しません）",
    tokenConfigured ? "設定済み" : "未設定");

if (!tokenConfigured)
{
    logger.LogWarning("SLACK_BOT_TOKEN が未設定のため、実際のSlackワークスペースへの接続はスキップします。");
    logger.LogWarning("代わりに組み込みfixtureデータでJSON/Markdown artifact生成パイプラインのみを検証します。");

    var nowUtc = DateTimeOffset.UtcNow;
    var messages = SampleWorkspaceFixture.BuildSampleMessages(nowUtc);
    var analysis = SlackMessageAnalyzer.Analyze(
        SampleWorkspaceFixture.Channel,
        messages,
        nowUtc.AddDays(-initialLookbackDays).UtcDateTime,
        nowUtc.UtcDateTime);

    var fixtureReport = new SlackWorkspaceReport
    {
        GeneratedAtUtc = nowUtc,
        Mode = "fixture-no-token",
        WorkspaceName = null,
        ChannelNameFilters = SlackWorkspaceScanner.DefaultChannelNameFilters,
        CandidateChannels = SampleWorkspaceFixture.Candidates,
        ChannelAnalyses = [analysis],
        Notes =
        [
            "SLACK_BOT_TOKEN が未設定のため実スキャンは実行していません。",
            "本レポートは組み込みfixture（サンプル）データによる出力パイプライン検証結果です。",
        ],
    };

    var (fixtureJson, fixtureMd) = await SlackWorkspaceScanner.WriteArtifactsAsync(fixtureReport, outputDirectory);
    logger.LogInformation("Fixture artifactを生成しました: {Json}, {Md}", fixtureJson, fixtureMd);
    return 0;
}

logger.LogInformation("SLACK_BOT_TOKEN が設定されているため、読み取り専用の実スキャンを開始します。");

using var reader = new SlackReader(logger: logger);
var scanner = new SlackWorkspaceScanner(reader, logger);

var report = await scanner.ScanWorkspaceAsync(
    initialLookbackDays: initialLookbackDays,
    extendedLookbackDays: extendedLookbackDays);

var (jsonPath, mdPath) = await SlackWorkspaceScanner.WriteArtifactsAsync(report, outputDirectory);

logger.LogInformation(
    "スキャン完了: 候補チャンネル {CandidateCount} 件、解析対象 {AnalyzedCount} 件。Artifacts: {Json}, {Md}",
    report.CandidateChannels.Count, report.ChannelAnalyses.Count, jsonPath, mdPath);

return 0;
