using System.Net;
using System.Text;
using WindowsComputerUseMCP.Skills.Slack;

namespace WindowsComputerUseMCP.Tests.Skills.Slack;

/// <summary>
/// テスト用の疑似HttpMessageHandler。リクエストURLに応じて事前に登録した応答を順番に返す。
/// 実際のネットワーク通信は一切行わない。
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<string> RequestedUrls { get; } = [];

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responders.Enqueue(responder);

    public void EnqueueJson(HttpStatusCode statusCode, string json, TimeSpan? retryAfter = null) =>
        Enqueue(_ =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (retryAfter is { } ra)
            {
                response.Headers.Add("Retry-After", ((int)ra.TotalSeconds).ToString());
            }

            return response;
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUrls.Add(request.RequestUri!.ToString());

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException("テストで想定していないリクエストが発生しました: " + request.RequestUri);
        }

        var responder = _responders.Dequeue();
        return Task.FromResult(responder(request));
    }
}

public class SlackReaderTests
{
    private static SlackReader CreateReader(FakeHttpMessageHandler handler, int maxRetries = 3) =>
        new(token: "xoxb-test-token", httpClient: new HttpClient(handler), maxRetries: maxRetries);

    [Fact]
    public void IsBotTokenConfigured_ReturnsTrue_WhenEnvironmentReaderReturnsValue()
    {
        var result = SlackReader.IsBotTokenConfigured(name => name == SlackReader.BotTokenEnvironmentVariable ? "xoxb-fake" : null);
        Assert.True(result);
    }

    [Fact]
    public void IsBotTokenConfigured_ReturnsFalse_WhenEnvironmentReaderReturnsNullOrEmpty()
    {
        Assert.False(SlackReader.IsBotTokenConfigured(_ => null));
        Assert.False(SlackReader.IsBotTokenConfigured(_ => string.Empty));
        Assert.False(SlackReader.IsBotTokenConfigured(_ => "   "));
    }

    [Fact]
    public void Constructor_Throws_WhenNoTokenAvailable()
    {
        Assert.Throws<InvalidOperationException>(() => new SlackReader(token: null, httpClient: new HttpClient(new FakeHttpMessageHandler())));
    }

    [Fact]
    public async Task ListChannelsAsync_FollowsCursorPagination_AcrossMultiplePages()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK,
            """{"ok":true,"channels":[{"id":"C1","name":"general"}],"response_metadata":{"next_cursor":"cursor123"}}""");
        handler.EnqueueJson(HttpStatusCode.OK,
            """{"ok":true,"channels":[{"id":"C2","name":"announcements"}],"response_metadata":{"next_cursor":""}}""");

        using var reader = CreateReader(handler);
        var channels = await reader.ListChannelsAsync();

        Assert.Equal(2, channels.Count);
        Assert.Contains(channels, c => c.Id == "C1" && c.Name == "general");
        Assert.Contains(channels, c => c.Id == "C2" && c.Name == "announcements");
        Assert.Equal(2, handler.RequestedUrls.Count);
        Assert.Contains("cursor=cursor123", handler.RequestedUrls[1]);
    }

    [Fact]
    public async Task GetChannelHistoryAsync_ParsesMessagesAndReactions()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK,
            """
            {"ok":true,"messages":[{"ts":"1700000000.000100","user":"U1","text":"提出手順です","reactions":[{"name":"white_check_mark","users":["U2"],"count":1}]}]}
            """);

        using var reader = CreateReader(handler);
        var messages = await reader.GetChannelHistoryAsync("C1");

        var message = Assert.Single(messages);
        Assert.Equal("U1", message.User);
        Assert.Equal("提出手順です", message.Text);
        Assert.Single(message.Reactions);
        Assert.Equal("white_check_mark", message.Reactions[0].Name);
    }

    [Fact]
    public async Task CallApi_RetriesAfter429_ThenSucceeds_RespectingRetryAfterHeader()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.TooManyRequests, """{"ok":false,"error":"ratelimited"}""", retryAfter: TimeSpan.FromMilliseconds(50));
        handler.EnqueueJson(HttpStatusCode.OK, """{"ok":true,"channels":[],"response_metadata":{"next_cursor":""}}""");

        using var reader = CreateReader(handler);
        var channels = await reader.ListChannelsAsync();

        Assert.Empty(channels);
        Assert.Equal(2, handler.RequestedUrls.Count);
    }

    [Fact]
    public async Task CallApi_Throws_SlackApiException_WhenOkIsFalse()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"ok":false,"error":"channel_not_found"}""");

        using var reader = CreateReader(handler);
        var ex = await Assert.ThrowsAsync<SlackApiException>(() => reader.GetChannelHistoryAsync("C_MISSING"));

        Assert.Equal("channel_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task TryGetWorkspaceNameAsync_ReturnsNull_WhenApiFails()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"ok":false,"error":"missing_scope"}""");

        using var reader = CreateReader(handler);
        var name = await reader.TryGetWorkspaceNameAsync();

        Assert.Null(name);
    }

    [Fact]
    public async Task TryGetWorkspaceNameAsync_ReturnsName_OnSuccess()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"ok":true,"team":{"name":"都知事杯 オープンデータ〜"}}""");

        using var reader = CreateReader(handler);
        var name = await reader.TryGetWorkspaceNameAsync();

        Assert.Equal("都知事杯 オープンデータ〜", name);
    }
}
