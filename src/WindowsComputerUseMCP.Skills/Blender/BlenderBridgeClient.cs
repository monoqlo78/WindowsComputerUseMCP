using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WindowsComputerUseMCP.Skills.Blender;

/// <summary>
/// BlenderMCPアドオン（Blender内で動くソケットサーバー、既定ポート9876）と通信するTCPクライアント。
///
/// プロトコル: 1リクエストにつき1つのJSONオブジェクト
///   送信: {"type": "&lt;コマンド名&gt;", "params": {...}}
///   受信: {"status": "success", "result": ...} または {"status": "error", "message": "..."}
/// を、TCP接続1本の上で送受信する（アドオン側の実装が単純なバッファ結合方式のため、
/// 受信バイト列がパース可能なJSONになるまで読み続ける）。
/// </summary>
public sealed class BlenderBridgeClient(ILogger<BlenderBridgeClient> logger)
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 9876;

    /// <summary>
    /// Blender側のアドオンにコマンドを送信し、応答の "result" 部分を返す。
    /// アドオン側が {"status": "error", ...} を返した場合は <see cref="BlenderBridgeException"/> をスローする。
    /// </summary>
    public async Task<JsonElement> SendCommandAsync(
        string commandType,
        object? parameters = null,
        int port = DefaultPort,
        string host = DefaultHost,
        TimeSpan? connectTimeout = null,
        TimeSpan? responseTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(connectTimeout ?? TimeSpan.FromSeconds(5));

        try
        {
            await client.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BlenderBridgeException(
                $"Blender (127.0.0.1:{port}) に接続できませんでした。" +
                "Blenderが起動しており、BlenderMCPアドオンのソケットサーバーが開始されていることを確認してください。");
        }
        catch (SocketException ex)
        {
            throw new BlenderBridgeException(
                $"Blender (127.0.0.1:{port}) への接続に失敗しました: {ex.Message} " +
                "Blenderが起動しており、BlenderMCPアドオンのソケットサーバーが開始されていることを確認してください。", ex);
        }

        var requestPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = commandType,
            @params = parameters ?? new { },
        });

        var stream = client.GetStream();
        await stream.WriteAsync(requestPayload, cancellationToken).ConfigureAwait(false);

        var responseElement = await ReadJsonResponseAsync(stream, responseTimeout ?? TimeSpan.FromSeconds(180), cancellationToken)
            .ConfigureAwait(false);

        if (!responseElement.TryGetProperty("status", out var statusProp))
        {
            throw new BlenderBridgeException($"Blenderから予期しない応答形式を受信しました: {responseElement.GetRawText()}");
        }

        var status = statusProp.GetString();
        if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            var message = responseElement.TryGetProperty("message", out var msgProp)
                ? msgProp.GetString()
                : "不明なエラー";
            logger.LogWarning("Blenderコマンド {CommandType} がエラーを返しました: {Message}", commandType, message);
            throw new BlenderBridgeException($"Blenderコマンド '{commandType}' がエラーを返しました: {message}");
        }

        return responseElement.TryGetProperty("result", out var resultProp)
            ? resultProp.Clone()
            : default;
    }

    /// <summary>疎通確認のみ行う（get_scene_info を1回呼び、例外が出なければ接続OKとみなす）。</summary>
    public async Task<bool> IsReachableAsync(int port = DefaultPort, string host = DefaultHost, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendCommandAsync("get_scene_info", port: port, host: host,
                connectTimeout: TimeSpan.FromSeconds(2), responseTimeout: TimeSpan.FromSeconds(5),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (BlenderBridgeException)
        {
            return false;
        }
    }

    private static async Task<JsonElement> ReadJsonResponseAsync(NetworkStream stream, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new BlenderBridgeException("Blenderからの応答がタイムアウトしました。");
            }

            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var remaining = deadline - DateTime.UtcNow;
            readCts.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1));

            int read;
            try
            {
                read = await stream.ReadAsync(chunk, readCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new BlenderBridgeException("Blenderからの応答がタイムアウトしました。");
            }

            if (read == 0)
            {
                throw new BlenderBridgeException("Blenderとの接続が応答を返す前に切断されました。");
            }

            buffer.Write(chunk, 0, read);

            // アドオン側は生JSONを送るだけ（長さプレフィックスなし）のため、
            // 現時点までの受信データがパース可能なJSONになるまで読み続ける。
            try
            {
                using var doc = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // 未完成のJSON。続けて受信する。
            }
        }
    }
}

/// <summary>Blenderとの通信・コマンド実行に失敗した場合の例外。</summary>
public sealed class BlenderBridgeException : Exception
{
    public BlenderBridgeException(string message) : base(message)
    {
    }

    public BlenderBridgeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
