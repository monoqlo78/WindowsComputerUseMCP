namespace WindowsComputerUseMCP.Core.Models;

/// <summary><c>system_get_capabilities</c> ツールの戻り値。機密情報は含めない。</summary>
public sealed record CapabilitiesInfo
{
    public required string OsVersion { get; init; }

    /// <summary>実行ユーザー名（ドメイン等の機密性が高い情報は含めない）。</summary>
    public required string CurrentUser { get; init; }

    public required bool IsAdministrator { get; init; }
    public required int MonitorCount { get; init; }
    public required ScreenRect VirtualScreenBounds { get; init; }
    public required bool UiAutomationAvailable { get; init; }
    public required bool ScreenCaptureAvailable { get; init; }
    public required bool EmergencyStopActive { get; init; }
    public required IReadOnlyList<string> AvailableFeatures { get; init; }
}
