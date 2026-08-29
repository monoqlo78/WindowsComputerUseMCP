<#
.SYNOPSIS
    WindowsComputerUseMCP サーバーをビルドして、MCP クライアントから参照する
    インストール先へ配置（publish）します。

.DESCRIPTION
    MCP サーバーは起動中に自身の DLL をロックするため、bin\Debug を直接
    MCP クライアントに登録していると「再ビルドできない」「古いバイナリを
    掴んだまま」という状態に陥ります。

    このスクリプトは次を行います。

      1. 実行中の WindowsComputerUseMCP.Server プロセスを停止する
      2. Release 構成で publish する
      3. インストール先へ反映する（既定: %LOCALAPPDATA%\WindowsComputerUseMCP\current）
      4. 配置されたサーバーが MCP ハンドシェイクに応答するか検証する

    MCP クライアントを再起動すると、新しいツールが利用可能になります。

.PARAMETER InstallRoot
    インストール先のルート。既定は %LOCALAPPDATA%\WindowsComputerUseMCP。

.PARAMETER Configuration
    ビルド構成。既定は Release。

.PARAMETER SkipVerify
    配置後の起動検証を省略します。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\install-mcp-server.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\install-mcp-server.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'WindowsComputerUseMCP'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipVerify,
    [switch]$UpdateClientConfig
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $repoRoot 'src\WindowsComputerUseMCP.Server\WindowsComputerUseMCP.Server.csproj'
$installDir = Join-Path $InstallRoot 'current'
$stagingDir = Join-Path $InstallRoot 'staging'
$exeName = 'WindowsComputerUseMCP.Server.exe'

if (-not (Test-Path $serverProject)) {
    throw "サーバープロジェクトが見つかりません: $serverProject"
}

Write-Host '== WindowsComputerUseMCP サーバーの更新 ==' -ForegroundColor Cyan
Write-Host "リポジトリ    : $repoRoot"
Write-Host "インストール先: $installDir"
Write-Host "構成          : $Configuration"
Write-Host ''

# --- 1. 実行中のサーバーを停止 -------------------------------------------------
# 稼働中プロセスは自身の DLL をロックするため、publish 前に必ず落とす。
$running = @(Get-Process -Name 'WindowsComputerUseMCP.Server' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "実行中のサーバーを停止します ($($running.Count) 件)..." -ForegroundColor Yellow
    foreach ($proc in $running) {
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            Write-Host "  停止: PID $($proc.Id)"
        }
        catch {
            Write-Warning "  PID $($proc.Id) を停止できませんでした: $($_.Exception.Message)"
        }
    }
    # ファイルハンドルが解放されるまで少し待つ
    Start-Sleep -Seconds 2
}
else {
    Write-Host '実行中のサーバーはありません。'
}

# --- 2. publish ---------------------------------------------------------------
if (Test-Path $stagingDir) {
    Remove-Item -Recurse -Force $stagingDir
}

Write-Host ''
Write-Host 'publish を実行しています...' -ForegroundColor Cyan
& dotnet publish $serverProject -c $Configuration -o $stagingDir --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish に失敗しました (終了コード $LASTEXITCODE)"
}

$stagedExe = Join-Path $stagingDir $exeName
if (-not (Test-Path $stagedExe)) {
    throw "publish 後に実行ファイルが見つかりません: $stagedExe"
}

# --- 3. インストール先へ反映 ---------------------------------------------------
# publish 中に別のクライアントがサーバーを起動し直す場合があるので、
# 差し替え直前にもう一度停止する。
$running = @(Get-Process -Name 'WindowsComputerUseMCP.Server' -ErrorAction SilentlyContinue)
foreach ($proc in $running) {
    try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch { }
}
if ($running.Count -gt 0) { Start-Sleep -Seconds 2 }

if (Test-Path $installDir) {
    Remove-Item -Recurse -Force $installDir
}
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -Path (Join-Path $stagingDir '*') -Destination $installDir -Recurse -Force
Remove-Item -Recurse -Force $stagingDir

$installedExe = Join-Path $installDir $exeName
$version = (Get-Item $installedExe).VersionInfo.FileVersion
Write-Host ''
Write-Host "配置しました: $installedExe" -ForegroundColor Green
Write-Host "バージョン  : $version"

# --- 4. 起動検証 --------------------------------------------------------------
if (-not $SkipVerify) {
    Write-Host ''
    Write-Host 'MCP ハンドシェイクを検証しています...' -ForegroundColor Cyan

    $initialize = (@{
            jsonrpc = '2.0'
            id      = 1
            method  = 'initialize'
            params  = @{
                protocolVersion = '2024-11-05'
                capabilities    = @{}
                clientInfo      = @{ name = 'install-script'; version = '1.0' }
            }
        } | ConvertTo-Json -Compress -Depth 10)

    $listTools = (@{ jsonrpc = '2.0'; id = 2; method = 'tools/list' } | ConvertTo-Json -Compress)
    $initialized = (@{ jsonrpc = '2.0'; method = 'notifications/initialized' } | ConvertTo-Json -Compress)

    # Start-Process のファイルリダイレクトは Windows PowerShell 5.1 で
    # stdin が届かず stdout が空になる。Process API で直接ストリームを扱う。
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $installedExe
    $psi.WorkingDirectory = $installDir
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    $proc.StandardInput.WriteLine($initialize)
    $proc.StandardInput.WriteLine($initialized)
    $proc.StandardInput.WriteLine($listTools)
    $proc.StandardInput.Flush()

    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    [void]$proc.StandardError.ReadToEndAsync()
    Start-Sleep -Seconds 5
    $proc.StandardInput.Close()

    if (-not $proc.WaitForExit(15000)) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
    }

    # 応答内の日本語は \uXXXX でエスケープされるため名前の抽出は ASCII だけで足りる。
    # $Matches は自動変数なので別名を使う。
    $raw = $stdoutTask.Result
    $found = [regex]::Matches($raw, '"name"\s*:\s*"([a-z][a-z0-9_]*)"')
    $toolNames = @($found | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)

    if ($toolNames.Count -eq 0) {
        throw 'ツール一覧を取得できませんでした。サーバーが正しく起動していない可能性があります。'
    }

    $background = @($toolNames | Where-Object { $_ -like 'background_desktop_*' })
    $skill = @($toolNames | Where-Object { $_ -like 'skill_*' })
    Write-Host "検証 OK: $($toolNames.Count) 個のツールを公開しています" -ForegroundColor Green
    Write-Host "  background_desktop_* : $($background.Count) 件"
    Write-Host "  skill_*              : $($skill.Count) 件"

    if ($background.Count -eq 0) {
        throw 'background_desktop_* ツールが見つかりません。古いバイナリが配置された可能性があります。'
    }
}

# --- 5. MCP クライアント設定の更新 ---------------------------------------------
$copilotConfig = Join-Path $env:USERPROFILE '.copilot\mcp-config.json'

if ($UpdateClientConfig) {
    Write-Host ''
    Write-Host 'GitHub Copilot CLI の設定を更新しています...' -ForegroundColor Cyan

    if (-not (Test-Path $copilotConfig)) {
        throw "設定ファイルが見つかりません: $copilotConfig"
    }

    # 破損に備えてタイムスタンプ付きバックアップを残す
    $backup = "$copilotConfig.$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
    Copy-Item $copilotConfig $backup -Force

    $json = Get-Content $copilotConfig -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $json.mcpServers) {
        throw '設定ファイルに mcpServers セクションがありません。'
    }

    $entry = $json.mcpServers.'windows-computer-use'
    if ($entry) {
        $previous = $entry.command
        $entry.command = $installedExe
        Write-Host "  変更前: $previous"
        Write-Host "  変更後: $installedExe"
    }
    else {
        $json.mcpServers | Add-Member -NotePropertyName 'windows-computer-use' -NotePropertyValue ([pscustomobject]@{
                type    = 'local'
                command = $installedExe
                args    = @()
                tools   = @('*')
            })
        Write-Host "  新規登録: $installedExe"
    }

    $json | ConvertTo-Json -Depth 32 | Set-Content -Path $copilotConfig -Encoding UTF8
    Write-Host "設定を更新しました（バックアップ: $backup）" -ForegroundColor Green
}

# --- 案内 ---------------------------------------------------------------------
Write-Host ''
Write-Host '== MCP クライアントの設定 ==' -ForegroundColor Cyan

if (-not $UpdateClientConfig) {
    Write-Host '以下の command を指すように登録してください。'
    Write-Host '（-UpdateClientConfig を付けて実行すると Copilot CLI 設定を自動更新します）'
    Write-Host ''
    Write-Host "  $installedExe" -ForegroundColor White
    Write-Host ''
    Write-Host "GitHub Copilot CLI  : $copilotConfig"
    Write-Host 'VS Code (ユーザー)  : コマンドパレット > MCP: Open User Configuration'
    Write-Host ''
}

Write-Host '設定変更を反映するには MCP クライアントを再起動してください。' -ForegroundColor Yellow
