namespace WindowsComputerUseMCP.Core.Configuration;

/// <summary>危険操作判定・許可リスト・連続操作制限に関する設定。</summary>
public sealed class SafetySettings
{
    /// <summary>許可するプロセス名の一覧（未指定/空の場合はブロックリストのみで判定）。</summary>
    public List<string> AllowedProcesses { get; set; } = [];

    /// <summary>拒否するプロセス名の一覧。</summary>
    public List<string> DeniedProcesses { get; set; } = [];

    public bool AllowMouseClicks { get; set; } = true;
    public bool AllowKeyboardInput { get; set; } = true;

    /// <summary>危険操作（削除・送信・購入・支払い・公開・上書き保存等）で承認を要求するか。</summary>
    public bool RequireConfirmationForDangerousActions { get; set; } = true;

    /// <summary>短時間に許容する最大連続操作回数（暴走防止）。</summary>
    public int MaxConsecutiveOperations { get; set; } = 30;

    /// <summary>上記カウントを評価する時間窓（秒）。</summary>
    public int RateLimitWindowSeconds { get; set; } = 10;

    /// <summary>入力操作間の最小間隔（ミリ秒）。0の場合は制限なし。</summary>
    public int MinOperationIntervalMs { get; set; } = 0;

    /// <summary>緊急停止ホットキー（例: "Ctrl+Shift+F12"）。</summary>
    public string EmergencyStopHotkey { get; set; } = "Ctrl+Shift+F12";

    /// <summary>パスワード入力欄への自動入力を許可するか（既定: 拒否）。</summary>
    public bool AllowPasswordFieldInput { get; set; } = false;
}
