using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Server.Services;

/// <summary>
/// ControlPanel (WPF) からの名前付きパイプ接続を受け付け、緊急停止の状態取得/切り替えを提供するホストサービス。
/// 1接続につき1リクエスト/1レスポンス（改行区切りJSON）を処理し、切断後は次の接続を待ち受ける。
/// MCP プロセスごとに専用パイプを公開し、ControlPanel 側で全接続先への要求結果を集約する。
/// </summary>
public sealed class ControlPanelIpcServer(
    IEmergencyStopService emergencyStop,
    ILogger<ControlPanelIpcServer> logger,
    string? pipeName = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    pipeName ?? IpcConstants.ProcessPipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                requestCts.CancelAfter(IpcConstants.RequestTimeoutMs);
                try
                {
                    await HandleClientAsync(pipe, requestCts.Token).ConfigureAwait(false);
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogDebug("ControlPanel IPC リクエストがタイムアウトしました。");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex, "ControlPanel IPC を開始できません。アクセス権を確認してください。IPC を停止します。");
                return;
            }
            catch (IOException ex)
            {
                var error = ex.HResult & 0xFFFF;
                if (error is 5 or 231 || ++consecutiveFailures >= 5)
                {
                    logger.LogError(ex, "ControlPanel IPC の競合または継続的なI/Oエラーにより IPC を停止します。");
                    return;
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, consecutiveFailures - 1));
                logger.LogWarning(ex, "ControlPanel IPC I/Oエラー（コード {Error}）。{Delay} 後に再試行します（{Attempt}/5）。",
                    error, delay, consecutiveFailures);
                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        IpcResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<IpcRequest>(line);
            response = request is null ? Fail("リクエストの解析に失敗しました。") : Handle(request);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "ControlPanel からのIPCリクエストのJSON解析に失敗しました。");
            response = Fail("リクエストのJSON形式が不正です。");
        }

        var json = JsonSerializer.Serialize(response);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private IpcResponse Handle(IpcRequest request)
    {
        switch (request.Command)
        {
            case IpcCommand.Status:
                return Ok("状態を取得しました。");

            case IpcCommand.Activate:
                emergencyStop.Activate(string.IsNullOrWhiteSpace(request.Reason) ? "ControlPanel からの手動操作" : request.Reason!);
                logger.LogWarning("ControlPanel からの要求により緊急停止を有効化しました。");
                return Ok("緊急停止を有効化しました。");

            case IpcCommand.Deactivate:
                emergencyStop.Deactivate();
                logger.LogInformation("ControlPanel からの要求により緊急停止を解除しました。");
                return Ok("緊急停止を解除しました。");

            default:
                return Fail("不明なコマンドです。");
        }
    }

    private IpcResponse Ok(string message) => new()
    {
        Success = true,
        EmergencyStopActive = emergencyStop.IsActive,
        Message = message,
    };

    private IpcResponse Fail(string message) => new()
    {
        Success = false,
        EmergencyStopActive = emergencyStop.IsActive,
        Message = message,
    };
}
