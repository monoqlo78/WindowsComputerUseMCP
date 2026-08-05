using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Core.Abstractions;

/// <summary>Windows UI Automation を利用した要素探索・操作の抽象。</summary>
public interface IUiAutomationService
{
    Task<UiTreeResult> GetTreeAsync(UiTreeRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UiElementInfo>> FindAsync(UiFindRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="elementId"/> が指す要素に対して適切な操作パターン（InvokePattern等）を実行する。
    /// 対応パターンが無い場合は座標クリックへ自動フォールバックせず、Invoked = false を返す。
    /// </summary>
    Task<UiInvokeResult> InvokeAsync(string elementId, CancellationToken cancellationToken = default);

    /// <summary>
    /// キャッシュ済み要素の現在の情報を取得する（Safety判定用途。ui_invoke実行前のパスワード欄・危険操作キーワード検出等）。
    /// キャッシュに存在しない場合は null。
    /// </summary>
    Task<UiElementInfo?> GetElementInfoAsync(string elementId, CancellationToken cancellationToken = default);
}
