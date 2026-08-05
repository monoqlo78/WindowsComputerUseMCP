using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Core.Abstractions;

/// <summary>ウィンドウ列挙・前面化・情報取得の抽象。</summary>
public interface IWindowService
{
    /// <summary>ユーザー操作可能なトップレベルウィンドウの一覧を返す。</summary>
    Task<IReadOnlyList<WindowInfo>> ListWindowsAsync(CancellationToken cancellationToken = default);

    /// <summary>指定ハンドルのウィンドウ情報を取得する。存在しない場合は null。</summary>
    Task<WindowInfo?> GetWindowAsync(long windowHandle, CancellationToken cancellationToken = default);

    /// <summary>現在の前面（フォアグラウンド）ウィンドウを取得する。</summary>
    Task<WindowInfo?> GetForegroundWindowAsync(CancellationToken cancellationToken = default);

    /// <summary>ウィンドウを前面化する（最小化されている場合は復元する）。</summary>
    Task<WindowFocusResult> FocusWindowAsync(WindowFocusRequest request, CancellationToken cancellationToken = default);
}
