namespace WindowsComputerUseMCP.Core.Results;

/// <summary>
/// MCPツールの応答で使用する、既知のエラーコード定数の一覧。
/// 文字列定数とすることで、将来のエラー種別追加時に破壊的変更を避けられる。
/// </summary>
public static class ErrorCodes
{
    /// <summary>予期しない内部エラー。詳細は診断ログを参照する。</summary>
    public const string Unknown = "UNKNOWN";

    /// <summary>要求された操作/パターンが対象要素・環境でサポートされていない。</summary>
    public const string NotSupported = "NOT_SUPPORTED";

    /// <summary>Safetyポリシーにより操作が拒否された。</summary>
    public const string Denied = "DENIED";

    /// <summary>操作前に人間の承認が必要（危険操作カテゴリに該当）。</summary>
    public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";

    /// <summary>緊急停止が有効なため、入力操作系ツールが拒否された。</summary>
    public const string EmergencyStopActive = "EMERGENCY_STOP_ACTIVE";

    /// <summary>引数が不正、または必須引数が不足している。</summary>
    public const string InvalidArgument = "INVALID_ARGUMENT";

    /// <summary>対象（ウィンドウ、UI要素等）が見つからない。</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>対象が複数見つかり、一意に特定できない。</summary>
    public const string Ambiguous = "AMBIGUOUS";

    /// <summary>操作がタイムアウトした。</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>操作がキャンセルされた。</summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>現在のプラットフォーム/環境では実行できない。</summary>
    public const string PlatformNotSupported = "PLATFORM_NOT_SUPPORTED";

    /// <summary>連続操作回数の上限、または操作間隔の制約に抵触した。</summary>
    public const string RateLimited = "RATE_LIMITED";
}
