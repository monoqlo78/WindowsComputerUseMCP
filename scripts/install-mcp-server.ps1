<#
.SYNOPSIS
    Publish WindowsComputerUseMCP without registering or starting it.
.DESCRIPTION
    Client configuration is modified only with -UpdateClientConfig. Stop clients using this installation
    before updating it. No running server processes are terminated automatically.
.PARAMETER Verify
    Explicitly start the published server for a bounded MCP handshake check.
.PARAMETER UpdateClientConfig
    Explicitly register/update windows-computer-use in the Copilot user config.
    This can cause connected clients to start it before any tool is called.
.PARAMETER RegisterPlaywright
    With -UpdateClientConfig, also explicitly register playwright-bg.
.PARAMETER SkipPlaywright
    Compatibility switch. Playwright registration is now opt-in by default.
.PARAMETER SkipVerify
    Compatibility switch. Verification is now opt-in by default.
.EXAMPLE
    .\scripts\install-mcp-server.ps1
.EXAMPLE
    .\scripts\install-mcp-server.ps1 -Verify
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'WindowsComputerUseMCP'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Verify,
    [switch]$UpdateClientConfig,
    [switch]$RegisterPlaywright,
    [switch]$SkipPlaywright,
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'
if ($Verify -and $SkipVerify) { throw 'Use either -Verify or -SkipVerify, not both.' }
if ($RegisterPlaywright -and (-not $UpdateClientConfig -or $SkipPlaywright)) {
    throw '-RegisterPlaywright requires -UpdateClientConfig and cannot be combined with -SkipPlaywright.'
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $repoRoot 'src\WindowsComputerUseMCP.Server\WindowsComputerUseMCP.Server.csproj'
$launcherProject = Join-Path $repoRoot 'src\WindowsComputerUseMCP.DesktopLauncher\WindowsComputerUseMCP.DesktopLauncher.csproj'
$installDir = Join-Path $InstallRoot 'current'
$installedExe = Join-Path $installDir 'WindowsComputerUseMCP.Server.exe'
$launcherDir = Join-Path $installDir 'launcher'

function Assert-InstallationStopped {
    $running = @(Get-Process -Name 'WindowsComputerUseMCP.Server' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $installedExe })
    if ($running.Count -gt 0) {
        throw "Installation is in use (PID: $($running.Id -join ', ')). Disable this server in its MCP clients and close those sessions before updating."
    }
}

Assert-InstallationStopped

# A unique staging directory avoids deleting another installation's build output.
$stagingDir = Join-Path $InstallRoot ("staging-" + [guid]::NewGuid().ToString('N'))
try {
    $serverStaging = Join-Path $stagingDir 'server'
    $launcherStaging = Join-Path $stagingDir 'launcher'
    & dotnet publish $serverProject -c $Configuration -o $serverStaging --nologo
    if ($LASTEXITCODE -ne 0) { throw "Server publish failed ($LASTEXITCODE)." }
    & dotnet publish $launcherProject -c $Configuration -o $launcherStaging --nologo
    if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed ($LASTEXITCODE)." }

    Assert-InstallationStopped
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Copy-Item -Path (Join-Path $serverStaging '*') -Destination $installDir -Recurse -Force
    New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null
    Copy-Item -Path (Join-Path $launcherStaging '*') -Destination $launcherDir -Recurse -Force
    Copy-Item (Join-Path $repoRoot 'config\playwright-mcp-background.config.json') $installDir -Force
}
finally {
    if (Test-Path $stagingDir) {
        Remove-Item -LiteralPath $stagingDir -Recurse -Force
    }
}

Write-Host "Published: $installedExe"
Write-Host 'Publishing does not start or register either MCP server.'

if ($Verify) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $installedExe
    $psi.WorkingDirectory = $installDir
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.Arguments = '--WindowsComputerUseMcp:BackgroundServices:EnableFigmaBridge=false'
    $proc = [System.Diagnostics.Process]::Start($psi)
    try {
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()
        $initialize = @{
            jsonrpc = '2.0'; id = 1; method = 'initialize'
            params = @{
                protocolVersion = '2024-11-05'; capabilities = @{}
                clientInfo = @{ name = 'install-script'; version = '1.0' }
            }
        } | ConvertTo-Json -Compress -Depth 10
        $proc.StandardInput.WriteLine($initialize)
        $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
        $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list"}')
        $proc.StandardInput.Flush()
        Start-Sleep -Seconds 5
        $proc.StandardInput.Close()
        if (-not $proc.WaitForExit(15000)) { throw 'MCP verification timed out.' }
        if ($proc.ExitCode -ne 0) { throw "MCP verification failed: $($stderrTask.Result)" }

        $responses = @($stdoutTask.Result -split '\r?\n' |
            Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
        $toolsResponse = @($responses | Where-Object { $_.id -eq 2 })
        $toolNames = @($toolsResponse | ForEach-Object { $_.result.tools.name })
        if ($toolNames -notcontains 'window_list' -or $toolNames -notcontains 'background_desktop_list') {
            throw 'MCP tools/list did not return the expected tools.'
        }
        Write-Host "Verification OK: $($toolNames.Count) tools."
    }
    finally {
        if (-not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            $proc.WaitForExit()
        }
        $proc.Dispose()
    }
}

if ($UpdateClientConfig) {
    $copilotHome = if ($env:COPILOT_HOME) { $env:COPILOT_HOME } else { Join-Path $env:USERPROFILE '.copilot' }
    $copilotConfig = Join-Path $copilotHome 'mcp-config.json'
    if (Test-Path $copilotConfig) {
        $json = Get-Content $copilotConfig -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    else {
        $json = [pscustomobject]@{ mcpServers = [pscustomobject]@{} }
    }
    if (-not $json.mcpServers -or $json.mcpServers -isnot [pscustomobject]) {
        throw 'Expected an mcpServers object in the Copilot configuration.'
    }

    $entry = $json.mcpServers.'windows-computer-use'
    if ($entry) {
        $entry.command = $installedExe
    }
    else {
        $json.mcpServers | Add-Member -NotePropertyName 'windows-computer-use' -NotePropertyValue ([pscustomobject]@{
            type = 'local'; command = $installedExe; args = @(); tools = @('*')
        })
    }
    if ($RegisterPlaywright) {
        $playwright = [pscustomobject]@{
            type = 'local'
            command = Join-Path $launcherDir 'wcu-desktop-launcher.exe'
            args = @('--desktop', 'WCU_Background', '--', 'cmd.exe', '/c', 'npx', '-y', '@playwright/mcp@latest',
                '--browser', 'msedge', '--config', (Join-Path $installDir 'playwright-mcp-background.config.json'))
            tools = @('*')
        }
        $json.mcpServers | Add-Member -NotePropertyName 'playwright-bg' -NotePropertyValue $playwright -Force
    }

    New-Item -ItemType Directory -Path $copilotHome -Force | Out-Null
    if (Test-Path $copilotConfig) {
        $backup = "$copilotConfig.$(Get-Date -Format 'yyyyMMdd-HHmmss-fff').bak"
        Copy-Item -LiteralPath $copilotConfig -Destination $backup
        Write-Host "Config backup: $backup"
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($copilotConfig, ($json | ConvertTo-Json -Depth 100), $utf8NoBom)
    Write-Warning 'Explicit registration complete. Clients may start registered servers during connection/tool discovery, even before a tool is called.'
}
else {
    Write-Host 'No client configuration was changed. Neither MCP server was registered or enabled.'
}

Write-Host 'Use the README opt-in instructions only when you need these tools.'
Write-Host 'To keep existing Copilot registrations inactive:'
Write-Host '  copilot mcp disable windows-computer-use'
Write-Host '  copilot mcp disable playwright-bg'
Write-Host 'Registration: -UpdateClientConfig (windows-computer-use only); add -RegisterPlaywright for playwright-bg.'
