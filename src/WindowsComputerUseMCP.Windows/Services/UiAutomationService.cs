using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>FlaUI(UIA3) を用いたUI Automationツリー探索・要素操作サービス。</summary>
public sealed class UiAutomationService : IUiAutomationService, IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly UiElementRegistry _registry = new();
    private readonly ILogger<UiAutomationService> _logger;

    public UiAutomationService(ILogger<UiAutomationService> logger)
    {
        _logger = logger;
    }

    public Task<UiTreeResult> GetTreeAsync(UiTreeRequest request, CancellationToken cancellationToken = default)
    {
        var root = _automation.FromHandle((nint)request.WindowHandle);
        var elements = new List<UiElementInfo>();
        var truncated = false;

        Traverse(root, parentId: null, depth: 0, request.MaxDepth, request.MaxElements, request.IncludeOffscreen, elements, ref truncated);

        return Task.FromResult(new UiTreeResult { Elements = elements, Truncated = truncated });
    }

    public Task<IReadOnlyList<UiElementInfo>> FindAsync(UiFindRequest request, CancellationToken cancellationToken = default)
    {
        var root = _automation.FromHandle((nint)request.WindowHandle);
        var elements = new List<UiElementInfo>();
        var truncated = false;

        Traverse(root, parentId: null, depth: 0, request.MaxDepth, request.MaxElements, includeOffscreen: true, elements, ref truncated);

        var filtered = elements.Where(e => MatchesFilter(e, request)).ToList();
        return Task.FromResult<IReadOnlyList<UiElementInfo>>(filtered);
    }

    public Task<UiInvokeResult> InvokeAsync(string elementId, CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGet(elementId, out var element) || element is null)
        {
            return Task.FromResult(new UiInvokeResult { Invoked = false, Reason = "指定された elementId は見つかりませんでした（キャッシュから失効した可能性があります）。" });
        }

        try
        {
            if (element.Patterns.Invoke.IsSupported)
            {
                element.Patterns.Invoke.Pattern.Invoke();
                return Task.FromResult(new UiInvokeResult { Invoked = true, PatternUsed = "Invoke" });
            }

            if (element.Patterns.Toggle.IsSupported)
            {
                element.Patterns.Toggle.Pattern.Toggle();
                return Task.FromResult(new UiInvokeResult { Invoked = true, PatternUsed = "Toggle" });
            }

            if (element.Patterns.SelectionItem.IsSupported)
            {
                element.Patterns.SelectionItem.Pattern.Select();
                return Task.FromResult(new UiInvokeResult { Invoked = true, PatternUsed = "SelectionItem" });
            }

            if (element.Patterns.ExpandCollapse.IsSupported)
            {
                var state = element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value;
                if (state == ExpandCollapseState.Collapsed)
                {
                    element.Patterns.ExpandCollapse.Pattern.Expand();
                }
                else
                {
                    element.Patterns.ExpandCollapse.Pattern.Collapse();
                }

                return Task.FromResult(new UiInvokeResult { Invoked = true, PatternUsed = "ExpandCollapse" });
            }

            // 対応パターンが無い場合は座標クリックへの自動フォールバックは行わない（安全設計）。
            return Task.FromResult(new UiInvokeResult { Invoked = false, Reason = "この要素は Invoke/Toggle/SelectionItem/ExpandCollapse のいずれもサポートしていません。" });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "UI要素の操作に失敗しました: {ElementId}", elementId);
            return Task.FromResult(new UiInvokeResult { Invoked = false, Reason = "操作の実行中にエラーが発生しました。" });
        }
    }

    public Task<UiElementInfo?> GetElementInfoAsync(string elementId, CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGet(elementId, out var element) || element is null)
        {
            return Task.FromResult<UiElementInfo?>(null);
        }

        try
        {
            return Task.FromResult<UiElementInfo?>(BuildElementInfo(element, parentId: null, existingId: elementId));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "要素情報の再取得に失敗しました: {ElementId}", elementId);
            return Task.FromResult<UiElementInfo?>(null);
        }
    }

    private void Traverse(
        AutomationElement element,
        string? parentId,
        int depth,
        int maxDepth,
        int maxElements,
        bool includeOffscreen,
        List<UiElementInfo> results,
        ref bool truncated)
    {
        if (results.Count >= maxElements)
        {
            truncated = true;
            return;
        }

        UiElementInfo info;
        try
        {
            info = BuildElementInfo(element, parentId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UI要素の情報取得に失敗したためスキップしました。");
            return;
        }

        // オフスクリーン要素は結果へ含めないが、子要素の探索は継続する。
        // WebView2/Chromium(Electron含む)系アプリでは、実際に画面へ表示されている要素の
        // 祖先ノードがUI Automation上で IsOffscreen=true を誤って報告することがあり、
        // ここで探索自体を打ち切ると配下の要素（実際に操作対象となるボタン等）が
        // ツリーから丸ごと欠落してしまう（ui_find が FindAllChildren を再帰的に
        // 辿って発見できる要素を ui_get_tree が一切返さない、という不整合の原因になっていた）。
        var include = includeOffscreen || !info.IsOffscreen;
        var effectiveParentId = include ? info.ElementId : parentId;

        if (include)
        {
            results.Add(info);
        }

        if (depth >= maxDepth)
        {
            if (TryGetChildren(element).Length > 0)
            {
                truncated = true;
            }

            return;
        }

        foreach (var child in TryGetChildren(element))
        {
            if (results.Count >= maxElements)
            {
                truncated = true;
                break;
            }

            Traverse(child, effectiveParentId, depth + 1, maxDepth, maxElements, includeOffscreen, results, ref truncated);
        }
    }

    private AutomationElement[] TryGetChildren(AutomationElement element)
    {
        try
        {
            return element.FindAllChildren();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "子要素の列挙に失敗しました。");
            return [];
        }
    }

    private UiElementInfo BuildElementInfo(AutomationElement element, string? parentId, string? existingId = null)
    {
        var elementId = existingId ?? _registry.Register(element);
        var bounds = element.BoundingRectangle;

        return new UiElementInfo
        {
            ElementId = elementId,
            Name = SafeGet(() => element.Name),
            AutomationId = SafeGet(() => element.AutomationId),
            ControlType = SafeGet(() => element.ControlType.ToString()),
            ClassName = SafeGet(() => element.ClassName),
            Bounds = new ScreenRect((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height),
            IsEnabled = SafeGet(() => element.IsEnabled, defaultValue: false),
            IsOffscreen = SafeGet(() => element.IsOffscreen, defaultValue: true),
            IsPassword = SafeGetBool(() => element.Properties.IsPassword.ValueOrDefault),
            SupportedPatterns = GetSupportedPatterns(element),
            ParentId = parentId,
        };
    }

    private static IReadOnlyList<string> GetSupportedPatterns(AutomationElement element)
    {
        var patterns = new List<string>();
        var p = element.Patterns;

        void AddIfSupported(string name, Func<bool> check)
        {
            try
            {
                if (check())
                {
                    patterns.Add(name);
                }
            }
            catch
            {
                // COM例外はパターン未対応として扱う。
            }
        }

        AddIfSupported("Invoke", () => p.Invoke.IsSupported);
        AddIfSupported("Toggle", () => p.Toggle.IsSupported);
        AddIfSupported("Value", () => p.Value.IsSupported);
        AddIfSupported("SelectionItem", () => p.SelectionItem.IsSupported);
        AddIfSupported("ExpandCollapse", () => p.ExpandCollapse.IsSupported);
        AddIfSupported("Scroll", () => p.Scroll.IsSupported);
        AddIfSupported("Window", () => p.Window.IsSupported);
        AddIfSupported("RangeValue", () => p.RangeValue.IsSupported);
        AddIfSupported("Text", () => p.Text.IsSupported);
        AddIfSupported("Selection", () => p.Selection.IsSupported);

        return patterns;
    }

    private static string? SafeGet(Func<string?> accessor)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return null;
        }
    }

    private static bool SafeGet(Func<bool> accessor, bool defaultValue)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return defaultValue;
        }
    }

    private static bool SafeGetBool(Func<bool> accessor)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesFilter(UiElementInfo element, UiFindRequest request)
    {
        if (request.Name is not null && !MatchesText(element.Name, request.Name, request.MatchMode))
        {
            return false;
        }

        if (request.AutomationId is not null && !MatchesText(element.AutomationId, request.AutomationId, request.MatchMode))
        {
            return false;
        }

        if (request.ControlType is not null && !MatchesText(element.ControlType, request.ControlType, request.MatchMode))
        {
            return false;
        }

        if (request.ClassName is not null && !MatchesText(element.ClassName, request.ClassName, request.MatchMode))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesText(string? candidate, string pattern, MatchMode matchMode)
    {
        if (candidate is null)
        {
            return false;
        }

        return matchMode switch
        {
            MatchMode.Exact => string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase),
            MatchMode.StartsWith => candidate.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            MatchMode.Regex => System.Text.RegularExpressions.Regex.IsMatch(candidate, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            _ => candidate.Contains(pattern, StringComparison.OrdinalIgnoreCase),
        };
    }

    public void Dispose()
    {
        _automation.Dispose();
    }
}
