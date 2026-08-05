namespace WindowsComputerUseMCP.Core.Configuration;

/// <summary>
/// ユーザー設定・監査ログ・一時キャプチャファイルの保存先パスを解決するヘルパー。
/// 要件どおり %LOCALAPPDATA%\WindowsComputerUseMCP 配下に集約する。
/// </summary>
public static class UserDataPaths
{
    /// <summary>ユーザー設定・ログのルートディレクトリ。</summary>
    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsComputerUseMCP");

    /// <summary>ユーザー設定ファイル（appsettings.json を上書きするユーザー固有設定）。</summary>
    public static string UserSettingsFilePath => Path.Combine(RootDirectory, "usersettings.json");

    /// <summary>監査ログの保存ディレクトリ。</summary>
    public static string LogsDirectory => Path.Combine(RootDirectory, "Logs");

    /// <summary>screen_capture の saveToFile 指定時の保存先（ユーザー一時フォルダー配下の専用ディレクトリ）。</summary>
    public static string ScreenshotsDirectory =>
        Path.Combine(Path.GetTempPath(), "WindowsComputerUseMCP", "Screenshots");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ScreenshotsDirectory);
    }
}
