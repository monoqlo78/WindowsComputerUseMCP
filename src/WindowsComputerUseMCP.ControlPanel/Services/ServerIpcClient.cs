using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.ControlPanel.Services;

/// <summary>
/// Server (MCP stdio プロセス) が公開する名前付きパイプへ接続し、緊急停止の状態取得/切り替えを行うクライアント。
/// 接続先がなければ null、検出済み接続先の部分失敗は Success=false として返す。
/// </summary>
public sealed class ServerIpcClient(string? pipePrefix = null)
{
    public async Task<IpcResponse?> SendAsync(IpcCommand command, string? reason = null, CancellationToken cancellationToken = default)
    {
        string[] pipes;
        try
        {
            var names = Directory.GetFiles(@"\\.\pipe\")
                .Select(Path.GetFileName)
                .OfType<string>();
            var prefix = pipePrefix ?? IpcConstants.SessionPipePrefix;
            pipes = names.Where(name => name.StartsWith(prefix, StringComparison.Ordinal) ||
                (pipePrefix is null && name == IpcConstants.PipeName)).Distinct().ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new IpcResponse { Success = false, Message = $"IPC 検出に失敗しました: {ex.Message}" };
        }

        if (pipes.Length == 0)
        {
            return null;
        }

        var responses = await Task.WhenAll(pipes.Select(name =>
            SendToPipeAsync(name, command, reason, cancellationToken))).ConfigureAwait(false);
        var connected = responses.OfType<IpcResponse>().ToArray();
        var failed = responses.Count(response => response is null || !response.Success);
        return new IpcResponse
        {
            Success = failed == 0,
            EmergencyStopActive = connected.Any(response => response.EmergencyStopActive),
            Message = failed == 0
                ? $"{pipes.Length} 個のサーバーに要求を適用しました。"
                : $"{pipes.Length} 個中 {failed} 個のサーバーへの要求に失敗しました。全サーバーの状態は確認できません。",
        };
    }

    private static async Task<IpcResponse?> SendToPipeAsync(
        string pipeName, IpcCommand command, string? reason, CancellationToken cancellationToken)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(IpcConstants.ClientConnectTimeoutMs);

            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            timeoutCts.CancelAfter(IpcConstants.RequestTimeoutMs);

            using var reader = new StreamReader(client, leaveOpen: true);
            await using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };

            var request = new IpcRequest { Command = command, Reason = reason };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeoutCts.Token).ConfigureAwait(false);

            var line = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<IpcResponse>(line);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or ObjectDisposedException or JsonException or UnauthorizedAccessException)
        {
            // 集約側がこの接続先を失敗として数え、正常表示を抑止する。
            return null;
        }
    }
}
