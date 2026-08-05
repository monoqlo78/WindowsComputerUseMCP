using System.Threading;
using WindowsComputerUseMCP.Core.Abstractions;

namespace WindowsComputerUseMCP.Safety;

/// <summary>
/// 緊急停止状態をプロセス内メモリで保持するサービス。ホットキー（既定 Ctrl+Shift+F12）や
/// 将来のControlPanelからの操作で <see cref="Activate"/>/<see cref="Deactivate"/> が呼ばれる想定。
/// スレッドセーフに状態を切り替える。
/// </summary>
public sealed class EmergencyStopService : IEmergencyStopService
{
    private readonly Lock _lock = new();
    private bool _isActive;

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _isActive;
            }
        }
    }

    public event EventHandler<bool>? StateChanged;

    public void Activate(string reason)
    {
        SetState(true);
    }

    public void Deactivate()
    {
        SetState(false);
    }

    private void SetState(bool active)
    {
        bool changed;
        lock (_lock)
        {
            changed = _isActive != active;
            _isActive = active;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, active);
        }
    }
}
