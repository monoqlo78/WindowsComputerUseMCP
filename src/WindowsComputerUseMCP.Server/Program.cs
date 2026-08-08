// WindowsComputerUseMCP.Server
//
// MCP stdio サーバーのエントリーポイント。
//
// 重要: 標準出力 (Console.Out) は MCP の JSON-RPC (stdio) 通信専用に予約する。
// このプロセス内で Console.WriteLine 等による診断ログ出力は一切行わないこと。
// ロギングは標準エラー出力（コンソール）とファイルにのみ出力する。

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Core.Diagnostics;
using WindowsComputerUseMCP.Safety;
using WindowsComputerUseMCP.Windows.Services;
using WindowsComputerUseMCP.Skills;
using WindowsComputerUseMCP.Skills.Abstractions;
using WindowsComputerUseMCP.Skills.Blender;
using WindowsComputerUseMCP.Skills.Figma;
using WindowsComputerUseMCP.Skills.Generic;
using WindowsComputerUseMCP.Skills.Illustrator;
using WindowsComputerUseMCP.Skills.Slack;

var builder = Host.CreateApplicationBuilder(args);

// ユーザー固有設定（%LOCALAPPDATA%\WindowsComputerUseMCP\usersettings.json）があれば
// appsettings.json の値を上書きする。存在しない場合は無視する。
builder.Configuration.AddJsonFile(UserDataPaths.UserSettingsFilePath, optional: true, reloadOnChange: false);

// 標準出力は MCP の JSON-RPC 通信専用のため、コンソールロガーは必ず標準エラー出力へ流す。
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.Configure<WindowsComputerUseMcpOptions>(
    builder.Configuration.GetSection(WindowsComputerUseMcpOptions.SectionName));

// Safety層
builder.Services.AddSingleton<IEmergencyStopService, EmergencyStopService>();
builder.Services.AddSingleton<ISafetyPolicyService, SafetyPolicyService>();

// Core層
builder.Services.AddSingleton<IAuditLogService, AuditLogService>();

// Windows層
builder.Services.AddSingleton<IWindowService, WindowService>();
builder.Services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
builder.Services.AddSingleton<IUiAutomationService, UiAutomationService>();
builder.Services.AddSingleton<IInputService, InputService>();
builder.Services.AddSingleton<IScreenChangeService, ScreenChangeService>();

// ControlPanel 連携（緊急停止ホットキー・IPCサーバー）
builder.Services.AddHostedService<HotkeyListenerService>();
builder.Services.AddHostedService<WindowsComputerUseMCP.Server.Services.ControlPanelIpcServer>();

// アプリスキルパック層（各アプリのAPI連携 + 画面操作フォールバックをまとめた「スキル」）
builder.Services.AddSingleton<BlenderBridgeClient>();
builder.Services.AddSingleton<ISkillPack, BlenderSkillPack>();
builder.Services.AddSingleton<ISkillPack, IllustratorSkillPack>();
builder.Services.AddSingleton<ISkillPack, SlackSkillPack>();

// Figmaブリッジ（本サーバーがWebSocketサーバーを開き、Figma側の専用プラグインが接続してくる）
builder.Services.AddSingleton<FigmaBridgeServer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FigmaBridgeServer>());
builder.Services.AddSingleton<ISkillPack, FigmaSkillPack>();

foreach (var appDefinition in BuiltInAppDefinitions.All)
{
    builder.Services.AddSingleton<ISkillPack>(sp => new GenericUiaSkillPack(
        appDefinition,
        sp.GetRequiredService<IWindowService>(),
        sp.GetRequiredService<IUiAutomationService>(),
        sp.GetRequiredService<IInputService>(),
        sp.GetRequiredService<IScreenCaptureService>(),
        sp.GetRequiredService<ILogger<GenericUiaSkillPack>>()));
}

builder.Services.AddSingleton<SkillRegistry>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

UserDataPaths.EnsureDirectoriesExist();

var app = builder.Build();
await app.RunAsync();
