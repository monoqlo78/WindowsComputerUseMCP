using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Core.Abstractions;

/// <summary>マウス・キーボードの物理入力（SendInput）を行う抽象。</summary>
public interface IInputService
{
    Task MouseMoveAsync(MouseMoveRequest request, CancellationToken cancellationToken = default);

    Task<MouseActionResult> MouseClickAsync(MouseClickRequest request, CancellationToken cancellationToken = default);

    Task<MouseActionResult> MouseDragAsync(MouseDragRequest request, CancellationToken cancellationToken = default);

    Task MouseScrollAsync(MouseScrollRequest request, CancellationToken cancellationToken = default);

    Task KeyboardTypeTextAsync(KeyboardTypeTextRequest request, CancellationToken cancellationToken = default);

    Task KeyboardPressAsync(KeyboardPressRequest request, CancellationToken cancellationToken = default);

    Task KeyboardHotkeyAsync(KeyboardHotkeyRequest request, CancellationToken cancellationToken = default);

    /// <summary>現在の仮想スクリーン（全モニター合成）の範囲を返す。座標検証に使用する。</summary>
    ScreenRect GetVirtualScreenBounds();
}
