namespace WindowsComputerUseMCP.Core.Models;

/// <summary><c>wait_for_screen_change</c> ツールの要求パラメーター。</summary>
public sealed record WaitForScreenChangeRequest
{
    public int TimeoutMs { get; init; } = 5000;
    public int PollingIntervalMs { get; init; } = 200;

    /// <summary>0.0〜1.0。差分ピクセル比率がこの値を超えたら「変化あり」と判定する。</summary>
    public double DifferenceThreshold { get; init; } = 0.02;

    /// <summary>比較対象を画面全体ではなく特定領域に限定する場合に指定する。</summary>
    public ScreenRect? Region { get; init; }

    public int? MonitorIndex { get; init; }
    public long? WindowHandle { get; init; }
}

/// <summary><c>wait_for_screen_change</c> ツールの戻り値。</summary>
public sealed record WaitForScreenChangeResult
{
    /// <summary>タイムアウト内に変化を検出できたか。タイムアウトしても例外にはせず false を返す。</summary>
    public required bool Changed { get; init; }

    public required double ObservedDifferenceRatio { get; init; }
    public required double ElapsedMs { get; init; }
    public required bool TimedOut { get; init; }
}
