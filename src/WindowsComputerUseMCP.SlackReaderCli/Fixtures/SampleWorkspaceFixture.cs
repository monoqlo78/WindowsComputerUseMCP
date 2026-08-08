using WindowsComputerUseMCP.Skills.Slack;

namespace WindowsComputerUseMCP.SlackReaderCli.Fixtures;

/// <summary>
/// SLACK_BOT_TOKEN が未設定の環境でも、JSON/Markdown artifact生成パイプラインの動作を検証できるようにするための
/// 固定サンプルデータ。実際のSlackワークスペースの内容ではない（"都知事杯 オープンデータ〜" 等の固有名詞は含めない）。
/// </summary>
public static class SampleWorkspaceFixture
{
    public const string SampleChannelId = "C0FIXTURE01";
    public const string SampleChannelName = "announcements-sample";

    public static SlackChannel Channel { get; } = new()
    {
        Id = SampleChannelId,
        Name = SampleChannelName,
        IsPrivate = false,
        Topic = "運営からのお知らせ（サンプル）",
    };

    public static IReadOnlyList<CandidateChannelInfo> Candidates { get; } =
    [
        new CandidateChannelInfo { Id = SampleChannelId, Name = SampleChannelName, IsPrivate = false, MatchedBy = "announce" },
    ];

    /// <summary>典型的なハッカソン運営告知メッセージの固定サンプル一覧（日付は生成時刻からの相対値）。</summary>
    public static IReadOnlyList<SlackMessage> BuildSampleMessages(DateTimeOffset nowUtc)
    {
        SlackMessage Msg(int daysAgo, string text, string? user = "U_ORGANIZER", IReadOnlyList<SlackReaction>? reactions = null) => new()
        {
            Ts = SlackTimestamp.FromDateTimeOffset(nowUtc.AddDays(-daysAgo)),
            User = user,
            Text = text,
            Reactions = reactions ?? [],
        };

        return
        [
            Msg(20, "【運営案内】最終成果物の提出手順を案内します。提出フォームは締切までに必ずご提出ください。 https://forms.example.com/submit"),
            Msg(18, "提出物は GitHub リポジトリのURLと、First Stage 用の2分デモ動画（収録済みのもの）の2点です。"),
            Msg(10, "締切: 8/20 18:00 までにフォーム提出をお願いします。@team_lead 確認よろしくお願いします。"),
            Msg(9, "審査は提出後、運営チームが確認します。承認された提出のみ次のステージに進みます。", reactions:
            [
                new SlackReaction { Name = "white_check_mark", Users = ["U_REVIEWER1", "U_REVIEWER2"], Count = 2 },
            ]),
            Msg(2, "TODO: デモ動画のURLをまだ提出していないチームは至急対応してください。@late_team お願いします。"),
        ];
    }
}
