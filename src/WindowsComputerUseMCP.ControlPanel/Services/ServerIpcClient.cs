using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.ControlPanel.Services;

/// <summary>
/// Server (MCP stdio プロセス) が公開する名前付きパイプへ接続し、緊急停止の状態取得/切り替えを行うクライアント。
/// Server が起動していない場合は例外を吸収し null を返す（呼び出し側は「未接続」として扱うこと）。
/// </summary>
public sealed class ServerIpcClient
{
    public async Task<IpcResponse?> SendAsync(IpcCommand command, string? reason = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(IpcConstants.ClientConnectTimeoutMs);

            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

            using var reader = new StreamReader(client, leaveOpen: true);
            await using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };

            var request = new IpcRequest { Command = command, Reason = reason };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), cancellationToken).ConfigureAwait(false);

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<IpcResponse>(line);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or ObjectDisposedException)
        {
            // Server 未起動、または応答待ちタイムアウト。ControlPanel 側は「未接続」表示にフォールバックする。
            return null;
        }
    }
}
