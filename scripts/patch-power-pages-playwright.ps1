#Requires -Version 5.1
<#
.SYNOPSIS
    power-pages プラグインの Playwright MCP をバックグラウンド起動へ切り替えます。

.DESCRIPTION
    GitHub Copilot CLI の power-pages プラグインは、独自に Playwright MCP を起動します。
    その既定設定は --start-fullscreen を含むため、ツールを呼ぶたびにブラウザが
    全画面で前面化し、ユーザーのフォアグラウンドを奪ってしまいます。

    このスクリプトは、プラグインの launch-playwright-mcp.js に
    「wcu-desktop-launcher があればそれを経由して隠しデスクトップ上で起動する」
    という分岐を注入します。ランチャーが見つからない場合や、環境変数
    POWER_PAGES_PLAYWRIGHT_FOREGROUND=1 が設定されている場合は元の挙動に戻ります。

    プラグインが更新されると変更は失われるため、その場合は再実行してください。
    冪等なので、適用済みの場合は何もしません。

.PARAMETER PluginScript
    パッチ対象のスクリプトパス。既定はユーザープロファイル配下の power-pages プラグイン。

.PARAMETER Revert
    バックアップ (.orig) から元の内容へ復元します。

.EXAMPLE
    .\scripts\patch-power-pages-playwright.ps1

.EXAMPLE
    .\scripts\patch-power-pages-playwright.ps1 -Revert
#>
[CmdletBinding()]
param(
    [string]$PluginScript = (Join-Path $env:USERPROFILE '.copilot\installed-plugins\copilot-plugins\power-pages\scripts\launch-playwright-mcp.js'),
    [switch]$Revert
)

$ErrorActionPreference = 'Stop'

function Write-Utf8NoBom {
    param([string]$Path, [string]$Text)
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

if (-not (Test-Path -LiteralPath $PluginScript)) {
    Write-Host "power-pages プラグインが見つかりません。パッチは不要です。" -ForegroundColor Yellow
    Write-Host "  探索先: $PluginScript"
    exit 0
}

$backup = "$PluginScript.orig"

if ($Revert) {
    if (-not (Test-Path -LiteralPath $backup)) {
        throw "バックアップが見つかりません: $backup"
    }
    Copy-Item -LiteralPath $backup -Destination $PluginScript -Force
    Write-Host "元の内容へ復元しました。" -ForegroundColor Green
    Write-Host "反映するには Copilot CLI を再起動してください。"
    exit 0
}

$content = [System.IO.File]::ReadAllText($PluginScript)

if ($content -match 'resolveBackgroundLauncher') {
    Write-Host "適用済みです。変更はありません。" -ForegroundColor Green
    exit 0
}

# --- 1. fs の require を追加 -------------------------------------------------
if ($content -notmatch [regex]::Escape("require('child_process')")) {
    throw "想定と異なる内容です。プラグインの実装が変わった可能性があります: $PluginScript"
}
$content = $content -replace [regex]::Escape("const { spawn } = require('child_process');"),
    "const { spawn } = require('child_process');`r`nconst fs = require('fs');"

# --- 2. バックグラウンド解決ロジックを注入 -----------------------------------
$helper = @'
// WindowsComputerUseMCP の wcu-desktop-launcher が入っていれば、そちらを経由して
// 隠しデスクトップ上でブラウザを起動する。ユーザーのフォアグラウンドを奪わないための対応。
// 無効化したい場合は環境変数 POWER_PAGES_PLAYWRIGHT_FOREGROUND=1 を設定する。
const BACKGROUND_DESKTOP_NAME = 'WCU_Background';

function resolveBackgroundLauncher({
  platform = process.platform,
  env = process.env,
  exists = fs.existsSync,
} = {}) {
  if (platform !== 'win32') return null;
  if (env.POWER_PAGES_PLAYWRIGHT_FOREGROUND === '1') return null;

  const root = env.LOCALAPPDATA;
  if (!root) return null;

  const base = path.join(root, 'WindowsComputerUseMCP', 'current');
  const launcher = path.join(base, 'launcher', 'wcu-desktop-launcher.exe');
  const configPath = path.join(base, 'playwright-mcp-background.config.json');

  if (!exists(launcher) || !exists(configPath)) return null;

  return { launcher, configPath };
}

// ランチャーは Windows のクォート規則に従って自前で再クォートするため、ここでは素のまま渡す。
// npx はバッチファイルなので cmd.exe /c を経由させる必要がある。
function buildBackgroundArgs(browser, configPath, desktop = BACKGROUND_DESKTOP_NAME) {
  return [
    '--desktop', desktop,
    '--',
    'cmd.exe', '/c', 'npx',
    '-y', '@playwright/mcp@latest',
    '--browser', browser,
    '--config', configPath,
  ];
}

function launch(
'@

$launchAnchor = 'function launch('
$idx = $content.IndexOf($launchAnchor)
if ($idx -lt 0) { throw "launch() 関数が見つかりません: $PluginScript" }
$content = $content.Substring(0, $idx) + ($helper -replace "`r?`n", "`r`n") + $content.Substring($idx + $launchAnchor.Length)

# --- 3. spawn をランチャー経由に差し替え -------------------------------------
$oldSpawn = @'
  const child = spawnFn('npx', buildMcpArgs(browser), {
    stdio: 'inherit',
    shell: true,
  });
'@
$newSpawn = @'
  const background = resolveBackgroundLauncher();

  const child = background
    ? spawnFn(background.launcher, buildBackgroundArgs(browser, background.configPath), {
        stdio: 'inherit',
        shell: false,
      })
    : spawnFn('npx', buildMcpArgs(browser), {
        stdio: 'inherit',
        shell: true,
      });
'@
$oldSpawnNorm = ($oldSpawn -replace "`r?`n", "`r`n")
if ($content -notmatch [regex]::Escape($oldSpawnNorm)) {
    $oldSpawnNorm = ($oldSpawn -replace "`r?`n", "`n")
}
if ($content -notmatch [regex]::Escape($oldSpawnNorm)) {
    throw "spawn 呼び出しが見つかりません。プラグインの実装が変わった可能性があります: $PluginScript"
}
$content = $content.Replace($oldSpawnNorm, ($newSpawn -replace "`r?`n", "`r`n"))

# --- 4. エクスポートを拡張 ---------------------------------------------------
$content = $content -replace [regex]::Escape('module.exports = { buildMcpArgs, launch, quoteShellArg };'),
    'module.exports = { buildMcpArgs, buildBackgroundArgs, resolveBackgroundLauncher, launch, quoteShellArg };'

if (-not (Test-Path -LiteralPath $backup)) {
    Copy-Item -LiteralPath $PluginScript -Destination $backup -Force
    Write-Host "バックアップを作成しました: $backup"
}

Write-Utf8NoBom -Path $PluginScript -Text $content

# --- 検証 --------------------------------------------------------------------
$probe = @'
const m = require(process.argv[2]);
const r = m.resolveBackgroundLauncher();
console.log(r ? 'BACKGROUND:' + r.launcher : 'FOREGROUND (ランチャー未検出)');
'@
$probeFile = Join-Path ([System.IO.Path]::GetTempPath()) 'wcu-probe.js'
Write-Utf8NoBom -Path $probeFile -Text $probe

$result = ''
$probeExit = 1
try {
    # node が stderr に書いても致命扱いしない（$ErrorActionPreference='Stop' 対策）
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $result = (& node $probeFile $PluginScript 2>&1 | Out-String)
    $probeExit = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $prev
    Remove-Item -LiteralPath $probeFile -Force -ErrorAction SilentlyContinue
}

if ($probeExit -ne 0) {
    Copy-Item -LiteralPath $backup -Destination $PluginScript -Force
    throw "パッチ後のスクリプトが読み込めませんでした。元に戻しました。`n$result"
}

Write-Host "パッチを適用しました。" -ForegroundColor Green
Write-Host "  $($result.Trim())"
Write-Host ""
Write-Host "反映するには Copilot CLI を再起動してください。"
