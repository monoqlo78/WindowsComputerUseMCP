using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Core.Results;
using WindowsComputerUseMCP.Server.Hosting;

namespace WindowsComputerUseMCP.Server.Tools;

[McpServerToolType]
public static class InputTools
{
    [McpServerTool(Name = "mouse_move")]
    [Description("マウスカーソルを指定した画面座標へ移動する。durationMsを指定すると滑らかに移動する。")]
    public static async Task<OperationResult<object?>> MouseMove(
        IInputService inputService,
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<object> logger,
        [Description("移動先のX座標（仮想スクリーン座標）。")] int x,
        [Description("移動先のY座標（仮想スクリーン座標）。")] int y,
        [Description("移動にかける時間（ミリ秒）。省略時は瞬間移動。")] int? durationMs = null,
        CancellationToken cancellationToken = default)
    {
        var request = new MouseMoveRequest { X = x, Y = y, DurationMs = durationMs };
        var safetyRequest = await SafetyContextResolver.BuildAsync(windowService, "mouse_move", cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ToolRunner.RunAsync<object?>(
            "mouse_move",
            safetyPolicy,
            auditLog,
            logger,
            safetyRequest,
            async () => { await inputService.MouseMoveAsync(request, cancellationToken).ConfigureAwait(false); return null; },
            new Dictionary<string, string?> { ["x"] = x.ToString(), ["y"] = y.ToString() },
            applyRateLimit: true).ConfigureAwait(false);
    }

    [McpServerTool(Name = "mouse_click")]
    [Description("指定した画面座標でマウスボタンをクリックする。危険操作（削除・送信・購入・支払い・公開・上書き保存等）に該当するテキストが前面ウィンドウ付近にある場合、Safetyポリシーにより承認が必要になることがある。")]
    public static async Task<OperationResult<MouseActionResult>> MouseClick(
        IInputService inputService,
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<object> logger,
        [Description("クリックするX座標（仮想スクリーン座標）。")] int x,
        [Description("クリックするY座標（仮想スクリーン座標）。")] int y,
        [Description("マウスボタン。既定はLeft。")] MouseButton button = MouseButton.Left,
        [Description("クリック回数。既定は1（2でダブルクリック）。")] int clickCount = 1,
        [Description("危険操作カテゴリに該当する場合でも実行することを呼び出し元が承認済みであることを示す。")] bool requireConfirmation = false,
        CancellationToken cancellationToken = default)
    {
        var request = new MouseClickRequest { X = x, Y = y, Button = button, ClickCount = clickCount, RequireConfirmation = requireConfirmation };
        var safetyRequest = await SafetyContextResolver.BuildAsync(
            windowService, "mouse_click", requireConfirmation: requireConfirmation, cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ToolRunner.RunAsync(
            "mouse_click",
            safetyPolicy,
            auditLog,
            logger,
            safetyRequest,
            () => inputService.MouseClickAsync(request, cancellationToken),
            new Dictionary<string, string?> { ["x"] = x.ToString(), ["y"] = y.ToString(), ["button"] = button.ToString(), ["clickCount"] = clickCount.ToString() },
            applyRateLimit: true).ConfigureAwait(false);
    }

    [McpServerTool(Name = "mouse_drag")]
    [Description("指定した開始座標から終了座標までマウスをドラッグする（ボタン押下→移動→解放）。")]
    public static async Task<OperationResult<MouseActionResult>> MouseDrag(
        IInputService inputService,
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<object> logger,
        [Description("開始X座標。")] int startX,
        [Description("開始Y座標。")] int startY,
        [Description("終了X座標。")] int endX,
        [Description("終了Y座標。")] int endY,
        [Description("ドラッグにかける時間（ミリ秒）。既定は300。")] int? durationMs = 300,
        [Description("マウスボタン。既定はLeft。")] MouseButton button = MouseButton.Left,
        CancellationToken cancellationToken = default)
    {
        var request = new MouseDragRequest { StartX = startX, StartY = startY, EndX = endX, EndY = endY, DurationMs = durationMs, Button = button };
        var safetyRequest = await SafetyContextResolver.BuildAsync(windowService, "mouse_drag", cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ToolRunner.RunAsync(
            "mouse_drag",
            safetyPolicy,
            auditLog,
            logger,
            safetyRequest,
            () => inputService.MouseDragAsync(request, cancellationToken),
            new Dictionary<string, string?>
            {
                ["startX"] = startX.ToString(),
                ["startY"] = startY.ToString(),
                ["endX"] = endX.ToString(),
                ["endY"] = endY.ToString(),
            },
            applyRateLimit: true).ConfigureAwait(false);
    }

    [McpServerTool(Name = "mouse_scroll")]
    [Description("マウスホイールをスクロールする。x/yを指定すると、その座標にカーソルを移動してからスクロールする。")]
    public static async Task<OperationResult<object?>> MouseScroll(
        IInputService inputService,
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<object> logger,
        [Description("スクロール量（正で上、負で下。1ノッチ=120目安）。")] int delta,
        [Description("スクロール前にカーソルを移動するX座標。省略可。")] int? x = null,
        [Description("スクロール前にカーソルを移動するY座標。省略可。")] int? y = null,
        CancellationToken cancellationToken = default)
    {
        var request = new MouseScrollRequest { Delta = delta, X = x, Y = y };
        var safetyRequest = await SafetyContextResolver.BuildAsync(windowService, "mouse_scroll", cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ToolRunner.RunAsync<object?>(
            "mouse_scroll",
            safetyPolicy,
            auditLog,
            logger,
            safetyRequest,
            async () => { await inputService.MouseScrollAsync(request, cancellationToken).ConfigureAwait(false); return null; },
            new Dictionary<string, string?> { ["delta"] = delta.ToString() },
            applyRateLimit: true).ConfigureAwait(false);
    }

    [McpServerTool(Name = "keyboard_type_text")]
    [Description("現在フォーカスされている入力欄へテキストを入力する。パスワード入力欄には既定で拒否される。既定では監査ログに入力文字列そのものは残さない。")]
    public static async Task<OperationResult<object?>> KeyboardTypeText(
        IInputService inputService,
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<object> logger,
        [Description("入力するテキスト。")] string text,
        [Description("1文字ごとの入力間隔（ミリ秒）。省略時は即時連続入力。")] int? intervalMs = null,
        [Description("監査ログに入力文字列そのものをマスクせず残すか（既定false、強く非推奨）。")] bool maskInLogs = true,
        [Description("危険操作カテゴリに該当する場合でも実行することを呼び出し元が承認済みであることを示す。")] bool requireConfirmation = false,
        CancellationToken cancellationToken = default)
    {
        var request = new KeyboardTypeTextRequest { Text = text, IntervalMs = intervalMs, MaskInLogs = maskInLogs, RequireConfirmation = requireConfirmation };
        var safetyRequest = await SafetyContextResolver.BuildAsync(
            windowService,
            "keyboard_type_text",
            inspectionTexts: [text],
            requireConfirmation: requireConfirmation,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var sanitizedArgs = new Dictionary<string, string?>
        {
            ["textLength"] = text.Length.ToString(),
            ["text"] = maskInLogs ? "***" : text,
        };

        return await ToolRunner.RunAsync<object?>(
            "keyboard_type_text",
            safetyPolicy,
            auditLog,
            logger,
            safetyRequest,
            async () => { await inputService.KeyboardTypeTextAsync(request, cancellationToken).ConfigureAwait(false); return null; },
            sanitizedArgs,
            applyRateLimit: true).ConfigureAwait(false);
    }

    [McpServerTool(Name = "keyboard_press")]
    [Description("指定した1つのキーを押下する（例: Enter, Tab, Escape, F5, A）。repeatCountで連続押下回数を指定できる。")]
    public static async Task<OperationResult<object?>> KeyboardPress(
        IInputService inputService,
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<object> logger,
        [Description("キー名（例: Enter, Tab, Escape, F5, A, 1）。")] string key,
        [Description("連続押下回数。既定は1。")] int repeatCount = 1,
        CancellationToken cancellationToken = default)
    {
        var request = new KeyboardPressRequest { Key = key, RepeatCount = repeatCount };
        var safetyRequest = await SafetyContextResolver.BuildAsync(windowService, "keyboard_press", cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ToolRunner.RunAsync<object?>(
            "keyboard_press",
            safetyPolicy,
            auditLog,
            logger,
            safetyRequest,
            async () => { await inputService.KeyboardPressAsync(request, cancellationToken).ConfigureAwait(false); return null; },
            new Dictionary<string, string?> { ["key"] = key, ["repeatCount"] = repeatCount.ToString() },
            applyRateLimit: true).ConfigureAwait(false);
    }

    [McpServerTool(Name = "keyboard_hotkey")]
    [Description("複数のキーを同時押しするホットキーを送信する（例: [\"Ctrl\",\"Shift\",\"Esc\"]）。指定順に押下し、逆順に解放する。")]
    public static async Task<OperationResult<object?>> KeyboardHotkey(
        IInputService inputService,
        IWindowService windowService,
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        ILogger<object> logger,
        [Description("同時押しするキー名の配列（例: [\"Ctrl\",\"Shift\",\"Esc\"]）。")] IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default)
    {
        var request = new KeyboardHotkeyRequest { Keys = keys };
        var safetyRequest = await SafetyContextResolver.BuildAsync(windowService, "keyboard_hotkey", cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ToolRunner.RunAsync<object?>(
            "keyboard_hotkey",
            safetyPolicy,
            auditLog,
            logger,
            safetyRequest,
            async () => { await inputService.KeyboardHotkeyAsync(request, cancellationToken).ConfigureAwait(false); return null; },
            new Dictionary<string, string?> { ["keys"] = string.Join("+", keys) },
            applyRateLimit: true).ConfigureAwait(false);
    }
}
