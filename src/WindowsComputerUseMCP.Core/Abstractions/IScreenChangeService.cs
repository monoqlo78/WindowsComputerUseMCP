using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Core.Abstractions;

/// <summary>画面の変化をポーリングで検出する機能の抽象。</summary>
public interface IScreenChangeService
{
    Task<WaitForScreenChangeResult> WaitForChangeAsync(WaitForScreenChangeRequest request, CancellationToken cancellationToken = default);
}
