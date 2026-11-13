$ErrorActionPreference = 'Stop'
$hook = Join-Path $PSScriptRoot '..\..\infra\hooks\initialize-consumer-pepper.ps1'
$env:KEYVAULT_NAME = 'test-vault'

function global:az {
    $global:PepperHookTestState.Commands.Add(($args -join ' '))
    $global:LASTEXITCODE = 0
    switch ($args[0..2] -join ' ') {
        'keyvault secret list' {
            if ($global:PepperHookTestState.MetadataFails) { $global:LASTEXITCODE = 1 }
            return $global:PepperHookTestState.Existing
        }
        'keyvault secret set' {
            if ($global:PepperHookTestState.WriteFails) { $global:LASTEXITCODE = 1 }
            $name = $args[[Array]::IndexOf($args, '--name') + 1]
            $pepper = $args[[Array]::IndexOf($args, '--value') + 1]
            $digest = [System.Security.Cryptography.SHA256]::HashData(
                [System.Text.Encoding]::UTF8.GetBytes('consumer-key-pepper'))
            $expectedName = 'aiproxyplus-' + [Convert]::ToHexString($digest).ToLowerInvariant()
            if ($name -ne $expectedName -or $pepper -cnotmatch '^[0-9a-f]{64}$') {
                throw 'Invalid consumer pepper mapping or value.'
            }
            if (-not $global:PepperHookTestState.WriteFails) {
                $global:PepperHookTestState.Existing = 'new-pepper'
            }
            return
        }
        default { throw 'Unexpected bootstrap command.' }
    }
}

try {
    foreach ($scenario in @('existing', 'bootstrap', 'metadata-failure', 'write-failure')) {
        $global:PepperHookTestState = @{
            Commands = [System.Collections.Generic.List[string]]::new()
            Existing = if ($scenario -eq 'existing') { 'existing-pepper' } else { '' }
            MetadataFails = $scenario -eq 'metadata-failure'
            WriteFails = $scenario -eq 'write-failure'
        }
        $failed = $false
        try {
            $output = & $hook
            if ($output) { throw 'Bootstrap must not emit a pepper.' }
        }
        catch {
            if ($scenario -notin @('metadata-failure', 'write-failure')) { throw }
            $failed = $true
        }
        if ($failed -ne ($scenario -in @('metadata-failure', 'write-failure'))) {
            throw "Incorrect error handling in $scenario."
        }
        $expectedCommands = if ($scenario -in @('existing', 'metadata-failure')) { 1 } else { 2 }
        if ($global:PepperHookTestState.Commands.Count -ne $expectedCommands) {
            throw "Incorrect bootstrap lifecycle in $scenario."
        }
        if ($scenario -eq 'bootstrap') {
            & $hook
            if ($global:PepperHookTestState.Commands.Count -ne 3) {
                throw 'A repeated deployment must not replace the pepper.'
            }
        }
        Write-Output "Passed: $scenario"
    }
}
finally {
    Remove-Item Function:\az
    Remove-Variable PepperHookTestState -Scope Global
}
