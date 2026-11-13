$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:KEYVAULT_NAME)) {
    throw 'KEYVAULT_NAME was not produced by provisioning.'
}

$digest = [System.Security.Cryptography.SHA256]::HashData(
    [System.Text.Encoding]::UTF8.GetBytes('consumer-key-pepper'))
$name = 'aiproxyplus-' + [System.Convert]::ToHexString($digest).ToLowerInvariant()
$existing = az keyvault secret list `
    --vault-name $env:KEYVAULT_NAME `
    --query "[?name=='$name'].id | [0]" `
    --output tsv `
    --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect consumer-key pepper metadata in Azure Key Vault.'
}
if (-not [string]::IsNullOrWhiteSpace($existing)) {
    return
}

$pepper = [System.Convert]::ToHexString(
    [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
try {
    az keyvault secret set `
        --vault-name $env:KEYVAULT_NAME `
        --name $name `
        --value $pepper `
        --output none `
        --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to provision the consumer-key pepper in Azure Key Vault.'
    }
}
finally {
    $pepper = $null
}
