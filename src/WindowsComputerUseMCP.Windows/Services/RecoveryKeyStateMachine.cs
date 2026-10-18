namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>一時表示中の復旧監視プロセスが認識するキー操作。</summary>
public sealed class RecoveryKeyStateMachine
{
    private bool _escapeDown;
    private bool _homeDown;
    private bool _leftWindowsDown;
    private bool _rightWindowsDown;
    private bool _windowsKeyUsedAsModifier;

    public RecoveryKeyAction Process(uint virtualKey, bool keyDown, bool anotherModifierIsDown = false)
    {
        if (virtualKey == 0x1B)
        {
            var wasDown = _escapeDown;
            _escapeDown = keyDown;
            return new RecoveryKeyAction(!keyDown && wasDown, true);
        }

        if (virtualKey == 0x24)
        {
            var wasDown = _homeDown;
            _homeDown = keyDown;
            return new RecoveryKeyAction(!keyDown && wasDown, true);
        }

        if (virtualKey is 0x5B or 0x5C)
        {
            if (keyDown)
            {
                if (virtualKey == 0x5B)
                {
                    _leftWindowsDown = true;
                }
                else
                {
                    _rightWindowsDown = true;
                }

                _windowsKeyUsedAsModifier |= anotherModifierIsDown;
                return new RecoveryKeyAction(false, false);
            }

            var wasDown = virtualKey == 0x5B ? _leftWindowsDown : _rightWindowsDown;
            if (virtualKey == 0x5B)
            {
                _leftWindowsDown = false;
            }
            else
            {
                _rightWindowsDown = false;
            }

            var recover = wasDown && !_windowsKeyUsedAsModifier && !anotherModifierIsDown;
            if (!_leftWindowsDown && !_rightWindowsDown)
            {
                _windowsKeyUsedAsModifier = false;
            }

            return new RecoveryKeyAction(recover, false);
        }

        if (keyDown && (_leftWindowsDown || _rightWindowsDown))
        {
            _windowsKeyUsedAsModifier = true;
        }

        return new RecoveryKeyAction(false, false);
    }
}

/// <summary>キー操作に対する復帰要求と入力抑止の判断。</summary>
public readonly record struct RecoveryKeyAction(bool ShouldRecover, bool SuppressKey);
