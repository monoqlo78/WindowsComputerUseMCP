using WindowsComputerUseMCP.Core.Diagnostics;

namespace WindowsComputerUseMCP.Core.Abstractions;

/// <summary>操作監査ログの記録を行う抽象。実装は JSON Lines ファイルへ書き込む。</summary>
public interface IAuditLogService
{
    Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}
