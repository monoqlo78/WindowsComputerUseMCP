namespace WindowsComputerUseMCP.Core.Models;

/// <summary>バックグラウンドデスクトップ（隠しワークスペース）の情報。</summary>
public sealed record BackgroundDesktopInfo
{
    /// <summary>デスクトップ名（例: "mcpbg-work1"）。</summary>
    public required string Name { get; init; }

    /// <summary>このMCPサーバーが作成・管理しているデスクトップかどうか。</summary>
    public required bool IsManaged { get; init; }

    /// <summary>ユーザーが現在見ている入力デスクトップかどうか。</summary>
    public required bool IsInputDesktop { get; init; }

    /// <summary>このデスクトップ上のトップレベルウィンドウ数（管理外の場合は null）。</summary>
    public int? WindowCount { get; init; }

    /// <summary>このデスクトップ上でMCPが起動したプロセスID。</summary>
    public IReadOnlyList<int> LaunchedProcessIds { get; init; } = [];
}

/// <summary><c>background_desktop_create</c> の要求。</summary>
public sealed record BackgroundDesktopCreateRequest
{
    /// <summary>作成するデスクトップ名。英数字とハイフン・アンダースコアのみ。</summary>
    public required string Name { get; init; }
}

/// <summary><c>background_desktop_launch</c> の要求。</summary>
public sealed record BackgroundDesktopLaunchRequest
{
    public required string DesktopName { get; init; }

    /// <summary>起動する実行ファイルのパス（例: "notepad.exe"）。</summary>
    public required string ExecutablePath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    /// <summary>ウィンドウが現れるまで待機する最大ミリ秒。既定は 15000。</summary>
    public int WaitForWindowMs { get; init; } = 15000;
}

/// <summary>バックグラウンドデスクトップ上でのプロセス起動結果。</summary>
public sealed record BackgroundDesktopLaunchResult
{
    public required string DesktopName { get; init; }
    public required int ProcessId { get; init; }
    public required bool WindowAppeared { get; init; }

    /// <summary>起動後に検出できたウィンドウ（見つからなければ null）。</summary>
    public WindowInfo? Window { get; init; }

    /// <summary>ウィンドウが現れなかった場合など、利用者への注意事項。</summary>
    public string? Note { get; init; }
}

/// <summary><c>background_desktop_click</c> の要求。</summary>
public sealed record BackgroundDesktopClickRequest
{
    public required string DesktopName { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public MouseButton Button { get; init; } = MouseButton.Left;
    public int ClickCount { get; init; } = 1;

    /// <summary>指定するとクリック前にこのウィンドウをそのデスクトップ内で前面化する。</summary>
    public long? WindowHandle { get; init; }
}

/// <summary><c>background_desktop_type_text</c> の要求。</summary>
public sealed record BackgroundDesktopTypeTextRequest
{
    public required string DesktopName { get; init; }
    public required string Text { get; init; }

    /// <summary>指定するとタイプ前にこのウィンドウをそのデスクトップ内で前面化する。</summary>
    public long? WindowHandle { get; init; }

    public int? IntervalMs { get; init; }
}

/// <summary><c>background_desktop_press_key</c> / <c>background_desktop_hotkey</c> の要求。</summary>
public sealed record BackgroundDesktopKeyRequest
{
    public required string DesktopName { get; init; }

    /// <summary>単一キー押下。ホットキーの場合は Keys を使う。</summary>
    public string? Key { get; init; }

    /// <summary>同時押しするキー配列（例: ["Ctrl","S"]）。</summary>
    public IReadOnlyList<string>? Keys { get; init; }

    public int RepeatCount { get; init; } = 1;

    public long? WindowHandle { get; init; }
}

/// <summary>バックグラウンドデスクトップ操作の汎用結果。</summary>
public sealed record BackgroundDesktopActionResult
{
    public required bool Executed { get; init; }
    public required string DesktopName { get; init; }
    public string? Reason { get; init; }
}

/// <summary><c>background_desktop_close</c> の要求。</summary>
public sealed record BackgroundDesktopCloseRequest
{
    public required string DesktopName { get; init; }

    /// <summary>true の場合、そのデスクトップ上でMCPが起動したプロセスを終了させる。既定 true。</summary>
    public bool TerminateProcesses { get; init; } = true;
}

/// <summary><c>background_desktop_switch</c> の要求。</summary>
public sealed record BackgroundDesktopSwitchRequest
{
    /// <summary>表示するデスクトップ名。null または "Default" で通常デスクトップへ戻る。</summary>
    public string? DesktopName { get; init; }

    /// <summary>
    /// 自動で元のデスクトップへ戻るまでの秒数。1〜120秒。既定は15秒。
    /// 対象デスクトップ内で独立した復旧監視プロセスも起動する。
    /// </summary>
    public int AutoReturnSeconds { get; init; } = 15;
}
