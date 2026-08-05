using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Configuration;

namespace WindowsComputerUseMCP.Core.Diagnostics;

/// <summary>
/// 監査ログを %LOCALAPPDATA%\WindowsComputerUseMCP\Logs 配下へ JSON Lines 形式（1日1ファイル）で記録する。
/// 書き込みはファイル単位のロックで直列化し、複数呼び出しからの同時書き込みでも行が壊れないようにする。
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IOptionsMonitor<WindowsComputerUseMcpOptions> _options;
    private readonly ILogger<AuditLogService> _logger;
    private DateOnly _lastCleanupDate;

    public AuditLogService(IOptionsMonitor<WindowsComputerUseMcpOptions> options, ILogger<AuditLogService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        var settings = _options.CurrentValue.Logging;
        if (!settings.AuditLogEnabled)
        {
            return;
        }

        UserDataPaths.EnsureDirectoriesExist();

        var fileName = $"audit-{entry.Timestamp:yyyy-MM-dd}.jsonl";
        var filePath = Path.Combine(UserDataPaths.LogsDirectory, fileName);
        var line = JsonSerializer.Serialize(entry, SerializerOptions);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(filePath, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            MaybeCleanupOldLogs(settings.AuditLogRetentionDays);
        }
        catch (IOException ex)
        {
            // 監査ログの書き込み失敗はツール呼び出し自体を失敗させない（ベストエフォート）。
            _logger.LogWarning(ex, "監査ログの書き込みに失敗しました: {FilePath}", filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void MaybeCleanupOldLogs(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today == _lastCleanupDate)
        {
            return;
        }

        _lastCleanupDate = today;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        try
        {
            foreach (var file in Directory.EnumerateFiles(UserDataPaths.LogsDirectory, "audit-*.jsonl"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "監査ログの保持期間クリーンアップに失敗しました。");
        }
    }
}
