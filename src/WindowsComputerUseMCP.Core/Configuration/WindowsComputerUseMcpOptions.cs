namespace WindowsComputerUseMCP.Core.Configuration;

/// <summary>監査ログ・スクリーンショット保存に関する設定。</summary>
public sealed class LoggingSettings
{
    public bool AuditLogEnabled { get; set; } = true;

    /// <summary>監査ログの保存期間（日数）。0以下の場合は自動削除しない。</summary>
    public int AuditLogRetentionDays { get; set; } = 30;

    /// <summary>screen_capture の saveToFile 指定を許可するか。</summary>
    public bool ScreenshotSaveEnabled { get; set; } = true;

    /// <summary>保存したスクリーンショットの保存期間（日数）。0以下の場合は自動削除しない。</summary>
    public int ScreenshotRetentionDays { get; set; } = 7;
}

/// <summary>UI Automation ツリー探索の上限設定。</summary>
public sealed class UiAutomationSettings
{
    public int MaxTreeElements { get; set; } = 500;
    public int MaxTreeDepth { get; set; } = 10;
}

/// <summary>appsettings.json / ユーザー設定のルート設定オブジェクト。</summary>
public sealed class WindowsComputerUseMcpOptions
{
    /// <summary>appsettings.json 内のセクション名。</summary>
    public const string SectionName = "WindowsComputerUseMcp";

    public SafetySettings Safety { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public UiAutomationSettings UiAutomation { get; set; } = new();
}
