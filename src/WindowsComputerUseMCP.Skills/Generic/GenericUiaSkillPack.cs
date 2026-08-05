using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Skills.Abstractions;
using Microsoft.Extensions.Logging;

namespace WindowsComputerUseMCP.Skills.Generic;

/// <summary>
/// 専用APIブリッジを持たないWindowsアプリ（Adobe各製品、Clipchamp等）向けの汎用スキルパック。
///
/// 戦略は「UI Automationを最優先で使い、対応パターンが無い/取得できない要素は物理クリック
/// （SendInput）にフォールバックする」というもの。Adobe製品はメニューバー等の一部にしか
/// UIAサポートが無いことが多いため、click_element は自動的にUIA InvokePattern→物理クリックの順に
/// 試行する。UIAツリーからも見つからない領域（キャンバス内のツールパレット等）については
/// click_relative（ウィンドウ内の相対座標%指定、DPIスケール非依存）で対応する。
///
/// 同一アプリの専用APIが後から使えるようになった場合でも、このパックはそのまま
/// 「APIが無い操作のフォールバック」として共存できる。
/// </summary>
public sealed class GenericUiaSkillPack(
    GenericAppDefinition definition,
    IWindowService windowService,
    IUiAutomationService uiAutomationService,
    IInputService inputService,
    IScreenCaptureService screenCaptureService,
    ILogger<GenericUiaSkillPack> logger) : ISkillPack
{
    public string AppId => definition.AppId;

    public string DisplayName => definition.DisplayName;

    public IReadOnlyList<string> ProcessNames => definition.ProcessNames;

    public IReadOnlyList<SkillActionDescriptor> ListActions() =>
    [
        new SkillActionDescriptor
        {
            Name = "find_window",
            Description = $"{definition.DisplayName} のウィンドウを探して情報を返す（起動していない場合は失敗を返す）。",
        },
        new SkillActionDescriptor
        {
            Name = "focus",
            Description = $"{definition.DisplayName} のウィンドウを前面化する。",
        },
        new SkillActionDescriptor
        {
            Name = "screenshot",
            Description = $"{definition.DisplayName} のウィンドウのスクリーンショットを取得する（現在の状況をAIが視覚確認するために使う）。",
        },
        new SkillActionDescriptor
        {
            Name = "find_elements",
            Description = "UI Automationでウィンドウ内の要素を検索する（名前・AutomationId・種別で絞り込み）。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "name", Type = "string", Required = false, Description = "要素名（部分一致）" },
                new SkillParameterDescriptor { Name = "automationId", Type = "string", Required = false, Description = "AutomationId" },
                new SkillParameterDescriptor { Name = "controlType", Type = "string", Required = false, Description = "コントロール種別（例: Button）" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "click_element",
            Description = "名前/AutomationIdで要素を検索し、UI Automation InvokePatternを試行、失敗時は要素中心を物理クリックする。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "name", Type = "string", Required = false, Description = "要素名（部分一致）" },
                new SkillParameterDescriptor { Name = "automationId", Type = "string", Required = false, Description = "AutomationId" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "click_relative",
            Description = "ウィンドウ内の相対位置（0.0〜1.0の割合）を物理クリックする。UIAで要素が見つからない独自描画UI向けの最終手段。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "xRatio", Type = "double", Required = true, Description = "ウィンドウ幅に対する割合（0.0=左端, 1.0=右端）" },
                new SkillParameterDescriptor { Name = "yRatio", Type = "double", Required = true, Description = "ウィンドウ高さに対する割合（0.0=上端, 1.0=下端）" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "type_text",
            Description = "現在フォーカスされている入力欄に文字列を送信する。",
            Parameters = [new SkillParameterDescriptor { Name = "text", Type = "string", Required = true, Description = "入力する文字列" }],
        },
        new SkillActionDescriptor
        {
            Name = "hotkey",
            Description = "キーの組み合わせを送信する（例: [\"ctrl\",\"s\"]）。",
            Parameters = [new SkillParameterDescriptor { Name = "keys", Type = "string[]", Required = true, Description = "同時押しするキー名の配列" }],
        },
    ];

    public async Task<SkillActionOutcome> InvokeAsync(
        string actionName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        return actionName switch
        {
            "find_window" => await FindWindowOutcomeAsync(cancellationToken).ConfigureAwait(false),
            "focus" => await FocusAsync(cancellationToken).ConfigureAwait(false),
            "screenshot" => await ScreenshotAsync(cancellationToken).ConfigureAwait(false),
            "find_elements" => await FindElementsAsync(arguments, cancellationToken).ConfigureAwait(false),
            "click_element" => await ClickElementAsync(arguments, cancellationToken).ConfigureAwait(false),
            "click_relative" => await ClickRelativeAsync(arguments, cancellationToken).ConfigureAwait(false),
            "type_text" => await TypeTextAsync(arguments, cancellationToken).ConfigureAwait(false),
            "hotkey" => await HotkeyAsync(arguments, cancellationToken).ConfigureAwait(false),
            _ => SkillActionOutcome.Fail($"未知のアクションです: {actionName}"),
        };
    }

    private async Task<WindowInfo?> FindTargetWindowAsync(CancellationToken cancellationToken)
    {
        var windows = await windowService.ListWindowsAsync(cancellationToken).ConfigureAwait(false);
        var candidates = windows
            .Where(w => definition.ProcessNames.Any(p => string.Equals(p, w.ProcessName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // 前面ウィンドウを優先、次に最小化されていないものを優先する。
        return candidates.FirstOrDefault(w => w.IsForeground)
            ?? candidates.FirstOrDefault(w => !w.IsMinimized)
            ?? candidates[0];
    }

    private async Task<SkillActionOutcome> FindWindowOutcomeAsync(CancellationToken cancellationToken)
    {
        var window = await FindTargetWindowAsync(cancellationToken).ConfigureAwait(false);
        return window is null
            ? SkillActionOutcome.Fail($"{definition.DisplayName} のウィンドウが見つかりません。起動しているか確認してください（対象プロセス名: {string.Join(", ", definition.ProcessNames)}）。")
            : SkillActionOutcome.Ok(window);
    }

    private async Task<SkillActionOutcome> FocusAsync(CancellationToken cancellationToken)
    {
        var window = await FindTargetWindowAsync(cancellationToken).ConfigureAwait(false);
        if (window is null)
        {
            return SkillActionOutcome.Fail($"{definition.DisplayName} のウィンドウが見つかりません。");
        }

        var result = await windowService.FocusWindowAsync(new WindowFocusRequest { WindowHandle = window.WindowHandle }, cancellationToken)
            .ConfigureAwait(false);
        return result.Focused
            ? SkillActionOutcome.Ok(result.Window)
            : SkillActionOutcome.Fail($"{definition.DisplayName} のウィンドウを前面化できませんでした。");
    }

    private async Task<SkillActionOutcome> ScreenshotAsync(CancellationToken cancellationToken)
    {
        var window = await FindTargetWindowAsync(cancellationToken).ConfigureAwait(false);
        if (window is null)
        {
            return SkillActionOutcome.Fail($"{definition.DisplayName} のウィンドウが見つかりません。");
        }

        var capture = await screenCaptureService.CaptureAsync(
            new ScreenCaptureRequest { WindowHandle = window.WindowHandle },
            cancellationToken).ConfigureAwait(false);

        return SkillActionOutcome.Ok(new
        {
            window,
            capture.Width,
            capture.Height,
            capture.MimeType,
            capture.ImageBase64,
        });
    }

    private async Task<SkillActionOutcome> FindElementsAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var window = await FindTargetWindowAsync(cancellationToken).ConfigureAwait(false);
        if (window is null)
        {
            return SkillActionOutcome.Fail($"{definition.DisplayName} のウィンドウが見つかりません。");
        }

        var elements = await uiAutomationService.FindAsync(new UiFindRequest
        {
            WindowHandle = window.WindowHandle,
            Name = arguments.GetString("name"),
            AutomationId = arguments.GetString("automationId"),
            ControlType = arguments.GetString("controlType"),
        }, cancellationToken).ConfigureAwait(false);

        return SkillActionOutcome.Ok(elements);
    }

    private async Task<SkillActionOutcome> ClickElementAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var window = await FindTargetWindowAsync(cancellationToken).ConfigureAwait(false);
        if (window is null)
        {
            return SkillActionOutcome.Fail($"{definition.DisplayName} のウィンドウが見つかりません。");
        }

        var name = arguments.GetString("name");
        var automationId = arguments.GetString("automationId");
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(automationId))
        {
            return SkillActionOutcome.Fail("name または automationId のいずれかを指定してください。");
        }

        var elements = await uiAutomationService.FindAsync(new UiFindRequest
        {
            WindowHandle = window.WindowHandle,
            Name = name,
            AutomationId = automationId,
        }, cancellationToken).ConfigureAwait(false);

        var target = elements.FirstOrDefault(e => !e.IsOffscreen) ?? elements.FirstOrDefault();
        if (target is null)
        {
            return SkillActionOutcome.Fail($"要素が見つかりませんでした（name='{name}', automationId='{automationId}'）。UIAツリーに存在しない可能性があります。click_relative の使用を検討してください。");
        }

        // 1. まずUI Automationの操作パターン（InvokePattern等）を試みる。
        var invokeResult = await uiAutomationService.InvokeAsync(target.ElementId, cancellationToken).ConfigureAwait(false);
        if (invokeResult.Invoked)
        {
            return SkillActionOutcome.Ok(new { method = "uia", target, invokeResult });
        }

        logger.LogInformation(
            "UIA操作パターンが使用できないため物理クリックにフォールバックします。element={ElementName} reason={Reason}",
            target.Name, invokeResult.Reason);

        // 2. UIAが使えない場合は要素の中心座標を物理クリックする。
        var centerX = target.Bounds.X + (target.Bounds.Width / 2);
        var centerY = target.Bounds.Y + (target.Bounds.Height / 2);

        var clickResult = await inputService.MouseClickAsync(new MouseClickRequest { X = centerX, Y = centerY }, cancellationToken)
            .ConfigureAwait(false);

        return clickResult.Executed
            ? SkillActionOutcome.Ok(new { method = "physical-click", target, x = centerX, y = centerY })
            : SkillActionOutcome.Fail($"物理クリックにも失敗しました: {clickResult.Reason}");
    }

    private async Task<SkillActionOutcome> ClickRelativeAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var window = await FindTargetWindowAsync(cancellationToken).ConfigureAwait(false);
        if (window is null)
        {
            return SkillActionOutcome.Fail($"{definition.DisplayName} のウィンドウが見つかりません。");
        }

        var xRatio = arguments.GetDouble("xRatio");
        var yRatio = arguments.GetDouble("yRatio");
        if (xRatio is null || yRatio is null)
        {
            return SkillActionOutcome.Fail("xRatio, yRatio (それぞれ0.0〜1.0) を指定してください。");
        }

        var x = window.Bounds.X + (int)(window.Bounds.Width * Math.Clamp(xRatio.Value, 0.0, 1.0));
        var y = window.Bounds.Y + (int)(window.Bounds.Height * Math.Clamp(yRatio.Value, 0.0, 1.0));

        var clickResult = await inputService.MouseClickAsync(new MouseClickRequest { X = x, Y = y }, cancellationToken).ConfigureAwait(false);

        return clickResult.Executed
            ? SkillActionOutcome.Ok(new { x, y })
            : SkillActionOutcome.Fail($"クリックに失敗しました: {clickResult.Reason}");
    }

    private async Task<SkillActionOutcome> TypeTextAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var text = arguments.GetRequiredString("text");
        await inputService.KeyboardTypeTextAsync(new KeyboardTypeTextRequest { Text = text }, cancellationToken).ConfigureAwait(false);
        return SkillActionOutcome.Ok(new { typed = true, length = text.Length });
    }

    private async Task<SkillActionOutcome> HotkeyAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var keys = arguments.GetStringList("keys");
        if (keys.Count == 0)
        {
            return SkillActionOutcome.Fail("keys が指定されていません。");
        }

        await inputService.KeyboardHotkeyAsync(new KeyboardHotkeyRequest { Keys = keys }, cancellationToken).ConfigureAwait(false);
        return SkillActionOutcome.Ok(new { keys });
    }
}
