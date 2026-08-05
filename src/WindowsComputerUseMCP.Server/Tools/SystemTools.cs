using System.ComponentModel;
using System.Security;
using System.Security.Principal;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Core.Results;
using WindowsComputerUseMCP.Server.Hosting;
using WindowsComputerUseMCP.Windows.Services;

namespace WindowsComputerUseMCP.Server.Tools;

[McpServerToolType]
public static class SystemTools
{
    [McpServerTool(Name = "system_get_capabilities")]
    [Description("このMCPサーバーが動作しているWindows環境の機能可否・モニター構成・緊急停止状態を返す。機密情報（ユーザー名詳細、環境変数、トークン等）は含めない。")]
    public static Task<OperationResult<CapabilitiesInfo>> SystemGetCapabilities(
        ISafetyPolicyService safetyPolicy,
        IAuditLogService auditLog,
        IEmergencyStopService emergencyStop,
        ILogger<CapabilitiesInfo> logger)
    {
        return ToolRunner.RunAsync(
            "system_get_capabilities",
            safetyPolicy,
            auditLog,
            logger,
            new SafetyCheckRequest { ToolName = "system_get_capabilities" },
            () =>
            {
                var monitors = MonitorEnumerator.GetMonitors();
                var isAdministrator = false;
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
                {
                    // 管理者判定に失敗した場合は false のまま扱う。
                }

                var capabilities = new CapabilitiesInfo
                {
                    OsVersion = Environment.OSVersion.VersionString,
                    CurrentUser = Environment.UserName,
                    IsAdministrator = isAdministrator,
                    MonitorCount = monitors.Count,
                    VirtualScreenBounds = MonitorEnumerator.GetVirtualScreenBounds(),
                    UiAutomationAvailable = true,
                    ScreenCaptureAvailable = true,
                    EmergencyStopActive = emergencyStop.IsActive,
                    AvailableFeatures =
                    [
                        "system_get_capabilities",
                        "window_list",
                        "screen_capture",
                        "ui_get_tree",
                        "ui_find",
                    ],
                };

                return Task.FromResult(capabilities);
            });
    }
}
