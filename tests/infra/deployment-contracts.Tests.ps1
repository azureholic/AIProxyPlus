param([string]$GeneratedApiTemplatePath)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$compiled = az bicep build --file (Join-Path $root 'infra\main.bicep') --stdout
if ($LASTEXITCODE -ne 0) { throw 'Bicep compilation failed.' }
$template = ($compiled -join "`n") | ConvertFrom-Json
$resources = $template.resources.PSObject.Properties.Value
$principal = @($resources | Where-Object { $_.type -eq 'Microsoft.Graph/servicePrincipals@v1.0' })
if ($principal.Count -ne 1 -or $principal[0].properties.appRoleAssignmentRequired -ne $true) {
    throw 'The generated service principal must require administrator assignment.'
}
$application = $resources | Where-Object { $_.type -eq 'Microsoft.Graph/applications@v1.0' }
if ($application.properties.web.logoutUrl -notmatch '/signout-oidc') {
    throw 'Front-channel logout must use the remote sign-out endpoint.'
}

$api = az bicep build --file (Join-Path $root 'infra\api\api-containerapp.module.bicep') --stdout
if ($LASTEXITCODE -ne 0) { throw 'API Bicep compilation failed.' }
$apiTemplate = ($api -join "`n") | ConvertFrom-Json
$probes = $apiTemplate.resources.properties.template.containers.probes
$startup = @($probes | Where-Object { $_.type -eq 'Startup' })
if ($startup.Count -ne 1 -or $startup[0].httpGet.path -ne '/health/live' -or
    $startup[0].httpGet.port -ne 8080 -or $startup[0].periodSeconds -ne 10 -or
    $startup[0].failureThreshold -ne 60 -or $startup[0].timeoutSeconds -ne 5) {
    throw 'Startup must gate liveness with the agreed migration budget.'
}
Write-Output 'Passed: compiled assignment, remote logout, and startup probe contracts.'

if ($GeneratedApiTemplatePath) {
    $generated = az bicep build --file $GeneratedApiTemplatePath --stdout
    if ($LASTEXITCODE -ne 0) { throw 'Generated AppHost API Bicep compilation failed.' }
    $generatedTemplate = ($generated -join "`n") | ConvertFrom-Json
    $generatedProbes = $generatedTemplate.resources.properties.template.containers.probes
    $generatedStartup = @($generatedProbes | Where-Object { $_.type -eq 'Startup' })
    if ($generatedStartup.Count -ne 1 -or
        $generatedStartup[0].httpGet.path -ne $startup[0].httpGet.path -or
        $generatedStartup[0].periodSeconds -ne $startup[0].periodSeconds -or
        $generatedStartup[0].timeoutSeconds -ne $startup[0].timeoutSeconds -or
        $generatedStartup[0].failureThreshold -ne $startup[0].failureThreshold) {
        throw 'Generated AppHost startup probe differs from the checked-in deployment contract.'
    }
    Write-Output 'Passed: generated AppHost startup probe matches checked-in Bicep.'
}
