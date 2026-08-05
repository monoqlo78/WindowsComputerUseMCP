using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Core.Abstractions;

/// <summary>画面キャプチャ機能の抽象。</summary>
public interface IScreenCaptureService
{
    Task<ScreenCaptureResult> CaptureAsync(ScreenCaptureRequest request, CancellationToken cancellationToken = default);
}
