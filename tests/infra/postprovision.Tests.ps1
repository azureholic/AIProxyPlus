$ErrorActionPreference = 'Stop'
$hook = Join-Path $PSScriptRoot '..\..\infra\hooks\postprovision.ps1'
$env:AZURE_ENTRA_CLIENT_ID = 'test-client-id'
$env:KEYVAULT_NAME = 'test-vault'
$env:AZURE_ENV_NAME = 'test-environment'

function global:az {
    if (($args[0..2] -join ' ') -eq 'keyvault secret list' -and
        ($args -join ' ') -notmatch 'oidc-client-secret') {
        $global:LASTEXITCODE = 0
        return 'existing-consumer-pepper'
    }
    $global:Pr49HookTestState.Commands.Add(($args -join ' '))
    $global:LASTEXITCODE = 0
    switch ($args[0..2] -join ' ') {
        'keyvault secret list' {
            if ($global:Pr49HookTestState.MetadataFails) { $global:LASTEXITCODE = 1 }
            return $global:Pr49HookTestState.ExistingSecret
        }
        'ad app credential' {
            if ($args[3] -ne 'reset' -or $args -notcontains '--append') {
                throw 'Provisioning must only append credentials, never replace or delete them.'
            }
            return 'test-only-new-credential'
        }
        'keyvault secret set' {
            if ($global:Pr49HookTestState.WriteFails) { $global:LASTEXITCODE = 1 }
            return
        }
        default { throw "Unexpected command: $($args -join ' ')" }
    }
}

try {
    foreach ($scenario in @('existing', 'bootstrap', 'rotate', 'metadata-failure', 'write-failure')) {
        $global:Pr49HookTestState = @{
            Commands = [System.Collections.Generic.List[string]]::new()
            ExistingSecret = if ($scenario -in @('existing', 'rotate')) { 'https://test-vault/secrets/oidc-client-secret' } else { '' }
            MetadataFails = $scenario -eq 'metadata-failure'
            WriteFails = $scenario -eq 'write-failure'
        }
        $failed = $false
        try {
            & $hook -RotateCredential:($scenario -eq 'rotate')
        }
        catch {
            if ($scenario -notin @('metadata-failure', 'write-failure')) { throw }
            $failed = $true
        }
        if ($failed -ne ($scenario -in @('metadata-failure', 'write-failure'))) {
            throw "Incorrect error handling in $scenario."
        }
        $expectedCommands = if ($scenario -in @('existing', 'metadata-failure')) { 1 } else { 3 }
        if ($global:Pr49HookTestState.Commands.Count -ne $expectedCommands) {
            throw "Incorrect credential lifecycle in $scenario."
        }
        if ($global:Pr49HookTestState.Commands | Where-Object { $_ -match 'credential delete' }) {
            throw "An old credential was retired during $scenario."
        }
        Write-Output "Passed: $scenario"
    }
}
finally {
    Remove-Item Function:\az
    Remove-Variable Pr49HookTestState -Scope Global
}
