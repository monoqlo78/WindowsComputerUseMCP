namespace WindowsComputerUseMCP.Core.Abstractions;

/// <summary>緊急停止状態を保持・通知する抽象。実装は WindowsComputerUseMCP.Safety が提供する。</summary>
public interface IEmergencyStopService
{
    /// <summary>緊急停止が有効かどうか。</summary>
    bool IsActive { get; }

    /// <summary>緊急停止を有効化する。</summary>
    void Activate(string reason);

    /// <summary>緊急停止を解除する。</summary>
    void Deactivate();

    /// <summary>状態が変化した際に発火する。引数は変化後の <see cref="IsActive"/> の値。</summary>
    event EventHandler<bool>? StateChanged;
}
