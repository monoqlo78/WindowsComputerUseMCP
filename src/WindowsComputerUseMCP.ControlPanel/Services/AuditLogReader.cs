using System.IO;
using System.Text.Json;
using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Core.Diagnostics;

namespace WindowsComputerUseMCP.ControlPanel.Services;

/// <summary>%LOCALAPPDATA%\WindowsComputerUseMCP\Logs\audit-*.jsonl の末尾を読み取る簡易リーダー。</summary>
public static class AuditLogReader
{
    /// <summary>直近の監査ログを最大 <paramref name="maxCount"/> 件、新しい順に返す。今日・前日分を対象とする。</summary>
    public static IReadOnlyList<AuditLogEntry> ReadRecent(int maxCount = 200)
    {
        var results = new List<AuditLogEntry>();

        if (!Directory.Exists(UserDataPaths.LogsDirectory))
        {
            return results;
        }

        var candidates = new[]
        {
            $"audit-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.jsonl",
            $"audit-{DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)):yyyy-MM-dd}.jsonl",
        };

        foreach (var fileName in candidates)
        {
            var path = Path.Combine(UserDataPaths.LogsDirectory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        var entry = JsonSerializer.Deserialize<AuditLogEntry>(line);
                        if (entry is not null)
                        {
                            results.Add(entry);
                        }
                    }
                    catch (JsonException)
                    {
                        // 壊れた行はスキップする（ベストエフォート表示）。
                    }
                }
            }
            catch (IOException)
            {
                // 書き込み中などで読めない場合はスキップし、次の候補ファイルへ。
            }
        }

        return results
            .OrderByDescending(e => e.Timestamp)
            .Take(maxCount)
            .ToList();
    }
}
