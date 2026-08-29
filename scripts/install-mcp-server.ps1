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

    あわせて wcu-desktop-launcher（他の MCP サーバーをバックグラウンド
    デスクトップ上で起動するためのラッパー）も配置します。

    MCP クライアントを再起動すると、新しいツールが利用可能になります。

.PARAMETER InstallRoot
    インストール先のルート。既定は %LOCALAPPDATA%\WindowsComputerUseMCP。

.PARAMETER Configuration
    ビルド構成。既定は Release。

.PARAMETER SkipVerify
    配置後の起動検証を省略します。

.PARAMETER UpdateClientConfig
    GitHub Copilot CLI の mcp-config.json を自動更新します。

.PARAMETER SkipPlaywright
    -UpdateClientConfig 時に playwright-bg（バックグラウンドのブラウザ操作）を
    登録しません。

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
    [switch]$UpdateClientConfig,
    [switch]$SkipPlaywright
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $repoRoot 'src\WindowsComputerUseMCP.Server\WindowsComputerUseMCP.Server.csproj'
$launcherProject = Join-Path $repoRoot 'src\WindowsComputerUseMCP.DesktopLauncher\WindowsComputerUseMCP.DesktopLauncher.csproj'
$playwrightConfigSource = Join-Path $repoRoot 'config\playwright-mcp-background.config.json'
$installDir = Join-Path $InstallRoot 'current'
$launcherDir = Join-Path $installDir 'launcher'
$stagingDir = Join-Path $InstallRoot 'staging'
$launcherStagingDir = Join-Path $InstallRoot 'staging-launcher'
$exeName = 'WindowsComputerUseMCP.Server.exe'
$launcherExeName = 'wcu-desktop-launcher.exe'

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
if (Test-Path $launcherStagingDir) {
    Remove-Item -Recurse -Force $launcherStagingDir
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

# デスクトップランチャーは別アセンブリなので独立した出力先へ publish する。
if (Test-Path $launcherProject) {
    Write-Host ''
    Write-Host 'デスクトップランチャーを publish しています...' -ForegroundColor Cyan
    & dotnet publish $launcherProject -c $Configuration -o $launcherStagingDir --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "ランチャーの publish に失敗しました (終了コード $LASTEXITCODE)"
    }
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

# ランチャーとブラウザ用設定を配置する。
$installedLauncher = $null
if (Test-Path $launcherStagingDir) {
    New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null
    Copy-Item -Path (Join-Path $launcherStagingDir '*') -Destination $launcherDir -Recurse -Force
    Remove-Item -Recurse -Force $launcherStagingDir
    $installedLauncher = Join-Path $launcherDir $launcherExeName
}

$installedPlaywrightConfig = Join-Path $installDir 'playwright-mcp-background.config.json'
if (Test-Path $playwrightConfigSource) {
    Copy-Item $playwrightConfigSource $installedPlaywrightConfig -Force
}

$installedExe = Join-Path $installDir $exeName
$version = (Get-Item $installedExe).VersionInfo.FileVersion
Write-Host ''
Write-Host "配置しました: $installedExe" -ForegroundColor Green
Write-Host "バージョン  : $version"
if ($installedLauncher) {
    Write-Host "ランチャー  : $installedLauncher"
}

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

    # PowerShell 5.1 の Set-Content -Encoding UTF8 は BOM を付けてしまう。
    # BOM 付き JSON を拒否するパーサーがあるため BOM なしで書き出す。
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($copilotConfig, ($json | ConvertTo-Json -Depth 32), $utf8NoBom)
    Write-Host "設定を更新しました（バックアップ: $backup）" -ForegroundColor Green
}

# --- 6. バックグラウンドブラウザ (playwright-bg) の登録 ------------------------
# Playwright MCP の node プロセス自体を隠しデスクトップ上で起動すると、
# そこから spawn されるブラウザも同じデスクトップを継承する。
# これによりヘッドありブラウザの忠実性を保ったままフォアグラウンドを奪わない。
if ($UpdateClientConfig -and -not $SkipPlaywright) {
    Write-Host ''
    Write-Host 'playwright-bg（バックグラウンドのブラウザ操作）を登録しています...' -ForegroundColor Cyan

    if (-not $installedLauncher -or -not (Test-Path $installedLauncher)) {
        Write-Warning 'ランチャーが配置されていないため playwright-bg の登録を省略します。'
    }
    elseif (-not (Test-Path $installedPlaywrightConfig)) {
        Write-Warning "ブラウザ用設定が見つからないため登録を省略します: $installedPlaywrightConfig"
    }
    else {
        $json = Get-Content $copilotConfig -Raw -Encoding UTF8 | ConvertFrom-Json

        # npx は .cmd なので cmd.exe 経由で起動する必要がある。
        $playwrightArgs = @(
            '--desktop', 'WCU_Background',
            '--',
            'cmd.exe', '/c', 'npx', '-y', '@playwright/mcp@latest',
            '--browser', 'msedge',
            '--config', $installedPlaywrightConfig
        )

        $entry = [pscustomobject]@{
            type    = 'local'
            command = $installedLauncher
            args    = $playwrightArgs
            tools   = @('*')
        }

        if ($json.mcpServers.PSObject.Properties.Name -contains 'playwright-bg') {
            $json.mcpServers.'playwright-bg' = $entry
            Write-Host '  既存の playwright-bg を更新しました。'
        }
        else {
            $json.mcpServers | Add-Member -NotePropertyName 'playwright-bg' -NotePropertyValue $entry
            Write-Host '  playwright-bg を新規登録しました。'
        }

        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($copilotConfig, ($json | ConvertTo-Json -Depth 32), $utf8NoBom)
        Write-Host "  デスクトップ: WCU_Background"
        Write-Host '登録しました。ブラウザは隠しデスクトップ上で動作します。' -ForegroundColor Green
    }
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
