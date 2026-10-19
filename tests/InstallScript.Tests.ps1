$ErrorActionPreference = 'Stop'
$installer = Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\install-mcp-server.ps1'
$testRoot = Join-Path $PSScriptRoot ('.install-test-' + [guid]::NewGuid().ToString('N'))
$publishCalls = [System.Collections.Generic.List[string]]::new()
$previousCopilotHome = $env:COPILOT_HOME

# Publish placeholders, never executable code or the user's real installation.
function dotnet {
    $outputIndex = [array]::IndexOf($args, '-o')
    if ($outputIndex -lt 0) { throw 'Expected dotnet publish output directory.' }
    $output = $args[$outputIndex + 1]
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Set-Content (Join-Path $output 'placeholder.txt') 'fixture'
    $publishCalls.Add($output)
    $global:LASTEXITCODE = 0
}

try {
    $env:COPILOT_HOME = Join-Path $testRoot 'copilot'
    & $installer -InstallRoot $testRoot
    if ($publishCalls.Count -ne 2) { throw 'Expected two publish calls.' }
    if (-not (Test-Path (Join-Path $testRoot 'current\launcher\placeholder.txt'))) {
        throw 'Launcher was not published.'
    }
    if (Get-ChildItem $testRoot -Directory -Filter 'staging-*') {
        throw 'Staging directory was not cleaned up.'
    }
    if (Test-Path $env:COPILOT_HOME) { throw 'Default install touched the client config directory.' }

    New-Item -ItemType Directory -Path $env:COPILOT_HOME | Out-Null
    $config = Join-Path $env:COPILOT_HOME 'mcp-config.json'
    $fixture = '{"mcpServers":{"unrelated":{"command":"keep-me"},"windows-computer-use":{"type":"local","command":"old","args":["custom"],"tools":["window_list"],"env":{"CUSTOM":"keep-me"}}}}'
    Set-Content $config $fixture
    & $installer -InstallRoot $testRoot -UpdateClientConfig -SkipPlaywright -SkipVerify
    $updated = Get-Content $config -Raw | ConvertFrom-Json
    if ($updated.mcpServers.'playwright-bg') { throw 'Playwright was registered without opt-in.' }
    if ($updated.mcpServers.unrelated.command -ne 'keep-me' -or
        $updated.mcpServers.'windows-computer-use'.env.CUSTOM -ne 'keep-me' -or
        $updated.mcpServers.'windows-computer-use'.args[0] -ne 'custom' -or
        $updated.mcpServers.'windows-computer-use'.tools[0] -ne 'window_list') {
        throw 'Explicit registration did not preserve existing settings.'
    }
    if (-not (Get-ChildItem $env:COPILOT_HOME -Filter '*.bak')) { throw 'Missing config backup.' }

    & $installer -InstallRoot $testRoot -UpdateClientConfig -RegisterPlaywright
    $updated = Get-Content $config -Raw | ConvertFrom-Json
    if (-not $updated.mcpServers.'playwright-bg') { throw 'Explicit Playwright opt-in failed.' }
    if ($updated.mcpServers.unrelated.command -ne 'keep-me') { throw 'Unrelated server was modified.' }
    Write-Host 'Installer defaults and explicit registration compatibility passed without launching executables.'
}
finally {
    $env:COPILOT_HOME = $previousCopilotHome
    if (Test-Path $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
