using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WindowsComputerUseMCP.Skills.Figma;

/// <summary>
/// Figmaデスクトップアプリはブラウザ相当のサンドボックス環境で動作し、外部プロセスからCOM/TCP
/// サーバーとして直接呼び出せる公式APIを持たない。そのため、Blenderのアドオンと同じ発想で
/// 「Figmaプラグイン（tools/figma-plugin）」を用意し、そのプラグインがこちら側（本サーバー）が
/// 開くWebSocketサーバーへ接続してくる方式でブリッジする（接続の向きがBlenderとは逆になる点に注意）。
///
/// プロトコル: 1メッセージにつき1つのJSONオブジェクト
///   送信（サーバー→プラグイン）: {"requestId": "...", "type": "&lt;コマンド名&gt;", "params": {...}}
///   受信（プラグイン→サーバー）: {"requestId": "...", "result": ...} または {"requestId": "...", "error": "..."}
/// </summary>
public sealed class FigmaBridgeServer(ILogger<FigmaBridgeServer> logger) : IHostedService
{
    public const int DefaultPort = 9877;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly object _socketLock = new();

    private HttpListener? _listener;
    private WebSocket? _socket;
    private CancellationTokenSource? _cts;

    /// <summary>Figmaプラグインが現在接続中かどうか。</summary>
    public bool IsPluginConnected => _socket is { State: WebSocketState.Open };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{DefaultPort}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            logger.LogWarning(ex,
                "Figmaブリッジサーバーの起動に失敗しました（ポート{Port}）。別プロセスがポートを使用している可能性があります。Figmaスキルパックは接続待ちのまま動作しません。",
                DefaultPort);
            return Task.CompletedTask;
        }

        _ = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        logger.LogInformation("Figmaブリッジサーバーを起動しました（ws://127.0.0.1:{Port}/）。", DefaultPort);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄済みなら無視する。
        }

        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Figmaブリッジサーバーで接続受付エラーが発生しました。");
                continue;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }

            HttpListenerWebSocketContext wsContext;
            try
            {
                wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "WebSocketへのアップグレードに失敗しました。");
                continue;
            }

            lock (_socketLock)
            {
                _socket = wsContext.WebSocket;
            }

            logger.LogInformation("Figmaプラグインが接続しました。");
            _ = Task.Run(() => ReceiveLoopAsync(wsContext.WebSocket, cancellationToken), CancellationToken.None);
        }
    }

    private async Task ReceiveLoopAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var messageBuffer = new MemoryStream();

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                messageBuffer.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                messageBuffer.SetLength(0);
                HandleIncomingMessage(json);
            }
        }
        catch (OperationCanceledException)
        {
            // サーバー停止に伴う正常な打ち切り。
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Figmaプラグインとの通信でエラーが発生しました。");
        }
        finally
        {
            lock (_socketLock)
            {
                if (ReferenceEquals(_socket, socket))
                {
                    _socket = null;
                }
            }

            logger.LogInformation("Figmaプラグインとの接続が切断されました。");
        }
    }

    private void HandleIncomingMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("requestId", out var idProp) || idProp.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var requestId = idProp.GetString() ?? string.Empty;
            if (!_pending.TryRemove(requestId, out var tcs))
            {
                return;
            }

            if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String)
            {
                tcs.TrySetException(new FigmaBridgeException(errorProp.GetString() ?? "Figma側でエラーが発生しました。"));
                return;
            }

            var resultElement = root.TryGetProperty("result", out var r) ? r.Clone() : default;
            tcs.TrySetResult(resultElement);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Figmaプラグインからの応答を解析できませんでした: {Json}", json);
        }
    }

    /// <summary>接続中のFigmaプラグインへコマンドを送り、応答（result）を待って返す。</summary>
    public async Task<JsonElement> SendCommandAsync(
        string commandType,
        object? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        WebSocket? socket;
        lock (_socketLock)
        {
            socket = _socket;
        }

        if (socket is not { State: WebSocketState.Open })
        {
            throw new FigmaBridgeException(
                "Figmaプラグインが接続されていません。Figmaデスクトップ版でプラグイン管理画面から " +
                "「開発 > マニフェストからインポート」で tools/figma-plugin を読み込み、実行してください。" +
                $"（本サーバーは ws://127.0.0.1:{DefaultPort}/ で接続を待ち受けています）");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            requestId,
            type = commandType,
            @params = parameters ?? new { },
        });

        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(requestId, out _);
            throw new FigmaBridgeException($"Figmaプラグインへの送信に失敗しました: {ex.Message}", ex);
        }

        using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        await using var registration = linkedCts.Token.Register(() => tcs.TrySetCanceled(linkedCts.Token));

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(requestId, out _);
            throw new FigmaBridgeException($"Figmaプラグインからの応答がタイムアウトしました（コマンド: {commandType}）。");
        }
    }
}

/// <summary>Figmaプラグインとの通信・コマンド実行に失敗した場合の例外。</summary>
public sealed class FigmaBridgeException : Exception
{
    public FigmaBridgeException(string message) : base(message)
    {
    }

    public FigmaBridgeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
