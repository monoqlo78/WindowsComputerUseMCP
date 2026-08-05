namespace WindowsComputerUseMCP.Core.Models;

/// <summary><c>mouse_move</c> ツールの要求パラメーター。</summary>
public sealed record MouseMoveRequest
{
    public required int X { get; init; }
    public required int Y { get; init; }

    /// <summary>指定した場合、この時間をかけて滑らかに移動する。未指定なら瞬間移動。</summary>
    public int? DurationMs { get; init; }
}

/// <summary><c>mouse_click</c> ツールの要求パラメーター。</summary>
public sealed record MouseClickRequest
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public MouseButton Button { get; init; } = MouseButton.Left;
    public int ClickCount { get; init; } = 1;

    /// <summary>危険操作カテゴリに該当する場合、この値に関わらずSafetyポリシーが承認要求を返す。</summary>
    public bool RequireConfirmation { get; init; }
}

/// <summary>クリック系ツール共通の戻り値。</summary>
public sealed record MouseActionResult
{
    public required bool Executed { get; init; }
    public WindowInfo? ForegroundWindow { get; init; }
    public string? Reason { get; init; }
}

/// <summary><c>mouse_drag</c> ツールの要求パラメーター。</summary>
public sealed record MouseDragRequest
{
    public required int StartX { get; init; }
    public required int StartY { get; init; }
    public required int EndX { get; init; }
    public required int EndY { get; init; }
    public int? DurationMs { get; init; }
    public MouseButton Button { get; init; } = MouseButton.Left;
}

/// <summary><c>mouse_scroll</c> ツールの要求パラメーター。</summary>
public sealed record MouseScrollRequest
{
    public required int Delta { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
}

/// <summary><c>keyboard_type_text</c> ツールの要求パラメーター。</summary>
public sealed record KeyboardTypeTextRequest
{
    public required string Text { get; init; }
    public int? IntervalMs { get; init; }

    /// <summary>既定で true。false の場合のみ監査ログに全文を残す（強く非推奨）。</summary>
    public bool MaskInLogs { get; init; } = true;

    public bool RequireConfirmation { get; init; }
}

/// <summary><c>keyboard_press</c> ツールの要求パラメーター。</summary>
public sealed record KeyboardPressRequest
{
    public required string Key { get; init; }
    public int RepeatCount { get; init; } = 1;
}

/// <summary><c>keyboard_hotkey</c> ツールの要求パラメーター。</summary>
public sealed record KeyboardHotkeyRequest
{
    public required IReadOnlyList<string> Keys { get; init; }
}
