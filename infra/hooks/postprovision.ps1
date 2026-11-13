param([switch]$RotateCredential)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:AZURE_ENTRA_CLIENT_ID)) {
    throw 'AZURE_ENTRA_CLIENT_ID was not produced by provisioning.'
}

if ([string]::IsNullOrWhiteSpace($env:KEYVAULT_NAME)) {
    throw 'KEYVAULT_NAME was not produced by provisioning.'
}

$displayName = "azd-$($env:AZURE_ENV_NAME)-oidc"
$existingSecret = az keyvault secret list `
    --vault-name $env:KEYVAULT_NAME `
    --query "[?name=='oidc-client-secret' && attributes.enabled].id | [0]" `
    --output tsv `
    --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect OIDC credential metadata in Azure Key Vault.'
}
if (-not $RotateCredential -and -not [string]::IsNullOrWhiteSpace($existingSecret)) {
    return
}

$clientSecret = az ad app credential reset `
    --id $env:AZURE_ENTRA_CLIENT_ID `
    --append `
    --display-name $displayName `
    --years 1 `
    --query password `
    --output tsv `
    --only-show-errors
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($clientSecret)) {
    throw 'Unable to create the Entra application credential.'
}

try {
    az keyvault secret set `
        --vault-name $env:KEYVAULT_NAME `
        --name oidc-client-secret `
        --value $clientSecret `
        --output none `
        --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to store the Entra application credential in Azure Key Vault.'
    }
}
finally {
    $clientSecret = $null
}
