using WindowsComputerUseMCP.Skills.Slack;

namespace WindowsComputerUseMCP.Tests.Skills.Slack;

public class SlackMessageAnalyzerTests
{
    private static readonly SlackChannel Channel = new() { Id = "C123", Name = "announcements" };

    private static SlackMessage Msg(DateTimeOffset ts, string text, string? user = "U1", IReadOnlyList<SlackReaction>? reactions = null) =>
        new()
        {
            Ts = SlackTimestamp.FromDateTimeOffset(ts),
            User = user,
            Text = text,
            Reactions = reactions ?? [],
        };

    [Fact]
    public void Analyze_DetectsProcedureKeyword()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new[] { Msg(now.AddDays(-1), "最終成果物の提出手順について案内します。") };

        var result = SlackMessageAnalyzer.Analyze(Channel, messages, now.AddDays(-7).UtcDateTime, now.UtcDateTime);

        Assert.Single(result.Procedures);
        Assert.Contains("提出手順", result.Procedures[0].Snippet);
    }

    [Fact]
    public void Analyze_DetectsTodoWithAssigneeAndDueDate()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new[] { Msg(now.AddDays(-1), "締切: 8/20 18:00 までにフォーム提出をお願いします。@team_lead") };

        var result = SlackMessageAnalyzer.Analyze(Channel, messages, now.AddDays(-7).UtcDateTime, now.UtcDateTime);

        var todo = Assert.Single(result.Todos);
        Assert.Equal("team_lead", todo.Assignee);
        Assert.NotNull(todo.DueDateRaw);
    }

    [Fact]
    public void Analyze_MarksTodoAsOverdue_WhenDueDateIsInThePast()
    {
        var now = DateTimeOffset.UtcNow;
        // 1年以上前に投稿されたメッセージにすることで、年省略の「M/D」形式でも
        // 補完される年（投稿年）が確実に現在より過去になるようにする。
        var postedAt = now.AddYears(-1);
        var pastDueText = $"締切: {postedAt:M/d} までに提出をお願いします。@someone";
        var messages = new[] { Msg(postedAt, pastDueText) };

        var result = SlackMessageAnalyzer.Analyze(Channel, messages, now.AddYears(-2).UtcDateTime, now.UtcDateTime);

        var todo = Assert.Single(result.Todos);
        Assert.Equal("overdue", todo.Status);
        Assert.Contains(result.Summary, s => s.Contains("期限超過"));
    }

    [Fact]
    public void Analyze_DetectsApprovalByKeyword()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new[] { Msg(now.AddDays(-1), "この提出物は運営チームにより承認されました。") };

        var result = SlackMessageAnalyzer.Analyze(Channel, messages, now.AddDays(-7).UtcDateTime, now.UtcDateTime);

        Assert.Single(result.Approvals);
    }

    [Fact]
    public void Analyze_DetectsApprovalByReaction()
    {
        var now = DateTimeOffset.UtcNow;
        var reactions = new[] { new SlackReaction { Name = "white_check_mark", Users = ["U_REVIEWER"], Count = 1 } };
        var messages = new[] { Msg(now.AddDays(-1), "提出物を確認しました。", reactions: reactions) };

        var result = SlackMessageAnalyzer.Analyze(Channel, messages, now.AddDays(-7).UtcDateTime, now.UtcDateTime);

        var approval = Assert.Single(result.Approvals);
        Assert.Contains("U_REVIEWER", approval.ApprovedByUserIds);
    }

    [Fact]
    public void Analyze_CountsKeywordHits_ForDefaultKeywordSet()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new[]
        {
            Msg(now.AddDays(-1), "最終成果物はGitHubリポジトリのURLとFirst Stage用の2分デモ動画（収録済み）を審査用フォームから提出してください。"),
        };

        var result = SlackMessageAnalyzer.Analyze(Channel, messages, now.AddDays(-7).UtcDateTime, now.UtcDateTime);

        var hitKeywords = result.KeywordHits.Select(h => h.Keyword).ToHashSet();
        Assert.Contains("最終成果物", hitKeywords);
        Assert.Contains("GitHub", hitKeywords);
        Assert.Contains("First Stage", hitKeywords);
        Assert.Contains("2分", hitKeywords);
        Assert.Contains("収録", hitKeywords);
        Assert.Contains("審査", hitKeywords);
        Assert.Contains("フォーム", hitKeywords);
        Assert.Contains("URL", hitKeywords);
    }

    [Fact]
    public void Analyze_ExtractsLinksFromProcedureMessage()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new[] { Msg(now.AddDays(-1), "提出手順はこちら https://forms.example.com/submit を確認してください。") };

        var result = SlackMessageAnalyzer.Analyze(Channel, messages, now.AddDays(-7).UtcDateTime, now.UtcDateTime);

        var proc = Assert.Single(result.Procedures);
        Assert.Contains("https://forms.example.com/submit", proc.Links);
    }

    [Fact]
    public void Analyze_ReturnsEmptyResults_ForUnrelatedMessage()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new[] { Msg(now.AddDays(-1), "おはようございます、今日は良い天気ですね。") };

        var result = SlackMessageAnalyzer.Analyze(Channel, messages, now.AddDays(-7).UtcDateTime, now.UtcDateTime);

        Assert.Empty(result.Procedures);
        Assert.Empty(result.Todos);
        Assert.Empty(result.Approvals);
        Assert.Empty(result.KeywordHits);
    }
}
