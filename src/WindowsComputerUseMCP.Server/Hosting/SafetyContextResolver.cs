using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Server.Hosting;

/// <summary>
/// 現在の前面ウィンドウの情報から <see cref="SafetyCheckRequest"/> の共通コンテキスト
/// （ProcessName / WindowTitle / IsProtectedSurface）を補完するヘルパー。
/// </summary>
public static class SafetyContextResolver
{
    /// <summary>UAC同意ダイアログ等、操作対象外とみなすプロセス名（小文字比較）。</summary>
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "consent",
        "credentialuibroker",
        "lockapp",
        "logonui",
    };

    public static async Task<SafetyCheckRequest> BuildAsync(
        IWindowService windowService,
        string toolName,
        IReadOnlyList<string>? inspectionTexts = null,
        bool isPasswordField = false,
        bool requireConfirmation = false,
        CancellationToken cancellationToken = default)
    {
        WindowInfo? foreground = null;
        try
        {
            foreground = await windowService.GetForegroundWindowAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 前面ウィンドウの取得に失敗しても、Safety判定自体は続行する（プロセス情報なしで判定）。
        }

        var isProtectedSurface = foreground is not null && ProtectedProcessNames.Contains(foreground.ProcessName);

        return new SafetyCheckRequest
        {
            ToolName = toolName,
            ProcessName = foreground?.ProcessName,
            WindowTitle = foreground?.Title,
            IsPasswordField = isPasswordField,
            IsProtectedSurface = isProtectedSurface,
            CallerAcknowledgedConfirmation = requireConfirmation,
            InspectionTexts = inspectionTexts ?? [],
        };
    }
}
