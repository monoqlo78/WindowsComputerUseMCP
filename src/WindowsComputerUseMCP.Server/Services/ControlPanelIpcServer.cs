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
/// Server は常駐しないstdioプロセスのため、ControlPanel側は接続失敗を「未接続」として扱う。
/// </summary>
public sealed class ControlPanelIpcServer(IEmergencyStopService emergencyStop, ILogger<ControlPanelIpcServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    IpcConstants.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await HandleClientAsync(pipe, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // シャットダウン中の待機キャンセルは正常終了。
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "ControlPanel IPC パイプの処理中にI/Oエラーが発生しました。再試行します。");
                await Task.Delay(500, stoppingToken).ConfigureAwait(false);
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
