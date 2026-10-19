namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>一時表示の開始、復帰期限、他desktopへの移動を純粋な状態として扱う。</summary>
public sealed class TemporaryDesktopRecoveryState
{
    private readonly string _source;
    private readonly string _target;
    private readonly TimeSpan _timeout;
    private TimeSpan? _activatedAt;
    private TemporaryRecoveryDecision? _terminalDecision;

    public TemporaryDesktopRecoveryState(string source, string target, int timeoutSeconds)
    {
        if (!IsOrdinaryDesktopName(source) || !IsOrdinaryDesktopName(target) ||
            string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Recovery requires two distinct ordinary Win32 desktops.");
        }

        if (timeoutSeconds is < 1 or > DesktopRecoveryAgent.MaxRecoverySeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        _source = source;
        _target = target;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public TemporaryRecoveryDecision Observe(string? inputDesktop, TimeSpan elapsed)
    {
        if (_terminalDecision is { } terminal)
        {
            return terminal;
        }

        if (_activatedAt is null)
        {
            if (elapsed >= TimeSpan.FromSeconds(10))
            {
                return Complete(TemporaryRecoveryDecision.Failed);
            }

            if (string.Equals(inputDesktop, _target, StringComparison.OrdinalIgnoreCase))
            {
                _activatedAt = elapsed;
            }
            else
            {
                return TemporaryRecoveryDecision.Wait;
            }
        }

        if (string.Equals(inputDesktop, _source, StringComparison.OrdinalIgnoreCase))
        {
            return Complete(TemporaryRecoveryDecision.Completed);
        }

        var activeElapsed = elapsed - _activatedAt.Value;
        if (activeElapsed >= _timeout + TimeSpan.FromSeconds(5))
        {
            return Complete(TemporaryRecoveryDecision.Failed);
        }

        if (inputDesktop is null || !IsOrdinaryDesktopName(inputDesktop))
        {
            return TemporaryRecoveryDecision.Wait;
        }

        if (!string.Equals(inputDesktop, _target, StringComparison.OrdinalIgnoreCase))
        {
            return Complete(TemporaryRecoveryDecision.Completed);
        }

        return activeElapsed >= _timeout
            ? TemporaryRecoveryDecision.Recover
            : TemporaryRecoveryDecision.Listen;
    }

    private TemporaryRecoveryDecision Complete(TemporaryRecoveryDecision decision)
    {
        _terminalDecision = decision;
        return decision;
    }

    public static bool IsOrdinaryDesktopName(string? name)
        => name is { Length: > 0 and <= 64 } &&
           name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') &&
           !string.Equals(name, "Winlogon", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(name, "Disconnect", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(name, "Screen-saver", StringComparison.OrdinalIgnoreCase);
}

public enum TemporaryRecoveryDecision { Wait, Listen, Recover, Completed, Failed }
