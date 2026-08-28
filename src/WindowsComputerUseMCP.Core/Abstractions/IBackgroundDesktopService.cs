using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Core.Abstractions;

/// <summary>
/// ユーザーの画面を占有せずにGUIアプリを操作するための、バックグラウンドデスクトップ管理サービス。
/// Windowsのデスクトップオブジェクト（HDESK）を使って別ワークスペースを作り、そこでアプリを起動・操作する。
/// </summary>
public interface IBackgroundDesktopService
{
    /// <summary>バックグラウンドデスクトップを作成する（既に存在する場合はそれを返す）。</summary>
    Task<BackgroundDesktopInfo> CreateAsync(BackgroundDesktopCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>現在のウィンドウステーション上のデスクトップを列挙する。</summary>
    Task<IReadOnlyList<BackgroundDesktopInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>バックグラウンドデスクトップを閉じる。</summary>
    Task<BackgroundDesktopActionResult> CloseAsync(BackgroundDesktopCloseRequest request, CancellationToken cancellationToken = default);

    /// <summary>指定のバックグラウンドデスクトップ上でプロセスを起動する。</summary>
    Task<BackgroundDesktopLaunchResult> LaunchAsync(BackgroundDesktopLaunchRequest request, CancellationToken cancellationToken = default);

    /// <summary>指定のバックグラウンドデスクトップ上のトップレベルウィンドウを列挙する。</summary>
    Task<IReadOnlyList<WindowInfo>> ListWindowsAsync(string desktopName, CancellationToken cancellationToken = default);

    /// <summary>指定のバックグラウンドデスクトップ上のウィンドウをキャプチャする。</summary>
    Task<ScreenCaptureResult> CaptureWindowAsync(string desktopName, long windowHandle, bool saveToFile = true, CancellationToken cancellationToken = default);

    /// <summary>バックグラウンドデスクトップ内でクリックする（ユーザーのカーソルは動かない）。</summary>
    Task<BackgroundDesktopActionResult> ClickAsync(BackgroundDesktopClickRequest request, CancellationToken cancellationToken = default);

    /// <summary>バックグラウンドデスクトップ内でテキストを入力する。</summary>
    Task<BackgroundDesktopActionResult> TypeTextAsync(BackgroundDesktopTypeTextRequest request, CancellationToken cancellationToken = default);

    /// <summary>バックグラウンドデスクトップ内でキー／ホットキーを送信する。</summary>
    Task<BackgroundDesktopActionResult> SendKeysAsync(BackgroundDesktopKeyRequest request, CancellationToken cancellationToken = default);

    /// <summary>指定デスクトップを一時的に画面表示する（確認用。既定で自動的に元へ戻る）。</summary>
    Task<BackgroundDesktopActionResult> SwitchAsync(BackgroundDesktopSwitchRequest request, CancellationToken cancellationToken = default);
}
